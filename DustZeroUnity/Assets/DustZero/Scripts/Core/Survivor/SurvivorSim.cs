// 대청소 (survival) mode rules.
//
// An endless run in an open arena: the leader moves, everything else is automatic. Level-ups
// recruit or upgrade companions from the player's deck, and a maxed companion paired with a
// level-3+ leader weapon unlocks that pair's combo.
//
// 10.15-h removed the five-minute finish. Enemies compound in health and damage once
// survScaleStart passes, bosses return on a fixed cycle, and the run ends only when the
// leader falls - so a stronger roster buys more time rather than an easier clear.
//
// All coefficients come from surv_param / surv_combo / surv_set / surv_wave / config.surv*.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Meta;
using DustZero.Data;

namespace DustZero.Core.Survivor
{
    /// <summary>
    /// 10.15-h made the run endless: it ends when the leader falls or the player leaves.
    /// There is no "cleared" state any more.
    /// </summary>
    public enum SurvOutcome { Running, Defeated, Quit }

    public sealed class SurvivorSim
    {
        /// <summary>Companions that ride on the leader rather than trailing behind.</summary>
        /// <summary>
        /// Appliances that help rather than attack as companions. Which ones those are is
        /// unit_behaviour.support, so a new support appliance needs no code here.
        /// </summary>
        bool IsSupport(string type)
            => Db.Units.TryGetValue(type, out var d) && d.IsSupport;

        public GameDatabase Db { get; }
        public PresentationBus Bus { get; }
        public SurvLeader Leader { get; }
        public float Arena { get; }

        readonly ConfigRow _cfg;
        readonly IRandom _rng;
        readonly PlayerProfile _profile;
        readonly List<string> _deck;

        readonly List<SurvCompanion> _comps = new();
        readonly List<SurvEnemy> _enemies = new();
        readonly List<SurvGem> _gems = new();
        readonly List<SurvShot> _shots = new();
        readonly List<SurvMark> _marks = new();
        readonly List<Vec2> _trail = new();

        readonly Dictionary<string, bool> _combos = new();
        readonly Dictionary<string, int> _passives = new() { ["move"] = 0, ["hp"] = 0 };
        readonly Dictionary<int, float> _spawnAccumulator = new();
        /// <summary>How many times each repeating burst row has fired.</summary>
        readonly Dictionary<int, int> _burstCount = new();
        readonly List<SurvEnemyShot> _enemyShots = new();
        /// <summary>Survival marks already passed this run, so each banners once.</summary>
        /// <summary>Whole minutes already paid into the daily log this run.</summary>
        int _minutesBanked;
        /// <summary>Reused spatial buckets; rebuilt in place each tick to avoid per-frame garbage.</summary>
        readonly Dictionary<int, List<SurvEnemy>> _grid = new();
        readonly List<SurvEnemy> _nearCache = new();

        public IReadOnlyList<SurvCompanion> Companions => _comps;
        public IReadOnlyList<SurvEnemy> Enemies => _enemies;
        public IReadOnlyList<SurvGem> Gems => _gems;
        public IReadOnlyList<SurvShot> Shots => _shots;
        public IReadOnlyList<SurvMark> Marks => _marks;
        public IReadOnlyList<SurvEnemyShot> EnemyShots => _enemyShots;
        public IReadOnlyDictionary<string, bool> Combos => _combos;
        /// <summary>Crystal already banked this run from survival marks.</summary>
        /// <summary>Crystal paid straight out during the run. 0 since 10.18-h.</summary>
        public int CrystalEarned { get; private set; }

        /// <summary>Daily-log points this run put in, for the result line.</summary>
        public int PoolPoints { get; private set; }
        /// <summary>Coin from spent level-up pools, paid at the end of the run.</summary>
        public int BonusCoin { get; private set; }
        public int BossesSpawned => _bossN;

        public SurvMods Mods { get; private set; } = new();
        public float Time { get; private set; }
        public int Kills { get; private set; }
        public int Level { get; private set; } = 1;
        public float Xp { get; private set; }
        public float XpNeeded { get; private set; }
        public SurvEnemy Boss { get; private set; }
        public SurvOutcome Outcome { get; private set; } = SurvOutcome.Running;

        /// <summary>Level-ups earned but not yet spent. The view opens a card screen per pending.</summary>
        public int PendingLevelUps { get; private set; }

        float _leaderCd;
        float _auraCd;
        float _trailTimer;
        int _nextEnemyId;
        int _bossN;
        bool _rangedSeen;

        /// <summary>Seconds of compounding growth so far; 0 until survScaleStart passes.</summary>
        public float ScaledMinutes => Math.Max(0f, (Time - _cfg.survScaleStart) / 60f);

        /// <summary>
        /// Current difficulty multipliers. HP and damage compound once survScaleStart passes;
        /// speed ramps from the very start and is capped, so late runs get deadlier rather
        /// than merely longer.
        /// </summary>
        public (float Hp, float Dmg, float Spd) Scale()
        {
            float m = ScaledMinutes;
            return (
                MathF.Pow(1f + _cfg.survHpGrowthPerMin, m),
                MathF.Pow(1f + _cfg.survDmgGrowthPerMin, m),
                Mathf2.Min(_cfg.survSpdMax, 1f + _cfg.survSpdGrowthPerMin * Time / 60f));
        }

        /// <summary>
        /// Seconds until the next whole minute, which is when the log gets its next points.
        /// Replaces the old surv_reward countdown.
        /// </summary>
        public float SecondsToNextPoint => 60f - (Time - _minutesBanked * 60f);

        public SurvivorSim(GameDatabase db, PlayerProfile profile, string leaderType,
                           IReadOnlyList<string> deck, IRandom rng, PresentationBus bus)
        {
            Db = db;
            _profile = profile;
            _cfg = db.Config;
            _rng = rng;
            Bus = bus;
            _deck = new List<string>(deck);
            Arena = _cfg.survArena;
            XpNeeded = _cfg.survXpBase;

            Leader = new SurvLeader
            {
                Type = leaderType,
                X = Arena / 2f,
                Y = Arena / 2f,
                Hp = _cfg.survLeaderHp,
                MaxHp = _cfg.survLeaderHp,
            };

            RecomputeMods();
            Bus.Banner("쓰러질 때까지 버텨요!", 2f);
        }

        float Sv(string id, string slot, string key) => Db.Sv(id, slot, key);

        /// <summary>Roster growth and run modifiers both multiply into a companion's output.</summary>
        float UnitDamage(string type, float baseDamage)
            => baseDamage * _profile.PowerOf(type) * Mods.DmgMul;

        public int CompanionLevel(string type) => _comps.FirstOrDefault(c => c.Type == type)?.Level ?? 0;

