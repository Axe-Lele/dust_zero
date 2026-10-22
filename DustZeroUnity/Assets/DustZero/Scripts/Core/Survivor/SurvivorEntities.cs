// Live state for the 대청소 survival mode.
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Survivor
{
    public sealed class SurvLeader
    {
        public string Type;
        public float X, Y;
        public float Hp, MaxHp;
        /// <summary>Weapon level, raised by the "리더 무기" level-up option.</summary>
        public int Level = 1;
        public float Kick;
        /// <summary>-1 or 1; flips the sprite.</summary>
        public int Face = 1;
        public float HurtT;
        public bool Moving;

        /// <summary>Current suction radius, published for the view's aura ring. Vacuum only.</summary>
        public float AuraRadius;
        /// <summary>Orbiting brush positions. Robot vacuum only.</summary>
        public readonly List<Vec2> Orbs = new();
    }

    public sealed class SurvCompanion
    {
        public string Type;
        public int Level = 1;
        public float Cd = 0.5f;
        /// <summary>Support companions ride on the leader instead of trailing behind.</summary>
        public bool Attached;
        /// <summary>Index within its own group, which sets the follow distance or offset.</summary>
        public int Slot;
        public float X, Y;
        public float Kick;
        /// <summary>Remaining duration of the vacuum companion's storm.</summary>
        public float StormT;
    }

    public sealed class SurvEnemy
    {
        public int Id;
        public string Type;
        public EnemyDef Def;
        public float Hp, MaxHp;
        public float X, Y;
        public float Radius;
        public float Speed;
        /// <summary>Damage per second dealt by touching the leader.</summary>
        public float ContactDps;
        public float Xp;

        public float SlowT, SlowAmt;
        public float FrozenT;
        public float HitT;
        /// <summary>Per-enemy cooldown so one brush sweep cannot multi-hit in a frame.</summary>
        public float BrushT;
        public float Seed;
        public bool Dead;

        // --- ranged attacker (spore), all zero for melee enemies ---
        /// <summary>Stand-off distance. Above 0 marks this enemy as a shooter.</summary>
        public float Range;
        public float ShotCd;
        public float ShotDmg;
        public float ShotSpeed;
        /// <summary>Counts down to the next shot.</summary>
        public float ShotT;

        public bool IsRanged => Range > 0f;
    }

    /// <summary>A projectile fired by an enemy at the leader.</summary>
    public sealed class SurvEnemyShot
    {
        public float X, Y;
        public float Vx, Vy;
        public float Damage;
        public float Life;
        public float MaxLife = 6f;
        public float Radius = 0.18f;
    }

    public sealed class SurvGem
    {
        public float X, Y;
        public float Value;
        public bool Big;
        /// <summary>Set once inside pickup range; from then on it homes in regardless of distance.</summary>
        public bool Pulling;
        public float Speed;
        public bool Collected;
    }

    public sealed class SurvShot
    {
        public float X, Y;
        public float Vx, Vy;
        public float Life;
        public float MaxLife = 1.4f;
        public float Radius = 0.16f;
        public float Damage;
        public float Slow, SlowSec;
        public float Freeze;
        public int Pierce;
        /// <summary>Area damage on impact instead of a single hit.</summary>
        public float Splash;
        public string Color;
        /// <summary>Drawn as a soap bubble rather than an icy dart.</summary>
        public bool Bubble;
        /// <summary>Staggers a volley so shots do not leave the muzzle in one clump.</summary>
        public float Delay;
        public readonly HashSet<SurvEnemy> Hits = new();
    }

    /// <summary>A delayed area strike: telegraphed first, then detonates.</summary>
    public sealed class SurvMark
    {
        public float X, Y;
        public float Radius;
        public float Life;
        public float MaxLife = 0.55f;
        public float Damage;
        public bool Done;
    }

    /// <summary>Aggregated run modifiers from companions, family sets and passives.</summary>
    public sealed class SurvMods
    {
        public float CdMul = 1f;
        public float DmgMul = 1f;
        public float Pickup;
        public float XpMul = 1f;
        public float EnemySlow;
        public float MoveSpeed;
    }

    public enum SurvOptionKind { Weapon, Join, Up, Combo, Passive }

    public readonly struct SurvOption
    {
        public readonly SurvOptionKind Kind;
        /// <summary>Unit id for Join/Up, combo id for Combo, passive key for Passive.</summary>
        public readonly string Id;
        public readonly float Weight;

        public SurvOption(SurvOptionKind kind, string id, float weight)
        {
            Kind = kind; Id = id; Weight = weight;
        }
    }

    /// <summary>What a level-up card should render.</summary>
    public sealed class SurvOptionView
    {
        public SurvOption Option;
        /// <summary>Unit id to draw, or null for a text glyph.</summary>
        public string Icon;
        /// <summary>Second icon, used by combo cards.</summary>
        public string Icon2;
        public string Glyph;
        public string Tag;
        public string Name;
        public string Desc;
        /// <summary>Combo cards are highlighted.</summary>
        public bool Hot;
    }
}
