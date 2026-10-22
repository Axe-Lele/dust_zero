// A single drifting mote, recycled through FxDirector's pool.
//
// Its own file: Unity only serializes a MonoBehaviour whose class name matches its file name.
using DustZero.Core;
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>A single drifting mote. Returned to the pool when its life runs out.</summary>
    public sealed class FxParticle : MonoBehaviour
    {
        public float Life, MaxLife, Size, Gravity, Delay;
        public Vector2 Velocity;
        public SpriteRenderer Renderer;

        public void Init(BurstEvent e, float angle, float speed, Sprite round, Sprite square)
        {
            Renderer.sprite = e.Rect ? square : round;
            Renderer.color = ParseColor(e.Color);
            Velocity = new Vector2(Mathf.Cos(angle) * speed,
                                   Mathf.Sin(angle) * speed - (e.Gravity != 0f ? e.Speed * 0.7f : 0f));
            Gravity = e.Gravity;
            Delay = e.Delay;
            Life = -e.Delay;
            MaxLife = e.Life;
            Size = e.Size;
            transform.localScale = Vector3.one * Size;
        }

        /// <summary>Advances the mote. Returns false once it should be recycled.</summary>
        public bool Step(float dt)
        {
            Life += dt;
            if (Life < 0f) { Renderer.enabled = false; return true; }
            Renderer.enabled = true;
            if (Life >= MaxLife) return false;

            var p = transform.localPosition;
            p.x += Velocity.x * dt;
            p.y += Velocity.y * dt;
            transform.localPosition = p;

            Velocity.y += Gravity * dt;
            Velocity.x *= 0.96f;

            float a = 1f - Life / MaxLife;
            var c = Renderer.color; c.a = Mathf.Clamp01(a);
            Renderer.color = c;
            transform.localScale = Vector3.one * (Size * (0.6f + 0.4f * a));
            return true;
        }

        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#' && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            // "r,g,b" form used by the original's ring tints.
            var parts = hex.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], out var r) &&
                float.TryParse(parts[1], out var g) &&
                float.TryParse(parts[2], out var b))
                return new Color(r / 255f, g / 255f, b / 255f);
            return Color.white;
        }
    }
}
