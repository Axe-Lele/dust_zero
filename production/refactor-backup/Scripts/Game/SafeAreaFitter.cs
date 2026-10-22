// Keeps UI inside the device's safe area (notches, home indicators, rounded corners).
//
// Re-applied whenever the safe area or resolution changes, which also covers rotating the
// device and resizing the Game view while iterating in the editor.
using UnityEngine;

namespace DustZero.Game
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("켜면 가로 여백을 무시합니다. 세로형 화면에서 좌우를 꽉 채우고 싶을 때 사용합니다.")]
        [SerializeField] bool ignoreHorizontal;
        [Tooltip("켜면 세로 여백을 무시합니다.")]
        [SerializeField] bool ignoreVertical;

        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void Awake()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            // Cheap guard; only recompute when something actually moved.
            if (Screen.safeArea == _lastSafeArea &&
                Screen.width == _lastScreen.x && Screen.height == _lastScreen.y) return;
            Apply();
        }

        void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _lastSafeArea = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            var area = Screen.safeArea;
            var min = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            var max = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);

            if (ignoreHorizontal) { min.x = 0f; max.x = 1f; }
            if (ignoreVertical) { min.y = 0f; max.y = 1f; }

            // Guard against degenerate values some platforms report during startup.
            if (float.IsNaN(min.x) || float.IsNaN(min.y) || float.IsNaN(max.x) || float.IsNaN(max.y)) return;
            if (max.x <= min.x || max.y <= min.y) return;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
