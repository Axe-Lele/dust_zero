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
        /// <summary>skill_param keys each unit's behaviour reads. Missing ones are hard errors.</summary>
        static readonly Dictionary<string, string[]> RequiredSkillParams = new()
        {
            ["vac"] = new[] { "pullRadius", "pullDps", "pullSpeed", "holdDist" },
            ["robot"] = new[] { "triggerHeat", "skillAtkBonus" },
            ["fridge"] = new[] { "freezeRadius", "freezeDps", "selfCool" },
            ["ac"] = new[] { "tick", "skillDmg", "skillSlow", "skillSlowSec", "atkSlow", "atkSlowSec" },
            ["micro"] = new[] { "rangeBonus", "blastRadius", "blastDmg", "noTargetRefund" },
            ["washer"] = new[] { "tick", "skillDmg", "knockback", "bossKnockback", "splashRadius" },
            ["pc"] = new[] { "buffRange", "buffAtk", "skillRange", "skillAtk", "skillIntervalMul" },
            ["fan"] = new[] { "coolRange", "skillRange", "skillCoolMul", "skillBurst", "triggerHeat" },
            ["gen"] = new[] { "regenPerSec", "skillRegenMul", "burst" },
        };

        /// <summary>Tile characters the map grid may contain.</summary>
        const string ValidTiles = "XHPFWE";

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
                if (kv.Key != "first" && kv.Key != "levelup")
                    problems.Add($"guide: 알 수 없는 flow '{kv.Key}'. first 또는 levelup 이어야 합니다.");
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

                if (RequiredSkillParams.TryGetValue(id, out var keys))
                    foreach (var k in keys)
                        if (!db.HasSkp(id, k))
                            problems.Add($"skill_param: '{id}' 의 '{k}' 값이 없습니다. (02_Unit.xlsx 의 skill_param 시트)");

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

                if (!string.IsNullOrEmpty(t.cond) && t.cond != "boss" && !t.cond.StartsWith("map:"))
                    problems.Add($"tutorial {t.id}: cond '{t.cond}' 는 비우거나 'boss' 또는 'map:맵ID' 여야 합니다.");
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

            if (db.SurvRewards.Count == 0)
                problems.Add("surv_reward 테이블이 비어 있습니다. 생존 시간 보상을 줄 수 없습니다.");
            for (int i = 0; i < db.SurvRewards.Count; i++)
            {
                var r = db.SurvRewards[i];
                if (r.sec <= 0) problems.Add($"surv_reward: sec 가 {r.sec} 입니다. 0보다 커야 합니다.");
                if (r.crystal <= 0) problems.Add($"surv_reward {r.sec}초: crystal 이 {r.crystal} 입니다.");
                if (i > 0 && r.sec <= db.SurvRewards[i - 1].sec)
                    problems.Add($"surv_reward: {r.sec}초 구간이 앞 구간보다 크지 않습니다.");
            }
        }
    }
}
