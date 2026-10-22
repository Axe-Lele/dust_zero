// Full-screen spotlight for the lobby's forced onboarding (the `guide` table).
//
// Four dimming bands frame whichever element the current step names, leaving that element
// itself uncovered so the real button underneath still receives the tap - a "hole by
// subtraction" built from screen-fraction anchors, so it holds up under any resolution the
// canvas scaler produces. AppRoot drives this each frame from whichever screen is active.
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    public sealed class GuideOverlay : MonoBehaviour
    {
        [SerializeField] RectTransform bandTop;
        [SerializeField] RectTransform bandBottom;
        [SerializeField] RectTransform bandLeft;
        [SerializeField] RectTransform bandRight;
        [SerializeField] RectTransform bubbleRoot;
        [SerializeField] TextMeshProUGUI bubbleText;
        [SerializeField] UiButton skipButton;

        public event System.Action SkipRequested;

        void Awake()
        {
            if (skipButton != null) skipButton.SetAction(() => SkipRequested?.Invoke());
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Spotlights target with text. A null target hides everything: the step is
        /// active but the screen currently shown does not own that element yet.</summary>
        public void Show(RectTransform target, string text)
        {
            if (target == null) { Hide(); return; }
            gameObject.SetActive(true);
            if (bubbleText != null) bubbleText.text = text ?? string.Empty;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            float fxMin = Mathf.Clamp01(bl.x / Screen.width);
            float fxMax = Mathf.Clamp01(tr.x / Screen.width);
            float fyMin = Mathf.Clamp01(bl.y / Screen.height);
            float fyMax = Mathf.Clamp01(tr.y / Screen.height);

            SetAnchors(bandTop, new Vector2(0f, fyMax), new Vector2(1f, 1f));
            SetAnchors(bandBottom, new Vector2(0f, 0f), new Vector2(1f, fyMin));
            SetAnchors(bandLeft, new Vector2(0f, fyMin), new Vector2(fxMin, fyMax));
            SetAnchors(bandRight, new Vector2(fxMax, fyMin), new Vector2(1f, fyMax));

            // The bubble sits on whichever side of the target has more room, so it never
            // covers the very thing it is explaining.
            const float bubbleH = 0.17f, gap = 0.02f;
            bool below = fyMin > 0.45f;
            float top = below ? fyMin - gap : Mathf.Min(1f, fyMax + gap + bubbleH);
            float bottom = below ? Mathf.Max(0f, fyMin - gap - bubbleH) : fyMax + gap;
            SetAnchors(bubbleRoot, new Vector2(0.08f, bottom), new Vector2(0.92f, top));
        }

        static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            if (rt == null) return;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
