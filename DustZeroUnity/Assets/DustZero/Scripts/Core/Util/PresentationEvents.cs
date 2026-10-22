// The channel between the rules and the screen.
//
// The simulation never touches a GameObject; it appends semantic events here and the view
// drains them each frame, turning them into pooled particles, damage labels and camera shake.
// Keeping the two apart is what lets pause and 2x speed stay correct: simulation time is the
// only thing that advances inside Tick(), while the view animates on its own clock.
using System.Collections.Generic;

namespace DustZero.Core
{
    public enum FxKind
    {
        Hit, Pop, Ring, Swirl, Splash, Blast, Skill, Drop, Bite, Boom, Proj, Lob, Dash, Mark
    }

    public struct FxEvent
    {
        public FxKind Kind;
        public float X, Y;
        /// <summary>Endpoint for Proj/Lob/Dash.</summary>
        public float X2, Y2;
        public float Radius;
        public float Duration;
        /// <summary>Hex colour like "#9ED0EC", or "r,g,b" for ring tints, matching the original.</summary>
        public string Color;
        public float Size;
    }

    public struct BurstEvent
    {
        public float X, Y;
        public int Count;
        public string Color;
        public float Speed, Size, Life, Gravity, Delay;
        /// <summary>Square confetti instead of round motes.</summary>
        public bool Rect;
    }

    public struct FloatTextEvent
    {
        public float X, Y;
        public string Text;
        public string Color;
        public bool Big;
        /// <summary>Pill-shaped label (skill names, "과열!") rather than a damage number.</summary>
        public bool Label;
    }

    public struct BannerEvent
    {
        public string Text;
        public float Duration;
    }

    /// <summary>
    /// Collects everything the view should show for the frame just simulated. The view calls
    /// <see cref="Clear"/> after draining, so nothing accumulates while paused.
    /// </summary>
    public sealed class PresentationBus
    {
        public readonly List<FxEvent> Fx = new();
        public readonly List<BurstEvent> Bursts = new();
        public readonly List<FloatTextEvent> Texts = new();
        public readonly List<BannerEvent> Banners = new();

        /// <summary>Largest shake requested this frame, in tiles.</summary>
        public float Shake;
        /// <summary>Set when the goal was hit, for the red screen flash.</summary>
        public float GoalFlash;

        public void Emit(FxEvent e) => Fx.Add(e);

        public void Emit(FxKind kind, float x, float y, float duration = 0.35f,
                         float radius = 0f, string color = null, float size = 0f,
                         float x2 = 0f, float y2 = 0f)
            => Fx.Add(new FxEvent
            {
                Kind = kind, X = x, Y = y, X2 = x2, Y2 = y2,
                Radius = radius, Duration = duration, Color = color, Size = size,
            });

        public void Burst(float x, float y, int count, string color, float speed, float size,
                          float life, float gravity = 0f, float delay = 0f, bool rect = false)
            => Bursts.Add(new BurstEvent
            {
                X = x, Y = y, Count = count, Color = color, Speed = speed,
                Size = size, Life = life, Gravity = gravity, Delay = delay, Rect = rect,
            });

        public void Text(float x, float y, string text, string color, bool big = false, bool label = false)
            => Texts.Add(new FloatTextEvent { X = x, Y = y, Text = text, Color = color, Big = big, Label = label });

        public void Banner(string text, float duration)
            => Banners.Add(new BannerEvent { Text = text, Duration = duration });

        public void RequestShake(float amount) { if (amount > Shake) Shake = amount; }

        public void Clear()
        {
            Fx.Clear(); Bursts.Clear(); Texts.Clear(); Banners.Clear();
            Shake = 0f; GoalFlash = 0f;
        }
    }

    /// <summary>Palette constants the original hard-coded in its draw calls.</summary>
    public static class FxColors
    {
        public const string White = "#FFFFFF";
        public const string Damage = "#FFFFFF";
        public const string Ice = "#BFE3FA";
        public const string IcePale = "#E6F6FF";
        public const string Fire = "#FFB050";
        public const string Orange = "#FF9A3C";
        public const string Gold = "#F2C14E";
        public const string Red = "#E0524A";
        public const string RedSoft = "#FF6B5E";
        public const string Mint = "#8EE3C8";
        public const string MintDeep = "#3FAE7A";
        public const string Steam = "#BFF0E6";
        public const string Dust = "#9A9DA1";
        public const string Grease = "#A8742A";
        public const string Mold = "#6E9B4C";
        public const string Fly = "#3A3330";
        public const string Water = "#7FC4F5";
        public const string Spark = "#FFF3B0";

        public static string ForEnemyLook(string look) => look switch
        {
            "grease" => Grease,
            "mold" => Mold,
            "fly" => Fly,
            _ => Dust,
        };
    }
}
