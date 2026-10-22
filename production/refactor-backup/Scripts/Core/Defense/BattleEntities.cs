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
        /// Roster multiplier times the in-battle boost multiplier. Skill damage reads this,
        /// so a boosted unit hits harder with its skill as well as its basic attack.
        /// </summary>
        public float Power = 1f;

        /// <summary>Roster multiplier alone, before any in-battle boost.</summary>
        public float BasePower = 1f;

        /// <summary>In-battle upgrade tier: 1 as deployed, 2 or 3 once boosted.</summary>
        public int Tier = 1;

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
