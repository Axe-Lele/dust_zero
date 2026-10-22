// 장비: common gear copies, exclusive gear per appliance, and what they do to the numbers.
//
// Ported from the 10.16-n build's gearAdd/gearEnhance/gearDismantle/gearFuse/gearEquip and
// gearMods/exclSkill. Two kinds live here and they behave differently on purpose:
//
//   공용 (grades 1-3) is a *copy*. Two 1101s level independently, so each carries a uid and
//   can be dismantled or fused away. A unit wears at most one.
//   전용 (grade 4) belongs to one appliance, is never a copy, and duplicates raise its star
//   instead of stacking - past the cap they turn into essence.
//
// Every number is read from the gear table and config; nothing about a specific piece is
// written here.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    /// <summary>What a pull or an exchange did, so the caller can word the toast.</summary>
    public enum GearGainKind { New, Awakened, Essence }

    public readonly struct GearGain
    {
        public readonly int GearId;
        public readonly GearGainKind Kind;
        /// <summary>Star reached when awakened, or essence paid when already at the cap.</summary>
        public readonly int Amount;

        public GearGain(int gearId, GearGainKind kind, int amount)
        {
            GearId = gearId; Kind = kind; Amount = amount;
        }
    }

    /// <summary>The stat bonuses an appliance gets from everything it is wearing.</summary>
    public readonly struct GearMods
    {
        public readonly float Atk, Heat, Sp;
        public GearMods(float atk, float heat, float sp) { Atk = atk; Heat = heat; Sp = sp; }

        /// <summary>Multiplier on attack, on heat capacity, and on skill charge time.</summary>
        public float AtkMul => 1f + Atk;
        public float HeatMul => 1f + Heat;
        public float ChargeMul => 1f - Sp;
    }

    /// <summary>
    /// An exclusive's edits to its appliance's skill coefficients. `dur` is handled on its
    /// own because the build treats it as a flat second count rather than a coefficient.
    /// </summary>
    public sealed class ExclusiveSkill
    {
        public readonly Dictionary<string, float> Mul = new();
        public readonly Dictionary<string, float> Add = new();
        public float Duration;

        /// <summary>Applies this edit to a raw skill_param value.</summary>
        public float Apply(string key, float raw)
        {
            float v = raw;
            if (Mul.TryGetValue(key, out var m)) v *= m;
            if (Add.TryGetValue(key, out var a)) v += a;
            return v;
        }
    }

    public sealed partial class PlayerProfile
    {
        /// <summary>Item id of the enhancement stone and of the exclusive essence.</summary>
        public const int GearStoneItem = 401, GearEssenceItem = 402;

        /// <summary>Gear's skill-charge reduction is capped, so stacking cannot reach zero.</summary>
        const float MaxChargeCut = 0.6f;

        // ------------------------------------------------------------------ lookups

        public OwnedGear GearInst(int uid)
        {
            if (uid == 0) return null;
            foreach (var g in Data.gear) if (g.uid == uid) return g;
            return null;
        }

        /// <summary>The appliance wearing this piece, or null when it is loose in the bag.</summary>
        public string GearOwner(int uid)
        {
            if (uid == 0) return null;
            foreach (var kv in Data.owned)
                if (kv.Value != null && kv.Value.eq == uid) return kv.Key;
            return null;
        }

        /// <summary>The exclusive gear row for an appliance, or null when it has none.</summary>
        public GearRow ExclusiveOf(string unitId)
            => unitId != null && Db.ExclusiveOfUnit.TryGetValue(unitId, out var r) ? r : null;

        public ExclusiveGear ExclusiveState(string unitId)
            => unitId != null && Data.excl.TryGetValue(unitId, out var x) ? x : null;

        /// <summary>A gear stat at a level: the table value, grown by gearStatPerLv per level.</summary>
        float GearStat(double baseValue, int lv)
            => (float)baseValue * (1f + _cfg.gearStatPerLv * lv);

        // ------------------------------------------------------------------ stats

        /// <summary>
        /// Everything an appliance is wearing, summed. Both the common piece and the
        /// exclusive contribute; the charge cut is clamped so no stack reaches zero.
        /// </summary>
        public GearMods ModsFor(string unitId)
        {
            var owned = Data.Own(unitId);
            if (owned == null) return new GearMods(0f, 0f, 0f);

            float atk = 0f, heat = 0f, sp = 0f;

            var common = GearInst(owned.eq);
            if (common != null && Db.Gear.TryGetValue(common.id, out var cr))
            {
                atk += GearStat(cr.atk, common.lv);
                heat += GearStat(cr.heat, common.lv);
                sp += GearStat(cr.sp, common.lv);
            }

            var x = ExclusiveState(unitId);
            var er = ExclusiveOf(unitId);
            if (x != null && er != null)
            {
                atk += GearStat(er.atk, x.lv);
                heat += GearStat(er.heat, x.lv);
                sp += GearStat(er.sp, x.lv);
            }

            return new GearMods(atk, heat, Math.Min(MaxChargeCut, sp));
        }

        /// <summary>
        /// The exclusive's skill edits, scaled by its star. An appliance with no exclusive
        /// gets an empty edit rather than null, so callers never branch on it.
        /// </summary>
        public ExclusiveSkill ExclusiveSkillOf(string unitId)
        {
            var result = new ExclusiveSkill();
            var x = ExclusiveState(unitId);
            var r = ExclusiveOf(unitId);
            if (x == null || r == null) return result;

            float f = 1f + _cfg.exclStarBonus * x.star;
            Edit(r.sk1Key, r.sk1Op, r.sk1Val);
            Edit(r.sk2Key, r.sk2Op, r.sk2Val);
            return result;

            void Edit(string key, string op, double value)
            {
                if (string.IsNullOrEmpty(key)) return;
                float v = (float)value;
                // The build special-cases `dur`: it is a flat second count on the skill, not
                // a coefficient, so it never goes through the mul/add tables.
                if (key == "dur") { result.Duration += v * f; return; }
                if (op == "mul") result.Mul[key] = 1f + (v - 1f) * f;
                else result.Add[key] = (result.Add.TryGetValue(key, out var prev) ? prev : 0f) + v * f;
            }
        }

        // ------------------------------------------------------------------ battle hooks

        // IUnitPowerSource. The sim asks for bare numbers so Core.Defense never sees a save
        // type; the lookups are cheap and happen at deploy, not per frame.

        public float GearAtkMul(string unitId) => ModsFor(unitId).AtkMul;
        public float GearHeatMul(string unitId) => ModsFor(unitId).HeatMul;
        public float GearChargeMul(string unitId) => ModsFor(unitId).ChargeMul;
        public float GearSkillDuration(string unitId) => ExclusiveSkillOf(unitId).Duration;

        /// <summary>One skill coefficient after this appliance's exclusive has edited it.</summary>
        public float SkillParam(string unitId, string key, float raw)
            => ExclusiveSkillOf(unitId).Apply(key, raw);

        // ------------------------------------------------------------------ acquiring

        /// <summary>
        /// Adds a piece. Grade 4 is exclusive: the first copy is the gear itself, later ones
        /// awaken it, and past the star cap they convert to essence. Everything else is a new
        /// copy in the bag.
        /// </summary>
        public GearGain AddGear(int gearId)
        {
            if (!Db.Gear.TryGetValue(gearId, out var r)) return new GearGain(gearId, GearGainKind.New, 0);

            if (r.grade == 4)
            {
                string t = r.unitId;
                var x = ExclusiveState(t);
                if (x == null)
                {
                    Data.excl[t] = new ExclusiveGear();
                    MarkDirty();
                    return new GearGain(gearId, GearGainKind.New, 0);
                }
                if (x.star < _cfg.exclMaxStar)
                {
                    x.star++;
                    MarkDirty();
                    return new GearGain(gearId, GearGainKind.Awakened, x.star);
                }
                Data.AddItem(GearEssenceItem, _cfg.exclDupeEssence);
                MarkDirty();
                return new GearGain(gearId, GearGainKind.Essence, _cfg.exclDupeEssence);
            }

            Data.gear.Add(new OwnedGear { uid = Data.gearUid++, id = gearId });
            MarkDirty();
            return new GearGain(gearId, GearGainKind.New, 0);
        }

        /// <summary>Picks a random piece of a grade, for a pull or a fusion result.</summary>
        public int RandomGearOf(int grade, IRandom rng)
        {
            var pool = Db.Gear.Values.Where(g => g.grade == grade).OrderBy(g => g.id).ToList();
            if (pool.Count == 0) return 0;
            return pool[Math.Min(pool.Count - 1, (int)(rng.Value * pool.Count))].id;
        }

        /// <summary>Rolls a grade from a banner's four weights.</summary>
        public static int RollGearGrade(int[] weights, IRandom rng)
        {
            int total = 0;
            foreach (var w in weights) total += w;
            if (total <= 0) return 1;
            float x = rng.Value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x < 0f) return i + 1;
            }
            return 1;
        }

        // ------------------------------------------------------------------ enhancing

        /// <summary>Stone and coin to take a piece of this grade from lv to lv+1, or null at the cap.</summary>
        public (int stone, int coin)? GearCost(int grade, int lv)
        {
            int i = Math.Clamp(grade - 1, 0, 3);
            int max = _cfg.gearMaxLv != null && i < _cfg.gearMaxLv.Length ? _cfg.gearMaxLv[i] : 0;
            if (lv >= max) return null;
            int stone = _cfg.gearStonePerLv[i] * (lv + 1);
            int coin = _cfg.gearCoinPerLv[i] * (lv + 1);
            return (stone, coin);
        }

        public (int stone, int coin)? CommonGearCost(int uid)
        {
            var g = GearInst(uid);
            return g != null && Db.Gear.TryGetValue(g.id, out var r) ? GearCost(r.grade, g.lv) : null;
        }

        public (int stone, int coin)? ExclusiveGearCost(string unitId)
        {
            var x = ExclusiveState(unitId);
            var r = ExclusiveOf(unitId);
            return x != null && r != null ? GearCost(r.grade, x.lv) : null;
        }

        /// <summary>
        /// Spends stone and coin to raise a common piece one level. The stone spent is
        /// remembered on the piece, because dismantling refunds a share of it.
        /// </summary>
        public bool EnhanceCommonGear(int uid)
        {
            var g = GearInst(uid);
            if (g == null || !Db.Gear.TryGetValue(g.id, out var r)) return false;
            var cost = GearCost(r.grade, g.lv);
            if (cost == null || !CanPay(cost.Value)) return false;
            Pay(cost.Value);
            g.lv++;
            g.sp += cost.Value.stone;
            MarkDirty();
            return true;
        }

        public bool EnhanceExclusiveGear(string unitId)
        {
            var x = ExclusiveState(unitId);
            var r = ExclusiveOf(unitId);
            if (x == null || r == null) return false;
            var cost = GearCost(r.grade, x.lv);
            if (cost == null || !CanPay(cost.Value)) return false;
            Pay(cost.Value);
            x.lv++;
            x.sp += cost.Value.stone;
            MarkDirty();
            return true;
        }

        bool CanPay((int stone, int coin) c)
            => ItemCount(GearStoneItem) >= c.stone && Coin >= c.coin;

        void Pay((int stone, int coin) c)
        {
            Data.AddItem(GearStoneItem, -c.stone);
            Data.AddItem(CoinItemId, -c.coin);
        }

        // ------------------------------------------------------------------ dismantle / fuse

        /// <summary>
        /// Breaks a loose piece down. Equipped gear is refused rather than silently
        /// unequipped: losing what an appliance is wearing should never be a side effect.
        /// Returns the stone gained, or 0 when nothing happened.
        /// </summary>
        public int DismantleGear(int uid)
        {
            var g = GearInst(uid);
            if (g == null || GearOwner(uid) != null) return 0;
            if (!Db.Gear.TryGetValue(g.id, out var r)) return 0;

            int n = _cfg.gearDismantle[Math.Clamp(r.grade - 1, 0, 2)]
                  + (int)Math.Floor(g.sp * _cfg.gearRefund);
            Data.gear.Remove(g);
            Data.AddItem(GearStoneItem, n);
            MarkDirty();
            return n;
        }

        /// <summary>Every loose grade-1 piece, which is what 일괄 분해 acts on.</summary>
        public List<OwnedGear> LooseGearOfGrade(int grade)
            => Data.gear.Where(g => Db.Gear.TryGetValue(g.id, out var r) && r.grade == grade
                                    && GearOwner(g.uid) == null).ToList();

        /// <summary>The stone a batch dismantle would yield, for the button's subtitle.</summary>
        public int DismantleYield(IEnumerable<OwnedGear> pieces)
        {
            int n = 0;
            foreach (var g in pieces)
                if (Db.Gear.TryGetValue(g.id, out var r))
                    n += _cfg.gearDismantle[Math.Clamp(r.grade - 1, 0, 2)]
                       + (int)Math.Floor(g.sp * _cfg.gearRefund);
            return n;
        }

        /// <summary>
        /// The pieces a fusion would consume: the lowest-levelled loose ones of that grade.
        /// Spending the least-invested copies first is what makes the button safe to press.
        /// </summary>
        public List<OwnedGear> GearFusePick(int grade)
            => LooseGearOfGrade(grade).OrderBy(g => g.lv).ThenBy(g => g.uid)
               .Take(_cfg.gearFuseNeed).ToList();

        public bool CanFuse(int grade)
            => grade < 3 && GearFusePick(grade).Count >= _cfg.gearFuseNeed;

        /// <summary>
        /// Consumes gearFuseNeed pieces of a grade and returns one of the next grade up,
        /// refunding a share of the stone sunk into what was consumed.
        /// </summary>
        public (GearGain gain, int refund)? FuseGear(int grade, IRandom rng)
        {
            if (!CanFuse(grade)) return null;
            var pick = GearFusePick(grade);

            int refund = (int)Math.Floor(pick.Sum(g => g.sp) * _cfg.gearRefund);
            foreach (var g in pick) Data.gear.Remove(g);
            if (refund > 0) Data.AddItem(GearStoneItem, refund);

            int id = RandomGearOf(grade + 1, rng);
            var gain = AddGear(id);
            MarkDirty();
            return (gain, refund);
        }

        /// <summary>Buys one exclusive outright with essence, or awakens it if already held.</summary>
        public GearGain? ExchangeExclusive(string unitId)
        {
            var r = ExclusiveOf(unitId);
            if (r == null) return null;
            var x = ExclusiveState(unitId);
            if (x != null && x.star >= _cfg.exclMaxStar) return null;
            if (ItemCount(GearEssenceItem) < _cfg.exclExchange) return null;

            Data.AddItem(GearEssenceItem, -_cfg.exclExchange);
            var gain = AddGear(r.id);
            MarkDirty();
            return gain;
        }

        // ------------------------------------------------------------------ equipping

        /// <summary>
        /// Puts a piece on an appliance. A piece worn elsewhere moves rather than duplicating,
        /// and whatever this appliance already wore goes back to the bag.
        /// </summary>
        public bool EquipGear(string unitId, int uid)
        {
            var owned = Data.Own(unitId);
            var g = GearInst(uid);
            if (owned == null || g == null) return false;

            string prev = GearOwner(uid);
            if (prev != null && prev != unitId) Data.owned[prev].eq = 0;
            owned.eq = uid;
            MarkDirty();
            return true;
        }

        public bool UnequipGear(string unitId)
        {
            var owned = Data.Own(unitId);
            if (owned == null || owned.eq == 0) return false;
            owned.eq = 0;
            MarkDirty();
            return true;
        }
    }
}