        /// <summary>Family set bonuses active for the current line-up, leader included.</summary>
        public List<SurvSetRow> ActiveSets()
        {
            var count = new Dictionary<int, int>();
            foreach (var t in _comps.Select(c => c.Type).Append(Leader.Type))
            {
                int f = Db.Units[t].Family;
                count[f] = count.TryGetValue(f, out var n) ? n + 1 : 1;
            }
            return Db.SurvSets.Where(r => count.TryGetValue(r.family, out var n) && n >= r.need).ToList();
        }

        /// <summary>
        /// Rebuilds every run modifier from scratch. Called after each level-up so effects
        /// never stack twice when the same companion is upgraded repeatedly.
        /// </summary>
        public void RecomputeMods()
        {
            var m = new SurvMods
            {
                Pickup = _cfg.survPickup,
                MoveSpeed = _cfg.survMoveSpeed,
            };

            int pc = CompanionLevel("pc");
            int gen = CompanionLevel("gen");
            if (pc > 0)
            {
                m.CdMul *= 1f - Sv("pc", "comp", "cdMul") * pc;
                m.DmgMul *= 1f + Sv("pc", "comp", "dmgMul") * pc;
            }
            if (gen > 0)
            {
                m.Pickup *= 1f + Sv("gen", "comp", "pickup") * gen;
                m.XpMul *= 1f + Sv("gen", "comp", "xp") * gen;
            }

            foreach (var s in ActiveSets())
            {
                switch (s.key)
                {
                    case "pickupMul": m.Pickup *= 1f + s.value; break;
                    case "dmgMul": m.DmgMul *= 1f + s.value; break;
                    case "cdMul": m.CdMul *= 1f - s.value; break;
                    case "enemySlow": m.EnemySlow += s.value; break;
                }
            }

            m.MoveSpeed *= 1f + Sv("move", "passive", "value") * _passives["move"];
            Mods = m;
        }

        // ------------------------------------------------------------------ spawning

        SurvEnemy Spawn(string type, float hpMul, float? px = null, float? py = null)
        {
            var def = Db.Enemy(type);
            // Hard cap keeps the step cost bounded; bosses are always allowed through.
            if (_enemies.Count >= 360 && !def.Boss) return null;

            float x, y;
            if (px == null)
            {
                float a = _rng.Value * MathF.PI * 2f;
                float d = 6.2f + _rng.Value * 1.5f;
                x = Leader.X + MathF.Cos(a) * d;
                y = Leader.Y + MathF.Sin(a) * d;
            }
            else { x = px.Value; y = py.Value; }

            x = Mathf2.Clamp(x, 0.6f, Arena - 0.6f);
            y = Mathf2.Clamp(y, 0.6f, Arena - 0.6f);

            // Difficulty scale is baked in at spawn, so an enemy keeps the strength of the
            // moment it arrived rather than growing while it chases.
            var sc = Scale();
            float hp = def.Hp * hpMul * sc.Hp;

            float speed = Sv(type, "enemy", "speed");
            float xpValue = Sv(type, "enemy", "xp");
            float range = Sv(type, "enemy", "range");

            var e = new SurvEnemy
            {
                Id = ++_nextEnemyId,
                Type = type,
                Def = def,
                Hp = hp,
                MaxHp = hp,
                X = x,
                Y = y,
                Radius = def.Radius * (def.Boss ? 1.25f : 0.9f),
                Speed = (speed > 0f ? speed : 1f) * sc.Spd,
                ContactDps = Sv(type, "enemy", "contactDps") * sc.Dmg,
                Xp = xpValue > 0f ? xpValue : 1f,
                Seed = _rng.Value * 10f,
                Range = range,
                ShotCd = Sv(type, "enemy", "shotCd"),
                ShotDmg = Sv(type, "enemy", "shotDmg") * sc.Dmg,
                ShotSpeed = Sv(type, "enemy", "shotSpeed"),
            };
            // Stagger the first shot so a cluster of shooters does not volley in unison.
            e.ShotT = e.ShotCd * (0.5f + _rng.Value * 0.6f);
            _enemies.Add(e);

            if (def.Boss)
            {
                Boss = e;
                Bus.Banner($"{def.Name} 등장!", 2f);
                Bus.RequestShake(0.5f);
            }
            else if (e.IsRanged && !_rangedSeen)
            {
                _rangedSeen = true;
                Bus.Banner($"{def.Name}: 멀리서 포자를 쏴요! 옆으로 피하세요", 2.4f);
            }
            return e;
        }

        void TickWaves(float dt)
        {
            foreach (var w in Db.SurvWaves)
            {
                if (w.burst > 0)
                {
                    // `every` repeats the ring; 0 fires it a single time.
                    int fired = _burstCount.TryGetValue(w.id, out var bn) ? bn : 0;
                    float due = w.t0 + fired * w.every;
                    if (Time < due || (w.every <= 0f && fired > 0)) continue;
                    _burstCount[w.id] = fired + 1;

                    float a0 = _rng.Value * MathF.PI * 2f;
                    // Leave a sixth of the circle open so the ring is escapable.
                    float span = w.burst == 1 ? 0f : MathF.PI * 2f * 5f / 6f;
                    for (int i = 0; i < w.burst; i++)
                    {
                        float a = a0 + (w.burst == 1 ? 0f : (float)i / (w.burst - 1) * span);
                        float d = w.burst == 1 ? 6f : 5.6f;
                        Spawn(w.enemy, w.hp0, Leader.X + MathF.Cos(a) * d, Leader.Y + MathF.Sin(a) * d);
                    }
                    if (w.burst > 1 && !Db.Enemy(w.enemy).Boss) Bus.Banner("먼지 떼가 몰려와요!", 2f);
                    continue;
                }

                // Trickle rows interpolate between rate0/hp0 and rate1/hp1 across t0..t1,
                // then hold at the t1 values for the rest of the run.
                if (Time < w.t0) continue;

                float span2 = Math.Max(1f, w.t1 - w.t0);
                float f = MathF.Pow(Mathf2.Min(1f, (Time - w.t0) / span2), w.curve <= 0f ? 1f : w.curve);
                float rate = w.rate0 + (w.rate1 - w.rate0) * f;
                float hp = w.hp0 + (w.hp1 - w.hp0) * f;

                float acc = _spawnAccumulator.TryGetValue(w.id, out var a2) ? a2 : 0f;
                acc += rate * dt;
                while (acc >= 1f) { acc -= 1f; Spawn(w.enemy, hp); }
                _spawnAccumulator[w.id] = acc;
            }
        }

        /// <summary>
        /// Bosses return on a fixed cycle, each tougher than the last, which is what keeps an
        /// endless run from flattening into an endless trickle.
        /// </summary>
        void TickBosses()
        {
            float due = _cfg.survBossFirst + _bossN * _cfg.survBossEvery;
            if (Time < due) return;

            var cycle = new[] { "boss_dust", "boss_grease", "boss_mold" };
            var type = cycle[_bossN % cycle.Length];
            float a = _rng.Value * MathF.PI * 2f;
            Spawn(type, _cfg.survBossHp * (1f + _cfg.survBossHpStep * _bossN),
                  Leader.X + MathF.Cos(a) * 6f, Leader.Y + MathF.Sin(a) * 6f);
            _bossN++;
        }

