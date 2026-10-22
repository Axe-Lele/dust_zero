// Cross-table reference checks.
//
// The original game failed softly - a missing skill_param logged a console warning and the
// value became 0, so a typo in the spreadsheet showed up as a unit that quietly did nothing.
// Here every dangling reference is collected and reported together, named by table and row,
// before the game is allowed to start.
using System;
using System.Collections.Generic;
using System.Linq;

namespace DustZero.Data
{
    public static class TableValidator
    {
        /// <summary>
        /// Behaviour keys the defense dispatch knows how to run. A unit_behaviour row naming
        /// anything else is a typo that would otherwise make the appliance silently fall back
        /// to a plain attack, which looks like a balance problem rather than a data one.
        /// </summary>
        static readonly string[] KnownDefenseKits =
            { "vac", "melee", "fridge", "ac", "micro", "washer", "pc", "fan", "gen" };
        static readonly string[] KnownSurvivorLeaders = { "vac", "robot", "ac" };
        static readonly string[] KnownSurvivorCompanions =
            { "vac", "robot", "fridge", "ac", "micro", "washer", "fan" };

        /// <summary>
        /// A unit_behaviour row must name kits the code actually has, and every skill_param
        /// key that kit reads must exist for this appliance - borrowing a kit means supplying
        /// your own numbers for it.
        /// </summary>
        static void ValidateBehaviour(GameDatabase db, string id, UnitDef u, List<string> problems)
        {
            var b = u.Behaviour;
            if (b == null)
            {
                problems.Add($"unit_behaviour: '{id}' 행이 없습니다. (11_Character.xlsx)");
                return;
            }
            Check(b.defense, KnownDefenseKits, "defense");
            Check(b.survLeader, KnownSurvivorLeaders, "survLeader");
            Check(b.survComp, KnownSurvivorCompanions, "survComp");

            foreach (var raw in (b.skp ?? string.Empty).Split(','))
            {
                var k = raw.Trim();
                if (k.Length == 0) continue;
                if (!db.HasSkp(id, k))
                    problems.Add($"skill_param: '{id}' 의 '{k}' 값이 없습니다. " +
                                 $"(unit_behaviour.skp 가 요구하는 값, 02_Unit.xlsx 의 skill_param 시트)");
            }

            void Check(string key, string[] known, string column)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                if (System.Array.IndexOf(known, key) >= 0) return;
                problems.Add($"unit_behaviour: '{id}' 의 {column} = '{key}' 는 코드에 없는 동작입니다. " +
                             $"쓸 수 있는 값: {string.Join(", ", known)}");
            }
        }

        /// <summary>
        /// Every appliance needs a unit_art row with a chibi name. The illustration slots may
        /// be blank while art is in progress - the builder warns about those rather than
        /// failing the load, so an unfinished appliance is still playable.
        /// </summary>
        static void ValidateArt(GameDatabase db, string id, UnitDef u, List<string> problems)
        {
            if (u.Art == null)
            {
                problems.Add($"unit_art: '{id}' 행이 없습니다. (11_Character.xlsx)");
                return;
            }
            if (string.IsNullOrWhiteSpace(u.Art.chibi))
                problems.Add($"unit_art: '{id}' 의 chibi(치비 파일명)가 비어 있습니다.");
            if (u.Art.roomSpeed <= 0f)
                problems.Add($"unit_art: '{id}' 의 roomSpeed 가 0 이하입니다. 로비에서 움직이지 않습니다.");
        }

        /// <summary>Tile characters the map grid may contain.</summary>
        // 10.16-n adds two ground tiles: R rug (slows) and O oil (slows and heats).
        const string ValidTiles = "XHPFWERO";

        public static void Validate(GameDatabase db, List<string> problems)
        {
            ValidateConfig(db, problems);
            ValidateEnums(db, problems);
            ValidateUnits(db, problems);
            ValidateMapsAndStages(db, problems);
            ValidateRewards(db, problems);
            ValidateGacha(db, problems);
            ValidateConditions(db, problems);
            ValidateTutorial(db, problems);
            ValidateSurvivor(db, problems);
            ValidateGrowthTables(db, problems);
            ValidateShopAndPass(db, problems);
            ValidateGuide(db, problems);
        }

