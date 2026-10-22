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
        /// <summary>라벨 뒤에 까는 어두운 알약. 숫자에는 쓰지 않는다.</summary>
        public SpriteRenderer Plate;
        public float Life, MaxLife;
        public bool IsLabel;

        /// <summary>
        /// 원본 렌더 규칙 그대로다:
        ///
        ///   라벨  → `k*0.22` 본문 글꼴, 뒤에 `rgba(29,34,38,.85)` 둥근 알약
        ///   큰 수 → `k*0.36` 숫자 글꼴
        ///   보통  → `k*0.22` 숫자 글꼴
        ///
        /// **라벨이면 `big` 은 무시한다.** 전에는 `big` 이 이겨 0.52칸으로 그려서, 협동처럼
        /// `big+label` 로 알리는 곳에서 글자가 2.4배로 나와 판을 가렸다.
        /// </summary>
        /// <param name="numberOutline">
        /// 숫자에 두를 외곽선 머티리얼. 라벨은 뒤에 알약을 깔므로 외곽선이 필요 없고,
        /// 글꼴도 달라서 같은 머티리얼을 쓰면 아틀라스가 어긋난다.
        /// </param>
        public void Init(FloatTextEvent e, TMP_FontAsset numberFont, TMP_FontAsset labelFont,
                         Material numberOutline = null)
        {
            IsLabel = e.Label;
            var font = IsLabel && labelFont != null ? labelFont : numberFont;
            Text.font = font;
            Text.fontSharedMaterial = !IsLabel && numberOutline != null
                                    ? numberOutline
                                    : font.material;
            Text.text = e.Text;
            Text.color = FxParticle.ParseColor(e.Color);
            Text.fontSize = IsLabel ? 2.2f : e.Big ? 3.6f : 2.2f;
            Text.fontStyle = FontStyles.Bold;
            Life = 0f;
            MaxLife = e.Label ? 1.1f : e.Big ? 1.0f : 0.6f;

            if (Plate == null) return;
            Plate.enabled = IsLabel;
            if (!IsLabel) return;
            // 알약은 글자 폭에 맞춘다 — 원본은 measureText + 12px, 높이 20px, 반지름 10.
            Text.ForceMeshUpdate();
            float w = Text.GetRenderedValues(false).x + 0.12f;
            Plate.transform.localScale = new Vector3(Mathf.Max(0.3f, w), 0.2f, 1f);
            Plate.color = new Color(0.114f, 0.133f, 0.149f, 0.85f);
        }

        public bool Step(float dt)
        {
            Life += dt;
            if (Life >= MaxLife) return false;
            var p = transform.localPosition;
            // Labels drift up gently; damage numbers pop faster, as in the original.
            p.y += (IsLabel ? 0.35f : 0.8f) * dt;
            transform.localPosition = p;
            float a = Mathf.Clamp01((1f - Life / MaxLife) * 2f);
            var c = Text.color;
            c.a = a;
            Text.color = c;
            if (Plate != null && Plate.enabled)
            {
                var pc = Plate.color;
                pc.a = 0.85f * a;
                Plate.color = pc;
            }
            return true;
        }
    }
}