        /// <summary>
        /// 버틴 1분마다 오늘의 청소 일지에 점수를 넣는다(10.18-h). 예전에는 시간 구간마다
        /// 크리스탈을 줬지만(surv_reward), 지금은 대청소·협동·리그가 한 점수통을 나눠 쓰고
        /// 크리스탈은 그 통의 상자에서 나온다.
        /// </summary>
        void TickMilestones()
        {
            int minutes = (int)Math.Floor(Time / 60f);
            if (minutes <= _minutesBanked) return;

            // 한 프레임에 여러 분이 넘어가도 분마다 한 번씩 넣는다.
            while (_minutesBanked < minutes)
            {
                _minutesBanked++;
                int add = _profile.AddPoolPoints(_cfg.poolSurvPerMin);
                PoolPoints += add;
                Bus.Banner(add > 0
                    ? $"{_minutesBanked}분 버팀! 청소 일지 +{add}"
                    : $"{_minutesBanked}분 버팀!", 2f);
            }
        }

        public static string Mmss(float seconds)
        {
            int t = (int)Math.Floor(seconds);
            return $"{t / 60}:{t % 60:00}";
        }

        /// <summary>Moves enemy projectiles and applies their damage to the leader.</summary>
        void TickEnemyShots(float dt)
        {
            for (int i = _enemyShots.Count - 1; i >= 0; i--)
            {
                var s = _enemyShots[i];
                s.Life += dt;
                s.X += s.Vx * dt;
                s.Y += s.Vy * dt;

                bool gone = s.Life >= s.MaxLife ||
                            s.X < 0f || s.Y < 0f || s.X > Arena || s.Y > Arena;

                if (!gone && Mathf2.Hypot(Leader.X - s.X, Leader.Y - s.Y) <= s.Radius + 0.3f)
                {
                    Leader.Hp -= s.Damage;
                    Leader.HurtT = 0.25f;
                    Bus.Burst(s.X, s.Y, 6, "#9BBF6A", 1.8f, 0.05f, 0.35f);
                    gone = true;
                }
                if (gone) _enemyShots.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ damage helpers

        void Hit(SurvEnemy e, float amount, string color, bool big = false)
        {
            if (e.Dead || amount <= 0f) return;
            e.Hp -= amount;
            e.HitT = 0.1f;
            // Only a sample of hits float a number; at this enemy count every hit would be noise.
            if (big || _rng.Value < 0.35f)
                Bus.Text(e.X + (_rng.Value - 0.5f) * 0.3f, e.Y - 0.4f,
                         ((int)Math.Round(amount)).ToString(), color ?? FxColors.White, amount >= 100f);
        }

        void SlowEnemy(SurvEnemy e, float amount, float duration)
        {
            e.SlowAmt = Mathf2.Max(e.SlowT > 0f ? e.SlowAmt : 0f, amount);
            e.SlowT = Mathf2.Max(e.SlowT, duration);
        }

        List<SurvEnemy> Near(float x, float y, float radius)
            => _enemies.Where(e => !e.Dead && Mathf2.Hypot(e.X - x, e.Y - y) <= radius + e.Radius).ToList();

        SurvEnemy Nearest(float x, float y, float radius)
        {
            SurvEnemy best = null;
            float bd = radius;
            foreach (var e in _enemies)
            {
                if (e.Dead) continue;
                float d = Mathf2.Hypot(e.X - x, e.Y - y);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        List<SurvEnemy> NearestN(float x, float y, float radius, int n)
            => _enemies.Where(e => !e.Dead && Mathf2.Hypot(e.X - x, e.Y - y) <= radius)
                       .OrderBy(e => Mathf2.Hypot(e.X - x, e.Y - y))
                       .Take(n).ToList();

        void Shoot(float x, float y, SurvEnemy target, SurvShot proto)
        {
            float a = MathF.Atan2(target.Y - y, target.X - x);
            proto.X = x; proto.Y = y;
            float speed = Mathf2.Hypot(proto.Vx, proto.Vy);
            proto.Vx = MathF.Cos(a) * speed;
            proto.Vy = MathF.Sin(a) * speed;
            _shots.Add(proto);
        }

        // ------------------------------------------------------------------ leader weapons

        void LeaderWeapon(float dt)
        {
            string t = Leader.Type;
            // The kit is data; the unit keeps its own surv_param rows under its own id.
            string kit = Db.Units.TryGetValue(t, out var ldef) ? ldef.SurvivorLeader : t;
            int lv = Leader.Level;

            switch (kit)
            {
                case "vac":
                    {
                        bool combo = _combos.ContainsKey("tornado");
                        float r = Sv(t, "leader", "radius") + Sv(t, "leader", "radiusPerLv") * (lv - 1);
                        float dps = Sv(t, "leader", "dps") * (1f + Sv(t, "leader", "dmgPerLv") * (lv - 1));
                        if (combo)
                        {
                            r *= Sv("tornado", "combo", "radiusMul");
                            dps *= Sv("tornado", "combo", "dpsMul");
                        }
                        Leader.AuraRadius = r;

                        _auraCd -= dt;
                        if (_auraCd <= 0f)
                        {
                            // Damage is applied in quarter-second chunks, not per frame, so the
                            // DPS figure in the table holds regardless of frame rate.
                            _auraCd = 0.25f * Mods.CdMul;
                            foreach (var e in Near(Leader.X, Leader.Y, r))
                            {
                                Hit(e, UnitDamage(t, dps * 0.25f), "#C9E6FF");
                                if (combo) SlowEnemy(e, Sv("tornado", "combo", "slow"), 0.4f);
                            }
                        }
                        break;
                    }

                case "robot":
                    {
                        bool combo = _combos.ContainsKey("turbo");
                        int n = (int)(Sv(t, "leader", "count") + MathF.Floor((lv - 1) * Sv(t, "leader", "countPerLv")));
                        float spin = Sv(t, "leader", "spin");
                        float dmg = Sv(t, "leader", "dmg") * (1f + Sv(t, "leader", "dmgPerLv") * (lv - 1));
                        if (combo)
                        {
                            n += (int)Sv("turbo", "combo", "countAdd");
                            spin *= Sv("turbo", "combo", "spinMul");
                            dmg *= Sv("turbo", "combo", "dmgMul");
                        }

                        float radius = Sv(t, "leader", "radius");
                        float gap = Sv(t, "leader", "hitGap") * Mods.CdMul;

                        Leader.Orbs.Clear();
                        for (int i = 0; i < n; i++)
                        {
                            float a = Time * spin + (float)i / n * MathF.PI * 2f;
                            float rr = radius * (0.55f + 0.45f * (0.5f + 0.5f * MathF.Sin(Time * 1.8f + i * 2.4f)));
                            float ox = Leader.X + MathF.Cos(a) * rr;
                            float oy = Leader.Y + MathF.Sin(a) * rr;
                            Leader.Orbs.Add(new Vec2(ox, oy));

                            foreach (var e in _enemies)
                            {
                                if (e.Dead || e.BrushT > 0f) continue;
                                if (Mathf2.Hypot(e.X - ox, e.Y - oy) > e.Radius + 0.32f) continue;
                                Hit(e, UnitDamage(t, dmg), FxColors.White);
                                e.BrushT = gap;
                                Bus.Burst(e.X, e.Y, 3, "#E2E6EA", 1.6f, 0.05f, 0.3f);
                            }
                        }
                        break;
                    }

                case "ac":
                    {
                        _leaderCd -= dt;
                        if (_leaderCd > 0f) break;

                        int n = (int)(Sv(t, "leader", "count") + MathF.Floor((lv - 1) * Sv(t, "leader", "countPerLv")));
                        float dmg = Sv(t, "leader", "dmg") * (1f + Sv(t, "leader", "dmgPerLv") * (lv - 1));
                        var targets = NearestN(Leader.X, Leader.Y, Sv(t, "leader", "range"), n);
                        if (targets.Count == 0) break;

                        _leaderCd = Sv(t, "leader", "cd") * Mods.CdMul;
                        Leader.Kick = 0.15f;
                        bool blizzard = _combos.ContainsKey("blizzard");

                        for (int i = 0; i < targets.Count; i++)
                        {
                            float speed = Sv(t, "leader", "speed");
                            Shoot(Leader.X, Leader.Y - 0.2f, targets[i], new SurvShot
                            {
                                Vx = speed, Vy = 0f,
                                Damage = UnitDamage(t, dmg),
                                Slow = Sv(t, "leader", "slow"),
                                SlowSec = Sv(t, "leader", "slowSec"),
                                Color = "#9ED0EC",
                                Freeze = blizzard ? Sv("blizzard", "combo", "freeze") : 0f,
                                Pierce = blizzard ? (int)Sv("blizzard", "combo", "pierce") : 0,
                                Delay = i * 0.06f,
                            });
                        }
                        break;
                    }
            }
        }

        void TickShots(float dt)
        {
            foreach (var s in _shots)
            {
                if (s.Delay > 0f) { s.Delay -= dt; continue; }
                s.Life += dt;
                s.X += s.Vx * dt;
                s.Y += s.Vy * dt;

                foreach (var e in _enemies)
                {
                    if (e.Dead || s.Hits.Contains(e)) continue;
                    if (Mathf2.Hypot(e.X - s.X, e.Y - s.Y) > e.Radius + s.Radius) continue;

                    s.Hits.Add(e);
                    if (s.Splash > 0f)
                    {
                        foreach (var o in Near(s.X, s.Y, s.Splash)) Hit(o, s.Damage, FxColors.Ice);
                        Bus.Burst(s.X, s.Y, 10, FxColors.Water, 2f, 0.06f, 0.4f);
                        Bus.Emit(FxKind.Ring, s.X, s.Y, 0.35f, s.Splash, "127,196,245");
                    }
                    else
                    {
                        Hit(e, s.Damage, s.Color == "#9ED0EC" ? FxColors.Ice : FxColors.White);
                        Bus.Burst(s.X, s.Y, 5, s.Color, 1.6f, 0.05f, 0.3f);
                    }

                    if (s.Slow > 0f) SlowEnemy(e, s.Slow, s.SlowSec > 0f ? s.SlowSec : 1.5f);
                    if (s.Freeze > 0f) e.FrozenT = Mathf2.Max(e.FrozenT, s.Freeze);

                    if (s.Pierce > 0) s.Pierce--;
                    else { s.Life = s.MaxLife; break; }
                }
            }
            _shots.RemoveAll(s => s.Life >= s.MaxLife);
        }

        // ------------------------------------------------------------------ companions

        Vec2 CompanionPosition(SurvCompanion c)
        {
            if (c.Attached)
            {
                var offsets = new[] { (-0.42f, -0.62f), (0.42f, -0.62f), (0f, -0.95f) };
                var o = offsets[c.Slot % 3];
                return new Vec2(Leader.X + o.Item1, Leader.Y + o.Item2);
            }
            // Trailing companions walk the leader's recorded path, spaced by slot.
            int idx = Math.Max(0, _trail.Count - 1 - (c.Slot + 1) * 9);
            return idx < _trail.Count ? _trail[idx] : new Vec2(Leader.X, Leader.Y);
        }

        void TickCompanions(float dt)
        {
            foreach (var c in _comps)
            {
                var p = CompanionPosition(c);
                c.X = p.X; c.Y = p.Y;
                c.Kick = Mathf2.Max(0f, c.Kick - dt);

                string t = c.Type;
                string kit = Db.Units.TryGetValue(t, out var cdef) ? cdef.SurvivorCompanion : t;
                float mul = 1f + Sv(t, "comp", "perLv") * (c.Level - 1);
                c.Cd -= dt;

                bool vacStorming = t == "vac" && c.StormT > 0f;
                if (c.Cd > 0f && !vacStorming) continue;

                float baseCd = Sv(t, "comp", "cd") * Mods.CdMul;

                switch (kit)
                {
                    case "vac":
                        {
                            float r = Sv(t, "comp", "radius");
                            if (c.StormT > 0f)
                            {
                                c.StormT -= dt;
                                foreach (var e in Near(c.X, c.Y, r))
                                {
                                    if (!e.Def.Boss)
                                    {
                                        float dx = c.X - e.X, dy = c.Y - e.Y;
                                        float d = Mathf2.Hypot(dx, dy);
                                        if (d <= 0f) d = 1f;
                                        float mv = Mathf2.Min(2.6f * dt, Mathf2.Max(0f, d - 0.35f));
                                        e.X += dx / d * mv;
                                        e.Y += dy / d * mv;
                                    }
                                    Hit(e, UnitDamage(t, Sv(t, "comp", "dps") * mul * dt), "#C9E6FF");
                                }
                                continue;
                            }
                            if (Near(c.X, c.Y, r).Count > 0)
                            {
                                c.StormT = Sv(t, "comp", "dur");
                                c.Cd = baseCd;
                                c.Kick = 0.3f;
                                Bus.Emit(FxKind.Ring, c.X, c.Y, 0.5f, r, "201,205,209");
                            }
                            break;
                        }

                    case "robot":
                        {
                            var e = Nearest(c.X, c.Y, Sv(t, "comp", "range"));
                            if (e == null) continue;
                            c.Cd = baseCd; c.Kick = 0.3f;

                            // A dash that damages everything along the line, not just the target.
                            float dx = e.X - c.X, dy = e.Y - c.Y;
                            float len2 = dx * dx + dy * dy;
                            if (len2 <= 0f) len2 = 1f;
                            foreach (var o in _enemies)
                            {
                                if (o.Dead) continue;
                                float u = Mathf2.Clamp(((o.X - c.X) * dx + (o.Y - c.Y) * dy) / len2, 0f, 1f);
                                if (Mathf2.Hypot(c.X + dx * u - o.X, c.Y + dy * u - o.Y) <= o.Radius + 0.4f)
                                    Hit(o, UnitDamage(t, Sv(t, "comp", "dmg") * mul), FxColors.White);
                            }
                            Bus.Emit(FxKind.Dash, c.X, c.Y, 0.3f, 0f, null, 0f, e.X, e.Y);
                            Bus.Burst(e.X, e.Y, 8, "#E2E6EA", 2f, 0.06f, 0.35f);
                            break;
                        }

                    case "fridge":
                        {
                            float r = Sv(t, "comp", "radius");
                            var es = Near(c.X, c.Y, r);
                            if (es.Count == 0) continue;
                            c.Cd = baseCd; c.Kick = 0.3f;
                            foreach (var e in es)
                            {
                                // Bosses get a fraction of the freeze so they never fully stall.
                                e.FrozenT = Mathf2.Max(e.FrozenT, Sv(t, "comp", "freeze") * (e.Def.Boss ? 0.3f : 1f));
                                Hit(e, UnitDamage(t, Sv(t, "comp", "dmg") * mul), FxColors.IcePale);
                            }
                            Bus.Emit(FxKind.Ring, c.X, c.Y, 0.5f, r, "190,230,250");
                            Bus.Burst(c.X, c.Y, 18, FxColors.IcePale, 2.6f, 0.06f, 0.6f);
                            break;
                        }

                    case "ac":
                        {
                            var e = Nearest(c.X, c.Y, 6f);
                            if (e == null) continue;
                            c.Cd = baseCd; c.Kick = 0.2f;
                            Shoot(c.X, c.Y - 0.15f, e, new SurvShot
                            {
                                Vx = 8f, Vy = 0f,
                                Damage = UnitDamage(t, Sv(t, "comp", "dmg") * mul),
                                Slow = Sv(t, "comp", "slow"),
                                SlowSec = 1.5f,
                                Color = "#9ED0EC",
                            });
                            break;
                        }

                    case "micro":
                        {
                            float r = Sv(t, "comp", "range");
                            var cands = Near(c.X, c.Y, r);
                            if (cands.Count == 0) continue;
                            c.Cd = baseCd; c.Kick = 0.3f;

                            // Aim where the blast catches the most; scan a slice to bound the cost.
                            float blast = Sv(t, "comp", "radius");
                            var best = cands[0];
                            int bestCount = 0;
                            foreach (var e in cands.Take(24))
                            {
                                int n = Near(e.X, e.Y, blast).Count;
                                if (n > bestCount) { bestCount = n; best = e; }
                            }
                            _marks.Add(new SurvMark
                            {
                                X = best.X, Y = best.Y, Radius = blast,
                                Damage = UnitDamage(t, Sv(t, "comp", "dmg") * mul),
                            });
                            break;
                        }

                    case "washer":
                        {
                            var e = Nearest(c.X, c.Y, 6f);
                            if (e == null) continue;
                            c.Cd = baseCd; c.Kick = 0.3f;

                            int n = (int)Sv(t, "comp", "count");
                            float a0 = MathF.Atan2(e.Y - c.Y, e.X - c.X);
                            for (int i = 0; i < n; i++)
                            {
                                float a = a0 + (i - (n - 1) / 2f) * 0.28f;
                                _shots.Add(new SurvShot
                                {
                                    X = c.X, Y = c.Y - 0.1f,
                                    Vx = MathF.Cos(a) * 6f, Vy = MathF.Sin(a) * 6f,
                                    MaxLife = 1.1f, Radius = 0.2f,
                                    Damage = UnitDamage(t, Sv(t, "comp", "dmg") * mul),
                                    Splash = Sv(t, "comp", "splash"),
                                    Color = FxColors.Water,
                                    Bubble = true,
                                });
                            }
                            break;
                        }

                    case "fan":
                        {
                            float r = Sv(t, "comp", "radius");
                            var es = Near(Leader.X, Leader.Y, r);
                            if (es.Count == 0) continue;
                            c.Cd = baseCd; c.Kick = 0.3f;
                            foreach (var e in es)
                            {
                                float dx = e.X - Leader.X, dy = e.Y - Leader.Y;
                                float d = Mathf2.Hypot(dx, dy);
                                if (d <= 0f) d = 1f;
                                float kb = Sv(t, "comp", "knock") * (e.Def.Boss ? 0.25f : 1f) * Mathf2.Min(1.6f, mul);
                                e.X = Mathf2.Clamp(e.X + dx / d * kb, 0.5f, Arena - 0.5f);
                                e.Y = Mathf2.Clamp(e.Y + dy / d * kb, 0.5f, Arena - 0.5f);
                                Hit(e, UnitDamage(t, Sv(t, "comp", "dmg") * mul), FxColors.Steam);
                            }
                            Bus.Emit(FxKind.Ring, Leader.X, Leader.Y, 0.45f, r, "127,211,195");
                            break;
                        }
                }
            }
        }

        void TickMarks(float dt)
        {
            foreach (var f in _marks)
            {
                f.Life += dt;
                if (f.Done || f.Life < f.MaxLife - 0.02f) continue;
                f.Done = true;
                foreach (var e in Near(f.X, f.Y, f.Radius)) Hit(e, f.Damage, FxColors.Fire, true);
                Bus.Burst(f.X, f.Y, 26, FxColors.Orange, 3.4f, 0.08f, 0.6f);
                Bus.Burst(f.X, f.Y, 10, "#FFE0A8", 1.8f, 0.1f, 0.8f);
                Bus.Emit(FxKind.Ring, f.X, f.Y, 0.4f, f.Radius, "255,154,60");
                Bus.RequestShake(0.15f);
            }
            _marks.RemoveAll(f => f.Life >= f.MaxLife);
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Advances the run. <paramref name="move"/> is a normalized intent from input.</summary>
        public void Tick(float dt, Vec2 move)
        {
            if (Outcome != SurvOutcome.Running) return;

            Time += dt;

            float mx = move.X, my = move.Y;
            if (float.IsNaN(mx) || float.IsNaN(my) || float.IsInfinity(mx) || float.IsInfinity(my)) { mx = 0f; my = 0f; }
            float ml = Mathf2.Hypot(mx, my);
            if (ml > 1f) { mx /= ml; my /= ml; }

            Leader.X = Mathf2.Clamp(Leader.X + mx * Mods.MoveSpeed * dt, 0.5f, Arena - 0.5f);
            Leader.Y = Mathf2.Clamp(Leader.Y + my * Mods.MoveSpeed * dt, 0.5f, Arena - 0.5f);
            if (MathF.Abs(mx) > 0.1f) Leader.Face = Math.Sign(mx);
            Leader.Moving = ml > 0.1f;

            _trailTimer -= dt;
            if (_trailTimer <= 0f)
            {
                _trailTimer = 0.04f;
                _trail.Add(new Vec2(Leader.X, Leader.Y));
                if (_trail.Count > 120) _trail.RemoveAt(0);
            }

            Leader.Kick = Mathf2.Max(0f, Leader.Kick - dt);
            Leader.HurtT = Mathf2.Max(0f, Leader.HurtT - dt);

            TickWaves(dt);
            TickBosses();
            LeaderWeapon(dt);
            TickCompanions(dt);
            TickShots(dt);
            TickEnemyShots(dt);
            TickMarks(dt);
            TickEnemies(dt);
            TickGems(dt);
            TickLevels();
            TickMilestones();

            // Endless: the only finish is the leader falling (or the player leaving).
            if (Leader.Hp <= 0f) { Leader.Hp = 0f; Outcome = SurvOutcome.Defeated; }
        }

        void TickEnemies(float dt)
        {
            // Spatial buckets keep separation O(n) rather than O(n^2) at 300+ enemies.
            // The dictionary and its lists are reused; only Clear() runs per tick.
            foreach (var cell in _grid.Values) cell.Clear();
            foreach (var e in _enemies)
            {
                int k = ((int)e.X << 8) | (int)e.Y;
                if (!_grid.TryGetValue(k, out var cell)) _grid[k] = cell = new List<SurvEnemy>();
                cell.Add(e);
            }
            var grid = _grid;

            float touching = 0f;
            foreach (var e in _enemies)
            {
                if (e.Dead) continue;
                e.HitT = Mathf2.Max(0f, e.HitT - dt);
                e.BrushT = Mathf2.Max(0f, e.BrushT - dt);
                if (e.SlowT > 0f) e.SlowT -= dt;

                float dx = Leader.X - e.X, dy = Leader.Y - e.Y;
                float d = Mathf2.Hypot(dx, dy);
                if (d <= 0f) d = 1f;

                if (d < e.Radius + 0.3f) touching += e.ContactDps;

                if (e.FrozenT > 0f) { e.FrozenT -= dt; continue; }

                float sp = e.Speed * (1f - (e.SlowT > 0f ? e.SlowAmt : 0f)) * (1f - Mods.EnemySlow);

                if (e.IsRanged)
                {
                    // Shooters hold a stand-off ring: close in from outside, back away when
                    // crowded, and fire on their own cadence. Stepping aside breaks the shot.
                    float inner = e.Range * 0.7f;
                    if (d > e.Range) { e.X += dx / d * sp * dt; e.Y += dy / d * sp * dt; }
                    else if (d < inner) { e.X -= dx / d * sp * dt; e.Y -= dy / d * sp * dt; }

                    e.ShotT -= dt;
                    if (e.ShotT <= 0f && e.ShotCd > 0f)
                    {
                        e.ShotT = e.ShotCd;
                        float inv = d <= 0f ? 1f : d;
                        _enemyShots.Add(new SurvEnemyShot
                        {
                            X = e.X, Y = e.Y,
                            Vx = dx / inv * e.ShotSpeed,
                            Vy = dy / inv * e.ShotSpeed,
                            Damage = e.ShotDmg,
                        });
                        Bus.Burst(e.X, e.Y, 3, "#9BBF6A", 1.2f, 0.05f, 0.25f);
                    }
                }
                else if (d > e.Radius + 0.25f)
                {
                    e.X += dx / d * sp * dt;
                    e.Y += dy / d * sp * dt;
                }

                int cx = (int)e.X, cy = (int)e.Y;
                for (int gx = cx - 1; gx <= cx + 1; gx++)
                    for (int gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        if (!grid.TryGetValue((gx << 8) | gy, out var cell)) continue;
                        foreach (var o in cell)
                        {
                            if (ReferenceEquals(o, e) || o.Dead) continue;
                            float ox = e.X - o.X, oy = e.Y - o.Y;
                            float od = Mathf2.Hypot(ox, oy);
                            float mn = (e.Radius + o.Radius) * 0.85f;
                            if (od > 0f && od < mn)
                            {
                                float push = (mn - od) * 0.5f;
                                e.X += ox / od * push;
                                e.Y += oy / od * push;
                            }
                        }
                    }

                e.X = Mathf2.Clamp(e.X, 0.4f, Arena - 0.4f);
                e.Y = Mathf2.Clamp(e.Y, 0.4f, Arena - 0.4f);
            }

            if (touching > 0f)
            {
                Leader.Hp -= touching * dt;
                if (Leader.HurtT <= 0f)
                {
                    Leader.HurtT = 0.25f;
                    Bus.Burst(Leader.X, Leader.Y - 0.2f, 4, FxColors.RedSoft, 1.6f, 0.05f, 0.3f);
                }
            }

            foreach (var e in _enemies)
            {
                if (e.Dead || e.Hp > 0f) continue;
                e.Dead = true;
                Kills++;

                string col = FxColors.ForEnemyLook(e.Def.Look);
                Bus.Burst(e.X, e.Y, e.Def.Boss ? 40 : 7, col,
                          e.Def.Boss ? 3.4f : 1.8f, e.Def.Boss ? 0.1f : 0.06f, 0.5f);

                if (e.Def.Boss)
                {
                    Boss = null;
                    Bus.Banner($"{e.Def.Name} 처치!", 2f);
                    Bus.RequestShake(0.5f);
                    // A boss scatters its experience so it cannot all be missed in one spot.
                    for (int i = 0; i < 12; i++)
                        _gems.Add(new SurvGem
                        {
                            X = e.X + (_rng.Value - 0.5f) * 1.6f,
                            Y = e.Y + (_rng.Value - 0.5f) * 1.6f,
                            Value = MathF.Ceiling(e.Xp / 12f),
                            Big = true,
                        });
                }
                else _gems.Add(new SurvGem { X = e.X, Y = e.Y, Value = e.Xp, Big = e.Xp >= 3f });
            }
            _enemies.RemoveAll(e => e.Dead);
        }

        void TickGems(float dt)
        {
            foreach (var g in _gems)
            {
                float dx = Leader.X - g.X, dy = Leader.Y - g.Y;
                float d = Mathf2.Hypot(dx, dy);
                if (!g.Pulling && d > Mods.Pickup) continue;

                g.Pulling = true;
                g.Speed += dt * 14f;
                float s = Mathf2.Min(d, (6f + g.Speed) * dt);
                float dd = d <= 0f ? 1f : d;
                g.X += dx / dd * s;
                g.Y += dy / dd * s;
                if (d < 0.3f) { g.Collected = true; Xp += g.Value * Mods.XpMul; }
            }
            _gems.RemoveAll(g => g.Collected);
            if (_gems.Count > 500) _gems.RemoveRange(0, _gems.Count - 500);
        }

        void TickLevels()
        {
            while (Xp >= XpNeeded)
            {
                Xp -= XpNeeded;
                Level++;
                // The quadratic term is what keeps late levels from arriving every few seconds.
                float n = Level - 1;
                XpNeeded = MathF.Round(_cfg.survXpBase + _cfg.survXpStep * n + _cfg.survXpQuad * n * n);
                PendingLevelUps++;
            }
        }

        // ------------------------------------------------------------------ level-up choices

        /// <summary>
        /// Builds up to three cards. A combo whose prerequisites are met is forced into the
        /// first slot so the pairing is never buried by random rolls.
        /// </summary>
        public List<SurvOption> RollOptions()
        {
            var pool = new List<SurvOption>();
            var forced = new List<SurvOption>();

            foreach (var c in Db.SurvCombos)
            {
                if (c.leader != Leader.Type || _combos.ContainsKey(c.id)) continue;
                if (CompanionLevel(c.comp) >= _cfg.survCompMaxLv && Leader.Level >= 3)
                    forced.Add(new SurvOption(SurvOptionKind.Combo, c.id, 9f));
            }

            if (Leader.Level < _cfg.survWeaponMaxLv)
                pool.Add(new SurvOption(SurvOptionKind.Weapon, Leader.Type, 1.1f));

            if (_comps.Count < _cfg.survMaxComp)
                foreach (var t in _deck)
                    if (t != Leader.Type && _comps.All(c => c.Type != t) && Db.HasSvSlot(t, "comp"))
                        pool.Add(new SurvOption(SurvOptionKind.Join, t, 1.3f));

            foreach (var c in _comps)
                if (c.Level < _cfg.survCompMaxLv)
                    pool.Add(new SurvOption(SurvOptionKind.Up, c.Type, 1f));

            foreach (var k in new[] { "move", "hp" })
                if (_passives[k] < Sv(k, "passive", "max"))
                    pool.Add(new SurvOption(SurvOptionKind.Passive, k, 0.5f));

            var result = forced.Take(1).ToList();
            while (result.Count < 3 && pool.Count > 0)
            {
                float total = pool.Sum(o => o.Weight);
                float r = _rng.Value * total;
                int i = 0;
                for (; i < pool.Count; i++) { r -= pool[i].Weight; if (r <= 0f) break; }
                i = Math.Min(i, pool.Count - 1);
                result.Add(pool[i]);
                pool.RemoveAt(i);
            }

            // 10.15-h removed the heal card: with nothing left to take, the level pays coin.
            // Healing is what let a strong roster survive forever, which an endless run cannot allow.
            if (result.Count == 0) result.Add(new SurvOption(SurvOptionKind.Passive, "bonus", 1f));
            return result;
        }

        public SurvOptionView Describe(SurvOption o)
        {
            switch (o.Kind)
            {
                case SurvOptionKind.Weapon:
                    return new SurvOptionView
                    {
                        Option = o, Icon = Leader.Type,
                        Tag = $"리더 무기 Lv.{Leader.Level} → {Leader.Level + 1}",
                        Name = Db.Text($"surv.leader.{Leader.Type}.name"),
                        Desc = FillDesc(Db.Text($"surv.leader.{Leader.Type}.desc"), Db.SurvParamsOf(Leader.Type, "leader")),
                    };

                case SurvOptionKind.Join:
                    return new SurvOptionView
                    {
                        Option = o, Icon = o.Id,
                        Tag = IsSupport(o.Id) ? "새 동료 · 리더에 붙어요" : "새 동료",
                        Name = $"{Db.Units[o.Id].Name} · {Db.Text($"surv.comp.{o.Id}.name")}",
                        Desc = FillDesc(Db.Text($"surv.comp.{o.Id}.desc"), Db.SurvParamsOf(o.Id, "comp")),
                    };

                case SurvOptionKind.Up:
                    {
                        int lv = CompanionLevel(o.Id);
                        return new SurvOptionView
                        {
                            Option = o, Icon = o.Id,
                            Tag = $"동료 Lv.{lv} → {lv + 1}",
                            Name = $"{Db.Units[o.Id].Name} · {Db.Text($"surv.comp.{o.Id}.name")}",
                            Desc = FillDesc(Db.Text($"surv.comp.{o.Id}.desc"), Db.SurvParamsOf(o.Id, "comp")),
                        };
                    }

                case SurvOptionKind.Combo:
                    {
                        var c = Db.SurvCombos.First(x => x.id == o.Id);
                        return new SurvOptionView
                        {
                            Option = o, Icon = c.leader, Icon2 = c.comp, Hot = true,
                            Tag = "협동기",
                            Name = Db.Text(c.nameKey),
                            Desc = FillDesc(Db.Text(c.descKey), Db.SurvParamsOf(c.id, "combo")),
                        };
                    }

                default:
                    return new SurvOptionView
                    {
                        Option = o,
                        Glyph = o.Id == "move" ? "≫" : o.Id == "hp" ? "+" : "¢",
                        Tag = o.Id == "bonus" ? "보너스" : "강화",
                        Name = Db.Text($"surv.passive.{o.Id}"),
                        Desc = o.Id == "bonus"
                            ? $"뽑을 수 있는 강화를 다 모았어요. 코인 +{(int)Sv("bonus", "passive", "value")}"
                            : "모든 리더 공통 강화",
                    };
            }
        }

        /// <summary>Substitutes {key} and {key_pct} in a description from its parameter block.</summary>
        public static string FillDesc(string template, IReadOnlyDictionary<string, float> p)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+?)(_pct)?\}", m =>
            {
                if (!p.TryGetValue(m.Groups[1].Value, out var v)) return m.Value;
                return m.Groups[2].Success
                    ? ((int)Math.Round(v * 100)).ToString()
                    : GameDatabase.Round2(v);
            });
        }

        public void Apply(SurvOption o)
        {
            switch (o.Kind)
            {
                case SurvOptionKind.Weapon:
                    Leader.Level++;
                    break;

                case SurvOptionKind.Join:
                    {
                        bool sup = IsSupport(o.Id);
                        _comps.Add(new SurvCompanion
                        {
                            Type = o.Id,
                            Level = 1,
                            Cd = 0.5f,
                            Attached = sup,
                            Slot = _comps.Count(c => c.Attached == sup),
                            X = Leader.X,
                            Y = Leader.Y,
                        });
                        Bus.Burst(Leader.X, Leader.Y, 16, "#FFD08A", 2.4f, 0.06f, 0.5f);
                        break;
                    }

                case SurvOptionKind.Up:
                    {
                        var c = _comps.First(x => x.Type == o.Id);
                        c.Level++;
                        break;
                    }

                case SurvOptionKind.Combo:
                    {
                        _combos[o.Id] = true;
                        var c = Db.SurvCombos.First(x => x.id == o.Id);
                        Bus.Banner($"협동기 · {Db.Text(c.nameKey)}!", 2f);
                        Bus.RequestShake(0.3f);
                        Bus.Burst(Leader.X, Leader.Y, 40, FxColors.Orange, 3.4f, 0.07f, 0.8f);
                        break;
                    }

                case SurvOptionKind.Passive:
                    if (o.Id == "bonus")
                        BonusCoin += (int)Sv("bonus", "passive", "value");
                    else
                    {
                        _passives[o.Id]++;
                        if (o.Id == "hp")
                        {
                            float v = Sv("hp", "passive", "value");
                            Leader.MaxHp += v;
                            Leader.Hp += v;
                        }
                    }
                    break;
            }

            PendingLevelUps = Math.Max(0, PendingLevelUps - 1);
            RecomputeMods();
        }

        public void Quit() { if (Outcome == SurvOutcome.Running) Outcome = SurvOutcome.Quit; }

        // ------------------------------------------------------------------ settlement

        public readonly struct SurvResult
        {
            public readonly bool Quit;
            /// <summary>Runs under 30s cost no daily attempt and pay no coin or parts.</summary>
            public readonly bool Counted;
            /// <summary>True when this run still had a daily coin/part attempt left.</summary>
            public readonly bool Rewarded;
            public readonly bool NewRecord;
            public readonly long Coin;
            public readonly int Parts;
            /// <summary>Crystal banked from survival marks during the run.</summary>
            public readonly int Crystal;
            public readonly int LeaderShards;
            public readonly string LeaderId;
            public readonly int PassXp;
            public readonly int Seconds;
            public readonly int Kills;
            public readonly int Level;
            public readonly int BestToday;
            public readonly int BestEver;
            /// <summary>
            /// 오늘 쌓인 / 오늘 채울 수 있는 청소 일지 점수. 결과 화면이 "일지 30/150"
            /// 을 쓰려면 한 판의 수확만으로는 모자라고 **그날 누적**을 알아야 한다.
            /// 10.18-h 전에는 같은 자리에 크리스탈이 들어갔다.
            /// </summary>
            public readonly int PoolToday;
            public readonly int PoolTotal;
            /// <summary>부품·코인을 받을 수 있는 남은 판 수와 하루 한도.</summary>
            public readonly int RunsLeft;
            public readonly int RunsTotal;

            public SurvResult(bool quit, bool counted, bool rewarded, bool newRecord,
                              long coin, int parts, int crystal, int leaderShards, string leaderId,
                              int passXp, int seconds, int kills, int level, int bestToday, int bestEver,
                              int poolToday, int poolTotal, int runsLeft, int runsTotal)
            {
                Quit = quit; Counted = counted; Rewarded = rewarded; NewRecord = newRecord;
                Coin = coin; Parts = parts; Crystal = crystal;
                LeaderShards = leaderShards; LeaderId = leaderId; PassXp = passXp;
                Seconds = seconds; Kills = kills; Level = level;
                BestToday = bestToday; BestEver = bestEver;
                PoolToday = poolToday; PoolTotal = poolTotal;
                RunsLeft = runsLeft; RunsTotal = runsTotal;
            }
        }

        /// <summary>오늘 청소 일지에 쌓인 점수와 오늘의 최대치.</summary>
        int PoolPointsToday() => _profile.Pool.pts;

        int PoolPointsMaxToday() => _profile.PoolMaxPoints;

        int RunsLeftToday(SurvivorRecord day)
            => Math.Max(0, _cfg.survDailyRewards - day.runs);

        bool _settled;

        /// <summary>
        /// Pays out the run and updates the daily record. Crystal was already banked by
        /// TickMilestones, so only coin, parts and leader shards are settled here. Guarded so
        /// a double call (result screen reopened, app resumed) cannot pay twice.
        /// </summary>
        public SurvResult Settle()
        {
            var day = _profile.SurvivorToday();
            int seconds = (int)Math.Floor(Time);

            if (_settled)
                return new SurvResult(Outcome == SurvOutcome.Quit, seconds >= 30, false, false,
                                      0, 0, CrystalEarned, 0, Leader.Type, 0,
                                      seconds, Kills, Level, day.bestT, day.bestEver,
                                      PoolPointsToday(), PoolPointsMaxToday(),
                                      RunsLeftToday(day), _cfg.survDailyRewards);
            _settled = true;

            bool quit = Outcome == SurvOutcome.Quit;
            bool counted = seconds >= 30;
            bool rewarded = counted && day.runs < _cfg.survDailyRewards;
            bool newRecord = seconds > day.bestEver;

            day.bestT = Math.Max(day.bestT, seconds);
            day.bestKills = Math.Max(day.bestKills, Kills);
            day.bestEver = Math.Max(day.bestEver, seconds);
            day.leader = Leader.Type;

            long coin = 0;
            int parts = 0, shards = 0;

            if (rewarded)
            {
                day.runs++;
                parts = seconds / 30 * _cfg.survPartPer30s;
                coin = (long)Kills * _cfg.survCoinPerKill + BonusCoin;

                if (coin > 0) _profile.Data.AddItem(PlayerProfile.CoinItemId, coin);
                if (parts > 0)
                    foreach (var partId in Db.PartOfFamily.Values)
                        _profile.Data.AddItem(partId, parts);

                // One shard of the leader per full minute, capped, so a long run also
                // feeds that leader's limit-break.
                shards = Math.Min(_cfg.survShardMax, seconds / 60 * _cfg.survShardPerMin);
                if (shards > 0 && Db.ShardOfUnit.TryGetValue(Leader.Type, out var shardItem))
                    _profile.Data.AddItem(shardItem, shards);
                else shards = 0;
            }

            int passXp = counted
                ? Math.Min(_cfg.passXpSurvMax, seconds / 30 * _cfg.passXpSurvPer30s)
                : 0;

            _profile.MarkDirty();
            return new SurvResult(quit, counted, rewarded, newRecord, coin, parts, CrystalEarned,
                                  shards, Leader.Type, passXp, seconds, Kills, Level,
                                  day.bestT, day.bestEver,
                                  PoolPointsToday(), PoolPointsMaxToday(),
                                  RunsLeftToday(day), _cfg.survDailyRewards);
        }

        /// <summary>Appliances that can lead a run, in roster order.</summary>
        public static List<string> Leaders(GameDatabase db)
            => db.UnitOrder.Where(t => db.HasSvSlot(t, "leader")).ToList();
    }
}