        /// <summary>unit_star / unit_boost / shards - the 10.15-h growth and boost tables.</summary>
        static void ValidateGrowthTables(GameDatabase db, List<string> problems)
        {
            var stars = db.UnitStars;
            if (stars.Count == 0) problems.Add("unit_star 테이블이 비어 있습니다. 돌파를 할 수 없습니다.");
            else
            {
                if (stars.Count != db.Config.starMax)
                    problems.Add($"unit_star 행 {stars.Count}개가 config.starMax({db.Config.starMax})와 다릅니다.");
                for (int i = 0; i < stars.Count; i++)
                {
                    var r = stars[i];
                    if (r.star != i + 1) problems.Add($"unit_star: star 값이 1부터 1씩 올라가야 하는데 {r.star} 입니다.");
                    if (r.shards <= 0) problems.Add($"unit_star {r.star}: shards 가 0 이하입니다.");
                    if (r.lvCap <= 0) problems.Add($"unit_star {r.star}: lvCap 이 0 이하입니다.");
                    if (i > 0 && r.lvCap < stars[i - 1].lvCap)
                        problems.Add($"unit_star {r.star}: 레벨 상한이 이전 단계보다 낮습니다.");
                }
                if (stars[^1].lvCap != db.Config.lvMax)
                    problems.Add($"unit_star 마지막 lvCap({stars[^1].lvCap})이 config.lvMax({db.Config.lvMax})와 다릅니다.");
                if (db.Config.lvCapBase <= 0 || db.Config.lvCapBase > db.Config.lvMax)
                    problems.Add($"config.lvCapBase({db.Config.lvCapBase})가 1~lvMax 범위를 벗어납니다.");
            }

            foreach (var arr in new[] { (db.Config.dupeShards, "dupeShards"), (db.Config.shardRecruit, "shardRecruit") })
            {
                if (arr.Item1 == null || arr.Item1.Length < 3)
                    problems.Add($"config.{arr.Item2} 는 R/SR/SSR 3개 값이 필요합니다.");
            }
            if (db.Config.multitapCost == null || db.Config.multitapCost.Length == 0)
                problems.Add("config.multitapCost 가 비어 있습니다. 멀티탭을 살 수 없습니다.");

            // Every appliance needs a shard item, otherwise duplicates have nowhere to go.
            foreach (var id in db.UnitOrder)
                if (!db.ShardOfUnit.ContainsKey(id))
                    problems.Add($"item: 가전 '{id}' 의 조각 아이템(unitId={id})이 없습니다.");

            foreach (var id in db.UnitOrder)
            {
                int max = db.MaxBoostTier(id);
                if (max < 3) { problems.Add($"unit_boost: '{id}' 의 강화 단계가 2·3단 모두 있어야 하는데 최대 {max} 입니다."); continue; }
                for (int tier = 2; tier <= 3; tier++)
                {
                    var b = db.Boost(id, tier);
                    if (b == null) { problems.Add($"unit_boost: '{id}' 의 {tier}단계가 없습니다."); continue; }
                    if (b.statMul <= 1f) problems.Add($"unit_boost '{id}' {tier}단: statMul({b.statMul})이 1 이하입니다.");
                    if (b.costMul < 0f) problems.Add($"unit_boost '{id}' {tier}단: costMul 이 음수입니다.");
                    if (b.spMul <= 0f) problems.Add($"unit_boost '{id}' {tier}단: spMul 이 0 이하입니다.");
                }
                if (db.Boost(id, 3).statMul <= db.Boost(id, 2).statMul)
                    problems.Add($"unit_boost '{id}': 3단 배율이 2단보다 크지 않습니다.");
            }

            if (db.Attendance.Count == 0) problems.Add("attendance 테이블이 비어 있습니다.");
            else
            {
                for (int i = 0; i < db.Attendance.Count; i++)
                {
                    var a = db.Attendance[i];
                    if (a.day != i + 1) problems.Add($"attendance: day 가 1부터 연속이어야 하는데 {a.day} 입니다.");
                    if (!db.RewardGroups.ContainsKey(a.rewardGroup))
                        problems.Add($"attendance {a.day}일: rewardGroup {a.rewardGroup} 이 reward_group 에 없습니다.");
                }
            }
        }

        static void ValidateShopAndPass(GameDatabase db, List<string> problems)
        {
            int payIap = db.Enum("PayKind", "Iap");
            int payCrystal = db.Enum("PayKind", "Crystal");
            int payAd = db.Enum("PayKind", "Ad");
            int maxTab = db.Raw.enums.TryGetValue("ShopTab", out var tabs) ? tabs.Values.Max() : 3;
            int maxLimit = db.Raw.enums.TryGetValue("LimitType", out var lim) ? lim.Values.Max() : 3;

            var seen = new HashSet<int>();
            foreach (var p in db.ShopProducts)
            {
                if (!seen.Add(p.id)) problems.Add($"shop_product: id {p.id} 가 중복됩니다. 구매 제한 기록이 겹칩니다.");
                if (p.tab < 1 || p.tab > maxTab) problems.Add($"shop_product {p.id}: tab {p.tab} 이 enums.ShopTab 범위 밖입니다.");
                if (p.payKind != payIap && p.payKind != payCrystal && p.payKind != payAd)
                    problems.Add($"shop_product {p.id}: payKind {p.payKind} 를 알 수 없습니다.");
                if (p.limitType < 0 || p.limitType > maxLimit)
                    problems.Add($"shop_product {p.id}: limitType {p.limitType} 이 enums.LimitType 범위 밖입니다.");
                if (p.limitType != 0 && p.limitCount <= 0)
                    problems.Add($"shop_product {p.id}: 제한이 있는데 limitCount 가 {p.limitCount} 입니다.");
                // rewardGroup 0 is legal when `special` is what delivers the goods
                // (shard packs, idle grants, pass unlocks). Anything else must point at a group.
                bool specialDelivers = !string.IsNullOrEmpty(p.special);
                if (p.rewardGroup != 0 && !db.RewardGroups.ContainsKey(p.rewardGroup))
                    problems.Add($"shop_product {p.id}: rewardGroup {p.rewardGroup} 이 reward_group 에 없습니다.");
                else if (p.rewardGroup == 0 && !specialDelivers)
                    problems.Add($"shop_product {p.id}: rewardGroup 도 special 도 없어 지급할 보상이 없습니다.");
                if (p.firstBonusGroup != 0 && !db.RewardGroups.ContainsKey(p.firstBonusGroup))
                    problems.Add($"shop_product {p.id}: firstBonusGroup {p.firstBonusGroup} 이 reward_group 에 없습니다.");
                if (p.unlockCond != 0 && !db.Conditions.ContainsKey(p.unlockCond))
                    problems.Add($"shop_product {p.id}: unlockCond {p.unlockCond} 가 condition 에 없습니다.");
                if (!db.HasText(p.nameKey)) problems.Add($"string_ko: '{p.nameKey}' 가 없습니다 (shop_product {p.id}).");
                if (p.payKind == payAd && p.price != 0)
                    problems.Add($"shop_product {p.id}: 광고 상품인데 price 가 {p.price} 입니다.");
                if (p.payKind != payAd && p.price <= 0)
                    problems.Add($"shop_product {p.id}: 유료 상품인데 price 가 {p.price} 입니다.");

                if (specialDelivers)
                {
                    var sp = p.special;
                    bool ok;
                    if (sp == "pass:month" || sp == "pass:noads" || sp == "pass:season") ok = true;
                    else if (sp.StartsWith("idle:"))
                        ok = float.TryParse(sp[5..], System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0f;
                    else if (sp.StartsWith("shard:"))
                    {
                        // shard:<rarity>:<count>, rarity 0 = any owned appliance.
                        var parts = sp.Split(':');
                        ok = parts.Length == 3 &&
                             int.TryParse(parts[1], out var rar) && rar >= 0 && rar <= 3 &&
                             int.TryParse(parts[2], out var n) && n > 0;
                    }
                    else if (sp.StartsWith("gearbox:"))
                    {
                        // gearbox:<grade> - a box of gear at that grade, 1 through 4.
                        ok = int.TryParse(sp[8..], out var grade) && grade >= 1 && grade <= 4;
                    }
                    else ok = false;
                    if (!ok) problems.Add($"shop_product {p.id}: special '{sp}' 형식을 알 수 없습니다.");
                }
            }

            var levels = db.PassLevels;
            if (levels.Count == 0) problems.Add("pass_level 테이블이 비어 있습니다.");
            else
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    var r = levels[i];
                    if (r.level != i + 1) problems.Add($"pass_level: level 이 1부터 연속이어야 하는데 {r.level} 입니다.");
                    if (i > 0 && r.xp < levels[i - 1].xp)
                        problems.Add($"pass_level {r.level}: 누적 XP 가 이전 레벨보다 작습니다.");
                    if (r.freeGroup != 0 && !db.RewardGroups.ContainsKey(r.freeGroup))
                        problems.Add($"pass_level {r.level}: freeGroup {r.freeGroup} 이 reward_group 에 없습니다.");
                    if (r.paidGroup != 0 && !db.RewardGroups.ContainsKey(r.paidGroup))
                        problems.Add($"pass_level {r.level}: paidGroup {r.paidGroup} 이 reward_group 에 없습니다.");
                }
                if (levels[0].xp != 0)
                    problems.Add($"pass_level 1레벨의 누적 XP 는 0이어야 하는데 {levels[0].xp} 입니다.");
            }

