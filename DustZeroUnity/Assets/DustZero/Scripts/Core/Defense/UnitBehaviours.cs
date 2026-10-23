// Per-appliance combat behaviour: normal attacks, skill trigger conditions and skill effects.
//
// Every coefficient comes from skill_param, so the kits here describe only the shape of each
// ability. A number changed in 02_Unit.xlsx lands in both the effect and its description text
// without touching this file.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Defense
{
    internal sealed class UnitBehaviours
    {
        readonly BattleSim _s;
        GameDatabase Db => _s.Db;

        public UnitBehaviours(BattleSim sim) { _s = sim; }

        /// <summary>A skill radius from skill_param, widened by the unit's boost tier.</summary>
        float SkillRange(BattleUnit u, string key) => u.Skp(key) + u.RangeAdd;

        // ------------------------------------------------------------------ shared attacks

        /// <summary>
        /// Melee swing. Prefers whatever this unit is currently holding, so a blocker keeps
        /// hitting the stain it stopped rather than retargeting to a passer-by.
        /// </summary>
        void MeleeAttack(BattleUnit u, float dt)
        {
            u.AtkT -= dt / u.SpdMul;
            if (u.AtkT <= 0f)
            {
                BattleEnemy t = null;
                foreach (var e in _s.Enemies)
                    if (ReferenceEquals(e.BlockedBy, u)) { t = e; break; }
                t ??= _s.FrontTarget(u, u.Range, u.Def.AtkTarget);

                if (t != null)
                {
                    _s.Hurt(t, u.Atk * u.AtkMul, by: u);
                    u.AtkT = u.Def.Interval;
                    var p = _s.PositionOf(t);
                    _s.Bus.Emit(FxKind.Hit, p.X, p.Y, 0.2f);
                    _s.Bus.Burst(p.X, p.Y, 5, FxColors.Spark, 2.2f, 0.05f, 0.25f);
                    u.Kick = 0.15f;
                }
                else u.AtkT = 0f;
            }
            if (u.Kick > 0f) u.Kick -= dt;
        }

        void RangedAttack(BattleUnit u, float dt, Action<BattleEnemy> onHit)
        {
            u.AtkT -= dt / u.SpdMul;
            if (u.AtkT > 0f) return;
            var t = _s.FrontTarget(u, u.Range, u.Def.AtkTarget);
            if (t != null) { onHit(t); u.AtkT = u.Def.Interval; }
            else u.AtkT = 0f;
        }

        // ------------------------------------------------------------------ dispatch

        public void Act(BattleUnit u, float dt)
        {
            switch (u.Behaviour)
            {
                case "vac": Vac(u, dt); break;
                case "robot": MeleeAttack(u, dt); break;
                case "fridge": Fridge(u, dt); break;
                case "ac": Ac(u, dt); break;
                case "micro": Micro(u, dt); break;
                case "washer": Washer(u, dt); break;
                case "pc": Pc(u, dt); break;
                case "fan": Fan(u, dt); break;
                case "gen": Gen(u, dt); break;
                // 10.21-f
                case "blender": Blender(u, dt); break;
                case "purifier": Purifier(u, dt); break;
                case "router": Router(u, dt); break;
                case "tv": Tv(u, dt); break;
                // An appliance added to the table without a kit still attacks by its tile role,
                // rather than standing inert with no indication why.
                default:
                    if (u.IsBlocker) MeleeAttack(u, dt);
                    else RangedAttack(u, dt, t => _s.Hurt(t, u.Atk * u.AtkMul, by: u));
                    break;
            }
        }

        // ------------------------------------------------------------------ kits

        /// <summary>
        /// 믹서기: 체력이 얼마 안 남은 적에게 마무리 피해를 더한다. 분쇄가 돌면 공격
        /// 간격이 짧아지고, 때린 적 주위의 적도 같이 갈린다.
        /// </summary>
        void Blender(BattleUnit u, float dt)
        {
            bool on = u.SkillActive;
            // 원본은 u.spdMul 을 그 프레임에만 곱한다. 우리 SpdMul 은 상태라서
            // 여기서 곱해 두면 누적된다 — 나눌 때만 쓰고 되돌려 놓는다.
            float spd = on ? u.SpdMul / u.Skp("grindItvMul") : u.SpdMul;

            float execHp = u.Skp("execHp"), execBonus = u.Skp("execBonus");
            float Exec(BattleEnemy e) => e.Hp <= e.MaxHp * execHp ? 1f + execBonus : 1f;

            u.AtkT -= dt / spd;
            if (u.AtkT <= 0f)
            {
                BattleEnemy t = null;
                foreach (var e in _s.Enemies)
                    if (ReferenceEquals(e.BlockedBy, u)) { t = e; break; }
                t ??= _s.FrontTarget(u, u.Range, u.Def.AtkTarget);

                if (t != null)
                {
                    float m = Exec(t);
                    _s.Hurt(t, u.Atk * u.AtkMul * m, m > 1f ? FxColors.Blender : null, u);
                    u.AtkT = u.Def.Interval;
                    var p = _s.PositionOf(t);
                    _s.Bus.Emit(FxKind.Hit, p.X, p.Y, 0.2f);
                    _s.Bus.Burst(p.X, p.Y, 5, m > 1f ? FxColors.Blender : FxColors.Spark, 2.2f, 0.05f, 0.25f);
                    u.Kick = 0.15f;

                    if (on)
                    {
                        float r = u.Skp("grindRadius") + 0.01f;
                        foreach (var e in _s.Enemies)
                        {
                            if (ReferenceEquals(e, t)) continue;
                            if (!BattleSim.CanHit(e, u.Def.SkillTarget)) continue;
                            if (_s.DistanceTo(u, e) > r) continue;
                            _s.Hurt(e, u.Atk * u.AtkMul * Exec(e), FxColors.Blender, u);
                        }
                        _s.Bus.Emit(FxKind.Ring, u.X, u.Y, 0.3f, u.Skp("grindRadius"));
                    }
                }
                else u.AtkT = 0f;
            }
            if (u.Kick > 0f) u.Kick -= dt;
        }

        /// <summary>
        /// 공기청정기: 공중을 먼저 노리고 공중에 더 아프다. 강력 정화가 돌면 사거리
        /// 안의 적을 주기로 때리고 공중 적을 둔화시킨다.
        /// </summary>
        void Purifier(BattleUnit u, float dt)
        {
            float air = u.Skp("airMul");
            float Air(BattleEnemy e) => e.IsFlying ? air : 1f;

            if (u.SkillActive)
            {
                u.Tick -= dt;
                if (u.Tick <= 0f)
                {
                    u.Tick = u.Skp("tick");
                    _s.Bus.Emit(FxKind.Ring, u.X, u.Y, 0.5f, u.Range);
                    float dmg = u.Skp("skillDmg") * u.Power * u.AtkMul;
                    float slow = u.Skp("skillSlow"), slowSec = u.Skp("skillSlowSec");
                    foreach (var e in _s.InRange(u, u.Range, u.Def.SkillTarget))
                    {
                        _s.Hurt(e, dmg * Air(e), FxColors.Purifier, u);
                        if (e.IsFlying) _s.Slow(e, slow, slowSec);
                    }
                }
            }

            u.AtkT -= dt / u.SpdMul;
            if (u.AtkT > 0f) return;
            // 공중 먼저. 없으면 평소 대상으로 떨어진다.
            var t = _s.FrontTarget(u, u.Range, TargetType.Air) ??
                    _s.FrontTarget(u, u.Range, u.Def.AtkTarget);
            if (t != null)
            {
                _s.Hurt(t, u.Atk * u.AtkMul * Air(t), FxColors.Purifier, u);
                u.AtkT = u.Def.Interval;
                var p = _s.PositionOf(t);
                _s.Bus.Burst(p.X, p.Y, 5, FxColors.Purifier, 1.4f, 0.05f, 0.3f);
            }
            else u.AtkT = 0f;
        }

        /// <summary>
        /// 공유기: 평타가 가까운 적에게 차례로 튄다(튈 때마다 피해가 줄어든다).
        /// 신호 폭주가 돌면 더 많이 튀고, 더 자주 쏘고, 맞은 적이 기절한다.
        /// </summary>
        void Router(BattleUnit u, float dt)
        {
            bool on = u.SkillActive;
            float spd = on ? u.SpdMul / u.Skp("skillItvMul") : u.SpdMul;

            u.AtkT -= dt / spd;
            if (u.AtkT > 0f) return;
            var first = _s.FrontTarget(u, u.Range, u.Def.AtkTarget);
            if (first == null) { u.AtkT = 0f; return; }
            u.AtkT = u.Def.Interval;

            int n = (int)Math.Round(u.Skp("chainN") + (on ? u.Skp("skillChainAdd") : 0f));
            float decay = u.Skp("chainDecay"), reach = u.Skp("chainRange"), stun = u.Skp("stun");
            var hit = new List<BattleEnemy> { first };
            var cur = first;
            float dmg = u.Atk * u.AtkMul;

            // 원본은 i<=n 이라 첫 대상 + n 번 튀는 것이다. 'n 번 맞는다' 로 읽으면 한 번 모자란다.
            for (int i = 0; i <= n && cur != null; i++)
            {
                _s.Hurt(cur, dmg, FxColors.Router, u);
                if (on) _s.Stun(cur, stun);
                var p = _s.PositionOf(cur);
                _s.Bus.Burst(p.X, p.Y, 3, FxColors.Router, 1.2f, 0.04f, 0.25f);
                dmg *= decay;

                BattleEnemy next = null;
                float best = reach;
                foreach (var e in _s.Enemies)
                {
                    if (e.Dead || hit.Contains(e)) continue;
                    if (!BattleSim.CanHit(e, u.Def.AtkTarget)) continue;
                    var q = _s.PositionOf(e);
                    float d = Mathf2.Hypot(q.X - p.X, q.Y - p.Y);
                    if (d < best) { best = d; next = e; }
                }
                if (next != null) hit.Add(next);
                cur = next;
            }
        }

        /// <summary>
        /// 스마트TV: 평타가 화면 빔이라 맞은 자리 둘레도 함께 맞는다.
        /// 광고 시간(스킬 발동)은 <see cref="OnSkillStart"/> 에 있다.
        /// </summary>
        void Tv(BattleUnit u, float dt)
        {
            RangedAttack(u, dt, t =>
            {
                var tp = _s.PositionOf(t);
                float r = u.Skp("splashRadius");
                foreach (var e in _s.Enemies)
                {
                    if (!BattleSim.CanHit(e, u.Def.AtkTarget)) continue;
                    var p = _s.PositionOf(e);
                    if (Mathf2.Hypot(p.X - tp.X, p.Y - tp.Y) <= r)
                        _s.Hurt(e, u.Atk * u.AtkMul, FxColors.Tv, u);
                }
                _s.Bus.Burst(tp.X, tp.Y, 8, FxColors.Tv, 1.8f, 0.05f, 0.35f);
            });
        }

        /// <summary>Vacuum: drags ground stains into itself and grinds them down.</summary>
        void Vac(BattleUnit u, float dt)
        {
            if (u.SkillActive)
            {
                float radius = SkillRange(u, "pullRadius");
                float dps = u.Skp("pullDps");
                float pullSpeed = u.Skp("pullSpeed");
                float holdDist = u.Skp("holdDist");

                foreach (var e in _s.Enemies)
                {
                    if (!BattleSim.CanHit(e, u.Def.SkillTarget)) continue;
                    if (_s.DistanceTo(u, e) > radius) continue;

                    e.PullBy = u;
                    if (e.BlockedBy != null && !ReferenceEquals(e.BlockedBy, u)) e.BlockedBy = null;

                    _s.Credit(u, e, dps * u.Power * u.AtkMul * dt);
                    e.HitT = Mathf2.Max(e.HitT, 0.04f);

                    if (e.IsFlying) continue;

                    float dx = u.X - e.X, dy = u.Y - e.Y;
                    float dd = Mathf2.Hypot(dx, dy);
                    float step = pullSpeed * dt;
                    if (dd > 0.45f)
                    {
                        float m = Mathf2.Min(step, dd - 0.45f);
                        e.X += dx / dd * m;
                        e.Y += dy / dd * m;
                    }
                    e.Cx = Mathf2.Clamp((int)Math.Floor(e.X), 0, _s.Field.Cols - 1);
                    e.Cy = Mathf2.Clamp((int)Math.Floor(e.Y), 0, _s.Field.Rows - 1);
                    // Dragging can land a stain on furniture; snap it back onto the vacuum's tile.
                    if (!_s.Field.Walkable(e.Cx, e.Cy)) { e.Cx = u.Col; e.Cy = u.Row; }
                    e.TargetSet = false;

                    if (dd < holdDist && e.BlockedBy == null) { e.BlockedBy = u; e.AtkT = 0.4f; }
                }
            }
            MeleeAttack(u, dt);
        }

        /// <summary>Fridge: freezes everything nearby solid while the skill runs.</summary>
        void Fridge(BattleUnit u, float dt)
        {
            if (u.SkillActive)
            {
                float radius = SkillRange(u, "freezeRadius");
                float dps = u.Skp("freezeDps");
                foreach (var e in _s.InRange(u, radius, u.Def.SkillTarget))
                {
                    e.FrozenT = Mathf2.Max(e.FrozenT, 0.25f);
                    _s.Credit(u, e, dps * u.Power * dt);
                    if (_s.Rand < dt * 4f)
                    {
                        var p = _s.PositionOf(e);
                        _s.Bus.Burst(p.X, p.Y, 2, FxColors.IcePale, 0.8f, 0.05f, 0.5f);
                    }
                }
            }
            MeleeAttack(u, dt);
        }

        /// <summary>Air conditioner: chilling shots that slow, plus a pulsing area blast.</summary>
        void Ac(BattleUnit u, float dt)
        {
            if (u.SkillActive)
            {
                u.Tick -= dt;
                if (u.Tick <= 0f)
                {
                    u.Tick = u.Skp("tick");
                    _s.Bus.Emit(FxKind.Ring, u.X, u.Y, 0.5f, u.Range, "61,139,217");
                    foreach (var e in _s.InRange(u, u.Range, u.Def.SkillTarget))
                    {
                        _s.Hurt(e, u.Skp("skillDmg") * u.Power * u.AtkMul, FxColors.Ice, by: u);
                        _s.Slow(e, u.Skp("skillSlow"), u.Skp("skillSlowSec"));
                    }
                    _s.Bus.Burst(u.X, u.Y, 8, FxColors.White, u.Range * 0.9f, 0.05f, 0.7f);
                }
            }

            RangedAttack(u, dt, t =>
            {
                _s.Hurt(t, u.Atk * u.AtkMul, FxColors.Ice, by: u);
                _s.Slow(t, u.Skp("atkSlow"), u.Skp("atkSlowSec"));
                var p = _s.PositionOf(t);
                float py = p.Y + (t.Def.Fly ? -0.18f : 0f);
                _s.Bus.Emit(FxKind.Proj, u.X, u.Y - 0.15f, 0.16f, 0f, "#9ED0EC", 0.09f, p.X, py);
                _s.Bus.Burst(p.X, py, 6, "#DDF1FD", 1.6f, 0.05f, 0.35f, 0f, 0.16f);
            });
        }

        /// <summary>Microwave: holds fire while charging, then detonates a delayed blast.</summary>
        void Micro(BattleUnit u, float dt)
        {
            if (u.SkillActive) return;
            RangedAttack(u, dt, t =>
            {
                _s.Hurt(t, u.Atk * u.AtkMul, FxColors.Fire, by: u);
                var p = _s.PositionOf(t);
                float py = p.Y + (t.Def.Fly ? -0.18f : 0f);
                _s.Bus.Emit(FxKind.Proj, u.X, u.Y - 0.15f, 0.18f, 0f, FxColors.Orange, 0.11f, p.X, py);
                _s.Bus.Burst(p.X, py, 8, "#FFC27A", 2.4f, 0.06f, 0.35f, 0f, 0.18f);
            });
        }

        /// <summary>Washer: splashing lobs, and a spin cycle that knocks loose stains back.</summary>
        void Washer(BattleUnit u, float dt)
        {
            if (u.SkillActive)
            {
                u.Tick -= dt;
                if (u.Tick <= 0f)
                {
                    u.Tick = u.Skp("tick");
                    _s.Bus.Emit(FxKind.Swirl, u.X, u.Y, 0.5f, u.Range);
                    _s.Bus.Burst(u.X, u.Y, 6, "#BFE0F2", 1.8f, 0.07f, 0.6f);

                    foreach (var e in _s.InRange(u, u.Range, u.Def.SkillTarget))
                    {
                        _s.Hurt(e, u.Skp("skillDmg") * u.Power * u.AtkMul, FxColors.Ice, by: u);
                        // Only free stains get shoved; held or vacuumed ones stay put.
                        if (e.BlockedBy != null || e.PullBy != null) continue;

                        float kb = e.Def.Weight > 0
                            ? u.Skp("bossKnockback")
                            : u.Skp("knockback");

                        if (e.IsFlying) e.D = Mathf2.Max(0f, e.D - kb);
                        else
                        {
                            float bx = e.Cx + 0.5f - e.X, by = e.Cy + 0.5f - e.Y;
                            float bd = Mathf2.Hypot(bx, by);
                            if (bd > 1e-3f)
                            {
                                float m = Mathf2.Min(kb, bd);
                                e.X += bx / bd * m;
                                e.Y += by / bd * m;
                            }
                        }
                    }
                }
            }

            RangedAttack(u, dt, t =>
            {
                var tp = _s.PositionOf(t);
                float splash = u.Skp("splashRadius");
                foreach (var e in _s.Enemies)
                {
                    if (!BattleSim.CanHit(e, u.Def.AtkTarget)) continue;
                    var p = _s.PositionOf(e);
                    if (Mathf2.Hypot(p.X - tp.X, p.Y - tp.Y) <= splash)
                        _s.Hurt(e, u.Atk * u.AtkMul, FxColors.Ice, by: u);
                }
                _s.Bus.Emit(FxKind.Lob, u.X, u.Y - 0.15f, 0.3f, 0f, null, 0f, tp.X, tp.Y);
                _s.Bus.Burst(tp.X, tp.Y, 10, FxColors.Water, 2.2f, 0.06f, 0.45f, 6f, 0.3f);
                _s.Bus.Emit(FxKind.Splash, tp.X, tp.Y, 0.35f);
            });
        }

        /// <summary>PC: pure support. Its aura is applied in BattleSim.ApplyAuras.</summary>
        void Pc(BattleUnit u, float dt)
        {
            if (!u.SkillActive || _s.Rand >= dt * 10f) return;
            float rng = SkillRange(u, "skillRange");
            foreach (var o in _s.Units)
            {
                if (ReferenceEquals(o, u)) continue;
                if (Mathf2.Hypot(o.X - u.X, o.Y - u.Y) > rng) continue;
                _s.Bus.Burst(o.X + (_s.Rand - 0.5f) * 0.5f, o.Y, 1, FxColors.Mint, 0.9f, 0.045f, 0.6f, 0f, 0f, true);
            }
        }

        /// <summary>Fan: pure cooling. Its aura is applied in BattleSim.ApplyAuras.</summary>
        void Fan(BattleUnit u, float dt)
        {
            if (!u.SkillActive || _s.Rand >= dt * 14f) return;
            float rng = SkillRange(u, "skillRange");
            foreach (var o in _s.Units)
            {
                if (!o.IsBlocker) continue;
                if (Mathf2.Hypot(o.X - u.X, o.Y - u.Y) > rng) continue;
                _s.Bus.Burst(u.X, u.Y - 0.2f, 1, FxColors.Steam, 1.6f, 0.05f, 0.6f);
            }
        }

        /// <summary>Generator: pure power. Its regen is folded into BattleSim.PowerRegen.</summary>
        void Gen(BattleUnit u, float dt)
        {
            if (_s.Rand < dt * (u.SkillActive ? 18f : 2f))
                _s.Bus.Burst(u.X + (_s.Rand - 0.5f) * 0.4f, u.Y - 0.3f, 1, "#FFE27A", 1.2f, 0.04f, 0.3f);
        }

        // ------------------------------------------------------------------ skill gating

        /// <summary>
        /// Whether a fully charged skill should fire now. Support units wait for someone to
        /// help and attackers wait for a target, so charge is never wasted on an empty board.
        /// </summary>
        public bool WantsSkill(BattleUnit u)
        {
            switch (u.Behaviour)
            {
                case "pc":
                    foreach (var o in _s.Units)
                        if (!ReferenceEquals(o, u) && o.Behaviour != "pc" && o.Behaviour != "fan" &&
                            Mathf2.Hypot(o.X - u.X, o.Y - u.Y) <= SkillRange(u, "skillRange")) return true;
                    return false;

                case "fan":
                    {
                        float rng = SkillRange(u, "coolRange") + 0.01f;
                        float trigger = u.Skp("triggerHeat");
                        foreach (var o in _s.Units)
                            if (o.IsBlocker && Mathf2.Hypot(o.X - u.X, o.Y - u.Y) <= rng &&
                                (o.Overheated || o.Heat >= o.MaxHeat * trigger)) return true;
                        return false;
                    }

                case "robot":
                    return u.Heat >= u.MaxHeat * u.Skp("triggerHeat");

                case "gen":
                    return _s.Power <= _s.Cfg.powerMax - u.Skp("burst") * u.Support * u.BoostMul;

                case "vac":
                    return _s.InRange(u, SkillRange(u, "pullRadius"), u.Def.SkillTarget).Count > 0;

                case "fridge":
                    return _s.InRange(u, SkillRange(u, "freezeRadius"), u.Def.SkillTarget).Count > 0;

                // 분쇄는 **붙잡고 있을 때만** 쓸모가 있다. 지나가는 적에게 켜면 헛돈다.
                case "blender":
                    foreach (var e in _s.Enemies) if (ReferenceEquals(e.BlockedBy, u)) return true;
                    return false;

                // 광고 시간은 판을 멈추는 기술이라 적이 몰렸을 때나 보스가 있을 때만 쓴다.
                case "tv":
                    {
                        if (_s.Enemies.Count >= u.Skp("adMin")) return true;
                        foreach (var e in _s.Enemies) if (e.Def.Boss) return true;
                        return false;
                    }

                default:
                    return _s.InRange(u, u.Range, u.Def.SkillTarget).Count > 0;
            }
        }

        /// <summary>One-shot effects at the moment a skill fires.</summary>
        public void OnSkillStart(BattleUnit u)
        {
            switch (u.Behaviour)
            {
                case "fan":
                    {
                        float rng = SkillRange(u, "skillRange") + 0.01f;
                        float burst = u.Skp("skillBurst");
                        foreach (var o in _s.Units)
                            if (o.IsBlocker && Mathf2.Hypot(o.X - u.X, o.Y - u.Y) <= rng)
                                o.Heat = Mathf2.Max(0f, o.Heat - o.MaxHeat * burst);
                        break;
                    }
                case "robot":
                    u.Heat = 0f;
                    break;
                case "gen":
                    {
                        float burst = u.Skp("burst") * u.Support * u.BoostMul;
                        _s.AddPower(burst);
                        _s.Bus.Text(u.X, u.Y - 0.55f, $"+{(int)(burst * 100)}W", FxColors.Gold, true);
                        _s.Bus.Burst(u.X, u.Y, 14, "#FFD54A", 2.4f, 0.07f, 0.5f);
                        break;
                    }
                case "fridge":
                    u.Heat *= 1f - u.Skp("selfCool");
                    break;

                // 10.21-f 광고 시간: 판 위의 **모든** 적을 지속시간만큼 멈추고(보스는 1/3),
                // 그동안 받는 피해를 늘린다. 방어 깎기는 기절보다 vulnSec 만큼 더 간다 —
                // 멈춤이 풀린 뒤에도 잠깐은 더 아프게 맞으라는 뜻이다.
                case "tv":
                    {
                        float vuln = u.Skp("adVuln"), extra = u.Skp("vulnSec");
                        foreach (var e in _s.Enemies)
                        {
                            if (e.Dead) continue;
                            _s.Stun(e, u.SkillT);
                            _s.Break(e, vuln, u.SkillT + extra);
                            var p = _s.PositionOf(e);
                            _s.Bus.Burst(p.X, p.Y - 0.1f, 4, FxColors.Gold, 1.2f, 0.05f, 0.5f);
                        }
                        break;
                    }
            }
        }

        /// <summary>Effects that land when a skill's duration runs out.</summary>
        public void OnSkillEnd(BattleUnit u)
        {
            if (u.Behaviour == "vac")
            {
                // Release anything over the block limit that the pull had gathered.
                int load = 0;
                foreach (var e in _s.Enemies)
                {
                    if (!ReferenceEquals(e.BlockedBy, u)) continue;
                    int w = e.Def.LeakCost;
                    if (load + w > u.Block) e.BlockedBy = null;
                    else load += w;
                }
                return;
            }

            if (u.Behaviour != "micro") return;

            // The charge pays off here: a wide blast at the frontmost target, or a partial
            // refund when nothing is in range so the wind-up is not simply lost.
            float bonus = u.Skp("rangeBonus");
            var t = _s.FrontTarget(u, u.Range + bonus, u.Def.SkillTarget);
            if (t == null)
            {
                u.Sp = u.SkillChargeTime * u.Skp("noTargetRefund");
                return;
            }

            var tp = _s.PositionOf(t);
            float radius = u.Skp("blastRadius");
            float dmg = u.Skp("blastDmg");
            foreach (var e in _s.Enemies)
            {
                if (!BattleSim.CanHit(e, u.Def.SkillTarget)) continue;
                var p = _s.PositionOf(e);
                if (Mathf2.Hypot(p.X - tp.X, p.Y - tp.Y) <= radius)
                    _s.Hurt(e, dmg * u.Power * u.AtkMul, FxColors.Fire, by: u);
            }

            _s.Bus.Burst(tp.X, tp.Y, 26, FxColors.Fire, 3.6f, 0.09f, 0.55f);
            _s.Bus.Burst(tp.X, tp.Y, 12, "#8A8F94", 1.4f, 0.12f, 0.9f);
            _s.Bus.RequestShake(0.35f);
            _s.Bus.Emit(FxKind.Blast, tp.X, tp.Y, 0.6f);
        }
    }
}
