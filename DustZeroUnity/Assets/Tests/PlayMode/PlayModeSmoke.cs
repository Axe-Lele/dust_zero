// A real play-through used as run evidence.
//
// Loads the shipped scene, boots the real services and drives the real BattleScreen: the
// first stage is played to a result by deploying from the actual deck onto legal tiles, then
// the profile is re-read from disk to prove the save round-tripped. Screenshots land in
// production/qa/evidence so the outcome is inspectable rather than asserted.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using DustZero.Core.Coop;
using DustZero.Core.Defense;
using DustZero.Core.Meta;
using DustZero.Core.League;
using DustZero.Core.Survivor;
using DustZero.Data;
using DustZero.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DustZero.Tests
{
    public class PlayModeSmoke
    {
        const string SceneName = "Main";

        static string EvidenceDir
        {
            get
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../production/qa/evidence"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Captures a frame as run evidence. Never uses WaitForEndOfFrame: in batch mode that
        /// yield never resumes, which would hang the whole run. Capture is best-effort, since
        /// a headless editor may have no backbuffer to read.
        /// </summary>
        static IEnumerator Shot(string name) => ShotAt(name, 1080, 1920);

        /// <summary>
        /// Captures at a specific resolution. The pack's prompt asks for several device shapes,
        /// and a layout that only ever renders at one aspect hides its own crowding.
        /// </summary>
        static IEnumerator ShotAt(string name, int w, int h)
        {
            // Deliberately does NOT call Screen.SetResolution: resizing the game view mid-run
            // destabilises the test runner. Aspect behaviour is covered by CanvasScalerTests,
            // which checks the setting that decides it.
            Canvas.ForceUpdateCanvases();
            yield return null;

            // 그림과 함께 **치수**도 남긴다. 눈으로 비교하면 놓친다 — 이번 이식에서 놓친
            // 것들(전체 화면 vs 330px 카드, 퍼센트 vs 개수, 가로 vs 세로 배열)은 전부
            // 보면 알 것 같은데 안 보이던 종류였다. 원본 쪽도 같은 형식으로 뽑아 둔다.
            DumpLayout(name);
            AssertAuditRulerIsHonest(name);

            var path = Path.Combine(EvidenceDir, name + ".png");

            // 화면을 **있는 그대로** 받는다. 전에는 카메라마다 `Render()` 를 불러 렌더타겟에
            // 모았는데, URP 에서는 그 호출이 월드를 그리지 않는다 — 캔버스만 남고 아레나와
            // 전투판 스프라이트가 빠진 그림이 '증거'로 쌓였다. 보이는 그대로 찍는 쪽이
            // 틀릴 여지가 없다.
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);

            // 비동기로 쓰이므로 파일이 생기고 **크기가 멎을 때까지** 기다린다.
            // `WaitForEndOfFrame` 은 에디터가 포커스를 잃으면 돌아오지 않아 러너를 멎게 한다.
            long size = -1;
            for (int i = 0; i < 180; i++)
            {
                yield return null;
                if (!File.Exists(path)) continue;
                long now = new FileInfo(path).Length;
                if (now > 0 && now == size) break;
                size = now;
            }

            Assert.IsTrue(File.Exists(path), $"{name}.png 이 만들어지지 않았습니다.");

            // PNG 머리말에서 치수를 읽어 **실제로** 요청한 크기인지 본다. 게임 뷰가 다른
            // 크기면 조용히 다른 그림을 남기지 말고 여기서 실패해야 한다.
            var head = new byte[24];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, 24);
            int shotW = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int shotH = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            Assert.AreEqual(w, shotW, $"{name}.png 가로가 {shotW} 입니다. 게임 뷰를 {w}x{h} 로 두세요.");
            Assert.AreEqual(h, shotH, $"{name}.png 세로가 {shotH} 입니다. 게임 뷰를 {w}x{h} 로 두세요.");
            Debug.Log($"[smoke] screenshot saved: {name}.png ({new FileInfo(path).Length} bytes)");
        }

        /// <summary>
        /// Every bottom-bar item must stay inside the strip that draws it. A layout group
        /// owns their positions, so anything that writes to those rects directly shows up
        /// here as an item hanging above the bar.
        /// </summary>
        static void AssertNavItemsSitInTheirStrip(LobbyScreen lobby)
        {
            var strip = lobby.transform.Find("BottomNav/Tabs") as RectTransform;
            Assert.IsNotNull(strip, "하단 탭 줄을 찾지 못했습니다.");

            var band = new Vector3[4];
            strip.GetWorldCorners(band);
            foreach (RectTransform item in strip)
            {
                var c = new Vector3[4];
                item.GetWorldCorners(c);
                Assert.LessOrEqual(c[1].y, band[1].y + 1f,
                    $"{item.name} 이 하단 바 위로 {c[1].y - band[1].y:F0}px 튀어나왔습니다.");
                Assert.GreaterOrEqual(c[0].y, band[0].y - 1f,
                    $"{item.name} 이 하단 바 아래로 내려갔습니다.");
            }
        }

        /// <summary>
        /// Redesign 1c: tapping a deployed appliance swaps the card row for the action bar.
        /// Covered here rather than in EditMode because the point is that the bar actually
        /// appears on screen with the right costs - the simulation API is tested separately.
        /// </summary>
        static IEnumerator CheckUnitActionBar(BattleScreen battle, BattleSim sim)
        {
            var unit = sim.Units.FirstOrDefault();
            Assert.IsNotNull(unit, "검사 전제: 배치된 가전이 있어야 합니다.");

            var bar = battle.GetComponentInChildren<UnitActionBar>(true);
            Assert.IsNotNull(bar, "UnitActionBar 가 씬에 없습니다.");
            Assert.IsFalse(bar.gameObject.activeInHierarchy, "선택 전에는 액션바가 숨어 있어야 합니다.");

            // Go through the screen's own selection path, not the bar's API.
            var select = typeof(BattleScreen).GetMethod("SelectUnit",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(select, "BattleScreen.SelectUnit 을 찾지 못했습니다.");
            select.Invoke(battle, new object[] { unit });
            yield return null;

            Assert.IsTrue(bar.gameObject.activeInHierarchy, "가전을 선택했는데 액션바가 뜨지 않았습니다.");
            Assert.AreSame(unit, bar.Unit, "액션바가 다른 가전을 가리킵니다.");

            // Let the layout settle before measuring, then prove the read-out is legible -
            // the labels existing in the hierarchy is not the same as them being on screen.
            Canvas.ForceUpdateCanvases();
            yield return null;

            var info = bar.transform.Find("Info");
            Assert.IsNotNull(info, "액션바에 Info 패널이 없습니다.");
            var infoRect = ((RectTransform)info).rect;
            Assert.Greater(infoRect.width, UiTheme.Touch,
                $"Info 패널 폭이 {infoRect.width:0}px 뿐입니다. 레이아웃이 적용되지 않았습니다.");

            AssertNoBlankLabels(bar.transform, "액션바");
            Assert.AreEqual(unit.Def.Name, info.Find("Name").GetComponent<TMPro.TextMeshProUGUI>().text,
                "Info 이름이 선택한 가전과 다릅니다.");

            var deckRoot = (RectTransform)typeof(BattleScreen)
                .GetField("deckRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(battle);
            Assert.IsFalse(deckRoot.gameObject.activeSelf, "액션바가 떴는데 카드줄이 그대로입니다.");

            var buttons = bar.GetComponentsInChildren<UiButton>(true);
            Assert.AreEqual(4, buttons.Length, "강화·멀티탭·철수·닫기 네 버튼이어야 합니다.");
            foreach (var b in buttons)
            {
                var rect = ((RectTransform)b.transform).rect;
                Assert.GreaterOrEqual(rect.width, UiTheme.Touch - 1f,
                    $"{b.name} 버튼 폭이 최소 터치 44pt 미만입니다.");
                Assert.GreaterOrEqual(rect.height, UiTheme.Touch - 1f,
                    $"{b.name} 버튼 높이가 최소 터치 44pt 미만입니다.");
            }

            // A button's own caption must fit too. The sub line was gated and the main one was
            // not, so "강화 ★★" quietly became "강화 …" and every test stayed green.
            foreach (var b in buttons)
            {
                var mainTmp = b.transform.Find("Content/Label")?.GetComponent<TMPro.TextMeshProUGUI>();
                Assert.IsNotNull(mainTmp, $"{b.name} 에 Content/Label 이 없습니다.");
                if (mainTmp == null || !mainTmp.gameObject.activeInHierarchy ||
                    string.IsNullOrEmpty(mainTmp.text)) continue;
                mainTmp.ForceMeshUpdate();
                Assert.LessOrEqual(mainTmp.preferredWidth, mainTmp.rectTransform.rect.width + 1f,
                    $"{b.name} 글자 '{mainTmp.text}' 이 잘립니다 " +
                    $"(필요 {mainTmp.preferredWidth:0}px / 공간 {mainTmp.rectTransform.rect.width:0}px).");
            }

            // Costs must be readable in full. Card names may ellipsize by design; a price
            // that reads "2,00…" cannot.
            // The path here used to be "Face/Sub", which UiKit never creates - the lookup
            // returned null, the loop skipped every button, and this gate checked nothing.
            foreach (var b in buttons)
            {
                var subTmp = b.transform.Find("Content/Sub")?.GetComponent<TMPro.TextMeshProUGUI>();
                if (subTmp == null || !subTmp.gameObject.activeInHierarchy ||
                    string.IsNullOrEmpty(subTmp.text)) continue;
                subTmp.ForceMeshUpdate();
                Assert.LessOrEqual(subTmp.preferredWidth, subTmp.rectTransform.rect.width + 1f,
                    $"{b.name} 비용 '{subTmp.text}' 이 잘립니다 " +
                    $"(필요 {subTmp.preferredWidth:0}px / 공간 {subTmp.rectTransform.rect.width:0}px).");
            }

            var retreat = buttons.First(b => b.name == "Retreat");
            Assert.AreEqual("+" + UiTheme.Watts(sim.RetreatRefund(unit)), retreat.SubText,
                "철수 환급 표시가 시뮬레이션 값과 다릅니다.");

            yield return BoostAndCheckTheStarBadge(battle, sim, unit, bar);

            Debug.Log($"[smoke] 액션바 {unit.Def.Name} · 강화 {buttons.First(b => b.name == "Boost").SubText} " +
                      $"· 멀티탭 {buttons.First(b => b.name == "Multitap").SubText} · {retreat.SubText}");

            yield return Shot("07-unit-action-bar");

            // Closing must hand the tray back to the cards.
            bar.Close();
            yield return null;
            Assert.IsTrue(deckRoot.gameObject.activeSelf, "액션바를 닫았는데 카드줄이 돌아오지 않았습니다.");
        }

        /// <summary>
        /// Every visible label with text must actually put glyphs on screen. TMP drops a whole
        /// line when its rect is shorter than the font's line box, and under Ellipsis overflow
        /// that shows up as a blank label rather than as clipped text - invisible in a test
        /// that only checks the string. This sweep is the guard for that whole class of bug.
        /// </summary>
        /// <summary>
        /// The board must sit between the HUD and the card tray. It is framed by moving an
        /// orthographic camera, so nothing in the layout system stops it from sliding under
        /// the tray - and when it did, every label still rendered and every test still
        /// passed. The only way to catch it is to measure where the board actually lands.
        /// </summary>
        static void AssertBoardClearsTheBands(BattleScreen battle)
        {
            // The board is a world-space object, not a child of the UI screen.
            var board = Object.FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);
            // The battle screen's own camera: the scene has more than one orthographic
            // camera, and picking the wrong one measures the wrong thing.
            var cam = battle.BoardCamera;
            Assert.IsNotNull(board, "BoardView 를 찾지 못했습니다.");
            Assert.IsNotNull(cam, "보드 카메라를 찾지 못했습니다.");

            var bounds = board.ScreenBounds(cam);
            float hud = BandBottomY(Find(battle.transform, "Hud"));
            float tray = BandTopY(Find(battle.transform, "DeckBar"));

            // Screen y grows upward: the board's top must be under the HUD's bottom edge and
            // its bottom must be above the tray's top edge.
            Assert.LessOrEqual(bounds.yMax, hud + 1f,
                $"보드가 HUD 를 침범합니다. 보드 위 {bounds.yMax:0} > HUD 아래 {hud:0}");
            Assert.GreaterOrEqual(bounds.yMin, tray - 1f,
                $"보드가 카드 트레이를 침범합니다. 보드 아래 {bounds.yMin:0} < 트레이 위 {tray:0}");
        }

        static RectTransform Find(Transform root, string name)
        {
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            Assert.Fail($"'{name}' 을 전투 화면에서 찾지 못했습니다.");
            return null;
        }

        static float BandBottomY(RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            return Mathf.Min(RectTransformUtility.WorldToScreenPoint(null, c[0]).y,
                             RectTransformUtility.WorldToScreenPoint(null, c[1]).y);
        }

        static float BandTopY(RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            return Mathf.Max(RectTransformUtility.WorldToScreenPoint(null, c[0]).y,
                             RectTransformUtility.WorldToScreenPoint(null, c[1]).y);
        }

        /// <summary>DumpLayout 이 마지막으로 적은 기둥 상자. 자를 검사할 때 쓴다.</summary>
        static Rect? _lastColumnCss;

        static readonly string AuditDir =
            Path.Combine(Application.dataPath, "..", "..", "production", "qa", "audit", "unity");

        /// <summary>
        /// 지금 화면에 보이는 모든 상자를 **원본과 같은 단위(CSS px)** 로 적는다.
        ///
        /// 되돌릴 때 쓰는 자는 화면을 그릴 때 쓴 자(<see cref="UiTheme"/>)와 **같아야 한다.**
        /// 예전에는 가로를 1080/430, 세로를 1920/932 로 나눴는데, 기준을 하나(2.0601)로
        /// 합치고 내용을 가운데 886 기둥에 넣은 뒤로는 그 환산이 틀렸다 — 가로가 통째로
        /// 18% 좁게 적혀, 멀쩡한 화면까지 전부 "어긋남" 으로 보고됐다(253건 중 242건).
        /// 자가 틀리면 보고서 전체가 틀린다.
        /// </summary>
        static void DumpLayout(string screen)
        {
            // 화면 픽셀 -> 페이지(CSS) px. 기둥의 좌우 여백과 페이지 위 여백을 빼고 나눈다.
            float perPage = UiTheme.ScreenPerPage;
            float marginX = UiTheme.ColumnScreenMargin;
            float topY = UiTheme.PageScreenTop;
            float screenH = Screen.height;
            float CssX(float x) => (x - marginX) / perPage;
            float CssY(float yFromBottom) => (screenH - yFromBottom - topY) / perPage;
            _lastColumnCss = null;

            var rows = new List<string>();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(false))
                {
                    // 자가 맞는지 확인할 기준 — 기둥은 정의상 원본 페이지 전체(0,0 430x932)다.
                    // 그림이 없어 아래 목록에는 안 들어가므로 여기서 따로 잰다.
                    // 이름만 보면 안 된다 — 스크롤 안에도 "Column" 이 있다. 기준은
                    // SafeArea 바로 아래의 설계 기둥 하나뿐이다.
                    if (rt.name == "Column" && rt.parent != null && rt.parent.name == "SafeArea")
                    {
                        var k = new Vector3[4];
                        rt.GetWorldCorners(k);
                        float kx0 = RectTransformUtility.WorldToScreenPoint(null, k[0]).x;
                        float kx1 = RectTransformUtility.WorldToScreenPoint(null, k[2]).x;
                        float kyB = RectTransformUtility.WorldToScreenPoint(null, k[0]).y;
                        float kyT = RectTransformUtility.WorldToScreenPoint(null, k[1]).y;
                        _lastColumnCss = new Rect(CssX(Mathf.Min(kx0, kx1)), CssY(Mathf.Max(kyT, kyB)),
                                                  Mathf.Abs(kx1 - kx0) / perPage,
                                                  Mathf.Abs(kyT - kyB) / perPage);
                    }

                    var img = rt.GetComponent<UnityEngine.UI.Graphic>();
                    var tmp = rt.GetComponent<TMPro.TextMeshProUGUI>();
                    if (img == null && tmp == null) continue;

                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    float x0 = RectTransformUtility.WorldToScreenPoint(null, c[0]).x;
                    float x1 = RectTransformUtility.WorldToScreenPoint(null, c[2]).x;
                    float yBottom = RectTransformUtility.WorldToScreenPoint(null, c[0]).y;
                    float yTop = RectTransformUtility.WorldToScreenPoint(null, c[1]).y;
                    float w = Mathf.Abs(x1 - x0), h = Mathf.Abs(yTop - yBottom);
                    if (w < 1f || h < 1f) continue;

                    // 화면 좌표는 아래가 0이고 CSS 는 위가 0이다.
                    float cssX = CssX(Mathf.Min(x0, x1));
                    float cssY = CssY(Mathf.Max(yTop, yBottom));
                    string colour = img != null
                        ? "#" + ColorUtility.ToHtmlStringRGBA(img.color) : "";
                    string text = tmp != null && !string.IsNullOrWhiteSpace(tmp.text)
                        ? tmp.text.Replace('\n', ' ').Replace('\t', ' ') : "";
                    if (text.Length > 40) text = text.Substring(0, 40);

                    // 글자가 **실제로 찍히는** 자리. 상자가 아니라 이것을 비교해야 한다 —
                    // 원본의 <b>아이콘+글자</b> 와 우리 글자 전용 라벨은 상자가 늘 다르고,
                    // 그걸 차이로 세면 멀쩡한 화면이 전부 틀린 것으로 나온다. 실제로
                    // 홈 화면 7건을 "진짜 차이" 로 잘못 보고했다.
                    string ink = "";
                    if (tmp != null && text.Length > 0)
                    {
                        tmp.ForceMeshUpdate();
                        var tb = tmp.textBounds;
                        if (tb.size.x > 0f && tb.size.y > 0f)
                        {
                            Vector3 lo = rt.TransformPoint(new Vector3(tb.min.x, tb.min.y, 0f));
                            Vector3 hi = rt.TransformPoint(new Vector3(tb.max.x, tb.max.y, 0f));
                            var p0 = RectTransformUtility.WorldToScreenPoint(null, lo);
                            var p1 = RectTransformUtility.WorldToScreenPoint(null, hi);
                            ink = string.Format("{0:0.0},{1:0.0},{2:0.0},{3:0.0}",
                                CssX(Mathf.Min(p0.x, p1.x)),
                                CssY(Mathf.Max(p0.y, p1.y)),
                                Mathf.Abs(p1.x - p0.x) / perPage,
                                Mathf.Abs(p1.y - p0.y) / perPage);
                        }
                    }

                    rows.Add(string.Join("\t", new[]
                    {
                        NodePath(rt), cssX.ToString("0.0"), cssY.ToString("0.0"),
                        (w / perPage).ToString("0.0"), (h / perPage).ToString("0.0"),
                        colour,
                        tmp != null ? (tmp.fontSize * UiTheme.CanvasToScreen / perPage).ToString("0.0") : "",
                        text, ink,
                    }));
                }
            }

            try
            {
                Directory.CreateDirectory(AuditDir);
                File.WriteAllLines(Path.Combine(AuditDir, screen + ".tsv"),
                    new[] { "path\tx\ty\tw\th\tcolor\tfs\ttext\tink" }.Concat(rows));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[audit] {screen} 치수를 적지 못했습니다: {e.Message}");
            }
        }

        /// <summary>
        /// 치수를 적는 자가 맞는지 그 자리에서 확인한다.
        ///
        /// 2026-10-22 에 이 자가 틀어져 있던 것을 보고서 전체를 읽고서야 알았다 — 가로를
        /// 옛 배율(1080/430)로 나누고 있어서, 멀쩡한 화면까지 18% 좁게 적히고 253건 중
        /// 242건이 "어긋남" 으로 올라왔다. 기둥은 **정의상** 원본 페이지 전체(0,0 430x932)이므로,
        /// 그렇게 적히지 않으면 자가 틀린 것이다.
        /// </summary>
        static void AssertAuditRulerIsHonest(string screen)
        {
            if (_lastColumnCss == null) return;
            var box = _lastColumnCss.Value;
            Assert.AreEqual(0f, box.x, 1.5f, $"{screen}: 기둥 x 가 0 이 아닙니다 — 치수를 적는 자가 틀렸습니다.");
            Assert.AreEqual(0f, box.y, 1.5f, $"{screen}: 기둥 y 가 0 이 아닙니다 — 치수를 적는 자가 틀렸습니다.");
            Assert.AreEqual(430f, box.width, 1.5f,
                            $"{screen}: 기둥 너비가 430 이 아닙니다 — 치수를 적는 자가 틀렸습니다.");
            Assert.AreEqual(932f, box.height, 1.5f,
                            $"{screen}: 기둥 높이가 932 가 아닙니다 — 치수를 적는 자가 틀렸습니다.");
        }

        static void AssertNoBlankLabels(Transform root, string where)
        {
            var blank = new System.Collections.Generic.List<string>();
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false))
            {
                if (string.IsNullOrWhiteSpace(tmp.text)) continue;
                tmp.ForceMeshUpdate();
                if (tmp.textInfo.characterCount > 0) continue;
                var r = tmp.rectTransform.rect;
                blank.Add($"{tmp.name}('{tmp.text}') rect={r.width:0}x{r.height:0} " +
                          $"필요={tmp.preferredWidth:0}x{tmp.preferredHeight:0}");
            }
            Assert.IsEmpty(blank, $"{where}에서 글자를 그리지 않는 라벨: " + string.Join(" | ", blank));
        }

        /// <summary>
        /// Reports the canvas size the scaler produces for a screen, which is what fixed-pixel
        /// layouts actually get. With Match 0.5 a tall phone yields a canvas NARROWER than the
        /// 1080 reference, and anything positioned in absolute pixels crowds.
        /// </summary>
        static void ReportCanvas(string tag)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null) return;
            var rect = ((RectTransform)canvas.transform).rect;
            Debug.Log($"[canvas] {tag} screen={Screen.width}x{Screen.height} " +
                      $"scale={canvas.scaleFactor:0.000} canvas={rect.width:0}x{rect.height:0}");
        }

        /// <summary>Dumps everything that can make a TMP label draw nothing.</summary>
        static void Probe(string tag, TMPro.TextMeshProUGUI tmp)
        {
            tmp.ForceMeshUpdate();
            var r = tmp.rectTransform.rect;
            Debug.Log($"[probe] {tag} '{tmp.text}' rect={r.width:0}x{r.height:0} " +
                      $"chars={tmp.textInfo.characterCount} lines={tmp.textInfo.lineCount} " +
                      $"pref={tmp.preferredWidth:0}x{tmp.preferredHeight:0} " +
                      $"fs={tmp.fontSize} auto={tmp.enableAutoSizing} " +
                      $"wrap={tmp.textWrappingMode} overflow={tmp.overflowMode} " +
                      $"maxVis={tmp.maxVisibleCharacters} firstVis={tmp.firstVisibleCharacter} " +
                      $"active={tmp.isActiveAndEnabled} alpha={tmp.color.a:0.00} " +
                      $"groups={CanvasGroupChain(tmp.transform)} " +
                      $"scale={tmp.transform.lossyScale}");
        }

        /// <summary>Any CanvasGroup up the chain, since one at alpha 0 hides a label silently.</summary>
        static string CanvasGroupChain(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var p = t; p != null; p = p.parent)
                if (p.TryGetComponent<CanvasGroup>(out var g))
                    parts.Add($"{p.name}:{g.alpha:0.00}");
            return parts.Count == 0 ? "none" : string.Join(">", parts);
        }

        /// <summary>
        /// Deletes the save before the scene boots, so every run starts from the same
        /// profile. Without this the audit compares a moving target: the file accumulates
        /// across runs - attendance claimed, roster grown, idle banked - and the same code
        /// measured 216 pairs / 133 diffs one run and 214 / 131 the next. The pairing is by
        /// text, so a claimed attendance row simply stops matching.
        /// </summary>
        static void ResetSaveOnDisk()
        {
            foreach (var name in new[] { SaveService.FileName, SaveService.FileName + ".bak",
                                         SaveService.FileName + ".tmp",
                                         SaveService.FileName + ".corrupt" })
            {
                var path = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// The state the original baseline was captured in: a brand-new save with 1-1
        /// cleared, which is what unlocks 콘텐츠 and 대청소. Mirrors
        /// production/qa/audit/original/CAPTURE.md - change one side and you must change
        /// the other, or the two stop being comparable.
        /// </summary>
        /// <summary>
        /// 10.19-e moved the 대청소 unlock from 1-1 to 1-2 (content_unlock survivor cond
        /// 1 -> 1003), so clearing only 1-1 no longer opens 콘텐츠. Both stages are cleared
        /// here, and the original side seeds the same two in CAPTURE.md.
        /// </summary>
        static readonly int[] ClearedStageIds = { 101, 102 };

        static void SeedAuditState(GameServices svc)
        {
            svc.Profile.Data.best = ClearedStageIds.Length;
            foreach (var id in ClearedStageIds) svc.Profile.Data.SetStars(id, 3);
            svc.Persist();

            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobby != null && lobby.gameObject.activeInHierarchy) lobby.Refresh();
        }

        static IEnumerator LoadScene()
        {
            ResetSaveOnDisk();
            var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"씬 '{SceneName}' 을 빌드 설정에서 찾지 못했습니다.");
            while (!op.isDone) yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstStage_Deploy_Battle_Result_Persisted()
        {
            yield return LoadScene();

            var app = Object.FindFirstObjectByType<AppRoot>();
            Assert.IsNotNull(app, "씬에 AppRoot 가 없습니다.");

            // AppRoot.Start boots the services.
            yield return null;
            var svc = GameServices.Instance;
            Assert.IsNotNull(svc, "GameServices 가 생성되지 않았습니다.");
            Assert.IsTrue(svc.Ready,
                "게임 데이터 로딩 실패: " + string.Join(" | ", svc.FatalProblems ?? new string[0]));

            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            var battle = Object.FindFirstObjectByType<BattleScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(lobby, "LobbyScreen 이 없습니다.");
            Assert.IsNotNull(battle, "BattleScreen 이 없습니다.");
            Assert.IsTrue(lobby.gameObject.activeInHierarchy, "부팅 후 로비가 보여야 합니다.");
            Assert.Greater(svc.Profile.Data.deck.Count, 0, "시작 편성이 비어 있습니다.");

            // Match the original dump's state before anything is photographed.
            SeedAuditState(svc);
            Canvas.ForceUpdateCanvases();
            yield return null;

            // Same guard as the battle screen: nothing on the lobby may be silently blank.
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNoBlankLabels(lobby.transform, "로비");

            yield return Shot("01-lobby");
            ReportCanvas("lobby");

            // ---- the stage tab, and the bug it exposed: the bottom bar's items are placed
            // by a layout group, so a button that wrote its own rest position dragged the
            // whole row up out of its strip the moment a tab was pressed.
            lobby.ShowTab(LobbyTab.Stage);
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNavItemsSitInTheirStrip(lobby);
            AssertNoBlankLabels(lobby.transform, "스테이지 탭");
            yield return Shot("09-lobby-stage");

            // ---- the three tabs the redesign added, each photographed and swept for the
            // blank-label failure that a too-short rect causes.
            foreach (var (tab, shot) in new[]
                     {
                         (LobbyTab.Content, "09b-lobby-content"),
                         (LobbyTab.Units, "10-lobby-units"),
                         (LobbyTab.Deck, "10b-lobby-deck"),
                         (LobbyTab.Gacha, "11-lobby-gacha"),
                         (LobbyTab.Shop, "12-lobby-shop"),
                     })
            {
                lobby.ShowTab(tab);
                Canvas.ForceUpdateCanvases();
                yield return null;
                AssertNavItemsSitInTheirStrip(lobby);
                AssertNoBlankLabels(lobby.transform, tab.ToString());
                AssertPanelHasContent(lobby.transform, tab.ToString(), 6);
                yield return ShotAt(shot, 1080, 1920);
            }

            // ---- 콘텐츠 탭의 아래쪽: 협동 · 먼지 타워 · 가전 대청소 리그 · 준비 중 카드.
            // 한 화면에 들어가지 않아 위쪽 사진만으로는 이 카드들이 있는지 알 수 없다.
            lobby.ShowTab(LobbyTab.Content);
            Canvas.ForceUpdateCanvases();
            yield return null;
            // "Content" 라는 이름은 버튼 안에도 있다. 탭 자체는 카드 묶음(Column)으로 찾는다.
            var modeCards = lobby.GetComponentsInChildren<ModeCard>(true);
            Assert.Greater(modeCards.Length, 0, "콘텐츠 탭에 모드 카드가 없습니다.");
            var contentRoot = modeCards[0].transform.parent.parent.parent;   // Column → Viewport → Content
            foreach (var id in new[] { "Coop", "Tower", "League", "Soon" })
                Assert.IsNotNull(contentRoot.Find("Viewport/Column/" + id),
                                 $"콘텐츠 탭에 {id} 카드가 없습니다.");

            // 협동 · 먼지 타워 · 가전 대청소 리그 — 원본과 같은 틀의 카드 세 장.
            Assert.AreEqual(3, modeCards.Length,
                            $"모드 카드가 3장이어야 하는데 {modeCards.Length}장입니다.");

            var contentScroll = contentRoot.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            Assert.IsNotNull(contentScroll, "콘텐츠 탭에 스크롤이 없습니다.");
            contentScroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNoBlankLabels(lobby.transform, "콘텐츠 탭 아래쪽");
            yield return ShotAt("09c-lobby-content-modes", 1080, 1920);
            contentScroll.verticalNormalizedPosition = 1f;

            // ---- 일러스트 정보창, opened the way the 가전 tab opens it, on all three tabs.
            // Every unit_art slot is bound, so a blank panel here means the viewer is reading
            // the wrong one - which is exactly what a screenshot alone would not tell us.
            var art = Object.FindFirstObjectByType<ArtViewerScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(art, "ArtViewerScreen 이 씬에 없습니다.");
            var openArt = typeof(LobbyScreen).GetMethod("OpenArtViewer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(openArt, "LobbyScreen.OpenArtViewer 를 찾지 못했습니다.");
            openArt.Invoke(lobby, null);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.IsTrue(art.gameObject.activeInHierarchy, "일러스트 정보창이 열리지 않았습니다.");
            Assert.IsNotNull(art.Unit, "일러스트 정보창이 아무 가전도 들고 있지 않습니다.");
            Assert.IsNotNull(svc.Profile.Data.Own(art.Unit.Id),
                "일러스트 정보창은 보유한 가전부터 열어야 합니다.");

            var showArtTab = typeof(ArtViewerScreen).GetMethod("ShowTab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(showArtTab, "ArtViewerScreen.ShowTab 을 찾지 못했습니다.");
            foreach (var (artTab, artShot) in new[]
                     {
                         (ArtTab.Profile, "15-art-profile"),
                         (ArtTab.Face, "16-art-face"),
                         (ArtTab.Sheet, "17-art-sheet"),
                     })
            {
                showArtTab.Invoke(art, new object[] { artTab });
                Canvas.ForceUpdateCanvases();
                yield return null;
                Assert.AreEqual(artTab, art.Tab);
                AssertNoBlankLabels(art.transform, "일러스트 " + artTab);
                AssertArtSlotsAreFilled(art, artTab);
                yield return ShotAt(artShot, 1080, 1920);
            }

            // The arrows must actually move through the catalogue.
            var before = art.Unit;
            var step = typeof(ArtViewerScreen).GetMethod("Step",
                BindingFlags.Instance | BindingFlags.NonPublic);
            step.Invoke(art, new object[] { 1 });
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreNotSame(before, art.Unit, "› 를 눌러도 다음 가전으로 넘어가지 않습니다.");

            var closeArt = typeof(ArtViewerScreen).GetMethod("Close",
                BindingFlags.Instance | BindingFlags.NonPublic);
            closeArt.Invoke(art, null);
            yield return null;
            Assert.IsFalse(art.gameObject.activeInHierarchy, "일러스트 정보창이 닫히지 않았습니다.");

            // ---- 장비 가방. Seeded first: an empty bag would photograph as a blank panel
            // and prove nothing, so the run gives itself a few pieces, wears one, and takes
            // one exclusive - which is also what makes the 전용장비 grid appear at all.
            var bag = Object.FindFirstObjectByType<GearBagScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(bag, "GearBagScreen 이 씬에 없습니다.");

            var commonIds = svc.Db.Gear.Values.Where(g => g.grade <= 3)
                .OrderBy(g => g.id).Select(g => g.id).ToList();
            Assert.IsNotEmpty(commonIds, "공용 장비가 테이블에 없습니다.");
            for (int i = 0; i < 7; i++) svc.Profile.AddGear(commonIds[i % commonIds.Count]);

            var exclusiveFor = svc.Profile.Data.deck[0];
            var exclusiveRow = svc.Profile.ExclusiveOf(exclusiveFor);
            if (exclusiveRow != null) svc.Profile.AddGear(exclusiveRow.id);

            svc.Profile.EquipGear(exclusiveFor, svc.Profile.Data.gear[0].uid);
            svc.Profile.Data.AddItem(PlayerProfile.GearStoneItem, 600);
            svc.Profile.Data.AddItem(PlayerProfile.GearEssenceItem, 150);
            svc.Persist();

            var openBag = typeof(LobbyScreen).GetMethod("OpenGearBag",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(openBag, "LobbyScreen.OpenGearBag 을 찾지 못했습니다.");
            openBag.Invoke(lobby, null);
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.IsTrue(bag.gameObject.activeInHierarchy, "장비 가방이 열리지 않았습니다.");
            Assert.AreEqual(GearBagPanel.Bag, bag.Panel);
            AssertNoBlankLabels(bag.transform, "장비 가방");
            yield return ShotAt("18-gear-bag", 1080, 1920);

            // The grid must actually hold the seeded pieces - a bag that lists nothing while
            // the save holds seven would pass a label sweep and still be broken.
            var cells = bag.GetComponentsInChildren<GearCell>(false);
            Assert.GreaterOrEqual(cells.Length, 7,
                $"가방에 장비 {svc.Profile.Data.gear.Count}개가 있는데 칸은 {cells.Length}개뿐입니다.");

            // ---- the detail for the piece that is being worn.
            bag.ShowDetail(GearTarget.Common(svc.Profile.Data.gear[0].uid));
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(GearBagPanel.Detail, bag.Panel);
            AssertNoBlankLabels(bag.transform, "장비 상세");
            yield return ShotAt("19-gear-detail", 1080, 1920);

            // ---- enhancing moves the level, which is the one action with a visible number.
            // The save carries over between runs, so this piece may already sit at its cap.
            // Assert both directions rather than assuming there is room: a capped piece that
            // still levels is as much a bug as one that refuses when it should not.
            int uidUnderTest = svc.Profile.Data.gear[0].uid;
            int lvBefore = svc.Profile.GearInst(uidUnderTest).lv;
            var cost = svc.Profile.CommonGearCost(uidUnderTest);
            bool affordable = cost != null
                && svc.Profile.ItemCount(PlayerProfile.GearStoneItem) >= cost.Value.stone
                && svc.Profile.Coin >= cost.Value.coin;

            var enhance = typeof(GearBagScreen).GetMethod("Enhance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            enhance.Invoke(bag, null);
            Canvas.ForceUpdateCanvases();
            yield return null;

            int lvAfter = svc.Profile.GearInst(uidUnderTest).lv;
            Assert.AreEqual(affordable ? lvBefore + 1 : lvBefore, lvAfter,
                affordable
                    ? "강화를 눌러도 레벨이 오르지 않았습니다."
                    : $"강화할 수 없는 장비(+{lvBefore})인데 레벨이 올랐습니다.");

            if (exclusiveRow != null)
            {
                bag.ShowDetail(GearTarget.Exclusive(exclusiveFor));
                Canvas.ForceUpdateCanvases();
                yield return null;
                AssertNoBlankLabels(bag.transform, "전용장비 상세");
                yield return ShotAt("20-gear-exclusive", 1080, 1920);
            }

            // ---- 전용장비 교환: every appliance is listed, affordable or not, so the list
            // length is the gate - a screen that silently dropped the ones you cannot take
            // would look fine and hide half the catalogue.
            bag.ShowExchange();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(GearBagPanel.Exchange, bag.Panel);
            var exRows = bag.GetComponentsInChildren<GearExchangeRow>(false);
            int withExclusive = svc.Db.UnitOrder.Count(u => svc.Profile.ExclusiveOf(u) != null);
            Assert.AreEqual(withExclusive, exRows.Length,
                $"전용장비를 가진 가전 {withExclusive}종이 모두 나와야 합니다.");
            AssertNoBlankLabels(bag.transform, "전용장비 교환");
            yield return ShotAt("21-gear-exchange", 1080, 1920);

            // Taking one must actually spend the essence and change what you hold.
            // This test runs against the real save, and every previous run left another
            // exclusive owned, so eventually there was nothing left to exchange and the
            // assertion died on an empty sequence instead of on the behaviour. Clear one
            // first: the point is that exchanging works, not that the save happens to be new.
            var takeTarget = svc.Db.UnitOrder.FirstOrDefault(
                u => svc.Profile.ExclusiveOf(u) != null && svc.Profile.ExclusiveState(u) == null);
            if (takeTarget == null)
            {
                takeTarget = svc.Db.UnitOrder.First(u => svc.Profile.ExclusiveOf(u) != null);
                svc.Profile.Data.excl.Remove(takeTarget);
            }
            long essenceBefore = svc.Profile.ItemCount(PlayerProfile.GearEssenceItem);
            var exchangeMethod = typeof(GearBagScreen).GetMethod("Exchange",
                BindingFlags.Instance | BindingFlags.NonPublic);
            exchangeMethod.Invoke(bag, new object[] { takeTarget });
            yield return null;
            Assert.IsNotNull(svc.Profile.ExclusiveState(takeTarget),
                $"'{takeTarget}' 전용장비를 교환했는데 보유로 바뀌지 않았습니다.");
            Assert.AreEqual(essenceBefore - svc.Db.Config.exclExchange,
                svc.Profile.ItemCount(PlayerProfile.GearEssenceItem),
                "교환했는데 정수가 줄지 않았습니다.");

            var closeBag = typeof(GearBagScreen).GetMethod("Close",
                BindingFlags.Instance | BindingFlags.NonPublic);
            closeBag.Invoke(bag, null);
            yield return null;
            Assert.IsFalse(bag.gameObject.activeInHierarchy, "장비 가방이 닫히지 않았습니다.");

            // ---- appliance detail, opened the way the roster opens it
            var detail = Object.FindFirstObjectByType<UnitDetailScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(detail, "UnitDetailScreen 이 씬에 없습니다.");
            var open = typeof(LobbyScreen).GetMethod("OpenUnitDetail",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(open, "LobbyScreen.OpenUnitDetail 을 찾지 못했습니다.");
            open.Invoke(lobby, new object[] { svc.Db.Units[svc.Profile.Data.deck[0]] });
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.IsTrue(detail.gameObject.activeInHierarchy, "상세 화면이 열리지 않았습니다.");
            Assert.AreEqual(svc.Profile.Data.deck[0], detail.Current.Id, "다른 가전이 열렸습니다.");
            AssertNoBlankLabels(detail.transform, "가전 상세");
            yield return ShotAt("13-unit-detail", 1080, 1920);

            // 10.17-b: the same sheet for a SUPPORT appliance carries two extra cells that
            // the attacker above must not have. Opening the generator proves both halves -
            // the cells appear, and they said nothing at all on 청소기.
            if (svc.Db.Config.TryNum("genLvBonus", out _))
            {
                if (!svc.Profile.Data.Owns("gen")) svc.Profile.AddUnit("gen");
                open.Invoke(lobby, new object[] { svc.Db.Units["gen"] });
                Canvas.ForceUpdateCanvases();
                yield return null;

                var row = FindChild(detail.transform, "SupportRow")
                              .GetComponent<TMPro.TextMeshProUGUI>();
                Assert.IsTrue(row.gameObject.activeInHierarchy,
                              "발전기 상세에 지원 성장 줄이 없습니다 (10.17-b).");
                Assert.IsTrue(row.text.Contains("전력 회복") && row.text.Contains("풀가동"),
                              $"지원 성장 줄 내용이 비었습니다: '{row.text}'");
                row.ForceMeshUpdate();
                Assert.LessOrEqual(row.preferredWidth, row.rectTransform.rect.width + 1f,
                    $"지원 성장 줄 '{row.text}' 이 잘립니다 " +
                    $"(필요 {row.preferredWidth:0}px / 공간 {row.rectTransform.rect.width:0}px).");
                AssertNoBlankLabels(detail.transform, "발전기 상세");
                Debug.Log($"[smoke] 지원 성장 · {row.text}");
                yield return ShotAt("13b-unit-detail-support", 1080, 1920);
            }
            detail.gameObject.SetActive(false);

            // ---- season pass
            var pass = Object.FindFirstObjectByType<PassScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(pass, "PassScreen 이 씬에 없습니다.");
            pass.Open(svc);
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNoBlankLabels(pass.transform, "시즌 패스");
            yield return ShotAt("14-pass", 1080, 1920);
            pass.gameObject.SetActive(false);

            lobby.ShowTab(LobbyTab.Home);
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNavItemsSitInTheirStrip(lobby);

            // ---- enter stage 1
            // Tutorial steps deliberately hold the simulation until acknowledged, and several
            // wait on specific card selections. This test covers the battle and save path, so
            // use the game's own "튜토리얼 끄기" setting rather than scripting each step.
            svc.Profile.Data.tutOff = 1;

            var stage = svc.Db.Stages[0];
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            battle.Begin(svc, stage);
            yield return null;

            var sim = battle.Sim;
            Assert.IsNotNull(sim, "전투 시뮬레이션이 시작되지 않았습니다.");
            Assert.AreEqual(svc.Db.Config.lifeMax, sim.Life, "시작 청결도가 config 와 다릅니다.");
            // 10.15-h gives each stage its own opening power; config.startPower is only the fallback.
            Assert.AreEqual(stage.StartPower, sim.Power, 0.01f, "시작 전력이 스테이지 설정과 다릅니다.");
            Assert.Greater(sim.Remaining, 0, "남은 적 수가 0입니다.");

            // The painted floor and the simulation must agree on where a tile is: a half-cell
            // mismatch between the Tilemap anchor and ToWorld once drew everything a row off.
            var board = Object.FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);
            Assert.IsNotNull(board, "BoardView 가 없습니다.");
            var floorMap = Object.FindFirstObjectByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsInactive.Include);
            Assert.IsNotNull(floorMap, "Floor 타일맵이 없습니다.");
            var cellCentre = floorMap.GetCellCenterWorld(new Vector3Int(stage.GoalX, -stage.GoalY, 0));
            var simCentre = board.ToWorld(stage.GoalX + 0.5f, stage.GoalY + 0.5f);
            Assert.AreEqual(simCentre.x, cellCentre.x, 0.01f, "타일맵 X 가 시뮬레이션 좌표와 어긋납니다.");
            Assert.AreEqual(simCentre.y, cellCentre.y, 0.01f, "타일맵 Y 가 시뮬레이션 좌표와 어긋납니다.");

            yield return Shot("02-battle-intro");

            // The brief gates the battle until the player taps 작전 시작, so press it the way
            // a player would rather than reaching past the screen.
            var brief = battle.transform.Find("Brief");
            Assert.IsNotNull(brief, "브리핑 패널을 찾지 못했습니다.");
            Assert.IsTrue(brief.gameObject.activeInHierarchy, "전투는 브리핑으로 시작해야 합니다.");
            AssertNoBlankLabels(brief, "브리핑");

            var startButton = brief.Find("BriefActions/Start")?.GetComponent<UiButton>();
            Assert.IsNotNull(startButton, "작전 시작 버튼을 찾지 못했습니다.");
            Assert.AreEqual(0f, sim.Time, "시작 전에는 전투 시간이 흐르면 안 됩니다.");

            yield return Shot("08-battle-brief");

            startButton.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
            yield return null;
            Assert.IsFalse(brief.gameObject.activeInHierarchy, "작전 시작 후에도 브리핑이 남아 있습니다.");

            // ---- play through the real screen for a few seconds of wall clock, so the
            // rendered board, deck cards and HUD are genuinely exercised
            int deployed = 0;
            for (int frame = 0; frame < 180 && !sim.Finished; frame++)
            {
                deployed += DeployWhatWeCan(sim);
                yield return null;
            }

            Assert.Greater(deployed, 0, "가전을 한 대도 배치하지 못했습니다.");
            Assert.Greater(sim.Time, 0f, "전투 시간이 흐르지 않았습니다. BattleScreen 이 시뮬레이션을 돌리지 않습니다.");
            Assert.IsTrue(sim.Units.Count > 0, "배치한 가전이 전장에 남아 있지 않습니다.");
            Debug.Log($"[smoke] 화면 구동 {sim.Time:0.0}초 · 배치 {deployed}대 · 적 {sim.Enemies.Count}마리 등장");

            // The whole battle screen, not just one panel: a blank label anywhere is a bug.
            AssertNoBlankLabels(battle.transform, "전투 화면");
            AssertBoardClearsTheBands(battle);

            yield return Shot("03-battle-deployed");

            yield return CheckUnitActionBar(battle, sim);


            // ---- drive the remainder directly; the fixed-step loop is what EditMode covers,
            // so here it only needs to reach a result for the save path to be verified
            for (int i = 0; i < 60_000 && !sim.Finished; i++)
            {
                DeployWhatWeCan(sim);
                sim.Tick(1f / 60f);
            }

            Assert.IsTrue(sim.Finished, "전투가 결말에 도달하지 않았습니다.");
            Debug.Log($"[smoke] 결과 {sim.Outcome} · 처치 {sim.Killed} · 유출 {sim.Leaked} · " +
                      $"청결도 {sim.Life} · 별 {sim.StarsEarned()} · {sim.Time:0}초");

            // Settle runs on the next frame after the sim finishes.
            yield return null;
            // Redesign 2g: the result screen replaced the text overlay, so the evidence shot
            // must come from it rather than from whatever is on screen.
            var resultScreen = Object.FindFirstObjectByType<ResultScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(resultScreen, "ResultScreen 이 씬에 없습니다.");
            Assert.IsTrue(resultScreen.gameObject.activeInHierarchy,
                "전투가 끝났는데 결과 화면이 열리지 않았습니다.");
            AssertNoBlankLabels(resultScreen.transform, "결과");
            yield return Shot("04-battle-result");

            // ---- the result must have been written to disk
            var best = svc.Profile.Data.best;
            var coin = svc.Profile.Data.Item(1);
            svc.Persist();

            var reloaded = svc.Saves.Load();
            Assert.AreEqual(best, reloaded.Data.best, "저장 후 다시 읽은 진행도가 다릅니다.");
            Assert.AreEqual(coin, reloaded.Data.Item(1), 1e-6, "저장 후 다시 읽은 코인이 다릅니다.");
            Debug.Log($"[smoke] 저장 복원 확인: best={reloaded.Data.best}, 코인={reloaded.Data.Item(1)}");
        }

        /// <summary>
        /// Walks every bottom tab and fails on any label that draws nothing. TMP drops a whole
        /// line when its rect is shorter than the line box, so a caption can vanish with no
        /// error anywhere - that is how the top bar's "+" became a bare disc, and how the room
        /// status capsule rendered empty. Checking one screen at a time missed both, so this
        /// sweeps the lobby as a whole.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryLobbyTabDrawsAllOfItsLabels()
        {
            yield return LoadScene();

            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(lobby, "로비를 찾지 못했습니다.");

            var tabs = (UiButton[])typeof(LobbyScreen)
                .GetField("tabButtons", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(lobby);
            Assert.AreEqual(7, tabs.Length, "하단 탭이 7개가 아닙니다.");

            var names = new[] { "홈", "스테이지", "콘텐츠", "가전", "편성", "소환", "상점" };
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].OnPointerClick(new PointerEventData(EventSystem.current));
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                AssertNoBlankLabels(lobby.transform, $"로비 {names[i]} 탭");
            }
        }

        [UnityTest]
        public IEnumerator BoardMatchesStageGridAndHonoursPlacementRules()
        {
            yield return LoadScene();
            yield return null;

            var svc = GameServices.Instance;
            Assert.IsTrue(svc != null && svc.Ready, "서비스가 준비되지 않았습니다.");

            var battle = Object.FindFirstObjectByType<BattleScreen>(FindObjectsInactive.Include);
            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);

            var stage = svc.Db.Stages[0];
            battle.Begin(svc, stage);
            yield return null;

            var sim = battle.Sim;

            // Placement legality must agree with the tile grid the table defines.
            int ground = 0, furniture = 0;
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                {
                    char ch = sim.Field.TileAt(c, r);
                    if (ch == BattleField.Furniture)
                    {
                        furniture++;
                        Assert.IsFalse(sim.CanPlace("vac", c, r), $"근접 가전이 가구({c},{r})에 놓입니다.");
                    }
                    else if (BattleField.IsGroundTile(ch) && !sim.Field.NoDeploy(c, r))
                    {
                        ground++;
                        Assert.IsFalse(sim.CanPlace("ac", c, r), $"원거리 가전이 바닥({c},{r})에 놓입니다.");
                    }
                }

            Assert.Greater(ground, 0, "배치 가능한 바닥 칸이 없습니다.");
            Assert.Greater(furniture, 0, "배치 가능한 가구 칸이 없습니다.");
            Debug.Log($"[smoke] {stage.No} 배치 가능 바닥 {ground}칸, 가구 {furniture}칸");

            yield return Shot("05-board-grid");
        }

        /// <summary>
        /// 대청소 end to end: pick a leader, run the arena, and check the HUD, the team strip
        /// and the level-up sheet actually draw. There was no PlayMode coverage of this screen
        /// at all - the only evidence on disk was two days old, so the whole mode could have
        /// been broken and every test would still have been green.
        /// </summary>
        /// <summary>
        /// 협동 청소를 실제로 띄워 돌린다. 고리판은 출구가 없어 본편 전투 검사로는
        /// 잡히지 않는 것들이 있다 — 두 줄이 켜지는지, 라벨이 비지 않는지,
        /// 소환이 실제로 가전을 세우는지, HUD 가 고리 먼지를 세는지.
        /// </summary>
        [UnityTest]
        public IEnumerator CoopRunsOnTheRingAndDrawsItsTwoStrips()
        {
            yield return LoadScene();
            yield return null;
            var svc = GameServices.Instance;
            Assert.IsNotNull(svc, "GameServices 가 생성되지 않았습니다.");

            // 협동은 1-5 를 깨야 열린다. 대조 기준과 같은 방식으로 계정을 고정한다.
            SeedAuditState(svc);
            svc.Profile.Data.best = 5;
            svc.Persist();

            var battle = Object.FindFirstObjectByType<BattleScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(battle, "BattleScreen 이 씬에 없습니다.");
            var bars = Object.FindFirstObjectByType<CoopBars>(FindObjectsInactive.Include);
            Assert.IsNotNull(bars, "CoopBars 가 씬에 없습니다.");

            var lobbyScreen = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);

            var partner = svc.Db.CoopPartner("rush");
            Assert.IsNotNull(partner, "coop_partner 에 rush 가 없습니다.");

            // 먼저 파트너 고르기를 띄워 남긴다. 원본 `coopEnter` 에 해당한다.
            var pick = Object.FindFirstObjectByType<CoopPickScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(pick, "CoopPickScreen 이 씬에 없습니다.");
            // 고르기 시트는 로비 위에 뜬다. 로비를 꺼 둔 채 열면 부모가 꺼져 있어
            // TMP 가 글자를 아예 만들지 않는다 — 비어 보이지만 자리는 멀쩡하다.
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(true);
            pick.Open(svc);
            // 꺼져 있던 화면은 켠 프레임에 레이아웃이 아직 안 돈다. 중첩 레이아웃 그룹이
            // 자리를 잡고 TMP 가 글자를 만들 때까지 두 프레임을 준다.
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertNoBlankLabels(pick.transform, "협동 파트너 고르기");
            yield return ShotAt("33-coop-pick", 1080, 1920);
            pick.gameObject.SetActive(false);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);

            battle.gameObject.SetActive(true);
            battle.BeginCoop(svc, partner);
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsTrue(battle.IsCoop, "협동 모드로 들어가지 않았습니다.");
            Assert.IsNotNull(battle.Coop, "CoopSim 이 만들어지지 않았습니다.");
            Assert.IsTrue(bars.gameObject.activeInHierarchy, "협동 두 줄이 켜지지 않았습니다.");

            // 원본 `.battle.rd` 는 slots · deck 을 감춘다. 브리핑도 협동에는 없다.
            var tray = FindChild(battle.transform, "DeckBar");
            Assert.IsNotNull(tray, "카드 트레이를 찾지 못했습니다.");
            Assert.IsFalse(tray.gameObject.activeInHierarchy,
                           "협동에서는 카드 트레이가 숨어야 합니다. 카드로 놓는 모드가 아닙니다.");
            var brief = FindChild(battle.transform, "Brief");
            Assert.IsTrue(brief == null || !brief.gameObject.activeInHierarchy,
                          "협동에는 작전 브리핑이 없습니다.");

            // 시작 안내를 먼저 남기고, 그다음 직접 돌린다. 이 검사는 BattleScreen 의
            // Update 대신 CoopSim 을 직접 틱하므로 시작 버튼을 누를 필요가 없다.
            yield return ShotAt("34-coop-intro", 1080, 1920);
            var overlay = FindChild(battle.transform, "Overlay");
            if (overlay != null) overlay.gameObject.SetActive(false);
            yield return null;

            var coop = battle.Coop;
            Assert.IsTrue(coop.Battle.LoopMode, "고리 모드가 아닙니다.");

            // 90초를 돌린다. 웨이브가 넘어가고 파트너가 가전을 세울 만큼이다.
            for (int i = 0; i < 90 * 60 && !coop.Finished; i++)
            {
                coop.Tick(1f / 60f);
                if (i % 60 == 0) yield return null;   // 연출이 한 프레임에 쏟아지지 않게 자주 양보한다
            }
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.AreEqual(0, coop.Battle.Leaked,
                            "고리판에는 골이 없습니다. 적이 샜다면 이동이 길을 벗어난 것입니다.");
            Assert.Greater(coop.Wave, 1, "90초가 지나도 웨이브가 넘어가지 않았습니다.");
            Assert.Greater(coop.Battle.Killed, 0, "한 마리도 못 잡았습니다.");

            int partnerUnits = 0;
            foreach (var u in coop.Battle.Units)
                if (!u.Dead && coop.SideOf(u) == CoopSide.Partner) partnerUnits++;
            Assert.Greater(partnerUnits, 0, "파트너가 가전을 한 대도 놓지 않았습니다.");

            // 내 쪽 소환이 실제로 서는지. 전력을 채우고 빈 고리 칸에 세운다.
            coop.GrantPower(CoopSide.Player, 60f);
            var free = coop.FreeCells(CoopSide.Player, TileKind.Ground);
            Assert.Greater(free.Count, 0, "내 고리 칸이 하나도 비어 있지 않습니다.");
            var mine = coop.Summon(CoopSide.Player, free[0]);
            Assert.IsNotNull(mine, "소환이 실패했습니다.");
            Assert.AreEqual(1, coop.StarOf(mine), "소환은 ★1 이어야 합니다.");

            bars.Refresh();
            Canvas.ForceUpdateCanvases();
            yield return null;

            AssertNoBlankLabels(bars.transform, "협동 두 줄");
            yield return ShotAt("35-coop-battle", 1080, 1920);

            // 결과 창. 고리를 강제로 채우는 대신 판을 끝내고 정산을 본다.
            int waves = coop.WavesCleared;
            coop.Battle.ForceLose();
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(waves, svc.Profile.CoopBest("rush"),
                            "버틴 웨이브가 최고 기록으로 남지 않았습니다.");
            yield return ShotAt("36-coop-result", 1080, 1920);
        }


        /// <summary>
        /// 리그 화면: 상점에서 사고, 판에 올리고, 한 라운드를 실제로 끝까지 싸운다.
        /// 사진만으로는 "눌러서 사지는가" 를 알 수 없어 조작까지 따라 한다.
        /// </summary>
        [UnityTest]
        public IEnumerator LeagueBuysPlacesAndFightsARound()
        {
            yield return LoadScene();
            yield return null;
            var svc = GameServices.Instance;
            Assert.IsNotNull(svc, "GameServices 가 생성되지 않았습니다.");

            // 리그는 3-5 를 깨야 열린다. 대조 기준과 같은 방식으로 계정을 고정한다.
            SeedAuditState(svc);
            svc.Profile.Data.best = 15;
            svc.Persist();

            var lobbyScreen = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);

            var league = Object.FindFirstObjectByType<LeagueScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(league, "LeagueScreen 이 씬에 없습니다.");
            league.gameObject.SetActive(true);

            // 먼저 단계를 고르는 시트가 뜬다 — 원본 lgEnter 와 같은 자리다.
            league.OpenTierPick(svc);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var tierCards = league.GetComponentsInChildren<LeagueTierCard>(true);
            Assert.AreEqual(svc.Db.LeagueTiers.Count, tierCards.Length,
                            "단계 카드 수가 league_tier 와 다릅니다.");
            AssertNoBlankLabels(league.transform, "리그 단계 고르기");
            yield return ShotAt("25b-league-tier", 1080, 1920);

            league.Begin(svc, 1);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var sim = (LeagueSim)typeof(LeagueScreen)
                .GetField("_sim", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(league);
            Assert.IsNotNull(sim, "리그 판이 열리지 않았습니다.");
            Assert.AreEqual(LeaguePhase.Prep, sim.Phase);

            AssertNoBlankLabels(league.transform, "리그 준비");
            yield return ShotAt("26-league-prep", 1080, 1920);

            // ---- 상점에서 살 수 있는 것을 사서 판에 올린다.
            var cards = FindChild(league.transform, "Shop").GetComponentsInChildren<LeagueShopCard>(true);
            Assert.AreEqual(sim.Shop.Length, cards.Length, "상점 칸 수가 다릅니다.");
            int before = sim.Gold;
            foreach (var c in cards)
            {
                var b = c.GetComponent<UnityEngine.UI.Button>();
                if (b != null) b.onClick.Invoke();
            }
            yield return null;
            Assert.Less(sim.Gold, before, "상점을 눌렀는데 코인이 줄지 않았습니다.");

            int bought = 0;
            foreach (var p in sim.Bench) if (p != null) bought++;
            Assert.Greater(bought, 0, "산 가전이 대기석에 없습니다.");

            // 대기석 첫 칸을 골라 판 한가운데에 놓는다.
            var benchCells = FindChild(league.transform, "Bench")
                .GetComponentsInChildren<LeagueBenchCell>(true);
            int slot = System.Array.FindIndex(sim.Bench, x => x != null);
            benchCells[slot].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            var cells = FindChild(league.transform, "Cells").GetComponentsInChildren<UiButton>(true);
            Assert.AreEqual(LeagueSim.BoardCells, cells.Length, "판 칸이 21개가 아닙니다.");
            cells[10].GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
            cells[10].SendMessage("OnPointerClick",
                new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),
                SendMessageOptions.DontRequireReceiver);
            yield return null;

            Assert.Greater(sim.OnBoard, 0, "판에 가전을 올리지 못했습니다.");
            Canvas.ForceUpdateCanvases();
            yield return ShotAt("27-league-board", 1080, 1920);

            // ---- 한 라운드를 실제로 싸운다.
            Assert.IsTrue(sim.StartBattle(), "전투가 시작되지 않았습니다.");
            yield return null;
            Assert.AreEqual(LeaguePhase.Battle, sim.Phase);
            Assert.Greater(sim.Units.Count, 1, "판에 아무도 서지 않았습니다.");
            Canvas.ForceUpdateCanvases();
            yield return ShotAt("28-league-battle", 1080, 1920);

            sim.RunBattleToEnd();
            Assert.IsTrue(sim.BattleOver, "전투가 끝나지 않았습니다.");
            var last = sim.EndRound();
            Assert.IsNotNull(last, "라운드 정산이 돌지 않았습니다.");
            Assert.AreEqual(1, last.Round);

            league.Sync();
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNoBlankLabels(league.transform, "리그 라운드 뒤");
        }

        /// <summary>
        /// 먼지 타워: 층 목록 → 층 상세 → 편성 → 전투까지 실제로 눌러서 지나간다.
        /// 층 목록 줄 수는 tower_floor 가 정하고, 출전 목록은 보유 가전이 정한다 —
        /// 사진만으로는 알 수 없는 것들이라 세어서 확인한다.
        /// </summary>
        [UnityTest]
        public IEnumerator TowerPicksAFloorFormsUpAndFights()
        {
            yield return LoadScene();
            yield return null;
            var svc = GameServices.Instance;
            Assert.IsNotNull(svc, "GameServices 가 생성되지 않았습니다.");

            // 타워는 2-5 를 깨야 열린다(content_unlock.tower → condition 1011).
            SeedAuditState(svc);
            svc.Profile.Data.best = 15;
            svc.Persist();
            Assert.IsTrue(svc.Profile.TowerUnlocked, "타워가 잠겨 있어 들어갈 수 없습니다.");

            var lobbyScreen = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);

            var league = Object.FindFirstObjectByType<LeagueScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(league, "LeagueScreen 이 씬에 없습니다.");
            league.gameObject.SetActive(true);

            // ---- 층 목록
            league.OpenFloorPick(svc);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var rows = league.GetComponentsInChildren<TowerFloorCard>(true);
            Assert.AreEqual(svc.Db.TowerFloors.Count, rows.Length,
                            "층 줄 수가 tower_floor 와 다릅니다.");
            AssertNoBlankLabels(league.transform, "타워 층 목록");
            yield return ShotAt("37-tower-floors", 1080, 1920);

            // ---- 층 상세
            int top = svc.Profile.TowerTopFloor();
            league.ShowFloorInfo(top);
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertNoBlankLabels(league.transform, "타워 층 상세");
            yield return ShotAt("38-tower-floor-info", 1080, 1920);

            // ---- 편성
            league.BeginTower(svc, top);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var sim = (LeagueSim)typeof(LeagueScreen)
                .GetField("_sim", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(league);
            Assert.IsNotNull(sim, "타워 판이 열리지 않았습니다.");
            Assert.IsTrue(sim.IsTower, "타워가 아니라 리그로 열렸습니다.");
            Assert.AreEqual(LeaguePhase.Prep, sim.Phase);

            var own = league.GetComponentsInChildren<TowerOwnCell>(true);
            Assert.AreEqual(sim.TowerRoster().Count, own.Length,
                            "출전 목록 칸 수가 보유 가전과 다릅니다.");
            Assert.Greater(sim.OnBoard, 0, "지난 편성도 자동 편성도 판을 채우지 못했습니다.");
            Assert.LessOrEqual(sim.OnBoard, sim.Level, "출전 수가 층이 허락한 수를 넘었습니다.");

            // 리그 전용 줄은 이 화면에 없어야 한다.
            Assert.IsFalse(FindChild(league.transform, "Shop").gameObject.activeSelf,
                           "타워에 상점이 남아 있습니다.");
            Assert.IsFalse(FindChild(league.transform, "Bench").gameObject.activeSelf,
                           "타워에 대기석이 남아 있습니다.");

            // 들어갈 때 뜨는 알림(1.3초)이 판을 가린다 — 지나간 뒤에 찍는다.
            float waited = 0f;
            while (waited < 1.5f) { waited += Time.unscaledDeltaTime; yield return null; }
            Canvas.ForceUpdateCanvases();

            AssertNoBlankLabels(league.transform, "타워 편성");
            yield return ShotAt("39-tower-prep", 1080, 1920);

            // ---- 전투
            Assert.IsTrue(sim.StartBattle(), "전투가 시작되지 않았습니다.");
            for (int i = 0; i < 60; i++) sim.Step(1f / 30f);
            league.Sync();
            Canvas.ForceUpdateCanvases();
            yield return null;
            AssertNoBlankLabels(league.transform, "타워 전투");
            yield return ShotAt("40-tower-battle", 1080, 1920);

            sim.RunBattleToEnd();
            Assert.IsTrue(sim.BattleOver, "전투가 끝나지 않았습니다.");

            // ---- 결과: 알림 0.9초가 지나면 창이 뜬다(원본 twShowOver).
            float settle = 0f;
            while (settle < 2.5f) { settle += Time.unscaledDeltaTime; yield return null; }
            Canvas.ForceUpdateCanvases();

            var result = FindChild(league.transform, "Result");
            Assert.IsTrue(result.gameObject.activeSelf, "결과 창이 뜨지 않았습니다.");
            AssertNoBlankLabels(league.transform, "타워 결과");
            yield return ShotAt("41-tower-result", 1080, 1920);

            // 이겼다면 첫 클리어가 저장에 남아야 한다 — 보상은 한 번만 준다.
            if (sim.BattleWin == true)
            {
                Assert.IsTrue(svc.Profile.TowerCleared(top), "첫 클리어가 기록되지 않았습니다.");
                Assert.GreaterOrEqual(svc.Profile.TowerState.best, top,
                                      "가장 높이 깬 층이 올라가지 않았습니다.");
            }
        }

        /// <summary>
        /// 층 규칙이 판 위에 실제로 그려지는지 — 물바닥·아지랑이·막힌 칸·공기청정기·
        /// 황금 먼지·거울. 사진이 증거이고, 그려야 할 것이 몇 개인지는 여기서 센다.
        /// 층 번호는 적지 않는다 — 규칙으로 찾는다(표가 바뀌어도 따라간다).
        /// </summary>
        [UnityTest]
        public IEnumerator TowerRulesDressTheBoard()
        {
            yield return LoadScene();
            yield return null;
            var svc = GameServices.Instance;
            SeedAuditState(svc);
            svc.Profile.Data.best = 15;
            svc.Persist();

            var lobbyScreen = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);
            var league = Object.FindFirstObjectByType<LeagueScreen>(FindObjectsInactive.Include);
            league.gameObject.SetActive(true);

            // 판은 UI 가 아니라 월드에 있다(LeagueWorld).
            var view = Object.FindFirstObjectByType<LeagueView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view, "LeagueView 가 없습니다.");
            var field = typeof(LeagueScreen).GetField("_sim",
                BindingFlags.Instance | BindingFlags.NonPublic);

            int shot = 42;
            foreach (var rule in new[] { "water", "heat", "narrow", "guard", "golden", "mirror" })
            {
                var row = svc.Db.TowerFloors.FirstOrDefault(f => f.rule == rule);
                Assume.That(row, Is.Not.Null, $"'{rule}' 규칙을 쓰는 층이 표에 없습니다.");

                league.BeginTower(svc, row.floor);
                yield return null;
                var sim = (LeagueSim)field.GetValue(league);
                Assert.IsTrue(sim.IsTower);

                // 알림이 판을 가리지 않게 지나간 뒤에 찍는다.
                float waited = 0f;
                while (waited < 1.5f) { waited += Time.unscaledDeltaTime; yield return null; }
                Canvas.ForceUpdateCanvases();

                int decor = CountActive(view, "TowerDecor");
                switch (rule)
                {
                    case "water":
                        Assert.AreEqual(LeagueSim.WaterPools.Count, decor,
                                        "물바닥 웅덩이 수가 다릅니다.");
                        break;
                    case "heat":
                        Assert.AreEqual(LeagueSim.Cols, decor, "아지랑이 줄 수가 다릅니다.");
                        break;
                    case "narrow":
                    {
                        int blocked = 0;
                        for (int i = 0; i < LeagueSim.BoardCells; i++)
                            if (!sim.TowerCellOk(i)) blocked++;
                        Assert.Greater(blocked, 0, "좁은 복도인데 막힌 칸이 없습니다.");
                        Assert.AreEqual(blocked, decor, "막힌 칸 수가 다릅니다.");
                        break;
                    }
                    case "guard":
                        Assert.IsTrue(HasSprite(view, "tw_purifier"),
                                      "공기청정기가 판에 서 있지 않습니다.");
                        break;
                    case "golden":
                        Assert.AreEqual(3, CountActive(view, "GoldSpark"),
                                        "황금 먼지 반짝임이 셋이 아닙니다.");
                        break;
                    case "mirror":
                        // 거울은 우리가 놓은 가전을 베껴 세운다 — 수는 전투에서 센다.
                        Assert.Greater(sim.OnBoard, 0, "놓인 가전이 없어 거울이 비칠 것이 없습니다.");
                        break;
                }

                AssertNoBlankLabels(league.transform, $"타워 {rule} 층");
                yield return ShotAt($"{shot++}-tower-rule-{rule}", 1080, 1920);
            }

            // 거울과 황금 먼지는 전투에 들어가야 제 모습이 된다.
            foreach (var rule in new[] { "golden", "mirror" })
            {
                var row = svc.Db.TowerFloors.First(f => f.rule == rule);
                league.BeginTower(svc, row.floor);
                yield return null;
                var sim = (LeagueSim)field.GetValue(league);
                // 알림(1.3초)이 판을 가린다 — 지나간 뒤에 싸운다.
                float settle = 0f;
                while (settle < 1.5f) { settle += Time.unscaledDeltaTime; yield return null; }
                Assert.IsTrue(sim.StartBattle(), "전투가 시작되지 않았습니다.");
                for (int i = 0; i < 45; i++) sim.Step(1f / 30f);
                view.Sync(sim);
                league.Sync();
                Canvas.ForceUpdateCanvases();
                yield return null;

                if (rule == "golden")
                    Assert.IsNotNull(sim.Tower.Gold, "황금 먼지가 없습니다.");
                else
                    Assert.Greater(sim.Units.Count(u => u.Mirror), 0, "거울 가전이 없습니다.");

                yield return ShotAt($"{shot++}-tower-rule-{rule}-battle", 1080, 1920);
            }
        }

        /// <summary>판 위에 켜져 있는 연출 오브젝트 수.</summary>
        static int CountActive(LeagueView view, string prefix, bool withSprite = false)
        {
            int n = 0;
            foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!sr.gameObject.activeInHierarchy) continue;
                if (!sr.gameObject.name.StartsWith(prefix)) continue;
                if (withSprite && sr.sprite == null) continue;
                n++;
            }
            return n;
        }

        /// <summary>그 이름의 그림이 판 위에 켜져 있는가.</summary>
        static bool HasSprite(LeagueView view, string spriteName)
        {
            foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.activeInHierarchy && sr.sprite != null
                    && sr.sprite.name.StartsWith(spriteName))
                    return true;
            return false;
        }

        [UnityTest]
        public IEnumerator SurvivorRunsAndDrawsItsHudTeamAndLevelUp()
        {
            yield return LoadScene();
            yield return null;      // AppRoot.Start boots the services
            var svc = GameServices.Instance;
            Assert.IsNotNull(svc, "GameServices 가 생성되지 않았습니다.");
            var surv = Object.FindFirstObjectByType<SurvivorScreen>(FindObjectsInactive.Include);
            Assert.IsNotNull(surv, "SurvivorScreen 이 씬에 없습니다.");

            // 대청소 needs 1-1 cleared, and the leader list is the owned roster - both come
            // from the profile, so it is fixed here the same way the original was.
            SeedAuditState(svc);

            // The real flow hides the lobby on the way in; do the same here so the
            // screenshot shows the survivor screen rather than both stacked.
            var lobbyScreen = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobbyScreen != null) lobbyScreen.gameObject.SetActive(false);

            surv.gameObject.SetActive(true);
            surv.Begin(svc);
            yield return null;
            Canvas.ForceUpdateCanvases();

            // Leader pick first.
            var leaderCards = surv.GetComponentsInChildren<UnitCard>(false);
            Assert.Greater(leaderCards.Length, 0, "리더 후보가 하나도 없습니다.");
            AssertNoBlankLabels(surv.transform, "대청소 리더 선택");

            // 원본 `openSurvPick`: 리더를 눌러도 출발하지 않는다. 시트가 다시 그려지고
            // 출동 버튼이 시작한다. 전에는 탭이 곧바로 StartRun 이었다.
            var simField = typeof(SurvivorScreen).GetField("_sim",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var tapped = typeof(UnitCard).GetField("button",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ((UnityEngine.UI.Button)tapped.GetValue(leaderCards[leaderCards.Length - 1]))
                .onClick.Invoke();
            yield return null;
            Assert.IsNull(simField.GetValue(surv), "리더를 고르는 것만으로 대청소가 시작됐습니다.");

            // 고른 리더의 생존 스킬 이름/설명과 오늘 보상 줄이 채워져 있어야 한다.
            foreach (var (row, leaf) in new[] { ("Skill", "Name"), ("Skill", "Desc"),
                                                ("Rewards", "Text") })
            {
                var rowNode = FindChild(FindChild(surv.transform, "LeaderSelect"), row);
                Assert.IsNotNull(rowNode, $"리더 시트에 {row} 줄이 없습니다.");
                var node = rowNode.Find(leaf);
                Assert.IsNotNull(node, $"리더 시트에 {row}/{leaf} 이 없습니다.");
                var tmp = node.GetComponent<TMPro.TextMeshProUGUI>();
                Assert.IsFalse(string.IsNullOrWhiteSpace(tmp.text), $"{row}/{leaf} 이 비어 있습니다.");
            }
            yield return ShotAt("22-survivor-pick", 1080, 1920);

            var start = typeof(SurvivorScreen).GetMethod("StartRun",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(start, "SurvivorScreen.StartRun 을 찾지 못했습니다.");
            start.Invoke(surv, new object[] { svc.Profile.Data.deck[0] });
            yield return null;

            // Let it actually play for a few seconds so enemies spawn and XP accrues.
            float until = Time.unscaledTime + 6f;
            while (Time.unscaledTime < until) yield return null;
            Canvas.ForceUpdateCanvases();

            var sim = (SurvivorSim)typeof(SurvivorScreen)
                .GetField("_sim", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(surv);
            Assert.IsNotNull(sim, "대청소 시뮬레이션이 시작되지 않았습니다.");
            Assert.Greater(sim.Time, 1f, "대청소 시계가 흐르지 않았습니다.");

            AssertNoBlankLabels(surv.transform, "대청소 화면");
            // The team strip must show the leader plus a dashed slot for every empty seat.
            int slots = 1 + svc.Db.Config.survMaxComp;
            var team = FindChild(surv.transform, "Team");
            Assert.AreEqual(slots, team.childCount,
                $"팀 칸이 {slots}개여야 하는데 {team.childCount}개입니다.");
            yield return ShotAt("23-survivor-arena", 1080, 1920);

            // Force a level-up and check the sheet stacks its choices instead of running off
            // the right edge, which is what the old row layout did.
            var show = typeof(SurvivorScreen).GetMethod("ShowLevelUp",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null)
            {
                // The property's setter is private, so push the auto-property's backing
                // field: the point is to see the sheet, not to grind to a real level-up.
                var pend = typeof(SurvivorSim).GetField("<PendingLevelUps>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (pend != null) pend.SetValue(sim, 1);
                show.Invoke(surv, null);
                yield return null;
                Canvas.ForceUpdateCanvases();

                var options = surv.GetComponentsInChildren<SurvivorOptionCard>(false);
                if (options.Length > 0)
                {
                    AssertNoBlankLabels(surv.transform, "대청소 레벨 업");
                    float right = 0f;
                    foreach (var o in options)
                    {
                        var c = new Vector3[4];
                        ((RectTransform)o.transform).GetWorldCorners(c);
                        right = Mathf.Max(right,
                            RectTransformUtility.WorldToScreenPoint(null, c[2]).x);
                    }
                    Assert.LessOrEqual(right, Screen.width + 1f,
                        $"레벨 업 카드가 화면 밖으로 나갑니다. 오른쪽 끝 {right:0} > {Screen.width}");
                    yield return ShotAt("24-survivor-levelup", 1080, 1920);
                }
            }

            // ---- 결과 화면. 여기까지 와야 대청소가 끝까지 도는 것을 확인한 것이다.
            // 지금까지 이 화면에는 PlayMode 검사가 전혀 없었고, 디스크의 증거는
            // 며칠 묵은 것이었다.
            var pick = typeof(SurvivorScreen).GetField("optionRoot",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(surv) as GameObject;
            if (pick != null) pick.SetActive(false);
            typeof(SurvivorScreen).GetField("_cardsShown",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(surv, false);

            sim.Quit();                      // 중간에 나온 경우로 끝낸다
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            var svResult = Object.FindFirstObjectByType<SurvivorResultScreen>(
                FindObjectsInactive.Include);
            Assert.IsNotNull(svResult, "SurvivorResultScreen 이 씬에 없습니다.");
            Assert.IsTrue(svResult.gameObject.activeInHierarchy,
                "대청소가 끝났는데 결과 화면이 열리지 않았습니다.");
            AssertNoBlankLabels(svResult.transform, "대청소 결과");
            yield return ShotAt("25-survivor-result", 1080, 1920);

            surv.gameObject.SetActive(false);
        }

        /// <summary>
        /// 10.17-d: one boost must put TWO stars on the board, matching the panel and the
        /// boost button. The count is the whole fix, so it is read off the rendered label
        /// rather than off u.Tier - the simulation was never the part that was wrong.
        /// </summary>
        static IEnumerator BoostAndCheckTheStarBadge(BattleScreen battle, BattleSim sim,
                                                     BattleUnit unit, UnitActionBar bar)
        {
            // The board is not a child of the battle screen - it is its own root with its own
            // camera - so GetComponentInChildren finds nothing. Read the wired reference.
            var board = (BoardView)typeof(BattleScreen)
                .GetField("board", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(battle);
            Assert.IsNotNull(board, "BattleScreen.board 가 비어 있습니다.");
            var badge = BadgeOf(board, unit);
            Assert.IsNotNull(badge, "가전에 강화 배지 라벨이 없습니다.");
            Assert.IsFalse(badge.enabled, "배치 직후에는 배지가 없어야 합니다 (단계 1).");

            // Boosting needs power; wait for it rather than forcing the field.
            for (int i = 0; i < 60 * 40 && !sim.CanBoost(unit) && !sim.Finished; i++) yield return null;
            if (!sim.CanBoost(unit)) Assert.Ignore("강화할 전력이 모이지 않았습니다.");

            Assert.IsTrue(sim.Boost(unit), "강화가 실패했습니다.");
            yield return null;
            board.Sync(Time.unscaledTime);
            bar.Refresh();
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.IsTrue(badge.enabled, "강화했는데 배지가 보이지 않습니다.");
            Assert.AreEqual(new string('★', unit.Tier), badge.text,
                "필드 배지의 별 개수가 단계와 다릅니다 (10.17-d).");
            Assert.AreEqual(2, badge.text.Length, "강화 1회면 ★★ 여야 합니다.");

            var infoName = bar.transform.Find("Info/Name").GetComponent<TMPro.TextMeshProUGUI>();
            Assert.IsTrue(infoName.text.EndsWith("★★"),
                $"패널 이름이 배지와 다릅니다: '{infoName.text}'");

            yield return Shot("03b-battle-boosted-badge");
        }

        static TMPro.TextMeshPro BadgeOf(BoardView board, BattleUnit unit)
        {
            foreach (var v in board.GetComponentsInChildren<EntityView>(true))
            {
                if (v.TierBadge == null) continue;
                // The unit's view is the one standing on its tile.
                var p = board.ToWorld(unit.X, unit.Y);
                if ((v.transform.localPosition - p).sqrMagnitude < 0.01f) return v.TierBadge;
            }
            return null;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Assert.Fail($"'{name}' 을 찾지 못했습니다.");
            return null;
        }

        [UnityTest]
        public IEnumerator TutorialShowsOnFirstBattleAndHoldsTheSimulation()
        {
            yield return LoadScene();
            yield return null;

            var svc = GameServices.Instance;
            Assert.IsTrue(svc != null && svc.Ready, "서비스가 준비되지 않았습니다.");

            // A fresh player: nothing seen, tutorials on.
            svc.Profile.Data.tut.Clear();
            svc.Profile.Data.tutOff = 0;
            svc.Profile.Data.tutOpen = 0;

            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            var battle = Object.FindFirstObjectByType<BattleScreen>(FindObjectsInactive.Include);
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            battle.Begin(svc, svc.Db.Stages[0]);
            yield return null;

            var sim = battle.Sim;
            battle.transform.Find("Brief/BriefActions/Start").GetComponent<UiButton>()
                .OnPointerClick(new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current));
            yield return null;

            // The first step waits until the melee card is actually affordable, so give the
            // battle a few seconds of power regen before expecting it.
            var bubble = battle.transform.Find("Tutorial");
            Assert.IsNotNull(bubble, "튜토리얼 말풍선 오브젝트가 없습니다.");

            var text = bubble.Find("TutText").GetComponent<TMPro.TextMeshProUGUI>();
            bool appeared = false;
            for (int frame = 0; frame < 900 && !appeared; frame++)
            {
                if (bubble.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                    appeared = true;
                else yield return null;
            }

            Assert.IsTrue(appeared,
                $"첫 전투에서 튜토리얼이 뜨지 않았습니다. (전력 {sim.Power:0.0}, 경과 {sim.Time:0.0}초)");
            Debug.Log($"[tutorial] 표시됨 @ {sim.Time:0.0}초 · 문구: {text.text}");

            // pause=1 steps must stop the simulation until acknowledged.
            float timeAtShow = sim.Time;
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(timeAtShow, sim.Time, 0.0001f,
                "pause 튜토리얼이 떠 있는 동안에도 전투가 진행됐습니다.");
            Debug.Log("[tutorial] 시뮬레이션 정지 확인");

            yield return Shot("06-tutorial");

            // 10.15-h's opening step closes on a Deploy onto a pinned tile, not on a tap,
            // so finishing it means actually placing the unit where it points.
            var active = GetTutorialState(battle);
            Assert.IsNotNull(active, "활성 튜토리얼 단계를 읽지 못했습니다.");
            Assert.IsNotNull(active.Target, "튜토리얼이 대상 가전을 정하지 못했습니다.");
            Assert.IsTrue(active.ForcedTile.HasValue,
                "1단계는 지정된 칸에 배치하도록 되어 있어야 합니다 (tutorial.tile).");

            var tile = active.ForcedTile.Value;
            Debug.Log($"[tutorial] 대상 {active.Target}, 지정 칸 ({tile.X},{tile.Y})");

            Assert.IsTrue(sim.CanPlace(active.Target, tile.X, tile.Y),
                $"지정 칸 ({tile.X},{tile.Y}) 에 {active.Target} 를 놓을 수 없습니다.");
            sim.Deploy(active.Target, tile.X, tile.Y);

            for (int i = 0; i < 120; i++) yield return null;

            // 시간이 늘어나는지로 보면 안 된다 — 1단계가 끝나면 2단계(20102)가 **곧바로**
            // 떠서 다시 시뮬을 멈춘다(pause=1). 원본도 같다. 그래서 '단계가 넘어갔는가'로
            // 본다: 지정 칸에 놓았으니 그 단계는 끝나 있어야 한다.
            int before = active.Step.id;
            var now = GetTutorialState(battle);
            Assert.IsTrue(now == null || now.Step.id != before,
                $"지정 칸에 놓았는데도 튜토리얼 {before} 단계가 그대로입니다.");
            Debug.Log(now == null
                ? "[tutorial] 단계 종료 확인 (더 뜰 단계 없음)"
                : $"[tutorial] 단계 종료 확인 → 다음 단계 {now.Step.id}");
        }

        /// <summary>Reads the battle screen's live tutorial step through its director.</summary>
        static DustZero.Core.Defense.ActiveTutorial GetTutorialState(BattleScreen battle)
        {
            var f = typeof(BattleScreen).GetField("_tutorial",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dir = f?.GetValue(battle) as DustZero.Core.Defense.TutorialDirector;
            return dir?.Active;
        }

        /// <summary>Deploys any affordable deck unit that is not already on the board.</summary>
        static int DeployWhatWeCan(BattleSim sim)
        {
            int n = 0;
            foreach (var type in sim.Deck.ToList())
            {
                if (!sim.Affordable(type)) continue;
                if (sim.Units.Any(u => u.Type == type)) continue;
                var spot = FindSpot(sim, type);
                if (spot == null) continue;
                sim.Deploy(type, spot.Value.c, spot.Value.r);
                n++;
            }
            return n;
        }

        /// <summary>First legal tile for this unit, preferring one the enemy route crosses.</summary>
        static (int c, int r)? FindSpot(BattleSim sim, string type)
        {
            (int c, int r)? fallback = null;
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                {
                    if (!sim.CanPlace(type, c, r)) continue;
                    fallback ??= (c, r);
                    for (int pi = 0; pi < sim.Stage.Paths.Length; pi++)
                        if (sim.Field.TileDistance(c, r, pi) != null) return (c, r);
                }
            return fallback;
        }

        /// <summary>
        /// The slots the viewer is supposed to fill with art, per tab. An Image left enabled
        /// with no sprite draws a white rectangle - it throws nothing and a label sweep cannot
        /// see it, so wiring the wrong field would otherwise pass review.
        ///
        /// Only meaningful for an appliance that has art; the viewer disables these slots and
        /// shows its own "준비 중" copy when it does not, which is checked separately.
        /// </summary>
        static void AssertArtSlotsAreFilled(ArtViewerScreen art, ArtTab tab)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var missing = new List<string>();

            void One(string field)
            {
                var f = typeof(ArtViewerScreen).GetField(field, flags);
                Assert.IsNotNull(f, $"ArtViewerScreen.{field} 가 없습니다.");
                var img = (UnityEngine.UI.Image)f.GetValue(art);
                if (img == null) { missing.Add($"{field}=필드 미연결"); return; }
                if (!img.enabled) { missing.Add($"{field}=꺼짐"); return; }
                if (img.sprite == null) missing.Add($"{field}=스프라이트 없음");
            }
            void Many(string field)
            {
                var f = typeof(ArtViewerScreen).GetField(field, flags);
                Assert.IsNotNull(f, $"ArtViewerScreen.{field} 가 없습니다.");
                var arr = (UnityEngine.UI.Image[])f.GetValue(art);
                Assert.IsNotNull(arr, $"ArtViewerScreen.{field} 가 비어 있습니다.");
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) { missing.Add($"{field}[{i}]=필드 미연결"); continue; }
                    if (!arr[i].enabled) { missing.Add($"{field}[{i}]=꺼짐"); continue; }
                    if (arr[i].sprite == null) missing.Add($"{field}[{i}]=스프라이트 없음");
                }
            }

            One("illustration");
            if (tab == ArtTab.Face) Many("faceImages");
            if (tab == ArtTab.Sheet) { One("turnImage"); One("keysImage"); Many("sdImages"); }

            Assert.IsEmpty(missing,
                $"일러스트 {tab}: 그림이 비어 있는 칸 — {string.Join(" | ", missing)}. " +
                $"unit_art 의 파일명과 Art/Illust 의 실제 파일을 확인하세요.");
        }

        /// <summary>
        /// A panel whose labels are all empty never bound to the profile. The blank-label
        /// sweep cannot see this - it skips empty text by design, since plenty of labels are
        /// legitimately blank until something happens - so a tab that silently failed to
        /// Bind looked perfectly healthy. This is the gate for that.
        /// </summary>
        static void AssertPanelHasContent(Transform root, string where, int atLeast)
        {
            int filled = 0;
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false))
                if (!string.IsNullOrWhiteSpace(tmp.text)) filled++;
            Assert.GreaterOrEqual(filled, atLeast,
                $"{where}: 글자가 들어 있는 라벨이 {filled}개뿐입니다. " +
                $"화면이 데이터에 연결되지 않았을 때 나타나는 증상입니다.");
        }

        /// <summary>
        /// 10.17-c: 나가기 during a battle opens the pause menu rather than arming a second
        /// tap. Checked here and not only in EditMode because the thing that broke before was
        /// never the logic - it was a panel that existed but drew nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator PauseMenuOpensFromExitAndDimsTheTray()
        {
            yield return LoadScene();
            yield return null;

            var svc = GameServices.Instance;
            Assert.IsTrue(svc != null && svc.Ready, "서비스가 준비되지 않았습니다.");
            // Tutorials would steal the first battle and hold the simulation.
            svc.Profile.Data.tutOff = 1;

            var lobby = Object.FindFirstObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            var battle = Object.FindFirstObjectByType<BattleScreen>(FindObjectsInactive.Include);
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            battle.Begin(svc, svc.Db.Stages[0]);
            yield return null;

            Click(battle.transform.Find("Brief/BriefActions/Start"));
            yield return null;

            var sim = battle.Sim;
            Assert.IsNotNull(sim, "전투 시뮬레이션이 없습니다.");
            // Read the clock BEFORE running, or "the battle advanced" compares a value with
            // itself and passes on a battle that never moved.
            float tBefore = sim.Time;
            for (int i = 0; i < 90; i++) yield return null;      // let the battle actually run
            Assert.Greater(sim.Time, tBefore, "전투가 애초에 진행되지 않았습니다.");

            var overlay = FindChild(battle.transform, "PauseOverlay");
            Assert.IsFalse(overlay.gameObject.activeInHierarchy,
                           "전투를 시작하자마자 일시정지 창이 떠 있으면 안 됩니다.");

            // Both the HUD control and the menu use the name "Exit"; take the HUD one.
            Click(FindChild(battle.transform, "Ctrls").Find("Exit"));
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsTrue(overlay.gameObject.activeInHierarchy,
                          "전투 중 나가기를 눌렀는데 일시정지 창이 열리지 않았습니다.");

            // The three ways out, with the build's own wording.
            var db = svc.Db;
            AssertLabel(overlay, "Card/Title", db.Text("battle.pause.title"));
            AssertLabel(overlay, "Card/Desc", db.Text("battle.pause.desc"));
            AssertLabel(overlay, "Card/Resume/Content/Label", db.Text("battle.pause.resume"));
            AssertLabel(overlay, "Card/Retry/Content/Label", db.Text("battle.pause.retry"));
            AssertLabel(overlay, "Card/Exit/Content/Label", db.Text("battle.pause.exit"));
            AssertNoBlankLabels(overlay, "일시정지 창");

            // The tray is greyed and untappable while the menu is up (.battle.pmenu).
            var tray = battle.transform.Find("DeckBar").GetComponent<CanvasGroup>();
            Assert.IsNotNull(tray, "트레이에 CanvasGroup 이 없습니다.");
            Assert.Less(tray.alpha, 0.99f, "일시정지 창이 떴는데 트레이가 그대로입니다.");
            Assert.IsFalse(tray.interactable, "일시정지 중에도 트레이를 누를 수 있습니다.");

            // Paused means the simulation stops. The presentation may keep animating.
            float tAtOpen = sim.Time;
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(tAtOpen, sim.Time, 0.0001f,
                            "일시정지 창이 떠 있는 동안에도 전투가 진행됐습니다.");

            yield return Shot("05b-battle-pause-menu");

            Click(overlay.Find("Card/Resume"));
            yield return null;
            Assert.IsFalse(overlay.gameObject.activeInHierarchy,
                           "계속하기를 눌렀는데 창이 닫히지 않았습니다.");
            Assert.AreEqual(1f, tray.alpha, 0.001f, "창을 닫았는데 트레이가 흐린 채입니다.");

            for (int i = 0; i < 60; i++) yield return null;
            Assert.Greater(sim.Time, tAtOpen, "계속하기 뒤에도 전투가 재개되지 않았습니다.");

            battle.gameObject.SetActive(false);
        }

        static void Click(Transform t)
        {
            var btn = t.GetComponent<UiButton>();
            Assert.IsNotNull(btn, $"'{t.name}' 에 UiButton 이 없습니다.");
            btn.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
        }

        static void AssertLabel(Transform root, string path, string expected)
        {
            var node = root.Find(path);
            Assert.IsNotNull(node, $"일시정지 창에서 '{path}' 를 찾지 못했습니다.");
            var tmp = node.GetComponent<TMPro.TextMeshProUGUI>();
            Assert.IsNotNull(tmp, $"'{path}' 에 라벨이 없습니다.");
            Assert.AreEqual(expected, tmp.text, $"'{path}' 문구가 string_ko 와 다릅니다.");
        }

        static string NodePath(Transform t)
        {
            var name = t.name;
            while (t.parent != null && t.parent.parent != null) { t = t.parent; name = t.name + "/" + name; }
            return name;
        }
    }
}
