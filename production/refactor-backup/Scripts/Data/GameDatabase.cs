// Indexed, validated view over the raw tables - the port of the original loadTables().
//
// Everything the simulation needs is derived once here: unit/enemy definitions with their
// localized strings resolved, stage schedules expanded from wave rows, skill parameters
// flattened into a (unitId, key) lookup, and skill descriptions filled in from those same
// parameters so a number edited in 02_Unit.xlsx changes both the behaviour and its wording.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DustZero.Data
{
    public enum Rarity { R = 0, SR = 1, SSR = 2 }

    /// <summary>Which tile a unit stands on. Mirrors enums.Tile.</summary>
    public enum TileKind { Ground = 1, Furniture = 2 }

    /// <summary>What an attack may hit. Mirrors enums.TargetType.</summary>
    public enum TargetType { Ground = 1, Air = 2, All = 3 }

    public sealed class UnitDef
    {
        public string Id;
        public int Order;
        public Rarity Rarity;
        public int Family;
        public TileKind Tile;
        public float Cost;
        public float Heat;
        public float Atk;
        public float Interval;
        public float Range;
        public int Block;
        public float SkillCharge;
        public float SkillDuration;
        public float Cool;
        public TargetType AtkTarget;
        public TargetType SkillTarget;

        public string Name;
        public string ShortName;
        public string Role;
        public string SkillName;
        /// <summary>Skill description with {param} placeholders already substituted.</summary>
        public string SkillDesc;
        public string[] Lines;

        public bool IsMelee => Tile == TileKind.Ground;
    }

    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public float Hp;
        public float Speed;
        /// <summary>Heat inflicted on the blocking unit per attack.</summary>
        public float Damage;
        public float Interval;
        public float Radius;
        /// <summary>Block slots consumed and cleanliness lost on leak. 0 in the table means 1.</summary>
        public int Weight;
        public bool Fly;
        public string Look;
        public bool Boss;
        /// <summary>Breakers ignore detours and push through whatever blocks them.</summary>
        public bool Breaker;

        public int LeakCost => Weight > 0 ? Weight : 1;
    }

    /// <summary>One scheduled spawn, expanded from a wave row's count/gap.</summary>
    public readonly struct SpawnEvent
    {
        public readonly float Time;
        public readonly string EnemyId;
        /// <summary>Zero-based entrance index into <see cref="StageDef.Paths"/>.</summary>
        public readonly int PathIndex;
        /// <summary>
        /// This wave's own HP multiplier, applied on top of the stage multiplier. Late waves
        /// ramp toward 3 so a stalled defence eventually gives way instead of holding forever.
        /// </summary>
        public readonly float WaveHpMul;
        /// <summary>
        /// 1-based wave this spawn belongs to, from the table's seq. The battle HUD counts
        /// waves for the player; the simulation itself only cares about the times.
        /// </summary>
        public readonly int Wave;

        public SpawnEvent(float time, string enemyId, int pathIndex, float waveHpMul, int wave = 1)
        {
            Time = time; EnemyId = enemyId; PathIndex = pathIndex; WaveHpMul = waveHpMul; Wave = wave;
        }
    }

    /// <summary>A single entrance route, pre-measured so distance along it maps to a position.</summary>
    public sealed class PathDef
    {
        public readonly struct Segment
        {
            public readonly int X1, Y1, X2, Y2;
            public readonly float Length;
            public readonly float Start;
            public Segment(int x1, int y1, int x2, int y2, float length, float start)
            {
                X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Length = length; Start = start;
            }
        }

        public Segment[] Segments;
        public float Length;
        public int StartX, StartY;

        public static PathDef Build(int[][] waypoints)
        {
            var segs = new List<Segment>();
            float total = 0f;
            for (int j = 0; j < waypoints.Length - 1; j++)
            {
                int x1 = waypoints[j][0], y1 = waypoints[j][1];
                int x2 = waypoints[j + 1][0], y2 = waypoints[j + 1][1];
                float len = Math.Abs(x2 - x1) + Math.Abs(y2 - y1);
                segs.Add(new Segment(x1, y1, x2, y2, len, total));
                total += len;
            }
            return new PathDef
            {
                Segments = segs.ToArray(),
                Length = total,
                StartX = waypoints[0][0],
                StartY = waypoints[0][1],
            };
        }
    }

    public sealed class StageDef
    {
        public int Id;
        public int Order;
        public int Chapter;
        public string No;
        public string MapId;
        public string Name;
        public string Floor;
        public string[] SpawnNames;
        public string GoalName;
        public string Gimmick;
        public string Desc;
        /// <summary>Row-major tile grid; Tiles[row][col]. X wall, H furniture, P floor, F stove, W wet, E goal.</summary>
        public string[] Tiles;
        public PathDef[] Paths;
        public int NoDeployFromEntrance;
        public int NoDeployBeforeGoal;
        public SpawnEvent[] Schedule;
        public float HpMul;
        public bool Boss;
        public string[] EnemyIds;
        public int UnlockCond;
        public int FirstRewardGroup;
        public int IdleCoin;
        public int IdlePart;
        public int IdleCrystal;
        /// <summary>Starting power for this stage; falls back to config.startPower when 0.</summary>
        public float StartPower;

        public int GoalX, GoalY;
        public int Cols => Tiles[0].Length;
        public int Rows => Tiles.Length;
    }

    /// <summary>
    /// Thrown when the tables are internally inconsistent. The message names the table, the
    /// offending row and what was expected, so a designer can fix the spreadsheet directly.
    /// </summary>
    public sealed class TableValidationException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public TableValidationException(IReadOnlyList<string> problems)
            : base("게임 데이터 검증 실패 (" + problems.Count + "건):\n  - " + string.Join("\n  - ", problems))
        {
            Problems = problems;
        }
    }

    public sealed class GameDatabase
    {
        public RawTables Raw { get; private set; }
        public ConfigRow Config => Raw.config;

        public IReadOnlyDictionary<string, UnitDef> Units => _units;
        public IReadOnlyList<string> UnitOrder => _unitOrder;
        public IReadOnlyDictionary<string, EnemyDef> Enemies => _enemies;
        public IReadOnlyList<StageDef> Stages => _stages;
        public IReadOnlyDictionary<int, ItemRow> Items => _items;
        /// <summary>family -> part item id.</summary>
        public IReadOnlyDictionary<int, int> PartOfFamily => _partOfFamily;
        public IReadOnlyDictionary<int, List<RewardGroupRow>> RewardGroups => _rewardGroups;
        public IReadOnlyDictionary<int, ConditionRow> Conditions => _conditions;
        public IReadOnlyDictionary<string, ContentUnlockRow> Unlocks => _unlocks;
        public IReadOnlyDictionary<int, List<StarRewardRow>> StarRewards => _starRewards;
        public IReadOnlyDictionary<int, List<GachaPoolRow>> Pools => _pools;
        public IReadOnlyList<GachaRow> Banners => Raw.gacha;
        public IReadOnlyList<TutorialRow> Tutorials => _tutorials;
        public IReadOnlyList<SurvComboRow> SurvCombos => Raw.surv_combo;
        public IReadOnlyList<SurvSetRow> SurvSets => Raw.surv_set;
        public IReadOnlyList<SurvWaveRow> SurvWaves => Raw.surv_wave;

        /// <summary>Survival crystal marks, ascending by seconds.</summary>
        public IReadOnlyList<SurvRewardRow> SurvRewards => _survRewards;
        /// <summary>Limit-break steps, ascending by star.</summary>
        public IReadOnlyList<UnitStarRow> UnitStars => _unitStars;
        /// <summary>Attendance wheel, ascending by day.</summary>
        public IReadOnlyList<AttendanceRow> Attendance => _attendance;
        /// <summary>Season pass rows, ascending by level.</summary>
        public IReadOnlyList<PassLevelRow> PassLevels => _passLevels;
        /// <summary>Shop products, ordered by tab then order.</summary>
        public IReadOnlyList<ShopProductRow> ShopProducts => _shopProducts;
        /// <summary>Onboarding flows keyed by flow name ("first", "levelup"), ascending by seq.</summary>
        public IReadOnlyDictionary<string, List<GuideRow>> Guides => _guides;
        /// <summary>Shard item id for an appliance, from item.unitId.</summary>
        public IReadOnlyDictionary<string, int> ShardOfUnit => _shardOfUnit;

        readonly Dictionary<string, UnitDef> _units = new();
        readonly List<string> _unitOrder = new();
        readonly Dictionary<string, EnemyDef> _enemies = new();
        readonly List<StageDef> _stages = new();
        readonly Dictionary<int, ItemRow> _items = new();
        readonly Dictionary<int, int> _partOfFamily = new();
        readonly Dictionary<int, List<RewardGroupRow>> _rewardGroups = new();
        readonly Dictionary<int, ConditionRow> _conditions = new();
        readonly Dictionary<string, ContentUnlockRow> _unlocks = new();
        readonly Dictionary<int, List<StarRewardRow>> _starRewards = new();
        readonly Dictionary<int, List<GachaPoolRow>> _pools = new();
        readonly List<TutorialRow> _tutorials = new();
        readonly Dictionary<string, Dictionary<string, float>> _skillParams = new();
        readonly Dictionary<string, Dictionary<string, Dictionary<string, float>>> _survParams = new();
        readonly Dictionary<string, Dictionary<string, int>> _enums = new();
        readonly Dictionary<int, StageDef> _stagesById = new();
        readonly List<SurvRewardRow> _survRewards = new();
        readonly List<UnitStarRow> _unitStars = new();
        readonly List<AttendanceRow> _attendance = new();
        readonly List<PassLevelRow> _passLevels = new();
        readonly List<ShopProductRow> _shopProducts = new();
        readonly Dictionary<string, List<GuideRow>> _guides = new();
        readonly Dictionary<string, int> _shardOfUnit = new();
        readonly Dictionary<string, Dictionary<int, UnitBoostRow>> _unitBoosts = new();

        /// <summary>In-battle upgrade row for a unit at a tier (2 or 3), or null.</summary>
        public UnitBoostRow Boost(string unitId, int tier)
            => _unitBoosts.TryGetValue(unitId, out var m) && m.TryGetValue(tier, out var r) ? r : null;

        /// <summary>Highest boost tier defined for a unit; 1 means it cannot be upgraded.</summary>
        public int MaxBoostTier(string unitId)
            => _unitBoosts.TryGetValue(unitId, out var m) && m.Count > 0 ? m.Keys.Max() : 1;

        /// <summary>Limit-break step for reaching <paramref name="star"/>, or null past the table.</summary>
        public UnitStarRow StarStep(int star)
            => _unitStars.FirstOrDefault(r => r.star == star);

        /// <summary>Level cap at a given limit-break count.</summary>
        public int LevelCap(int star)
        {
            var row = _unitStars.Where(r => r.star <= star).OrderByDescending(r => r.star).FirstOrDefault();
            return row?.lvCap ?? Config.lvCapBase;
        }

        public ShopProductRow ShopProduct(int id) => _shopProducts.FirstOrDefault(p => p.id == id);

        /// <summary>Localized string lookup; returns the key itself when missing, as the original did.</summary>
        public string Text(string key)
            => key != null && Raw.string_ko != null && Raw.string_ko.TryGetValue(key, out var v) ? v : key;

        public bool HasText(string key) => key != null && Raw.string_ko != null && Raw.string_ko.ContainsKey(key);

        public int Enum(string enumName, string member)
            => _enums.TryGetValue(enumName, out var e) && e.TryGetValue(member, out var v) ? v : 0;

        /// <summary>skill_param lookup. Missing keys are a validation failure, not a silent zero.</summary>
        public float Skp(string unitId, string key)
            => _skillParams.TryGetValue(unitId, out var m) && m.TryGetValue(key, out var v) ? v : 0f;

        public bool HasSkp(string unitId, string key)
            => _skillParams.TryGetValue(unitId, out var m) && m.ContainsKey(key);

        public IReadOnlyDictionary<string, float> SkillParamsOf(string unitId)
            => _skillParams.TryGetValue(unitId, out var m) ? m : EmptyFloats;

        /// <summary>surv_param lookup by (id, slot, key).</summary>
        public float Sv(string id, string slot, string key)
            => _survParams.TryGetValue(id, out var a) && a.TryGetValue(slot, out var b) && b.TryGetValue(key, out var v) ? v : 0f;

        public bool HasSvSlot(string id, string slot)
            => _survParams.TryGetValue(id, out var a) && a.ContainsKey(slot);

        public IReadOnlyDictionary<string, float> SurvParamsOf(string id, string slot)
            => _survParams.TryGetValue(id, out var a) && a.TryGetValue(slot, out var b) ? b : EmptyFloats;

        static readonly Dictionary<string, float> EmptyFloats = new();

        public StageDef StageById(int id) => _stagesById.TryGetValue(id, out var s) ? s : null;
        public StageDef StageByOrder(int order) => _stages.FirstOrDefault(s => s.Order == order);

        public UnitDef Unit(string id) => _units.TryGetValue(id, out var u) ? u : null;
        public EnemyDef Enemy(string id) => _enemies.TryGetValue(id, out var e) ? e : null;

        /// <summary>Builds the indexed database, then validates it. Throws <see cref="TableValidationException"/>.</summary>
        public static GameDatabase Build(RawTables raw)
        {
            var db = new GameDatabase { Raw = raw };
            var problems = new List<string>();

            db.CheckRequiredTables(problems);
            if (problems.Count > 0) throw new TableValidationException(problems);

            db.IndexEnums();
            db.IndexItems(problems);
            db.IndexSkillParams();
            db.IndexUnits(problems);
            db.IndexEnemies(problems);
            db.IndexLookups();
            db.IndexStages(problems);
            db.IndexSurvParams();
            db.IndexNewTables(problems);

            TableValidator.Validate(db, problems);
            if (problems.Count > 0) throw new TableValidationException(problems);
            return db;
        }

        void CheckRequiredTables(List<string> problems)
        {
            void Need(object table, string name)
            {
                if (table == null) problems.Add($"{name}.json 테이블이 비어 있거나 읽히지 않았습니다.");
            }
            Need(Raw.enums, "enums"); Need(Raw.config, "config"); Need(Raw.string_ko, "string_ko");
            Need(Raw.item, "item"); Need(Raw.unit, "unit"); Need(Raw.unit_levelup, "unit_levelup");
            Need(Raw.enemy, "enemy"); Need(Raw.map, "map"); Need(Raw.stage, "stage"); Need(Raw.wave, "wave");
            Need(Raw.reward_group, "reward_group"); Need(Raw.condition, "condition");
            Need(Raw.content_unlock, "content_unlock"); Need(Raw.star_reward, "star_reward");
            Need(Raw.skill_param, "skill_param"); Need(Raw.gacha, "gacha"); Need(Raw.gacha_pool, "gacha_pool");
            Need(Raw.tutorial, "tutorial"); Need(Raw.surv_param, "surv_param");
            Need(Raw.surv_combo, "surv_combo"); Need(Raw.surv_set, "surv_set"); Need(Raw.surv_wave, "surv_wave");
        }

        void IndexEnums()
        {
            foreach (var kv in Raw.enums) _enums[kv.Key] = kv.Value;
        }

        void IndexItems(List<string> problems)
        {
            foreach (var it in Raw.item)
            {
                if (_items.ContainsKey(it.id)) { problems.Add($"item: id {it.id} 가 중복됩니다."); continue; }
                _items[it.id] = it;
                if (it.family != 0)
                {
                    if (_partOfFamily.ContainsKey(it.family))
                        problems.Add($"item: 계열 {it.family} 의 부품이 둘 이상입니다 (id {_partOfFamily[it.family]}, {it.id}).");
                    else _partOfFamily[it.family] = it.id;
                }
                if (!string.IsNullOrEmpty(it.unitId))
                {
                    if (_shardOfUnit.ContainsKey(it.unitId))
                        problems.Add($"item: 가전 '{it.unitId}' 의 조각 아이템이 둘 이상입니다 (id {_shardOfUnit[it.unitId]}, {it.id}).");
                    else _shardOfUnit[it.unitId] = it.id;
                }
            }
        }

        /// <summary>Indexes the tables added in 10.15-h.</summary>
        void IndexNewTables(List<string> problems)
        {
            _unitStars.AddRange((Raw.unit_star ?? new List<UnitStarRow>()).OrderBy(r => r.star));
            _survRewards.AddRange((Raw.surv_reward ?? new List<SurvRewardRow>()).OrderBy(r => r.sec));
            _attendance.AddRange((Raw.attendance ?? new List<AttendanceRow>()).OrderBy(r => r.day));
            _passLevels.AddRange((Raw.pass_level ?? new List<PassLevelRow>()).OrderBy(r => r.level));
            _shopProducts.AddRange((Raw.shop_product ?? new List<ShopProductRow>())
                .OrderBy(r => r.tab).ThenBy(r => r.order));

            foreach (var b in Raw.unit_boost ?? new List<UnitBoostRow>())
            {
                if (!_unitBoosts.TryGetValue(b.unitId, out var m))
                    _unitBoosts[b.unitId] = m = new Dictionary<int, UnitBoostRow>();
                if (m.ContainsKey(b.tier))
                    problems.Add($"unit_boost: '{b.unitId}' 의 {b.tier}단계가 중복됩니다.");
                else m[b.tier] = b;
            }

            foreach (var g in (Raw.guide ?? new List<GuideRow>()).OrderBy(r => r.seq))
            {
                var flow = g.flow ?? string.Empty;
                if (!_guides.TryGetValue(flow, out var list))
                    _guides[flow] = list = new List<GuideRow>();
                list.Add(g);
            }
        }

        void IndexSkillParams()
        {
            foreach (var r in Raw.skill_param)
            {
                if (!_skillParams.TryGetValue(r.unitId, out var m))
                    _skillParams[r.unitId] = m = new Dictionary<string, float>();
                m[r.key] = r.value;
            }
        }

        void IndexSurvParams()
        {
            foreach (var r in Raw.surv_param)
            {
                if (!_survParams.TryGetValue(r.id, out var a))
                    _survParams[r.id] = a = new Dictionary<string, Dictionary<string, float>>();
                if (!a.TryGetValue(r.slot, out var b))
                    a[r.slot] = b = new Dictionary<string, float>();
                b[r.key] = r.value;
            }
        }

        static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

        void IndexUnits(List<string> problems)
        {
            int ground = Enum("Tile", "Ground");
            foreach (var u in Raw.unit.OrderBy(x => x.order))
            {
                if (_units.ContainsKey(u.id)) { problems.Add($"unit: id '{u.id}' 가 중복됩니다."); continue; }
                _unitOrder.Add(u.id);

                var def = new UnitDef
                {
                    Id = u.id,
                    Order = u.order,
                    Rarity = RarityOf(u.rarity),
                    Family = u.family,
                    Tile = u.tile == ground ? TileKind.Ground : TileKind.Furniture,
                    Cost = u.cost,
                    Heat = u.heat,
                    Atk = u.atk,
                    Interval = u.itv,
                    Range = u.range,
                    Block = u.block,
                    SkillCharge = u.sp,
                    SkillDuration = u.dur,
                    Cool = u.cool,
                    AtkTarget = (TargetType)(u.atkTarget == 0 ? 3 : u.atkTarget),
                    SkillTarget = (TargetType)(u.skillTarget == 0 ? 3 : u.skillTarget),
                    Name = Text(u.nameKey),
                    ShortName = Text(u.shortKey),
                    Role = Text(u.roleKey),
                    SkillName = Text(u.skillKey),
                    SkillDesc = Text(u.descKey),
                };
                def.Lines = System.Linq.Enumerable
                    .Range(1, Math.Max(0, u.lineCount))
                    .Select(i => Text($"unit.{u.id}.line{i}"))
                    .ToArray();

                _units[u.id] = def;
            }

            // Fill {placeholders} in skill descriptions from skill_param, exactly as the
            // original did, so the wording tracks the spreadsheet numbers.
            foreach (var id in _unitOrder)
            {
                var def = _units[id];
                var ctx = new Dictionary<string, float>
                {
                    ["dur"] = def.SkillDuration,
                    ["cool"] = def.Cool,
                };
                foreach (var kv in SkillParamsOf(id))
                {
                    ctx[kv.Key] = kv.Value;
                    ctx[kv.Key + "_pct"] = (float)Math.Round(kv.Value * 100);
                    ctx[kv.Key + "_w"] = (float)Math.Round(kv.Value * 100);
                }
                if (ctx.TryGetValue("skillIntervalMul", out var sim) && Math.Abs(sim) > 1e-6f)
                    ctx["speedMul"] = 1f / sim;

                def.SkillDesc = Placeholder.Replace(def.SkillDesc ?? string.Empty, m =>
                    ctx.TryGetValue(m.Groups[1].Value, out var v)
                        ? Round2(v)
                        : m.Value);
            }
        }

        /// <summary>Matches the original's +(+v).toFixed(2): two decimals with trailing zeros dropped.</summary>
        public static string Round2(float v)
        {
            var r = (float)Math.Round(v, 2, MidpointRounding.AwayFromZero);
            return r.ToString("0.##", CultureInfo.InvariantCulture);
        }

        Rarity RarityOf(int value)
        {
            if (value == Enum("Rarity", "SSR")) return Rarity.SSR;
            if (value == Enum("Rarity", "SR")) return Rarity.SR;
            return Rarity.R;
        }

        void IndexEnemies(List<string> problems)
        {
            int breaker = Enum("EnemyAI", "Breaker");
            foreach (var e in Raw.enemy)
            {
                if (_enemies.ContainsKey(e.id)) { problems.Add($"enemy: id '{e.id}' 가 중복됩니다."); continue; }
                _enemies[e.id] = new EnemyDef
                {
                    Id = e.id,
                    Name = Text(e.nameKey),
                    Hp = e.hp,
                    Speed = e.spd,
                    Damage = e.dmg,
                    Interval = e.itv,
                    Radius = e.radius,
                    Weight = e.weight > 1 ? e.weight : 0,
                    Fly = e.fly != 0,
                    Look = e.look,
                    Boss = e.boss != 0,
                    Breaker = e.ai == breaker,
                };
            }
        }

        void IndexLookups()
        {
            foreach (var r in Raw.reward_group)
            {
                if (!_rewardGroups.TryGetValue(r.groupId, out var list))
                    _rewardGroups[r.groupId] = list = new List<RewardGroupRow>();
                list.Add(r);
            }
            foreach (var list in _rewardGroups.Values) list.Sort((a, b) => a.seq.CompareTo(b.seq));

            foreach (var c in Raw.condition) _conditions[c.id] = c;
            foreach (var u in Raw.content_unlock) _unlocks[u.id] = u;

            foreach (var p in Raw.gacha_pool)
            {
                if (!_pools.TryGetValue(p.poolId, out var list))
                    _pools[p.poolId] = list = new List<GachaPoolRow>();
                list.Add(p);
            }

            foreach (var r in Raw.star_reward)
            {
                if (!_starRewards.TryGetValue(r.chapter, out var list))
                    _starRewards[r.chapter] = list = new List<StarRewardRow>();
                list.Add(r);
            }
            foreach (var list in _starRewards.Values) list.Sort((a, b) => a.needStars.CompareTo(b.needStars));

            // Unconditional groups open one per battle; sort by (group, seq) as the original did.
            _tutorials.AddRange(Raw.tutorial
                .OrderBy(r => r.group).ThenBy(r => r.seq)
                .Select(r =>
                {
                    r.cond ??= string.Empty;
                    r.focus ??= string.Empty;
                    return r;
                }));
        }

        void IndexStages(List<string> problems)
        {
            var maps = new Dictionary<string, MapRow>();
            foreach (var m in Raw.map)
            {
                if (maps.ContainsKey(m.id)) { problems.Add($"map: id '{m.id}' 가 중복됩니다."); continue; }
                maps[m.id] = m;
            }

            var wavesByGroup = new Dictionary<int, List<WaveRow>>();
            foreach (var w in Raw.wave)
            {
                if (!wavesByGroup.TryGetValue(w.groupId, out var list))
                    wavesByGroup[w.groupId] = list = new List<WaveRow>();
                list.Add(w);
            }

            foreach (var st in Raw.stage.OrderBy(s => s.order))
            {
                if (!maps.TryGetValue(st.mapId, out var m))
                {
                    problems.Add($"stage {st.id} ({st.no}): mapId '{st.mapId}' 가 map 테이블에 없습니다.");
                    continue;
                }
                if (!wavesByGroup.TryGetValue(st.waveGroup, out var waves))
                {
                    problems.Add($"stage {st.id} ({st.no}): waveGroup {st.waveGroup} 에 해당하는 wave 행이 없습니다.");
                    continue;
                }
                if (m.paths == null || m.paths.Length == 0)
                {
                    problems.Add($"map '{m.id}': 경로(paths)가 비어 있습니다.");
                    continue;
                }

                var sched = new List<SpawnEvent>();
                int waveNo = 0;
                foreach (var w in waves.OrderBy(x => x.seq))
                {
                    waveNo++;
                    int pi = (w.entrance <= 0 ? 1 : w.entrance) - 1;
                    // A blank cell means "no extra pressure", not "zero HP".
                    float waveMul = w.hpMul > 0f ? w.hpMul : 1f;
                    for (int i = 0; i < w.count; i++)
                        sched.Add(new SpawnEvent(w.t + i * w.gap, w.enemy, pi, waveMul, waveNo));
                }
                sched.Sort((a, b) => a.Time.CompareTo(b.Time));

                var paths = m.paths.Select(PathDef.Build).ToArray();
                var last = m.paths[0][m.paths[0].Length - 1];

                var def = new StageDef
                {
                    Id = st.id,
                    Order = st.order,
                    Chapter = st.chapter,
                    No = st.no,
                    MapId = st.mapId,
                    Name = Text(m.nameKey),
                    Floor = m.floor,
                    SpawnNames = (m.spawnKeys ?? Array.Empty<string>()).Select(Text).ToArray(),
                    GoalName = Text(m.goalKey),
                    Gimmick = string.IsNullOrEmpty(m.gimKey) ? string.Empty : Text(m.gimKey),
                    Desc = Text(m.descKey),
                    Tiles = m.tiles,
                    Paths = paths,
                    NoDeployFromEntrance = m.noDeployFromEntrance,
                    NoDeployBeforeGoal = m.noDeployBeforeGoal,
                    Schedule = sched.ToArray(),
                    HpMul = st.hpMul,
                    Boss = st.boss != 0,
                    EnemyIds = sched.Select(s => s.EnemyId).Distinct().ToArray(),
                    UnlockCond = st.unlockCond,
                    FirstRewardGroup = st.firstRewardGroup,
                    IdleCoin = st.idleCoin,
                    IdlePart = st.idlePart,
                    IdleCrystal = st.idleCrystal,
                    StartPower = st.startPower > 0f ? st.startPower : Config.startPower,
                    GoalX = last[0],
                    GoalY = last[1],
                };
                _stages.Add(def);
                _stagesById[def.Id] = def;
            }
        }
    }
}
