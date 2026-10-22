// One pooled sprite following a simulation entity.
//
// Lives in its own file because Unity only serializes a MonoBehaviour whose class name
// matches its file name; declared elsewhere, every prefab reference to it silently breaks.
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>One pooled sprite following a simulation entity.</summary>
    public sealed class EntityView : MonoBehaviour
    {
        public SpriteRenderer Body;
        public SpriteRenderer HeatFill;
        public SpriteRenderer SkillFill;
        public SpriteRenderer HpFill;
        public Transform BarRoot;

        /// <summary>
        /// 10.17-d: the in-battle boost stars, drawn at the tile's top-right. Shows
        /// <c>Tier</c> stars and nothing at tier 1, so a just-deployed appliance is bare and
        /// one boost reads ★★ - the same count the sheet and the boost button use.
        /// </summary>
        public TMPro.TextMeshPro TierBadge;

        float _flash;

        public void SetFlash(float v) => _flash = v;

        public void Tint(Color baseColor)
        {
            // A white pulse on hit, fading on presentation time.
            Body.color = _flash > 0f
                ? Color.Lerp(baseColor, Color.white, Mathf.Clamp01(_flash * 6f))
                : baseColor;
        }

        public void StepFlash(float dt) => _flash = Mathf.Max(0f, _flash - dt);
    }
}
