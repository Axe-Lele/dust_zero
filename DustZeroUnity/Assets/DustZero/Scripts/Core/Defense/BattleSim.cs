// The defense battle rules, independent of Unity.
//
// Tick() advances exactly one simulation step. The view calls it once per frame at 1x and
// twice at 2x, and not at all while paused or while a blocking tutorial step is showing -
// which is why nothing in here reads a frame time or a wall clock.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Defense
{
    public enum BattleOutcome { Running, Won, Lost }

    /// <summary>Supplies the roster multiplier for a unit type, so Core needs no save-file types.</summary>
    public interface IUnitPowerSource
    {
        /// <summary>1 + lvBonus*(lv-1) + starBonus*star, or 1 for an unowned unit.</summary>
        float PowerOf(string unitId);
        /// <summary>Display level, used by the unit sheet.</summary>
        int LevelOf(string unitId);

        /// <summary>
        /// 10.17-b: the separate growth multiplier for support appliances, from config
        /// <c>&lt;unitId&gt;LvBonus</c> / <c>&lt;unitId&gt;StarBonus</c>. 1 for everything
        /// else - the generator and the computer do nothing that <see cref="PowerOf"/>
        /// scales, so without this they stayed exactly as strong at Lv.45 as at Lv.1.
        /// </summary>
        float SupportPowerOf(string unitId);

        /// <summary>
        /// What the appliance's gear does, as bare multipliers so this layer needs no
        /// save-file types: attack and heat scale up, skill charge time scales down.
        /// </summary>
        float GearAtkMul(string unitId);
        float GearHeatMul(string unitId);
        float GearChargeMul(string unitId);
        /// <summary>Flat seconds the exclusive adds to the skill's duration.</summary>
        float GearSkillDuration(string unitId);
        /// <summary>
        /// One skill coefficient after the exclusive's edits. Everything that reads
        /// skill_param during a battle goes through here, so an exclusive that sharpens a
        /// kit changes the kit rather than being a separate special case.
        /// </summary>
        float SkillParam(string unitId, string key, float raw);
    }

    public sealed class BattleSim
    {
        public GameDatabase Db { get; }
        public StageDef Stage { get; }
        public BattleField Field { get; }
        public PresentationBus Bus { get; }

        readonly IRandom _rng;
        readonly IUnitPowerSource _power;
        readonly ConfigRow _cfg;
        readonly UnitBehaviours _behaviours;

        readonly List<BattleUnit> _units = new();
        readonly List<BattleEnemy> _enemies = new();
        readonly Dictionary<string, float> _cooldowns = new();
        readonly List<string> _deck;

        /// <summary>Enemy types that have appeared, for tutorial triggers.</summary>
        public readonly HashSet<string> Spawned = new();
        /// <summary>Unit types whose skill has fired, for tutorial triggers. "" tracks "any".</summary>
        public readonly HashSet<string> SkillsUsed = new();

        public IReadOnlyList<BattleUnit> Units => _units;
        public IReadOnlyList<BattleEnemy> Enemies => _enemies;
        public IReadOnlyList<string> Deck => _deck;

        /// <summary>Simulation time in seconds. Advances only inside Tick().</summary>
        public float Time { get; private set; }
        public float Power { get; private set; }
        public int Life { get; private set; }
        public int Killed { get; private set; }

        /// <summary>The most appliances on the board at once, which the result screen reports.</summary>
        public int PeakDeployed { get; private set; }
        public int Leaked { get; private set; }
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Running;

        int _scheduleIndex;

        /// <summary>
        /// 이 판의 웨이브표. 보통은 스테이지 표를 그대로 복사하지만, 협동은 끝이 없어
        /// 진행하면서 뒤에 계속 덧붙인다 — 그래서 배열이 아니라 목록이다.
        /// </summary>
        readonly List<SpawnEvent> _schedule;
        int _nextUnitId;
        int _nextEnemyId;
        int _taps;

        /// <summary>
        /// How far the schedule has been consumed. The board's regen warning reads ahead
        /// from here, which is what keeps it from re-announcing enemies already on the field.
        /// </summary>
        public int ScheduleCursor => _scheduleIndex;

        public int Remaining => _schedule.Count - Killed - Leaked;

        /// <summary>Deploy slots, including any multitaps bought this battle.</summary>
        public int MaxUnits => _cfg.maxUnits + _taps;
        /// <summary>Multitaps bought so far.</summary>
        public int Taps => _taps;
        /// <summary>Power cost of the next multitap, or null when none are left to buy.</summary>
        public float? NextTapCost
            => _cfg.multitapCost != null && _taps < _cfg.multitapCost.Length ? _cfg.multitapCost[_taps] : null;
        public bool Finished => Outcome != BattleOutcome.Running;

        /// <summary>협동 고리판인가. 이동·승패 판정이 달라진다.</summary>
        public bool LoopMode { get; }

        /// <summary>Adds one spawn to the end of this run's schedule. 협동이 웨이브를 잇는 길.</summary>
        public void AppendSpawn(SpawnEvent ev)
        {
            _schedule.Add(ev);
            if (ev.Wave > WaveCount) WaveCount = ev.Wave;
        }

        /// <summary>The wave the most recent spawn belonged to, 0 before anything has come out.</summary>
        public int SpawnedWave => _scheduleIndex > 0 ? _schedule[_scheduleIndex - 1].Wave : 0;

        /// <summary>Ends the run as a loss from outside — 협동은 고리가 가득 차면 진다.</summary>
        public void ForceLose()
        {
            if (Finished) return;
            Life = 0;
            Outcome = BattleOutcome.Lost;
        }

        /// <summary>
        /// 한 판. <paramref name="loop"/> 가 참이면 협동 고리판이다 — 적이 길을 돌고,
        /// 표가 바닥나도 끝나지 않으며, 근접 가전은 길을 막는 대신 곁에서 붙잡는다.
        /// </summary>
        public BattleSim(GameDatabase db, StageDef stage, IReadOnlyList<string> deck,
                         IUnitPowerSource power, IRandom rng, PresentationBus bus,
                         bool loop = false)
        {
            LoopMode = loop;
            Db = db;
            Stage = stage;
            _deck = new List<string>(deck);
            _power = power;
            _rng = rng;
            Bus = bus;
            _cfg = db.Config;
            Field = new BattleField(stage);
            _behaviours = new UnitBehaviours(this);

            // Scripted stages may open with more power than the global default.
            Power = stage.StartPower > 0f ? stage.StartPower : _cfg.startPower;
            Life = _cfg.lifeMax;
            _schedule = new List<SpawnEvent>(stage.Schedule);
            foreach (var e in stage.Schedule) if (e.Wave > WaveCount) WaveCount = e.Wave;
            foreach (var t in _deck) _cooldowns[t] = 0f;
        }

        // ------------------------------------------------------------------ queries

        public float CooldownOf(string type) => _cooldowns.TryGetValue(type, out var v) ? v : 0f;

        /// <summary>Cost, cooldown and the deployed-unit cap, independent of which tile is tapped.</summary>
        public bool Affordable(string type)
        {
            var def = Db.Unit(type);
            return def != null && Power >= def.Cost && CooldownOf(type) <= 0f && _units.Count < MaxUnits;
        }

        public bool CanPlace(string type, int c, int r)
        {
            var def = Db.Unit(type);
            return def != null && Field.CanPlace(def, c, r, _units);
        }

        public BattleUnit UnitAt(int c, int r)
        {
            foreach (var u in _units)
                if (u.Col == c && u.Row == r) return u;
            return null;
        }

        /// <summary>An active melee unit standing on (c,r). Overheated units stop blocking.</summary>
        public BattleUnit BlockerAt(int c, int r)
        {
            foreach (var u in _units)
                if (u.Col == c && u.Row == r && u.IsBlocker && !u.Overheated) return u;
            return null;
        }

        public Vec2 PositionOf(BattleEnemy e)
            => e.IsFlying ? Field.PositionAt(e.D, e.PathIndex) : new Vec2(e.X, e.Y);

        /// <summary>Distance still to travel. Smaller means closer to the goal, so it ranks targets.</summary>
        public float RemainingDistance(BattleEnemy e)
        {
            // 고리판에는 골이 없다. 앞뒤를 가릴 기준은 고리 위 진행거리뿐이라
            // 많이 돈 적일수록 '앞' 으로 본다 — 오래 버틴 먼지를 먼저 친다.
            if (LoopMode) return -e.Travelled;
            if (e.IsFlying) return Stage.Paths[e.PathIndex].Length - e.D;
            if (e.TargetSet)
                return Field.DistAll[e.Ty, e.Tx] + Mathf2.Hypot(e.Tx + 0.5f - e.X, e.Ty + 0.5f - e.Y);
            return Field.DistAll[e.Cy, e.Cx];
        }

        public float DistanceTo(BattleUnit u, BattleEnemy e)
        {
            var p = PositionOf(e);
            return Mathf2.Hypot(p.X - u.X, p.Y - u.Y);
        }

        public static bool CanHit(BattleEnemy e, TargetType t) => t switch
        {
            TargetType.Ground => !e.Def.Fly,
            TargetType.Air => e.Def.Fly,
            _ => true,
        };

        public List<BattleEnemy> InRange(BattleUnit u, float radius, TargetType t)
        {
            var list = new List<BattleEnemy>();
            foreach (var e in _enemies)
                if (CanHit(e, t) && DistanceTo(u, e) <= radius) list.Add(e);
            return list;
        }

        /// <summary>The in-range enemy closest to the goal, which is the original's targeting rule.</summary>
        public BattleEnemy FrontTarget(BattleUnit u, float radius, TargetType t)
        {
            BattleEnemy best = null;
            float bestRem = float.PositiveInfinity;
            foreach (var e in _enemies)
            {
                if (!CanHit(e, t)) continue;
                if (DistanceTo(u, e) > radius) continue;
                float rem = RemainingDistance(e);
                if (rem < bestRem) { bestRem = rem; best = e; }
            }
            return best;
        }

        /// <summary>Power regenerated per second: base plus every generator, doubled while its skill runs.</summary>
        public float PowerRegen()
        {
            float r = _cfg.regenPerSec;
            foreach (var u in _units)
                if (u.Behaviour == "gen")
                    r += u.Skp("regenPerSec") * u.Support * u.BoostMul
                         * (u.SkillActive ? u.Skp("skillRegenMul") : 1f);
            return r;
        }

        // ------------------------------------------------------------------ commands

        /// <summary>Places a unit. Caller must have checked Affordable and CanPlace.</summary>
        public BattleUnit Deploy(string type, int c, int r)
        {
            var baseDef = Db.Unit(type);
            float pw = _power.PowerOf(type);
            // Gear rides on top of the roster multiplier, exactly as the build stacks them:
            // atk x pw x ga, heat x pw x gh, charge x gsp. Cooling takes no gear bonus.
            float ga = _power.GearAtkMul(type);
            float gh = _power.GearHeatMul(type);
            float gsp = _power.GearChargeMul(type);

            var u = new BattleUnit
            {
                Id = ++_nextUnitId,
                Type = type,
                Def = baseDef,
                Power = pw * ga,
                BasePower = pw,
                GearAtk = ga,
                GearHeat = gh,
                GearCharge = gsp,
                Tier = 1,
                Support = _power.SupportPowerOf(type),
                Spent = baseDef.Cost,
                Atk = baseDef.Atk * pw * ga,
                MaxHeat = baseDef.Heat > 0f ? baseDef.Heat * pw * gh : 0f,
                CoolRate = baseDef.Cool > 0f ? baseDef.Cool * pw : 0f,
                Block = baseDef.Block,
                Range = baseDef.Range,
                SkillChargeTime = baseDef.SkillCharge * gsp,
                SkillDurationAdd = _power.GearSkillDuration(type),
                // One lookup point for the whole battle: skill_param, then the exclusive's
                // edits. Kits call u.Skp(key) and never touch Db.Skp directly.
                Skp = key => _power.SkillParam(type, key, Db.Skp(type, key)),
                Col = c,
                Row = r,
                X = c + 0.5f,
                Y = r + 0.5f,
                AtkT = 0.3f,
            };
            // Only ground units carry a heat bar; furniture units never overheat.
            if (baseDef.Tile != TileKind.Ground) u.MaxHeat = 0f;

            _units.Add(u);
            if (_units.Count > PeakDeployed) PeakDeployed = _units.Count;
            Power -= baseDef.Cost;

            Bus.Emit(FxKind.Drop, u.X, u.Y, 0.4f);
            Bus.Burst(u.X, u.Y + 0.3f, 12, "#E2D2B5", 1.6f, 0.08f, 0.5f);
            Bus.Burst(u.X, u.Y - 0.1f, 8, FxColors.White, 2.2f, 0.04f, 0.35f);
            return u;
        }

        /// <summary>Withdraws a unit, refunding half its cost and starting the redeploy cooldown.</summary>
        /// <summary>
        /// What retreating this unit would pay back. Covers the boosts too, so upgrading is
        /// not a one-way commitment. The action bar quotes this, so it lives here rather than
        /// being recomputed in the view.
        /// </summary>
        /// <summary>
        /// The radius to draw when this unit is selected. Mirrors the original's selUnit
        /// branch: the appliances whose reach is a skill parameter show that parameter, and
        /// everything else shows its attack range. Boost's range bonus is included.
        /// </summary>
        /// <summary>How many waves this stage's table defines. 0 when it has none.</summary>
        public int WaveCount { get; private set; }

        /// <summary>
        /// The wave currently arriving, 1-based. Before the first spawn it reads 1 rather
        /// than 0, because the HUD shows it as "웨이브 1 / N" from the moment the battle opens.
        /// </summary>
        public int WaveIndex
        {
            get
            {
                int w = 1;
                foreach (var e in Stage.Schedule)
                {
                    if (e.Time > Time) break;
                    if (e.Wave > w) w = e.Wave;
                }
                return w;
            }
        }

        public float DisplayRange(BattleUnit u)
        {
            if (u == null) return 0f;
            bool on = u.SkillActive;
            switch (u.Behaviour)
            {
                case "vac": return u.Skp("pullRadius") + u.RangeAdd;
                case "fridge": return u.Skp("freezeRadius") + u.RangeAdd;
                case "pc": return u.Skp(on ? "skillRange" : "buffRange") + u.RangeAdd;
                case "fan": return u.Skp(on ? "skillRange" : "coolRange") + u.RangeAdd;
                default: return u.Range;
            }
        }

        public float RetreatRefund(BattleUnit u)
            => u == null ? 0f : (float)Math.Floor(u.Spent * _cfg.retreatRefund * 10f) / 10f;

        /// <summary>
        /// Sets the power pool outright. 협동이 봇 차례에 자기 전력으로 바꿔 끼웠다
        /// 되돌리는 용도다 — 보통 전투에서는 쓰지 않는다.
        /// </summary>
        internal void SetPower(float value) => Power = value;

        /// <summary>Lets a type be redeployed at once. 협동의 갈아 끼우기는 쿨을 두지 않는다.</summary>
        internal void ClearCooldown(string type) => _cooldowns[type] = 0f;

        /// <summary>
        /// Drops a unit off the board with no refund and no cooldown — 협동의 합치기는
        /// 철수가 아니라 둘이 하나가 되는 것이라 <see cref="Retreat"/> 와 달라야 한다.
        /// </summary>
        internal void Remove(BattleUnit u)
        {
            u.Dead = true;
            ReleaseHolds(u);
            _units.Remove(u);
        }

        /// <summary>Frees every enemy this unit was blocking, queueing, pulling or holding.</summary>
        internal void ReleaseHolds(BattleUnit u)
        {
            foreach (var e in _enemies)
            {
                if (e.BlockedBy == u) e.BlockedBy = null;
                if (e.QueuedBy == u) e.QueuedBy = null;
                if (e.PullBy == u) e.PullBy = null;
                if (e.HeldBy == u) e.HeldBy = null;
            }
        }

        public void Retreat(BattleUnit u)
        {
            u.Dead = true;
            Power = Mathf2.Min(_cfg.powerMax, Power + RetreatRefund(u));
            _cooldowns[u.Type] = _cfg.redeployCooldown;
            foreach (var e in _enemies)
            {
                if (e.BlockedBy == u) e.BlockedBy = null;
                if (e.QueuedBy == u) e.QueuedBy = null;
                if (e.PullBy == u) e.PullBy = null;
            }
            _units.Remove(u);
        }

        /// <summary>The deployed appliance's roster level, for the unit panel's "Lv.N".</summary>
        public int LevelOf(string unitId) => _power.LevelOf(unitId);

        /// <summary>The unit_boost row for this unit's next tier, or null at max.</summary>
        public UnitBoostRow NextBoost(BattleUnit u) => Db.Boost(u.Type, u.Tier + 1);

        /// <summary>Power cost of the next boost, or null when fully upgraded.</summary>
        public float? BoostCost(BattleUnit u)
        {
            var b = NextBoost(u);
            if (b == null) return null;
            return (float)Math.Round(u.Def.Cost * b.costMul, 1);
        }

        public bool CanBoost(BattleUnit u)
        {
            var c = BoostCost(u);
            return c.HasValue && Power >= c.Value;
        }

        /// <summary>
        /// Upgrades a deployed unit in place. This is where leftover power goes once the board
        /// is full: every stat the table lists moves together, and the heat bar keeps its
        /// current fill ratio so a boost never overheats the unit it just improved.
        /// </summary>
        public bool Boost(BattleUnit u)
        {
            var b = NextBoost(u);
            var cost = BoostCost(u);
            if (b == null || !cost.HasValue || Power < cost.Value) return false;

            var baseDef = u.Def;
            float ratio = u.MaxHeat > 0f ? u.Heat / u.MaxHeat : 0f;

            Power -= cost.Value;
            u.Spent += cost.Value;
            u.Tier = b.tier;
            // Recomputed from the table, so the gear multipliers have to be re-applied here
            // or a boost would quietly strip whatever the appliance is wearing.
            u.Power = u.BasePower * b.statMul * u.GearAtk;
            u.BoostMul = b.statMul;
            u.RangeAdd = b.rangeAdd;

            u.Atk = baseDef.Atk * u.Power;
            u.CoolRate = baseDef.Cool > 0f ? baseDef.Cool * u.BasePower * b.statMul : 0f;
            u.Block = baseDef.Block + b.blockAdd;
            u.Range = baseDef.Range + b.rangeAdd;
            u.SkillChargeTime = baseDef.SkillCharge * b.spMul * u.GearCharge;

            if (u.MaxHeat > 0f)
            {
                u.MaxHeat = baseDef.Heat * u.BasePower * b.statMul * u.GearHeat;
                if (!u.Overheated) u.Heat = ratio * u.MaxHeat;
            }
            u.Sp = Mathf2.Min(u.Sp, u.SkillChargeTime);

            SkillsUsed.Add("_boost");
            Bus.Burst(u.X, u.Y, 18, "#FFD54A", 2.6f, 0.07f, 0.6f);
            Bus.Burst(u.X, u.Y - 0.2f, 10, FxColors.White, 1.6f, 0.05f, 0.5f, -1.2f);
            Bus.Emit(FxKind.Skill, u.X, u.Y, 0.6f);
            Bus.Text(u.X, u.Y - 0.62f, $"강화 {new string('★', u.Tier)}", FxColors.Gold, true, true);
            return true;
        }

        /// <summary>Buys one more deploy slot with leftover power.</summary>
        public bool BuyMultitap()
        {
            var cost = NextTapCost;
            if (!cost.HasValue || Power < cost.Value) return false;
            Power -= cost.Value;
            _taps++;
            Bus.Banner($"멀티탭 연결! 배치 {MaxUnits}대", 1.6f);
            return true;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>
        /// 직전 Tick 이 흘린 시간. 연출이 **전투와 같은 시계**로 돌게 하려고 둔다 —
        /// 일시정지면 0 이고 2배속이면 두 배다. 규칙에는 쓰지 않는다.
        /// </summary>
        public float LastDt { get; private set; }

        public void Tick(float dt)
        {
            LastDt = 0f;
            if (Finished) return;
            LastDt = dt;

            Time += dt;
            Power = Mathf2.Min(_cfg.powerMax, Power + dt * PowerRegen());
            Field.RefreshFree(_units);

            SpawnDue();
            TickCooldowns(dt);
            ApplyAuras(dt);
            TickUnits(dt);
            TickEnemies(dt);
            ResolveOverheat();
            ResolveDeaths();
            RecomputeBlockLoad();
            CheckEnd();
        }

        void SpawnDue()
        {
            while (_scheduleIndex < _schedule.Count && _schedule[_scheduleIndex].Time <= Time)
            {
                var ev = _schedule[_scheduleIndex];
                Spawn(ev.EnemyId, ev.PathIndex, ev.WaveHpMul);
                _scheduleIndex++;
            }
        }

        void Spawn(string type, int pathIndex, float waveHpMul)
        {
            var def = Db.Enemy(type);
            Spawned.Add(type);

            // Stage multiplier sets the baseline; the wave multiplier is the late-run squeeze.
            float hp = def.Hp * (Stage.HpMul <= 0f ? 1f : Stage.HpMul) * (waveHpMul <= 0f ? 1f : waveHpMul);
            if (def.Boss)
            {
                Bus.Banner($"{def.Name} 등장!", 2.2f);
                Bus.RequestShake(0.4f);
            }

            var path = Stage.Paths[pathIndex];
            _enemies.Add(new BattleEnemy
            {
                Id = ++_nextEnemyId,
                Type = type,
                Def = def,
                Hp = hp,
                MaxHp = hp,
                D = 0f,
                PathIndex = pathIndex,
                Cx = path.StartX,
                Cy = path.StartY,
                X = path.StartX + 0.5f,
                Y = path.StartY + 0.5f,
                TargetSet = false,
                Off = (_rng.Value - 0.5f) * 0.24f,
                Seed = _rng.Value * 10f,
            });
        }

        void TickCooldowns(float dt)
        {
            // Materialise the keys first; the dictionary is written inside the loop.
            foreach (var key in new List<string>(_cooldowns.Keys))
                _cooldowns[key] = Mathf2.Max(0f, _cooldowns[key] - dt);
        }

        /// <summary>
        /// Recomputes every per-tick modifier from scratch: PC attack buffs, fan cooling,
        /// stove/wet tile effects and the robot's self-buff. Resetting first is what keeps a
        /// buff from compounding when its source is retreated mid-battle.
        /// </summary>
        void ApplyAuras(float dt)
        {
            foreach (var u in _units) { u.AtkMul = 1f; u.SpdMul = 1f; u.Cool = 0f; }

            foreach (var p in _units)
            {
                if (p.Behaviour == "pc")
                {
                    bool on = p.SkillActive;
                    // Boost tier 3 widens support radii as well as attack range.
                    float rng = p.Skp(on ? "skillRange" : "buffRange") + p.RangeAdd;
                    foreach (var u in _units)
                    {
                        if (ReferenceEquals(u, p)) continue;
                        if (Mathf2.Hypot(u.X - p.X, u.Y - p.Y) > rng + 0.01f) continue;
                        u.AtkMul += p.Skp(on ? "skillAtk" : "buffAtk") * p.Support * p.BoostMul;
                        if (on) u.SpdMul = Mathf2.Min(u.SpdMul, p.Skp("skillIntervalMul"));
                    }
                }
                else if (p.Behaviour == "fan")
                {
                    bool on = p.SkillActive;
                    float rng = p.Skp(on ? "skillRange" : "coolRange") + p.RangeAdd;
                    foreach (var u in _units)
                    {
                        if (!u.IsBlocker) continue;
                        if (Mathf2.Hypot(u.X - p.X, u.Y - p.Y) > rng + 0.01f) continue;
                        u.Cool += (on ? p.Skp("skillCoolMul") : 1f) * p.CoolRate;
                    }
                }
            }

            foreach (var u in _units)
            {
                if (u.IsBlocker)
                {
                    char ch = Field.TileAt(u.Col, u.Row);
                    if (ch == BattleField.Stove) u.Heat += _cfg.fireTileHeatPerSec * dt;
                    if (ch == BattleField.Wet) u.Cool += _cfg.wetTileCoolPerSec;
                }
                if (u.Behaviour == "robot" && u.SkillActive)
                    u.AtkMul += u.Skp("skillAtkBonus");
            }

            // Vacuum pull is re-established each tick by the behaviour that still holds the enemy.
            foreach (var e in _enemies) e.PullBy = null;
        }

        void TickUnits(float dt)
        {
            // Snapshot: a behaviour may retreat nothing, but deaths are handled elsewhere.
            for (int i = 0; i < _units.Count; i++)
            {
                var u = _units[i];

                if (u.IsBlocker)
                {
                    if (u.Overheated)
                    {
                        // Recovery drains the whole bar over overheatSec, fans speeding it up.
                        u.Heat -= (u.MaxHeat / _cfg.overheatSec + u.Cool) * dt;
                        if (u.Heat <= 0f)
                        {
                            u.Heat = 0f;
                            u.Overheated = false;
                            Bus.Emit(FxKind.Drop, u.X, u.Y, 0.4f);
                            Bus.Burst(u.X, u.Y, 10, FxColors.Mint, 1.6f, 0.05f, 0.5f);
                            Bus.Text(u.X, u.Y - 0.6f, "복구", FxColors.MintDeep, false, true);
                        }
                        continue;
                    }
                    u.Heat = Mathf2.Max(0f, u.Heat - (_cfg.naturalCoolPerSec + u.Cool) * dt);
                }

                if (u.SkillT > 0f)
                {
                    u.SkillT -= dt;
                    if (u.SkillT <= 0f) { u.SkillT = 0f; _behaviours.OnSkillEnd(u); }
                }
                else
                {
                    u.Sp = Mathf2.Min(u.SkillChargeTime, u.Sp + dt);
                    if (u.Sp >= u.SkillChargeTime && _behaviours.WantsSkill(u)) UseSkill(u);
                }

                _behaviours.Act(u, dt);
            }
        }

        internal void UseSkill(BattleUnit u)
        {
            SkillsUsed.Add(u.Type);
            SkillsUsed.Add(string.Empty);

            u.Sp = 0f;
            u.SkillT = u.Def.SkillDuration + u.SkillDurationAdd;
            u.Tick = 0f;

            Bus.Emit(FxKind.Skill, u.X, u.Y, 0.6f);
            Bus.Text(u.X, u.Y - 0.62f, u.Def.SkillName, FxColors.Orange, false, true);
            Bus.Burst(u.X, u.Y, 10, "#FFD08A", 2.4f, 0.05f, 0.4f);

            _behaviours.OnSkillStart(u);
        }

        void TickEnemies(float dt)
        {
            foreach (var e in _enemies)
            {
                if (e.SlowT > 0f) e.SlowT -= dt;
                if (e.BrkT > 0f) e.BrkT -= dt;
                if (e.StunT > 0f) e.StunT -= dt;
                if (e.FrozenT > 0f) { e.FrozenT -= dt; continue; }
                if (e.StunT > 0f) continue;

                if (e.BlockedBy != null && (e.BlockedBy.Dead || e.BlockedBy.Overheated)) e.BlockedBy = null;

                if (e.BlockedBy != null)
                {
                    if (e.Def.Damage > 0f)
                    {
                        e.AtkT -= dt;
                        if (e.AtkT <= 0f)
                        {
                            e.AtkT = e.Def.Interval;
                            e.BlockedBy.Heat += e.Def.Damage;
                            Bus.Emit(FxKind.Bite, e.BlockedBy.X, e.BlockedBy.Y - 0.1f, 0.25f);
                        }
                    }
                    continue;
                }

                if (e.PullBy != null) continue;

                float sp = e.Def.Speed * (1f - (e.SlowT > 0f ? e.SlowAmt : 0f));

                if (LoopMode) { LoopStep(e, sp, dt); continue; }

                if (!e.IsFlying)
                {
                    if (Field.TileAt(e.Cx, e.Cy) == BattleField.Wet) sp *= _cfg.wetTileEnemySpeedMul;
                    GroundStep(e, sp, dt);
                    if (Field.TileAt(e.Cx, e.Cy) == BattleField.Goal) Leak(e);
                    continue;
                }

                e.D += sp * dt;
                if (e.D >= Stage.Paths[e.PathIndex].Length) Leak(e);
            }
        }

        /// <summary>
        /// 고리판 이동. 적은 입구 길을 지나 고리로 올라타고 영원히 돈다 — 새는 곳이 없다.
        ///
        /// 근접 가전은 길을 막지 않는다. 곁(0.62칸)을 지나는 적을 **정원이 찰 때까지**
        /// 붙잡아 느리게 하고, 붙잡힌 적은 그 가전에 과열을 쌓는다. 정원이 차면 그냥
        /// 지나간다 — 그래서 고리가 막히지 않고 먼지가 쌓인다.
        ///
        /// 보스와 비행은 붙잡히지 않는다. 보스가 한 자리에 묶이면 판이 멈춰 버린다.
        /// </summary>
        void LoopStep(BattleEnemy e, float sp, float dt)
        {
            if (!e.IsFlying && !e.Def.Boss)
            {
                var h = e.HeldBy;
                if (h != null && (h.Dead || h.Overheated ||
                                  Mathf2.Hypot(h.X - e.X, h.Y - e.Y) > 0.95f))
                    e.HeldBy = null;

                if (e.HeldBy == null)
                {
                    int weight = e.Def.Weight > 0 ? e.Def.Weight : 1;
                    foreach (var u in _units)
                    {
                        if (!u.IsBlocker || u.Overheated || u.Load + weight > u.Block) continue;
                        if (Mathf2.Hypot(e.X - u.X, e.Y - u.Y) >= 0.62f) continue;
                        e.HeldBy = u;
                        u.Load += weight;
                        e.AtkT = 0.3f;
                        break;
                    }
                }

                if (e.HeldBy != null)
                {
                    sp *= _cfg.coopHoldSpeed;
                    if (e.Def.Damage > 0f)
                    {
                        e.AtkT -= dt;
                        if (e.AtkT <= 0f)
                        {
                            e.AtkT = e.Def.Interval;
                            e.HeldBy.Heat += e.Def.Damage;
                            Bus.Emit(FxKind.Bite, e.HeldBy.X, e.HeldBy.Y - 0.1f, 0.25f);
                        }
                    }
                }
            }

            e.D += sp * dt;
            e.Travelled += sp * dt;

            // 입구 길이 끝나면 고리에 올라탄다. 그다음은 길이로 나눈 나머지 — 영원히 돈다.
            if (e.PathIndex == CoopEntryPath && e.D >= Stage.Paths[CoopEntryPath].Length)
            {
                e.D -= Stage.Paths[CoopEntryPath].Length;
                e.PathIndex = CoopRingPath;
            }
            if (e.PathIndex == CoopRingPath)
            {
                float len = Stage.Paths[CoopRingPath].Length;
                e.D = ((e.D % len) + len) % len;
            }

            // PositionOf 는 지상 적에게 e.X/e.Y 를 그대로 돌려준다 — 보통은 BFS 로 걷기
            // 때문이다. 고리판에서는 지상 적도 길을 따르므로 길 위 위치를 직접 묻는다.
            var p = Field.PositionAt(e.D, e.PathIndex);
            e.Dir = Field.DirectionAt(e.D, e.PathIndex);
            e.X = p.X;
            e.Y = p.Y;
            e.Cx = Mathf2.FloorToInt(p.X);
            e.Cy = Mathf2.FloorToInt(p.Y);
        }

        /// <summary>Path 0 walks the dust in from the window; path 1 is the ring it then circles.</summary>
        public const int CoopEntryPath = 0;
        public const int CoopRingPath = 1;

        void Leak(BattleEnemy e)
        {
            e.Dead = true;
            Leaked++;
            Life -= e.Def.LeakCost;
            Bus.GoalFlash = 0.5f;
            Bus.RequestShake(e.Def.Boss ? 0.5f : 0.22f);
            Bus.Burst(Field.GoalX + 0.5f, Field.GoalY + 0.5f, 12, FxColors.Red, 2.4f, 0.07f, 0.5f);
            Bus.Text(Field.GoalX + 0.5f, Field.GoalY + 0.1f, $"-{e.Def.LeakCost}", FxColors.RedSoft, true);
        }

        /// <summary>
        /// Picks the next tile. Enemies prefer the field that routes around blockers; breakers,
        /// and anyone with no detour left, fall back to the raw field and walk into the unit.
        /// </summary>
        void ChooseNext(BattleEnemy e)
        {
            float free = Field.DistFree[e.Cy, e.Cx];
            bool useFree = !float.IsPositiveInfinity(free) && !e.Def.Breaker;
            var d = useFree ? Field.DistFree : Field.DistAll;

            float best = d[e.Cy, e.Cx];
            int bx = -1, by = -1;
            foreach (var (dc, dr) in BattleField.Probe)
            {
                int nc = e.Cx + dc, nr = e.Cy + dr;
                if (!Field.Walkable(nc, nr)) continue;
                float v = d[nr, nc];
                // On the raw field, nudge toward an occupied tile so breakers commit to the fight.
                if (!useFree && BlockerAt(nc, nr) != null) v -= 0.01f;
                if (v < best) { best = v; bx = nc; by = nr; }
            }

            if (bx >= 0) { e.Tx = bx; e.Ty = by; }
            else { e.Tx = e.Cx; e.Ty = e.Cy; }
            e.TargetSet = true;
        }

        /// <summary>Attaches to a blocker, or queues behind it and pushes heat at a reduced rate.</summary>
        void Engage(BattleEnemy e, BattleUnit u, float dt)
        {
            int w = e.Def.LeakCost;
            if (u.Load + w <= u.Block)
            {
                e.BlockedBy = u;
                u.Load += w;
                e.AtkT = 0.3f;
                return;
            }

            e.QueuedBy = u;
            if (e.Def.Damage > 0f)
            {
                e.AtkT -= dt;
                if (e.AtkT <= 0f)
                {
                    e.AtkT = e.Def.Interval;
                    u.Heat += e.Def.Damage * _cfg.queueHeatRatio;
                }
            }
        }

        void GroundStep(BattleEnemy e, float sp, float dt)
        {
            e.QueuedBy = null;
            if (!e.TargetSet) ChooseNext(e);

            float tx = e.Tx + 0.5f, ty = e.Ty + 0.5f;
            float dx = tx - e.X, dy = ty - e.Y;
            float dd = Mathf2.Hypot(dx, dy);

            var occupant = BlockerAt(e.Tx, e.Ty);
            if (occupant != null)
            {
                // A detour exists: back onto the current tile and re-plan next step.
                if (!e.Def.Breaker && !float.IsPositiveInfinity(Field.DistFree[e.Cy, e.Cx]) &&
                    !(e.Cx == e.Tx && e.Cy == e.Ty))
                {
                    e.Tx = e.Cx; e.Ty = e.Cy;
                    return;
                }
                if (dd < 0.95f) { Engage(e, occupant, dt); return; }
            }

            if (dd > 1e-3f)
            {
                float ux = dx / dd, uy = dy / dd;
                // Fall in behind an enemy already stopped directly ahead.
                foreach (var o in _enemies)
                {
                    if (ReferenceEquals(o, e) || o.IsFlying) continue;
                    if (o.BlockedBy == null && o.QueuedBy == null) continue;
                    float ox = o.X - e.X, oy = o.Y - e.Y;
                    float along = ox * ux + oy * uy;
                    if (along > 0f && along < 0.34f && Math.Abs(ox * uy - oy * ux) < 0.3f)
                    {
                        var u = o.BlockedBy ?? o.QueuedBy;
                        e.QueuedBy = u;
                        if (e.Def.Damage > 0f)
                        {
                            e.AtkT -= dt;
                            if (e.AtkT <= 0f)
                            {
                                e.AtkT = e.Def.Interval;
                                u.Heat += e.Def.Damage * _cfg.queueHeatRatio;
                            }
                        }
                        return;
                    }
                }
                e.Dir = new Vec2(Mathf2.Sign((float)Math.Round(ux)), Mathf2.Sign((float)Math.Round(uy)));
            }

            float step = sp * dt;
            if (step >= dd)
            {
                e.X = tx; e.Y = ty;
                e.Cx = e.Tx; e.Cy = e.Ty;
                e.TargetSet = false;
                if (Field.TileAt(e.Cx, e.Cy) != BattleField.Goal) ChooseNext(e);
            }
            else
            {
                e.X += dx / dd * step;
                e.Y += dy / dd * step;
            }
        }

        void ResolveOverheat()
        {
            foreach (var u in _units)
            {
                if (!u.IsBlocker || u.Overheated || u.Heat < u.MaxHeat) continue;

                u.Heat = u.MaxHeat;
                u.Overheated = true;
                u.SkillT = 0f;

                Bus.Emit(FxKind.Boom, u.X, u.Y, 0.6f);
                Bus.Burst(u.X, u.Y - 0.2f, 14, FxColors.White, 1.8f, 0.09f, 0.8f, -1.5f);
                Bus.RequestShake(0.12f);
                Bus.Text(u.X, u.Y - 0.6f, "과열!", FxColors.RedSoft, true, true);

                // The whole queue is released at once, which is the pressure-release the
                // original leaned on: overheating a blocker floods the lane behind it.
                foreach (var e in _enemies)
                    if (e.BlockedBy == u) e.BlockedBy = null;
            }
        }

        void ResolveDeaths()
        {
            foreach (var e in _enemies)
            {
                if (e.Hp > 0f || e.Dead) continue;
                e.Dead = true;
                Killed++;

                var p = PositionOf(e);
                Bus.Emit(FxKind.Pop, p.X, p.Y, 0.4f, e.Def.Radius);
                string col = FxColors.ForEnemyLook(e.Def.Look);
                Bus.Burst(p.X, p.Y, e.Def.Boss ? 34 : 10, col,
                          e.Def.Boss ? 3.2f : 2f, e.Def.Boss ? 0.1f : 0.07f, 0.6f, 3f);
                if (e.Def.Boss)
                {
                    Bus.Burst(p.X, p.Y, 24, FxColors.Gold, 3.6f, 0.07f, 0.8f, 3f);
                    Bus.RequestShake(0.45f);
                    Bus.Text(p.X, p.Y - 0.6f, $"{e.Def.Name} 처치!", FxColors.Gold, true, true);
                }
            }

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (!e.Dead) continue;
                foreach (var u in _units) if (ReferenceEquals(e.BlockedBy, u)) e.BlockedBy = null;
                _enemies.RemoveAt(i);
            }
        }

        void RecomputeBlockLoad()
        {
            foreach (var u in _units) u.Load = 0;
            foreach (var e in _enemies)
                if (e.BlockedBy != null) e.BlockedBy.Load += e.Def.LeakCost;
        }

        void CheckEnd()
        {
            if (Life <= 0) { Life = Math.Max(0, Life); Outcome = BattleOutcome.Lost; }
            // 고리판은 표가 바닥나도 이기지 않는다. 끝없이 이어지고 지기만 한다.
            else if (!LoopMode && _scheduleIndex >= _schedule.Count && _enemies.Count == 0)
                Outcome = BattleOutcome.Won;
        }

        // ------------------------------------------------------------------ damage helpers

        /// <summary>Applies damage and raises the floating number, as the original's hurt() did.</summary>
        /// <summary>
        /// 원본 `eArmor` — 적의 방어에서 깎인 만큼을 빼고 위아래로 조인다.
        /// 아래 끝이 음수(-0.3)인 것은 방어를 0 밑으로 깎으면 **더 아프게** 맞기 때문이다.
        /// </summary>
        public float ArmorOf(BattleEnemy e)
        {
            float a = e.Def.Armor - (e.BrkT > 0f ? e.Brk : 0f);
            return Mathf2.Clamp(a, -0.3f, _cfg.armorMax);
        }

        /// <summary>원본 `dmgE` — 방어를 통과한 실제 피해. pierce 1 이면 방어를 무시한다.</summary>
        public float AfterArmor(BattleEnemy e, float amount, float pierce)
            => amount * (1f - ArmorOf(e) * (1f - pierce));

        internal void Hurt(BattleEnemy e, float amount, string color = null, BattleUnit by = null,
                           float pierce = 0f)
        {
            amount = AfterArmor(e, amount, pierce);
            // Credit only what landed: overkill would make the MVP share meaningless.
            if (by != null && amount > 0f && !e.Dead)
            {
                by.Damage += Mathf2.Min(amount, Mathf2.Max(0f, e.Hp));
                if (e.Hp - amount <= 0f) by.Kills++;
            }
            e.Hp -= amount;
            e.HitT = 0.12f;
            if (amount >= 1f)
            {
                var p = PositionOf(e);
                Bus.Text(p.X + (_rng.Value - 0.5f) * 0.25f, p.Y - 0.35f,
                         ((int)Math.Round(amount)).ToString(), color ?? FxColors.Damage, amount >= 200f);
            }
        }

        /// <summary>
        /// Silent damage, for the beams: they tick every frame, so routing them through Hurt
        /// would print a floating number sixty times a second. The tally is the same.
        /// </summary>
        internal void Credit(BattleUnit by, BattleEnemy e, float amount, float pierce = 0f)
        {
            amount = AfterArmor(e, amount, pierce);
            if (amount <= 0f || e.Dead) return;
            by.Damage += Mathf2.Min(amount, Mathf2.Max(0f, e.Hp));
            if (e.Hp - amount <= 0f) by.Kills++;
            e.Hp -= amount;
        }

        /// <summary>원본 `stunE` — 보스는 config.stunBossMul 만큼만 걸린다.</summary>
        internal void Stun(BattleEnemy e, float duration)
        {
            float mul = e.Def.Boss ? _cfg.stunBossMul : 1f;
            e.StunT = Mathf2.Max(e.StunT, duration * mul);
        }

        /// <summary>
        /// 방어 깎기. 이미 걸려 있으면 큰 쪽을 남기고, 꺼져 있었다면 새로 쓴다 —
        /// 원본의 `e.brk=Math.max(e.brkT>0?e.brk:0, …)` 와 같은 뜻이다.
        /// </summary>
        internal void Break(BattleEnemy e, float amount, float duration)
        {
            e.Brk = Mathf2.Max(e.BrkT > 0f ? e.Brk : 0f, amount);
            e.BrkT = Mathf2.Max(e.BrkT, duration);
        }

        internal void Slow(BattleEnemy e, float amount, float duration)
        {
            e.SlowAmt = Mathf2.Max(e.SlowT > 0f ? e.SlowAmt : 0f, amount);
            e.SlowT = Mathf2.Max(e.SlowT, duration);
        }

        internal float Rand => _rng.Value;
        internal ConfigRow Cfg => _cfg;
        internal void AddPower(float amount) => Power = Mathf2.Min(_cfg.powerMax, Power + amount);

        /// <summary>Number of stars earned, from config.starLifeMin thresholds.</summary>
        public int StarsEarned()
        {
            if (Outcome != BattleOutcome.Won) return 0;
            int stars = 0;
            foreach (var v in _cfg.starLifeMin) if (Life >= v) stars++;
            return stars;
        }
    }
}