            if (db.Config.monthPassDailyGroup != 0 && !db.RewardGroups.ContainsKey(db.Config.monthPassDailyGroup))
                problems.Add($"config.monthPassDailyGroup {db.Config.monthPassDailyGroup} 이 reward_group 에 없습니다.");
            if (!DateTime.TryParse(db.Config.passSeasonStart, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.None, out _))
                problems.Add($"config.passSeasonStart '{db.Config.passSeasonStart}' 를 날짜로 읽을 수 없습니다 (yyyy-MM-dd).");
            if (db.Config.passSeasonDays <= 0) problems.Add("config.passSeasonDays 가 0 이하입니다.");
        }

        static void ValidateGuide(GameDatabase db, List<string> problems)
        {
            foreach (var kv in db.Guides)
            {
                // 10.18-d added the "gacha" onboarding flow (first win on 1-2 -> free SSR).
                if (kv.Key != "first" && kv.Key != "levelup" && kv.Key != "gacha")
                    problems.Add($"guide: 알 수 없는 flow '{kv.Key}'. first, levelup, gacha 중 하나여야 합니다.");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var g = kv.Value[i];
                    if (g.seq != i + 1) problems.Add($"guide '{kv.Key}': seq 가 1부터 연속이어야 하는데 {g.seq} 입니다.");
                    if (!db.HasText(g.textKey)) problems.Add($"string_ko: '{g.textKey}' 가 없습니다 (guide {kv.Key} {g.seq}).");
                    if (string.IsNullOrWhiteSpace(g.target))
                        problems.Add($"guide '{kv.Key}' {g.seq}: target 이 비어 있습니다.");
                    else if (g.target.StartsWith("roster.unit:"))
                    {
                        var unit = g.target["roster.unit:".Length..];
                        if (!db.Units.ContainsKey(unit))
                            problems.Add($"guide '{kv.Key}' {g.seq}: target 이 가리키는 가전 '{unit}' 가 없습니다.");
                    }
                }
            }
        }

        static void ValidateConfig(GameDatabase db, List<string> problems)
        {
            var c = db.Config;
            if (c.deckMax <= 0) problems.Add("config.deckMax 는 1 이상이어야 합니다.");
            if (c.lvMax <= 0) problems.Add("config.lvMax 는 1 이상이어야 합니다.");
            if (c.maxUnits <= 0) problems.Add("config.maxUnits 는 1 이상이어야 합니다.");
            if (c.lifeMax <= 0) problems.Add("config.lifeMax 는 1 이상이어야 합니다.");
            if (c.powerMax <= 0) problems.Add("config.powerMax 는 0보다 커야 합니다.");
            if (c.overheatSec <= 0) problems.Add("config.overheatSec 는 0보다 커야 합니다 (과열 복구 시간).");
            // 10.15-h made survival endless; survDuration/survClearCrystal no longer exist.
            if (c.survScaleStart < 0) problems.Add("config.survScaleStart 가 음수입니다.");
            if (c.survBossEvery <= 0) problems.Add("config.survBossEvery 는 0보다 커야 합니다 (보스 주기).");
            if (c.survSpdMax < 1f) problems.Add("config.survSpdMax 는 1 이상이어야 합니다.");
            if (c.survXpQuad < 0f) problems.Add("config.survXpQuad 가 음수입니다.");
            if (c.survArena <= 1) problems.Add("config.survArena 는 1보다 커야 합니다.");
            if (c.starLifeMin == null || c.starLifeMin.Length == 0)
                problems.Add("config.starLifeMin 이 비어 있습니다. 별 평가 기준 배열이 필요합니다.");
            else
            {
                for (int i = 1; i < c.starLifeMin.Length; i++)
                    if (c.starLifeMin[i] < c.starLifeMin[i - 1])
                        problems.Add("config.starLifeMin 은 오름차순이어야 합니다. 현재: [" + string.Join(", ", c.starLifeMin) + "]");
            }

            foreach (var (group, label) in new[]
            {
                (c.startRewardGroup, "startRewardGroup"),
                (c.attendanceRewardGroup, "attendanceRewardGroup"),
                (c.dupeOverflowRewardGroup, "dupeOverflowRewardGroup"),
            })
            {
                if (!db.RewardGroups.ContainsKey(group))
                    problems.Add($"config.{label} = {group} 에 해당하는 reward_group 행이 없습니다.");
            }
        }

        static void ValidateEnums(GameDatabase db, List<string> problems)
        {
            foreach (var name in new[] { "Rarity", "Family", "Tile", "RewardKind", "CondKind", "TargetType", "EnemyAI", "TutTrigger", "TutUntil" })
                if (!db.Raw.enums.ContainsKey(name))
                    problems.Add($"enums: '{name}' 묶음이 없습니다.");
        }

        static void ValidateUnits(GameDatabase db, List<string> problems)
        {
            if (db.UnitOrder.Count == 0) { problems.Add("unit 테이블에 가전이 하나도 없습니다."); return; }

            foreach (var id in db.UnitOrder)
            {
                var u = db.Units[id];
                if (u.Cost < 0) problems.Add($"unit '{id}': cost 는 0 이상이어야 합니다.");
                if (u.SkillCharge <= 0) problems.Add($"unit '{id}': sp(스킬 충전 시간)는 0보다 커야 합니다.");
                if (u.Atk > 0 && u.Interval <= 0) problems.Add($"unit '{id}': 공격력이 있는데 itv(공격 간격)가 0입니다.");
                if (u.IsMelee && u.Heat <= 0) problems.Add($"unit '{id}': 바닥 배치 가전인데 heat(과열 한도)가 0입니다.");
                if (u.IsMelee && u.Block <= 0) problems.Add($"unit '{id}': 바닥 배치 가전인데 block(저지 수)이 0입니다.");
                if (!db.PartOfFamily.ContainsKey(u.Family))
                    problems.Add($"unit '{id}': 계열 {u.Family} 에 대응하는 부품 아이템이 item 테이블에 없습니다.");

                ValidateBehaviour(db, id, u, problems);
                ValidateArt(db, id, u, problems);

                foreach (var (key, label) in new[] { (u.Name, "nameKey"), (u.ShortName, "shortKey"), (u.Role, "roleKey"), (u.SkillName, "skillKey") })
                    if (string.IsNullOrWhiteSpace(key))
                        problems.Add($"unit '{id}': {label} 가 가리키는 string_ko 문구가 비어 있습니다.");

                for (int i = 0; i < u.Lines.Length; i++)
                    if (!db.HasText($"unit.{id}.line{i + 1}"))
                        problems.Add($"string_ko: 'unit.{id}.line{i + 1}' 대사가 없습니다 (unit.lineCount = {u.Lines.Length}).");
            }

            // Level-up cost bands must cover 1..lvMax-1 without holes.
            var bands = db.Raw.unit_levelup.OrderBy(b => b.lvMin).ToList();
            if (bands.Count == 0) problems.Add("unit_levelup 테이블이 비어 있습니다.");
            else
            {
                if (bands[0].lvMin != 1) problems.Add($"unit_levelup: 첫 구간이 레벨 1에서 시작해야 합니다 (현재 {bands[0].lvMin}).");
                for (int i = 1; i < bands.Count; i++)
                    if (bands[i].lvMin != bands[i - 1].lvMax + 1)
                        problems.Add($"unit_levelup: 레벨 {bands[i - 1].lvMax} 와 {bands[i].lvMin} 사이가 비어 있거나 겹칩니다.");
                if (bands[^1].lvMax < db.Config.lvMax - 1)
                    problems.Add($"unit_levelup: 마지막 구간이 레벨 {bands[^1].lvMax} 까지라 config.lvMax({db.Config.lvMax}) 직전까지 못 채웁니다.");
            }
        }

        static void ValidateMapsAndStages(GameDatabase db, List<string> problems)
        {
            foreach (var m in db.Raw.map)
            {
                if (m.tiles == null || m.tiles.Length == 0) { problems.Add($"map '{m.id}': tiles 가 비어 있습니다."); continue; }

                int width = m.tiles[0].Length;
                for (int r = 0; r < m.tiles.Length; r++)
                {
                    if (m.tiles[r].Length != width)
                    {
                        problems.Add($"map '{m.id}': {r}행의 길이가 {m.tiles[r].Length} 로 다른 행({width})과 다릅니다.");
                        continue;
                    }
                    foreach (var ch in m.tiles[r])
                        if (ValidTiles.IndexOf(ch) < 0)
                            problems.Add($"map '{m.id}' {r}행: 알 수 없는 타일 문자 '{ch}'. 허용: {ValidTiles}");
                }

                int goals = m.tiles.Sum(row => row.Count(ch => ch == 'E'));
                if (goals != 1) problems.Add($"map '{m.id}': 목표 타일 'E' 가 정확히 1개여야 하는데 {goals}개입니다.");

                if (m.paths == null || m.paths.Length == 0) { problems.Add($"map '{m.id}': paths 가 비어 있습니다."); continue; }
                if (m.spawnKeys == null || m.spawnKeys.Length < m.paths.Length)
                    problems.Add($"map '{m.id}': 입구 이름(spawnKeys) {m.spawnKeys?.Length ?? 0}개가 경로 {m.paths.Length}개보다 적습니다.");

                int rows = m.tiles.Length;
                for (int pi = 0; pi < m.paths.Length; pi++)
                {
                    var wp = m.paths[pi];
                    if (wp == null || wp.Length < 2) { problems.Add($"map '{m.id}' 입구 {pi + 1}: 경로 점이 2개 미만입니다."); continue; }

                    for (int j = 0; j < wp.Length; j++)
                    {
                        if (wp[j].Length < 2) { problems.Add($"map '{m.id}' 입구 {pi + 1}: {j}번째 점의 좌표가 2개가 아닙니다."); continue; }
                        int x = wp[j][0], y = wp[j][1];
                        if (x < 0 || x >= width || y < 0 || y >= rows)
                            problems.Add($"map '{m.id}' 입구 {pi + 1}: 경로 점 ({x},{y}) 이 맵({width}x{rows}) 밖입니다.");
                    }
                    // Each leg must be axis-aligned; posAt() interpolates straight lines only.
                    for (int j = 0; j < wp.Length - 1; j++)
                        if (wp[j][0] != wp[j + 1][0] && wp[j][1] != wp[j + 1][1])
                            problems.Add($"map '{m.id}' 입구 {pi + 1}: ({wp[j][0]},{wp[j][1]}) → ({wp[j + 1][0]},{wp[j + 1][1]}) 구간이 가로·세로가 아닙니다.");

                    var end = wp[^1];
                    if (end.Length >= 2 && end[1] < rows && end[0] < width && m.tiles[end[1]][end[0]] != 'E')
                        problems.Add($"map '{m.id}' 입구 {pi + 1}: 경로 끝 ({end[0]},{end[1]}) 이 목표 타일 'E' 가 아닙니다.");
                }
            }

            if (db.Stages.Count == 0) { problems.Add("stage 테이블에 스테이지가 없습니다."); return; }

            var seenOrder = new HashSet<int>();
            foreach (var st in db.Stages)
            {
                if (!seenOrder.Add(st.Order)) problems.Add($"stage {st.Id} ({st.No}): order {st.Order} 가 다른 스테이지와 겹칩니다.");
                if (st.HpMul <= 0) problems.Add($"stage {st.Id} ({st.No}): hpMul 이 0 이하입니다.");
                if (st.Schedule.Length == 0) problems.Add($"stage {st.Id} ({st.No}): 웨이브가 비어 있습니다.");
                if (!db.RewardGroups.ContainsKey(st.FirstRewardGroup))
                    problems.Add($"stage {st.Id} ({st.No}): firstRewardGroup {st.FirstRewardGroup} 에 해당하는 reward_group 이 없습니다.");
                if (st.UnlockCond != 0 && !db.Conditions.ContainsKey(st.UnlockCond))
                    problems.Add($"stage {st.Id} ({st.No}): unlockCond {st.UnlockCond} 가 condition 테이블에 없습니다.");

                foreach (var ev in st.Schedule)
                {
                    if (!db.Enemies.ContainsKey(ev.EnemyId))
                        problems.Add($"stage {st.Id} ({st.No}): wave 가 가리키는 적 '{ev.EnemyId}' 가 enemy 테이블에 없습니다.");
                    if (ev.PathIndex < 0 || ev.PathIndex >= st.Paths.Length)
                        problems.Add($"stage {st.Id} ({st.No}): wave 의 입구 번호 {ev.PathIndex + 1} 이 맵 '{st.MapId}' 의 입구 수({st.Paths.Length})를 벗어납니다.");
                }

                if (st.Boss && !st.EnemyIds.Any(id => db.Enemies.TryGetValue(id, out var e) && e.Boss))
                    problems.Add($"stage {st.Id} ({st.No}): boss=1 인데 웨이브에 보스 적이 없습니다.");

                if (!db.HasText($"chapter.{st.Chapter}.name"))
                    problems.Add($"string_ko: 'chapter.{st.Chapter}.name' 이 없습니다 (stage {st.Id}).");
            }

            // A stage must not unlock on a condition that points at a later stage.
            foreach (var st in db.Stages)
            {
                if (st.UnlockCond == 0 || !db.Conditions.TryGetValue(st.UnlockCond, out var c)) continue;
                if (c.kind != db.Enum("CondKind", "StageClear")) continue;
                if (!int.TryParse(c.p1.AsString(), out var reqId)) continue;
                var req = db.StageById(reqId);
                if (req == null) continue;
                if (req.Order >= st.Order)
                    problems.Add($"stage {st.Id} ({st.No}): 해금 조건이 자신보다 뒤(또는 같은) 스테이지 {req.No} 클리어라 영원히 열리지 않습니다.");
            }
        }

        static void ValidateRewards(GameDatabase db, List<string> problems)
        {
            int kindItem = db.Enum("RewardKind", "Item");
            int kindUnit = db.Enum("RewardKind", "Unit");

            foreach (var kv in db.RewardGroups)
                foreach (var r in kv.Value)
                {
                    if (r.prob <= 0 || r.prob > 10000)
                        problems.Add($"reward_group {r.groupId} seq {r.seq}: prob {r.prob} 이 1~10000 범위를 벗어납니다.");
                    if (r.min > r.max)
                        problems.Add($"reward_group {r.groupId} seq {r.seq}: min({r.min}) 이 max({r.max}) 보다 큽니다.");

                    if (r.kind == kindItem)
                    {
                        if (!int.TryParse(r.refId.AsString(), out var itemId) || !db.Items.ContainsKey(itemId))
                            problems.Add($"reward_group {r.groupId} seq {r.seq}: 아이템 id '{r.refId}' 가 item 테이블에 없습니다.");
                    }
                    else if (r.kind == kindUnit)
                    {
                        if (!db.Units.ContainsKey(r.refId.AsString()))
                            problems.Add($"reward_group {r.groupId} seq {r.seq}: 가전 id '{r.refId}' 가 unit 테이블에 없습니다.");
                    }
                    else problems.Add($"reward_group {r.groupId} seq {r.seq}: 알 수 없는 kind {r.kind}.");
                }

            foreach (var kv in db.StarRewards)
                foreach (var r in kv.Value)
                {
                    if (!db.RewardGroups.ContainsKey(r.rewardGroup))
                        problems.Add($"star_reward {r.id}: rewardGroup {r.rewardGroup} 이 reward_group 에 없습니다.");
                    int maxStars = db.Stages.Count(s => s.Chapter == r.chapter) * 3;
                    if (r.needStars > maxStars)
                        problems.Add($"star_reward {r.id}: 챕터 {r.chapter} 의 최대 별 {maxStars}개보다 많은 {r.needStars}개를 요구합니다.");
                }

            var starIds = new HashSet<int>();
            foreach (var kv in db.StarRewards)
                foreach (var r in kv.Value)
                    if (!starIds.Add(r.id))
                        problems.Add($"star_reward: id {r.id} 가 중복됩니다. 보상 수령 기록이 겹쳐 중복 지급될 수 있습니다.");
        }

        static void ValidateGacha(GameDatabase db, List<string> problems)
        {
            foreach (var b in db.Banners)
            {
                if (!db.Pools.TryGetValue(b.poolId, out var pool) || pool.Count == 0)
                {
                    problems.Add($"gacha {b.id}: poolId {b.poolId} 에 해당하는 gacha_pool 행이 없습니다.");
                    continue;
                }
                if (!db.Items.ContainsKey(b.costItem))
                    problems.Add($"gacha {b.id}: costItem {b.costItem} 이 item 테이블에 없습니다.");
                if (b.pulls <= 0) problems.Add($"gacha {b.id}: pulls 가 {b.pulls} 입니다. 1 이상이어야 합니다.");
                if (b.unlockCond != 0 && !db.Conditions.ContainsKey(b.unlockCond))
                    problems.Add($"gacha {b.id}: unlockCond {b.unlockCond} 가 condition 에 없습니다.");
                if (!db.HasText(b.nameKey)) problems.Add($"string_ko: '{b.nameKey}' 가 없습니다 (gacha {b.id}).");

                if (b.guaranteeRarity != 0)
                {
                    bool any = pool.Any(p => db.Units.TryGetValue(p.unitId, out var u) && (int)u.Rarity >= RarityRank(db, b.guaranteeRarity));
                    if (!any)
                        problems.Add($"gacha {b.id}: 확정 등급({b.guaranteeRarity}) 이상인 가전이 풀 {b.poolId} 에 없어 확정 뽑기가 불가능합니다.");
                }
            }

            foreach (var kv in db.Pools)
            {
                float total = 0;
                foreach (var p in kv.Value)
                {
                    if (!db.Units.ContainsKey(p.unitId))
                        problems.Add($"gacha_pool {kv.Key}: 가전 '{p.unitId}' 가 unit 테이블에 없습니다.");
                    if (p.weight <= 0)
                        problems.Add($"gacha_pool {kv.Key}: '{p.unitId}' 의 weight 가 {p.weight} 입니다. 0보다 커야 합니다.");
                    total += p.weight;
                }
                if (total <= 0) problems.Add($"gacha_pool {kv.Key}: weight 합계가 0이라 뽑기를 할 수 없습니다.");
            }
        }

        static int RarityRank(GameDatabase db, int enumValue)
        {
            if (enumValue == db.Enum("Rarity", "SSR")) return (int)Rarity.SSR;
            if (enumValue == db.Enum("Rarity", "SR")) return (int)Rarity.SR;
            return (int)Rarity.R;
        }

        static void ValidateConditions(GameDatabase db, List<string> problems)
        {
            int stageClear = db.Enum("CondKind", "StageClear");
            foreach (var c in db.Conditions.Values)
            {
                if (c.kind == stageClear)
                {
                    if (!int.TryParse(c.p1.AsString(), out var id) || db.StageById(id) == null)
                        problems.Add($"condition {c.id}: p1 '{c.p1}' 가 stage 테이블에 없습니다.");
                }
                else if (c.kind != db.Enum("CondKind", "None"))
                    problems.Add($"condition {c.id}: 알 수 없는 kind {c.kind}.");
            }

            foreach (var u in db.Unlocks.Values)
            {
                if (u.cond != 0 && !db.Conditions.ContainsKey(u.cond))
                    problems.Add($"content_unlock '{u.id}': cond {u.cond} 가 condition 에 없습니다.");
                if (!db.HasText(u.lockKey))
                    problems.Add($"string_ko: '{u.lockKey}' 가 없습니다 (content_unlock '{u.id}').");
            }

            foreach (var id in new[] { "idle", "survivor" })
                if (!db.Unlocks.ContainsKey(id))
                    problems.Add($"content_unlock: '{id}' 행이 없습니다. 게임이 해당 콘텐츠 해금 조건을 알 수 없습니다.");
        }

        static void ValidateTutorial(GameDatabase db, List<string> problems)
        {
            var ids = new HashSet<int>();
            int maxTrigger = db.Raw.enums["TutTrigger"].Values.Max();
            int maxUntil = db.Raw.enums["TutUntil"].Values.Max();
            int tSpawn = db.Enum("TutTrigger", "Spawn");
            int uSelect = db.Enum("TutUntil", "Select");
            int uDeploy = db.Enum("TutUntil", "Deploy");

            foreach (var t in db.Tutorials)
            {
                if (!ids.Add(t.id)) problems.Add($"tutorial: id {t.id} 가 중복됩니다. 본 안내가 다시 뜰 수 있습니다.");
                if (!db.HasText(t.textKey)) problems.Add($"string_ko: '{t.textKey}' 가 없습니다 (tutorial {t.id}).");
                if (t.trigger < 1 || t.trigger > maxTrigger) problems.Add($"tutorial {t.id}: trigger {t.trigger} 가 enums.TutTrigger 범위 밖입니다.");
                if (t.until < 1 || t.until > maxUntil) problems.Add($"tutorial {t.id}: until {t.until} 가 enums.TutUntil 범위 밖입니다.");

                if (t.trigger == tSpawn && !db.Enemies.ContainsKey(t.param.AsString()))
                    problems.Add($"tutorial {t.id}: trigger=적 등장 인데 param '{t.param}' 가 enemy 테이블에 없습니다.");

                if ((t.until == uSelect || t.until == uDeploy))
                {
                    var p = t.untilParam.AsString();
                    if (p != "melee" && p != "ranged" && !db.Units.ContainsKey(p))
                        problems.Add($"tutorial {t.id}: untilParam '{p}' 는 가전 id 이거나 melee/ranged 여야 합니다.");
                }

                if (!string.IsNullOrEmpty(t.focus))
                {
                    var parts = t.focus.Split(':');
                    if (parts.Length != 2) problems.Add($"tutorial {t.id}: focus '{t.focus}' 형식이 '종류:값' 이 아닙니다.");
                    else if (parts[0] == "card" && parts[1] != "melee" && parts[1] != "ranged" && !db.Units.ContainsKey(parts[1]))
                        problems.Add($"tutorial {t.id}: focus 가 가리키는 가전 '{parts[1]}' 가 unit 테이블에 없습니다.");
                }

                // 10.16-n teaches two more things: overheating, and the power skills, so cond
                // also accepts "over" and "ps:<power_skill.id>".
                bool condOk = string.IsNullOrEmpty(t.cond) || t.cond == "boss" || t.cond == "over"
                              || t.cond.StartsWith("map:") || t.cond.StartsWith("ps:");
                if (!condOk)
                    problems.Add($"tutorial {t.id}: cond '{t.cond}' 는 비우거나 'boss', 'over', " +
                                 "'map:맵ID', 'ps:전력스킬ID' 여야 합니다.");
                if (t.cond != null && t.cond.StartsWith("ps:") && !db.PowerSkills.ContainsKey(t.cond[3..]))
                    problems.Add($"tutorial {t.id}: cond 가 가리키는 전력 스킬 '{t.cond[3..]}' 가 power_skill 테이블에 없습니다.");
                if (t.cond != null && t.cond.StartsWith("map:") && db.Raw.map.All(m => m.id != t.cond[4..]))
                    problems.Add($"tutorial {t.id}: cond 가 가리키는 맵 '{t.cond[4..]}' 가 map 테이블에 없습니다.");
            }
        }

        static void ValidateSurvivor(GameDatabase db, List<string> problems)
        {
            bool anyLeader = false;
            foreach (var id in db.UnitOrder)
                if (db.HasSvSlot(id, "leader")) anyLeader = true;
            if (!anyLeader) problems.Add("surv_param: leader 슬롯을 가진 가전이 없어 대청소 모드를 시작할 수 없습니다.");

            foreach (var r in db.Raw.surv_param)
            {
                bool knownSlot = r.slot is "leader" or "comp" or "combo" or "enemy" or "passive";
                if (!knownSlot) problems.Add($"surv_param '{r.id}': 알 수 없는 slot '{r.slot}'.");
                if (r.slot == "enemy" && !db.Enemies.ContainsKey(r.id))
                    problems.Add($"surv_param: slot=enemy 인 '{r.id}' 가 enemy 테이블에 없습니다.");
                if ((r.slot == "leader" || r.slot == "comp") && !db.Units.ContainsKey(r.id))
                    problems.Add($"surv_param: slot={r.slot} 인 '{r.id}' 가 unit 테이블에 없습니다.");
            }

            foreach (var id in db.UnitOrder)
                if (db.HasSvSlot(id, "leader") && !db.HasText($"surv.leader.{id}.name"))
                    problems.Add($"string_ko: 'surv.leader.{id}.name' 이 없습니다.");

            foreach (var c in db.SurvCombos)
            {
                if (!db.Units.ContainsKey(c.leader)) problems.Add($"surv_combo '{c.id}': leader '{c.leader}' 가 unit 에 없습니다.");
                if (!db.Units.ContainsKey(c.comp)) problems.Add($"surv_combo '{c.id}': comp '{c.comp}' 가 unit 에 없습니다.");
                if (!db.HasSvSlot(c.id, "combo")) problems.Add($"surv_combo '{c.id}': surv_param 에 slot=combo 인 수치가 없습니다.");
                if (!db.HasText(c.nameKey)) problems.Add($"string_ko: '{c.nameKey}' 가 없습니다 (surv_combo '{c.id}').");
                if (!db.HasText(c.descKey)) problems.Add($"string_ko: '{c.descKey}' 가 없습니다 (surv_combo '{c.id}').");
            }

            foreach (var s in db.SurvSets)
            {
                if (s.need <= 0) problems.Add($"surv_set (계열 {s.family}): need 는 1 이상이어야 합니다.");
                if (s.key is not ("pickupMul" or "dmgMul" or "cdMul" or "enemySlow"))
                    problems.Add($"surv_set (계열 {s.family}): 알 수 없는 효과 key '{s.key}'.");
                if (!db.HasText(s.descKey)) problems.Add($"string_ko: '{s.descKey}' 가 없습니다 (surv_set 계열 {s.family}).");
            }

            foreach (var w in db.SurvWaves)
            {
                if (!db.Enemies.ContainsKey(w.enemy))
                    problems.Add($"surv_wave {w.id}: 적 '{w.enemy}' 가 enemy 테이블에 없습니다.");
                if (w.burst == 0 && w.t1 <= w.t0)
                    problems.Add($"surv_wave {w.id}: t1({w.t1}) 이 t0({w.t0}) 보다 커야 합니다.");
                if (w.burst == 0 && w.rate0 <= 0 && w.rate1 <= 0)
                    problems.Add($"surv_wave {w.id}: rate0/rate1 이 모두 0이라 적이 나오지 않습니다.");
            }

            foreach (var key in new[] { "move", "hp" })
            {
                if (!db.HasSvSlot(key, "passive"))
                    problems.Add($"surv_param: passive 슬롯에 '{key}' 가 없습니다.");
                else if (db.Sv(key, "passive", "max") <= 0)
                    problems.Add($"surv_param: passive '{key}' 의 max 가 0이라 강화 선택지가 뜨지 않습니다.");
                if (!db.HasText($"surv.passive.{key}"))
                    problems.Add($"string_ko: 'surv.passive.{key}' 가 없습니다.");
            }
            // 10.15-h removed the heal card; a spent card pool now pays bonus coin instead.
            if (!db.HasSvSlot("bonus", "passive"))
                problems.Add("surv_param: passive 슬롯에 'bonus' 가 없습니다. 뽑을 강화를 다 모았을 때 주는 보너스입니다.");
            if (!db.HasText("surv.passive.bonus"))
                problems.Add("string_ko: 'surv.passive.bonus' 가 없습니다.");

            // surv_reward was removed in 10.18-h; the daily log replaced it.
            if (db.DailyPool.Count == 0)
                problems.Add("daily_pool 테이블이 비어 있습니다. 청소 일지 상자를 줄 수 없습니다.");
            for (int i = 0; i < db.DailyPool.Count; i++)
            {
                var r = db.DailyPool[i];
                if (r.pts <= 0) problems.Add($"daily_pool {r.box}번: pts 가 {r.pts} 입니다. 0보다 커야 합니다.");
                if (r.crystal <= 0) problems.Add($"daily_pool {r.box}번: crystal 이 {r.crystal} 입니다.");
                if (i > 0 && r.pts <= db.DailyPool[i - 1].pts)
                    problems.Add($"daily_pool: {r.pts}점 상자가 앞 상자보다 크지 않습니다.");
            }
            if (db.Config.poolSurvPerMin <= 0)
                problems.Add($"config.poolSurvPerMin 이 {db.Config.poolSurvPerMin} 입니다. 대청소가 일지 점수를 못 줍니다.");

            CheckLeagueAndTower(db, problems);
            CheckCoop(db, problems);

        }
    
        /// <summary>
        /// 리그와 먼지 타워. 둘은 전투판을 공유하므로 타워 층이 가리키는 적·규칙이
        /// 리그 표에 실제로 있는지 같이 본다 — 없으면 그 층에서 조용히 빈 판이 된다.
        /// </summary>
        static void CheckLeagueAndTower(GameDatabase db, List<string> problems)
        {
            if (db.LeagueUnits.Count == 0) problems.Add("league_unit 테이블이 비어 있습니다.");
            if (db.LeagueEnemies.Count == 0) problems.Add("league_enemy 테이블이 비어 있습니다.");
            if (db.LeagueTiers.Count == 0) problems.Add("league_tier 테이블이 비어 있습니다.");
            if (db.LeagueRounds.Count == 0) problems.Add("league_round 테이블이 비어 있습니다.");

            // 리그에 나오는 가전은 본편 unit 에도 있어야 이름·그림을 붙일 수 있다.
            foreach (var u in db.LeagueUnits.Values)
                if (!db.Units.ContainsKey(u.id))
                    problems.Add($"league_unit '{u.id}' 이 unit 테이블에 없습니다.");

            foreach (var r in db.LeagueRounds)
                CheckSquad(db, r.squad, $"league_round {r.round}", problems);

            if (db.TowerFloors.Count == 0)
            {
                problems.Add("tower_floor 테이블이 비어 있습니다.");
                return;
            }

            int expected = 1;
            foreach (var f in db.TowerFloors)
            {
                if (f.floor != expected)
                    problems.Add($"tower_floor: {expected}층이 비었거나 순서가 어긋납니다 (받은 값 {f.floor}).");
                expected = f.floor + 1;

                if (f.slots <= 0) problems.Add($"tower_floor {f.floor}층: slots 가 {f.slots} 입니다.");
                if (f.hpMul <= 0f || f.atkMul <= 0f)
                    problems.Add($"tower_floor {f.floor}층: hpMul/atkMul 이 0 이하입니다.");

                CheckSquad(db, f.squad, $"tower_floor {f.floor}층", problems);

                if (!string.IsNullOrEmpty(f.rule) && !db.TowerRules.ContainsKey(f.rule))
                    problems.Add($"tower_floor {f.floor}층: 규칙 '{f.rule}' 이 tower_rule 에 없습니다.");

                if (string.IsNullOrEmpty(f.bonus)) continue;
                bool known = false;
                foreach (var b in db.TowerBonuses) if (b.id == f.bonus) known = true;
                if (!known)
                    problems.Add($"tower_floor {f.floor}층: 보너스 '{f.bonus}' 가 tower_bonus 에 없습니다.");
            }
        }

        /// <summary>"dust*3, mite*2" 꼴의 편성 문자열이 실제 적을 가리키는지 본다.</summary>
        static void CheckSquad(GameDatabase db, string squad, string where, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(squad)) return;
            foreach (var part in squad.Split(','))
            {
                var piece = part.Trim();
                if (piece.Length == 0) continue;
                var bits = piece.Split('*');
                var id = bits[0].Trim();
                if (!db.LeagueEnemies.ContainsKey(id))
                    problems.Add($"{where}: 적 '{id}' 이 league_enemy 에 없습니다.");
                if (bits.Length > 1 && (!int.TryParse(bits[1].Trim(), out var n) || n <= 0))
                    problems.Add($"{where}: '{piece}' 의 마릿수를 읽지 못했습니다.");
            }
        }

        /// <summary>
        /// 협동. 파트너가 들고 나오는 가전이 실제로 있어야 하고, 고리 판은 근접·원거리
        /// 칸이 둘 다 있어야 한다 — 한쪽이 없으면 그 자리 소환이 영영 안 된다.
        /// </summary>
        static void CheckCoop(GameDatabase db, List<string> problems)
        {
            if (db.CoopPartners.Count == 0)
            {
                problems.Add("coop_partner 테이블이 비어 있습니다.");
                return;
            }
            foreach (var p in db.CoopPartners)
            {
                if (p.style != "rush" && p.style != "greedy")
                    problems.Add($"coop_partner '{p.id}': style 가 '{p.style}' 입니다. rush 또는 greedy 여야 합니다.");
                if (!db.Units.ContainsKey(p.unit ?? ""))
                    problems.Add($"coop_partner '{p.id}': 얼굴 가전 '{p.unit}' 이 unit 에 없습니다.");

                int ring = 0, furniture = 0;
                foreach (var raw in (p.deck ?? "").Split(','))
                {
                    var id = raw.Trim();
                    if (id.Length == 0) continue;
                    if (!db.Units.TryGetValue(id, out var u))
                    {
                        problems.Add($"coop_partner '{p.id}': 편성 가전 '{id}' 이 unit 에 없습니다.");
                        continue;
                    }
                    if (u.Tile == TileKind.Ground) ring++; else furniture++;
                }
                if (ring == 0) problems.Add($"coop_partner '{p.id}': 근접 가전이 없어 고리 칸을 못 채웁니다.");
                if (furniture == 0) problems.Add($"coop_partner '{p.id}': 원거리 가전이 없어 가구 칸을 못 채웁니다.");
            }

            int p_cells = 0, h_cells = 0;
            foreach (var row in GameDatabase.CoopMap)
                foreach (var ch in row)
                {
                    if (ch == 'P') p_cells++;
                    else if (ch == 'H') h_cells++;
                }
            if (p_cells == 0 || h_cells == 0)
                problems.Add($"협동 판에 고리 칸 {p_cells}개 · 가구 칸 {h_cells}개 입니다. 둘 다 있어야 합니다.");

            if (db.Config.coopLoopMax <= 0)
                problems.Add($"config.coopLoopMax 가 {db.Config.coopLoopMax} 입니다. 판이 끝나지 않습니다.");
            if (db.Config.coopStarMul == null || db.Config.coopStarMul.Length < db.Config.coopStarMax)
                problems.Add("config.coopStarMul 이 coopStarMax 만큼의 배율을 갖고 있지 않습니다.");
        }
}
}
