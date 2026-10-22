// Live state for a deployed appliance and an attacking stain.
//
// Stat scaling is baked in at deploy time (level and limit-break multiply attack, heat
// capacity and cooling), matching the original's deploy(): the table row is the base and
// powOf(type) is the only multiplier applied.
using DustZero.Data;

namespace DustZero.Core.Defense
{
    public sealed class BattleUnit
    {
        public int Id;
        public string Type;
        public UnitDef Def;

        /// <summary>
        /// Which coded kit this appliance drives, from unit_behaviour. Empty means a plain
        /// attacker. Skill dispatch reads this rather than <see cref="Type"/>, so a new
        /// appliance can reuse a kit while keeping its own skill_param rows.
        /// </summary>
        public string Behaviour => Def != null ? Def.DefenseBehaviour : string.Empty;

        /// <summary>
        /// Gear multipliers captured at deploy. Kept on the unit because an in-battle boost
        /// recomputes the stats from the table values, and would otherwise drop the gear.
        /// </summary>
        public float GearAtk = 1f, GearHeat = 1f, GearCharge = 1f;
        /// <summary>Seconds the exclusive adds to this appliance's skill duration.</summary>
        public float SkillDurationAdd;

        /// <summary>
        /// The skill coefficient this appliance actually uses: its skill_param row, then its
        /// exclusive gear's edits. Set by BattleSim at deploy so every kit reads one place.
        /// </summary>
        public System.Func<string, float> Skp = _ => 0f;

        /// <summary>
        /// Roster multiplier times the in-battle boost multiplier. Skill damage reads this,
        /// so a boosted unit hits harder with its skill as well as its basic attack.
        /// </summary>
        public float Power = 1f;

        /// <summary>Roster multiplier alone, before any in-battle boost.</summary>
        public float BasePower = 1f;

        /// <summary>In-battle upgrade tier: 1 as deployed, 2 or 3 once boosted.</summary>
        public int Tier = 1;

        /// <summary>
        /// 10.17-b: the roster growth that applies to what a SUPPORT appliance does rather
        /// than to its attack - the generator's regen and burst, the computer's buff. 1 for
        /// every other appliance, so it can be multiplied in unconditionally.
        /// </summary>
        public float Support = 1f;

        /// <summary>
        /// unit_boost.statMul for the current tier, 1 as deployed. Support output is scaled by
        /// it the same way attack is, which the stat recalculation in Boost cannot do: regen
        /// and buff come from skill_param, not from the unit row.
        /// </summary>
        public float BoostMul = 1f;

        /// <summary>Total power spent on this unit (deploy + boosts); retreat refunds a share.</summary>
        public float Spent;

        /// <summary>Scaled stats; Def keeps the unscaled table values.</summary>
        public float Atk;
        public float MaxHeat;
        public float CoolRate;

        /// <summary>Per-unit stats that boosting changes, so Def stays the table row.</summary>
        public int Block;
        public float Range;
        public float SkillChargeTime;
        /// <summary>Added to skill radii as well as the basic attack range.</summary>
        public float RangeAdd;

        public int Col, Row;
        public float X, Y;

        /// <summary>Skill charge in seconds, capped at Def.SkillCharge.</summary>
        public float Sp;
        /// <summary>Remaining skill duration; >0 means the skill is active.</summary>
        public float SkillT;
        public float AtkT = 0.3f;
        /// <summary>Internal retrigger timer for tick-based skills (ac, washer).</summary>
        public float Tick;

        public float Heat;
        public bool Overheated;

        /// <summary>Block slots currently taken, recomputed at the end of each tick.</summary>
        public int Load;

        /// <summary>Attack recoil, drives the lean-forward pose.</summary>
        public float Kick;

        // Per-tick aura accumulators, reset at the top of every tick.
        public float AtkMul = 1f;
        public float SpdMul = 1f;
        /// <summary>Extra cooling per second from fans and wet tiles.</summary>
        public float Cool;

        public bool Dead;

        /// <summary>Damage this appliance actually landed, overkill excluded. For the MVP line.</summary>
        public float Damage;
        /// <summary>Dust this appliance finished off.</summary>
        public int Kills;

        /// <summary>Melee units hold ground and overheat; ranged units on furniture do not.</summary>
        public bool IsBlocker => MaxHeat > 0f;

        public float HeatRatio => MaxHeat > 0f ? Heat / MaxHeat : 0f;
        public bool SkillActive => SkillT > 0f;
        public bool SkillReady => SkillT <= 0f && Sp >= Def.SkillCharge;
    }

    public sealed class BattleEnemy
    {
        public int Id;
        public string Type;
        public EnemyDef Def;

        public float Hp;
        public float MaxHp;

        /// <summary>Distance travelled along the flight path. Flyers only.</summary>
        public float D;
        /// <summary>Which entrance route this enemy belongs to.</summary>
        public int PathIndex;

        /// <summary>Tile currently occupied. Ground enemies only.</summary>
        public int Cx, Cy;
        public float X, Y;
        /// <summary>Tile being stepped toward; null (TargetSet=false) means "pick one".</summary>
        public int Tx, Ty;
        public bool TargetSet;

        public Vec2 Dir = new(0, 1);

        public float SlowT, SlowAmt;
        public float FrozenT;

        /// <summary>The unit holding this enemy in place, if any.</summary>
        public BattleUnit BlockedBy;

        /// <summary>
        /// 협동 고리판 전용. 길을 막는 것이 아니라 **곁을 지나다 붙잡히는** 것이라
        /// <see cref="BlockedBy"/> 와 다르다. 붙잡힌 적은 멈추지 않고 느려지며,
        /// 붙잡은 가전에 과열을 쌓는다. 멀어지면 저절로 풀린다.
        /// </summary>
        public BattleUnit HeldBy;

        /// <summary>Distance walked in total. The ring wraps D, so this is what grows.</summary>
        public float Travelled;
        /// <summary>The unit this enemy is queued behind; it still pushes heat onto it.</summary>
        public BattleUnit QueuedBy;
        /// <summary>Set while a vacuum is dragging this enemy, which suspends normal movement.</summary>
        public BattleUnit PullBy;

        public float AtkT;
        /// <summary>Sideways jitter so a column of enemies does not overlap exactly.</summary>
        public float Off;
        /// <summary>Stable per-enemy phase for idle bob and dust-mote placement.</summary>
        public float Seed;
        /// <summary>Counts down the white hit flash.</summary>
        public float HitT;

        public bool Dead;

        public bool IsFlying => Def.Fly;
    }
}
