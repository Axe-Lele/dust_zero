// 캔버스 스케일러가 고정 픽셀 배치를 실제 기기에 올릴 수 있는지를 정한다.
//
// 이 프로젝트는 원본 페이지(430x932)를 **한 배율**로 올려 가운데 886 기둥에 담는다.
// 그래서 기준이 되는 건 세로다 — `matchWidthOrHeight = 1` 이면 캔버스 세로는 어느
// 기기에서나 1920 이고, 932 로 잰 모든 세로 치수가 그대로 맞는다. 남거나 모자라는 건
// 가로 여백뿐이다.
//
// 전에는 가로에 맞췄다(캔버스 폭 1080 고정). 그때는 화면 비율이 바뀌면 캔버스 세로가
// 1920 이 아니게 되어, 세로로 잰 것들이 어긋났다 — 좁은 창에서 전투판이 카드 줄을
// 넘어 흐른 것이 그 증상이었다.
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using DustZero.Game;

namespace DustZero.Tests
{
    public class CanvasScalerTests
    {
        const string ScenePath = "Assets/DustZero/Scenes/Main.unity";

        /// <summary>스케일러가 내는 배율(캔버스 1px 당 화면 px).</summary>
        static float ScaleFor(CanvasScaler scaler, float screenW, float screenH)
        {
            float sw = screenW / scaler.referenceResolution.x;
            float sh = screenH / scaler.referenceResolution.y;
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) return Mathf.Min(sw, sh);
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink) return Mathf.Max(sw, sh);
            float m = scaler.matchWidthOrHeight;
            return Mathf.Pow(sw, 1f - m) * Mathf.Pow(sh, m);
        }

        static float CanvasWidth(CanvasScaler scaler, float screenW, float screenH)
            => screenW / ScaleFor(scaler, screenW, screenH);

        static float CanvasHeight(CanvasScaler scaler, float screenW, float screenH)
            => screenH / ScaleFor(scaler, screenW, screenH);

        [Test]
        public void TheDesignColumnFitsOnEveryTargetDevice()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                var scaler = Object.FindFirstObjectByType<CanvasScaler>(FindObjectsInactive.Include);
                Assert.IsNotNull(scaler, "Canvas Scaler 가 씬에 없습니다.");
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(1080f, scaler.referenceResolution.x, 0.1f);
                Assert.AreEqual(1920f, scaler.referenceResolution.y, 0.1f);

                // The shapes the art pack's prompt names, plus the reference itself.
                var devices = new[]
                {
                    ("1080x1920", 1080f, 1920f),
                    ("1170x2532", 1170f, 2532f),
                    ("720x1600", 720f, 1600f),
                    ("1440x3200", 1440f, 3200f),
                };

                Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode,
                    "두 축 중 작은 쪽에 맞춰야 기둥이 잘리지 않습니다.");

                foreach (var (name, w, h) in devices)
                {
                    // 기둥(430 페이지 폭 = 886 캔버스 px)이 들어가야 한다.
                    Assert.GreaterOrEqual(CanvasWidth(scaler, w, h), UiTheme.ColumnWidth - 1f,
                        $"{name}: 캔버스 폭이 기둥({UiTheme.ColumnWidth:F0})보다 좁습니다.");

                    // 세로 932 페이지도 들어가야 한다.
                    Assert.GreaterOrEqual(CanvasHeight(scaler, w, h), UiTheme.Py(UiTheme.PageH) - 1f,
                        $"{name}: 캔버스 세로가 페이지 높이보다 짧습니다.");

                    // 그리고 두 축의 배율이 같아야 원본 비율이 산다.
                    float fromW = CanvasWidth(scaler, w, h) / w;
                    float fromH = CanvasHeight(scaler, w, h) / h;
                    Assert.AreEqual(fromW, fromH, 0.0001f,
                        $"{name}: 두 축 배율이 다르면 정사각형이 퍼집니다.");
                }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void MatchingOneAxisWouldCropTheColumnOrStretchTheCanvas()
        {
            // 이 테스트가 막는 두 실패를 사실로 적어 둔다.
            var scaler = new GameObject("probe").AddComponent<CanvasScaler>();
            try
            {
                scaler.referenceResolution = new Vector2(1080f, 1920f);

                // 가로에 맞추면 긴 화면에서 캔버스 세로가 1920 을 넘어 세로 치수가 밀린다.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0f;
                Assert.Greater(CanvasHeight(scaler, 1170f, 2532f), 2000f);

                // 세로에 맞추면 원본보다 좁은 화면에서 기둥이 캔버스 폭을 넘는다.
                scaler.matchWidthOrHeight = 1f;
                Assert.Less(CanvasWidth(scaler, 720f, 1600f), UiTheme.ColumnWidth);

                // Expand 는 둘 다 막는다.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                Assert.GreaterOrEqual(CanvasWidth(scaler, 720f, 1600f), UiTheme.ColumnWidth);
                Assert.GreaterOrEqual(CanvasHeight(scaler, 1170f, 2532f), 1920f);
            }
            finally
            {
                Object.DestroyImmediate(scaler.gameObject);
            }
        }
    }
}
