// A rising damage number or skill label, recycled through FxDirector's pool.
//
// Its own file: Unity only serializes a MonoBehaviour whose class name matches its file name.
using DustZero.Core;
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>A rising damage number or skill label.</summary>
    public sealed class FxLabel : MonoBehaviour
    {
        public TextMeshPro Text;
        public float Life, MaxLife;
        public bool IsLabel;

        public void Init(FloatTextEvent e, TMP_FontAsset font)
        {
            Text.font = font;
            Text.text = e.Text;
            Text.color = FxParticle.ParseColor(e.Color);
            Text.fontSize = e.Big ? 5.2f : e.Label ? 3.4f : 3.2f;
            Text.fontStyle = FontStyles.Bold;
            IsLabel = e.Label;
            Life = 0f;
            MaxLife = e.Label ? 1.1f : e.Big ? 1.0f : 0.6f;
        }

        public bool Step(float dt)
        {
            Life += dt;
            if (Life >= MaxLife) return false;
            var p = transform.localPosition;
            // Labels drift up gently; damage numbers pop faster, as in the original.
            p.y += (IsLabel ? 0.35f : 0.8f) * dt;
            transform.localPosition = p;
            var c = Text.color;
            c.a = Mathf.Clamp01((1f - Life / MaxLife) * 2f);
            Text.color = c;
            return true;
        }
    }
}
