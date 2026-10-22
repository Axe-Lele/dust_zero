// Drives one defense battle: input, HUD, overlays and the simulation clock.
//
// Speed and pause are handled here, not with Time.timeScale, because only the simulation
// should change rate. At 2x the sim is ticked twice with the same dt the original used, so
// per-tick rules (cooldowns, heat, skill charge) behave identically at either speed.
using System.Collections.Generic;
using DustZero.Core;
using DustZero.Core.Coop;
using DustZero.Core.Defense;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class BattleScreen : MonoBehaviour
    {
        [Header("보드")]
        [SerializeField] BoardView board;
        /// <summary>Exposed so tests can ask where the board actually landed.</summary>
        public Camera BoardCamera => boardCamera;
        [SerializeField] FxDirector fx;
        [SerializeField] Camera boardCamera;

        [Header("결과")]
        [SerializeField] ResultScreen result;

        [Header("브리핑")]
        [SerializeField] GameObject briefRoot;
        /// <summary>Top and bottom bands while the brief is up; the board gets what is left.</summary>
        [SerializeField] RectTransform briefPanelRect;
        [SerializeField] RectTransform briefActionsRect;
        [SerializeField] TextMeshProUGUI briefTitleLabel;
        [SerializeField] TextMeshProUGUI briefSubLabel;
        [SerializeField] TextMeshProUGUI briefDescLabel;
        /// <summary>One chip per star the stage can award, filled from config.starLifeMin.</summary>
        [SerializeField] TextMeshProUGUI[] briefStarLabels;
        [SerializeField] TextMeshProUGUI briefEnemyLabel;
        [SerializeField] UiButton briefStartButton;
        [SerializeField] UiButton briefDeckButton;

        [Header("일시정지")]
        [SerializeField] GameObject pauseOverlay;
        [SerializeField] TextMeshProUGUI pauseTitleLabel;
        [SerializeField] TextMeshProUGUI pauseDescLabel;
        [SerializeField] UiButton pauseResumeButton;
        /// <summary>10.17-c: replay this stage from the start, losing the current run.</summary>
        [SerializeField] UiButton pauseRetryButton;
        /// <summary>The overlay's own way out. The HUD's 나가기 is the primary route.</summary>
        [SerializeField] UiButton pauseExitButton;
        /// <summary>Dimmed and made untappable while the pause menu is up (.battle.pmenu).</summary>
        [SerializeField] CanvasGroup trayGroup;

        [Header("레이아웃")]
        /// <summary>Top band; the board viewport starts below it.</summary>
        [SerializeField] RectTransform hudRect;
        /// <summary>Bottom tray; the board viewport ends above it.</summary>
        [SerializeField] RectTransform trayRect;

        [Header("HUD 상단")]
        /// <summary>"1-3" in the LED face - the stage number, muted, ahead of the name.</summary>
        [SerializeField] TextMeshProUGUI stageNoLabel;
        /// <summary>"거실" - the room name, in the display face.</summary>
        [SerializeField] TextMeshProUGUI stageNameLabel;
        [SerializeField] TextMeshProUGUI remainLabel;
        [SerializeField] UiButton pauseButton;
        [SerializeField] TextMeshProUGUI pauseButtonLabel;
        [SerializeField] UiButton speedButton;
        [SerializeField] TextMeshProUGUI speedButtonLabel;
        [SerializeField] UiButton exitButton;

        [Header("HUD 청결도")]
        /// <summary>Cleanliness is a life count in 10.16-n, not a percentage bar.</summary>
        [SerializeField] TextMeshProUGUI cleanLabel;
        [SerializeField] TextMeshProUGUI cleanCaptionLabel;
        [SerializeField] TextMeshProUGUI remainCaptionLabel;

        [Header("협동 청소")]
        [SerializeField] CoopBars coopBars;

        /// <summary>카드 트레이 전체. 협동에서는 원본 `.battle.rd .deck` 처럼 감춘다.</summary>
        [SerializeField] RectTransform trayRoot;
        /// <summary>배치 칸 수와 멀티탭 줄. 원본 `.battle.rd .slots` 도 감춘다.</summary>
        [SerializeField] RectTransform slotsRow;
        /// <summary>협동 캡션이 길어 두 읽기칸의 폭을 늘려 줘야 한다.</summary>
        [SerializeField] LayoutElement lifeReadout;
        [SerializeField] LayoutElement remainReadout;

        [Header("HUD 전력")]
        [SerializeField] TextMeshProUGUI powerValueLabel;
        /// <summary>"전력 · 초당 +100W" - the regen rate doubles as the read-out's label.</summary>
        [SerializeField] TextMeshProUGUI regenLabel;
        /// <summary>
        /// Fills with the fraction of the next whole power unit, not with the total. The
        /// original reads (power % 1), so the bar is a tick-to-tick progress indicator.
        /// </summary>
        [SerializeField] RectTransform powerFill;
        [SerializeField] TextMeshProUGUI hintLabel;

        [Header("배치 슬롯")]
        [SerializeField] TextMeshProUGUI slotCountLabel;
        [SerializeField] UiButton multitapButton;
        [SerializeField] TextMeshProUGUI multitapLabel;

        [Header("덱")]
        [SerializeField] RectTransform deckRoot;
        [SerializeField] DeckCard deckCardPrefab;
        /// <summary>Replaces the card row while a deployed unit is selected (redesign 1c).</summary>
        [SerializeField] UnitActionBar unitActionBar;

        [Header("오버레이")]
        [SerializeField] GameObject overlayRoot;
        [SerializeField] TextMeshProUGUI overlayTitle;
        [SerializeField] TextMeshProUGUI overlayBody;
        /// <summary>오버레이 카드. 원본처럼 **내용 높이**로 맞추려고 들고 있다.</summary>
        [SerializeField] RectTransform overlayCard;
        [SerializeField] Button overlayPrimary;
        [SerializeField] TextMeshProUGUI overlayPrimaryLabel;
        [SerializeField] Button overlaySecondary;
        [SerializeField] TextMeshProUGUI overlaySecondaryLabel;

        [Header("튜토리얼")]
        [SerializeField] GameObject tutorialRoot;
        [SerializeField] TextMeshProUGUI tutorialText;
        [SerializeField] Button tutorialOk;
        [SerializeField] Button tutorialOff;

        BattleSim _sim;
        BattleUnit _selectedUnit;
        bool _barHooked;
        TutorialDirector _tutorial;
        PresentationBus _bus;
        GameServices _svc;
        StageDef _stage;

        readonly List<DeckCard> _cards = new();
        string _selectedCard;
        int _speed = 1;
        bool _paused;
        bool _running;
        bool _settled;
        float _presentationTime;
        bool _pauseMenu;

        /// <summary>Raised when the player leaves, so the host can return to the lobby.</summary>
        public event System.Action Exited;

        /// <summary>브리핑의 [편성 변경] — 원본 `goLobby('deck')` 과 같다.</summary>
        public event System.Action DeckRequested;

        /// <summary>Simulation step. Fixed so battles play the same regardless of frame rate.</summary>
        const float SimStep = 1f / 60f;
        float _accumulator;

        public BattleSim Sim => _sim;

        /// <summary>The coop run, or null when this is an ordinary stage.</summary>
        public CoopSim Coop => _coop;

        /// <summary>협동 판인가. 원본의 `$('battle').classList.toggle('rd')` 에 해당한다.</summary>
        public bool IsCoop => _coop != null;

        /// <summary>The lobby guide's "battle.start"/"result.lobby" spotlight targets.</summary>
        public RectTransform ResolveGuideTarget(string key)
        {
            if (key == "battle.start")
                return briefStartButton != null && briefRoot != null && briefRoot.activeInHierarchy
                    ? (RectTransform)briefStartButton.transform : null;
            if (key == "result.lobby")
                return result != null && result.gameObject.activeInHierarchy
                    ? result.ResolveGuideTarget(key) : null;
            return null;
        }

        void Awake()
        {
            if (speedButton != null) speedButton.SetAction(ToggleSpeed);
            if (pauseButton != null) pauseButton.SetAction(TogglePause);
            if (exitButton != null) exitButton.SetAction(OnExitPressed);
            if (pauseResumeButton != null) pauseResumeButton.SetAction(ClosePauseMenu);
            if (pauseRetryButton != null) pauseRetryButton.SetAction(RetryStage);
            if (pauseExitButton != null) pauseExitButton.SetAction(LeaveFromPauseMenu);
            if (briefStartButton != null) briefStartButton.SetAction(StartBattle);
            if (result != null)
            {
                result.HomeRequested += () =>
                {
                    _svc?.Guide?.ReportReached("result.lobby");
                    result.gameObject.SetActive(false);
                    LeaveToLobby();
                };
                result.RetryRequested += () => Restart(_stage);
                result.NextRequested += stage => Restart(stage ?? _stage);
            }
            // 원본은 여기서 로비의 편성 탭으로 보낸다(`goLobby('deck')`). 전에는 안내만
            // 띄웠는데, 브리핑에는 그 안내 줄이 보이지 않아 **버튼이 죽은 것처럼** 보였다.
            if (briefDeckButton != null)
                briefDeckButton.SetAction(() => DeckRequested?.Invoke());
            if (tutorialOk != null) tutorialOk.onClick.AddListener(() => _tutorial?.Complete());
            if (tutorialOff != null) tutorialOff.onClick.AddListener(() => _tutorial?.Complete(true));
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// 협동 청소 한 판. 원본은 같은 전투 화면에 `.rd` 를 붙이고 coBar·rdBar 를 켠다 —
        /// 우리도 같은 화면을 쓰고, 스테이지 전용 표시(청결도·남은 적·브리핑)만 바꿔 단다.
        /// </summary>
        public void BeginCoop(GameServices services, CoopPartnerRow partner)
        {
            _svc = services;
            _bus = new PresentationBus();
            _settled = false;
            _presentationTime = 0f;
            _accumulator = 0f;
            ShowBoard();

            var (level, star) = services.Profile.CoopPartnerStrength();
            _coop = new CoopSim(services.Db, partner, services.Profile.Data.deck,
                                services.Profile, services.Rng, _bus,
                                services.Profile.Data.best, level, star);
            _coop.PartnerSpoke += OnPartnerSpoke;
            _sim = _coop.Battle;
            _stage = _sim.Stage;

            // 협동에는 튜토리얼이 없다. 디렉터는 두되 걸릴 단계가 없는 상태로 만든다.
            _tutorial = new TutorialDirector(services.Db, services.Profile.Data, _sim);
            _tutorial.SaveDirty += services.Persist;

            HookActionBar();

            _speed = services.Profile.Data.speed == 2 ? 2 : 1;
            _paused = false;
            _pauseMenu = false;
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            SetMenuDim(false);
            _selectedCard = null;

            board.Build(_sim);
            fx.ClearAll();
            BuildDeck();
            FrameCamera();
            RequestReframe();

            if (stageNoLabel != null) stageNoLabel.text = "협동";
            if (stageNameLabel != null) stageNameLabel.text = partner != null ? CoopName(partner) : "협동 청소";
            SyncSpeedLabel();
            if (pauseButtonLabel != null) pauseButtonLabel.text = "일시정지";
            if (tutorialRoot != null) tutorialRoot.SetActive(false);

            SetCoopChrome(true);
            SetHint("빈칸을 누르고 [가전 소환] → 그 칸에 무작위 가전(고리 칸 = 근접, 가구 칸 = 원거리). " +
                    "같은 ★ 두 대를 누르면 합쳐져요.");

            // 브리핑이 없다. 원본도 설명 오버레이 하나를 띄우고 바로 시작한다.
            ShowCoopIntro(partner);
        }

        string CoopName(CoopPartnerRow p) => _svc.Db.Text(p.nameKey);

        public void Begin(GameServices services, StageDef stage)
        {
            _coop = null;
            SetCoopChrome(false);
            _svc = services;
            _stage = stage;
            _bus = new PresentationBus();
            _settled = false;
            _presentationTime = 0f;
            _accumulator = 0f;
            ShowBoard();

            _sim = new BattleSim(services.Db, stage, services.Profile.Data.deck,
                                 services.Profile, services.Rng, _bus);

            HookActionBar();

            _tutorial = new TutorialDirector(services.Db, services.Profile.Data, _sim);
            _tutorial.SaveDirty += services.Persist;

            _speed = services.Profile.Data.speed == 2 ? 2 : 1;
            _paused = false;
            _pauseMenu = false;
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            SetMenuDim(false);
            _running = false;
            _selectedCard = null;

            board.Build(_sim);
            fx.ClearAll();
            BuildDeck();
            FrameCamera();
            RequestReframe();

            if (stageNoLabel != null) stageNoLabel.text = stage.No;
            if (stageNameLabel != null) stageNameLabel.text = stage.Name;
            SetHint(null);
            SyncSpeedLabel();
            if (pauseButtonLabel != null) pauseButtonLabel.text = "일시정지";
            if (tutorialRoot != null) tutorialRoot.SetActive(false);

            ShowBrief();
        }

        /// <summary>
        /// 마지막으로 판을 잡았을 때의 **화면 크기와 자리**. 전에는 띠의 가로세로 비를
        /// 비교했는데, 자리를 원본 고정 상자(398x560)로 바꾼 뒤로 그 비가 **상수**가 되어
        /// 창 크기가 바뀌어도 다시 잡지 않았다 — 창을 줄이면 판이 그대로 커져 넘쳤다.
        /// </summary>
        Vector3 _framedAt = new(-1f, -1f, -1f);

        /// <summary>
        /// Fits the board into the strip the HUD and tray leave behind. The camera keeps the
        /// full viewport - carving it with Camera.rect fights anything that renders the scene
        /// at another resolution - so the strip is honoured by choosing the orthographic size
        /// and sliding the camera instead. The guide states the sizing rule directly: a tile
        /// is min(width / cols, remaining height / rows).
        /// </summary>
        /// <summary>
        /// Frames the board again on the next few frames. Showing or hiding the HUD and tray
        /// does not resize their rects until layout runs, so the framing done in the same
        /// frame reads stale bands and drops the board into the card tray.
        /// </summary>
        void RequestReframe() => _reframeFrames = 3;

        int _reframeFrames;

        /// <summary>

        /// 이 화면이 보이는 동안 자기 카메라를 위로 올린다.

        ///

        /// 전투판과 대청소판은 각자 카메라를 들고 **둘 다 켜져 있다.** 둘 다 깊이 0 에

        /// 배경까지 칠하므로 누가 나중에 그려질지 정해지지 않는다 — 실제로 전투

        /// 카메라가 대청소를 덮어 빈 화면이 됐다. 카메라를 끄면 Camera.main 이

        /// null 이 되어 캡처가 통째로 비므로, 깊이만 바꾼다.

        /// </summary>

        void OnEnable()
        {
            if (boardCamera != null) boardCamera.depth = 10f;
        }

        void OnDisable()
        {
            if (boardCamera != null) boardCamera.depth = 0f;
        }


        void LateUpdate()
        {
            if (_reframeFrames > 0) { _reframeFrames--; FrameCamera(); return; }
            // Even when nothing asked for a reframe, keep the board inside its strip. The
            // bands resize a frame or more after they are shown, and every attempt to
            // predict exactly when left the board sitting under the card tray.
            CentreBoardInStrip();
        }

        /// <summary>
        /// Slides the camera until the board's measured centre sits on the free strip's
        /// centre. Measured, not derived: deriving it means modelling the board origin, the
        /// tile pivot and the camera transform together, and one wrong term puts the board
        /// through the HUD with every label still rendering and every other test passing.
        /// </summary>
        /// <summary>
        /// 원본 `#stage` 는 16,135 에 놓인 **398x560 고정 상자**다. 전에는 HUD 띠와 카드
        /// 트레이 띠 사이의 남는 자리를 전부 판에 줬는데, 그 자리가 원본 상자보다 커서
        /// 판이 아래로 82px 더 내려가 트레이를 24px 파고들었다. 남는 자리가 아니라
        /// 원본 상자를 쓴다 — 판 아래의 배치 카운터와 멀티탭 버튼 자리가 거기서 나온다.
        /// </summary>
        /// 치수는 **화면 픽셀**로 잰다. `Px`/`Py` 는 캔버스 단위라 창 비율이 1080x1920 이
        /// 아니면 화면 픽셀과 달라진다 — 그대로 쓰면 판이 기둥 밖으로 넘친다.
        static float BoardBoxTopPx => 135f * UiTheme.ScreenPerPage;
        static float BoardBoxHeightPx => 560f * UiTheme.ScreenPerPage;
        static float BoardBoxWidthPx => 398f * UiTheme.ScreenPerPage;

        /// <summary>판이 받을 자리 — 화면 아래에서 잰 바닥과 높이.</summary>
        void BoardStrip(out float bottomPx, out float freePx, out float widthPx)
        {
            float screenH = Mathf.Max(1f, Screen.height);
            float screenW = Mathf.Max(1f, Screen.width);

            // 브리핑은 패널과 버튼 줄이 자리를 달리 먹으므로 전처럼 띠를 재서 쓴다.
            if (briefRoot != null && briefRoot.activeInHierarchy)
            {
                float topPx = BandPixels(briefPanelRect, true);
                bottomPx = BandPixels(briefActionsRect, false);
                freePx = Mathf.Clamp(screenH - topPx - bottomPx, screenH * 0.2f, screenH);
                widthPx = screenW;
                return;
            }

            // 페이지가 화면 가운데에 놓이므로 위 여백을 더해야 한다. 빼먹으면 판이
            // 그만큼 위로 올라가 HUD 뒤로 숨는다.
            freePx = Mathf.Min(BoardBoxHeightPx, screenH);
            bottomPx = Mathf.Max(0f, screenH - UiTheme.PageScreenTop - BoardBoxTopPx - freePx);
            widthPx = Mathf.Min(BoardBoxWidthPx, screenW);
        }

        void CentreBoardInStrip()
        {
            if (boardCamera == null || board == null || _stage == null) return;
            BoardStrip(out float bottomPx, out float freePx, out _);

            var bounds = board.ScreenBounds(boardCamera);
            if (bounds.height <= 0f) return;

            // 가로도 같이 맞춘다. 칠해진 바닥은 월드 원점이 아니라 **반 칸 오른쪽**에
            // 가운데가 오므로(칸 (0,0) 의 모서리가 Origin 이다), 카메라를 0 에 두면
            // 판이 화면 가운데에서 반 칸 밀린다.
            float errX = bounds.center.x - (UiTheme.ColumnScreenMargin + UiTheme.ColumnScreenWidth * 0.5f);
            float errY = bounds.center.y - (bottomPx + freePx * 0.5f);
            if (Mathf.Abs(errX) < 0.5f && Mathf.Abs(errY) < 0.5f) return;

            // 카메라가 기둥만 쓰므로 세로로 차지하는 화면 픽셀은 `Screen.height * rect.height` 다.
            float camPx = Mathf.Max(1f, Screen.height * boardCamera.rect.height);
            float pxPerUnit = camPx / (2f * boardCamera.orthographicSize);
            var p = boardCamera.transform.position;
            // Moving the camera up moves the board down the screen.
            boardCamera.transform.position =
                new Vector3(p.x + errX / pxPerUnit, p.y + errY / pxPerUnit, p.z);
        }

        void FrameCamera()
        {
            if (boardCamera == null || _stage == null) return;
            // 판은 기둥 안에서만 그린다. 화면 전체를 주면 좌우 여백에도 판이 비친다.
            boardCamera.rect = UiTheme.PageViewport(0f, 0f, UiTheme.PageW, UiTheme.PageH);
            // The bands are measured, so they have to be laid out first. Framing straight
            // after StartStage read last frame's rects and put the board under the tray.
            Canvas.ForceUpdateCanvases();

            float camPx = Mathf.Max(1f, Screen.height * boardCamera.rect.height);
            BoardStrip(out _, out float freePx, out float widthPx);

            // 원본 `resize()` 는 여유를 두지 않는다 — `TS = min(폭/칸수, 남는높이/줄수)` 로
            // 상자를 꽉 채운다(1-1 은 56x56 이 392x560 을 정확히 메운다). 두 축 배율을
            // 통일한 뒤로는 우리도 같은 값이 나오므로 여유를 없앤다.
            const float Margin = 1f;
            float rows = _stage.Rows * BoardView.TileSize;
            float cols = _stage.Cols * BoardView.TileSize;
            float pxPerUnit = Mathf.Min(freePx / (rows * Margin), widthPx / (cols * Margin));
            boardCamera.orthographicSize = camPx / (2f * pxPerUnit);

            // Then put the board's middle on the strip's middle by MEASURING where it
            // actually landed and correcting, rather than by deriving it. Deriving meant
            // modelling the board's origin, the tile pivot and the camera transform all at
            // once, and being one of them wrong put the board through the HUD. The mapping
            // is linear, so one correction pass is exact.
            CentreBoardInStrip();
            BoardStrip(out float framedBottom, out float framedFree, out _);
            _framedAt = new Vector3(Screen.width, Screen.height, framedBottom + framedFree);
        }

        /// <summary>
        /// How much of the screen a UI band actually eats, measured from the edge it hangs
        /// off. Not the band's own height: 10.16-n's HUD floats 12px below the top, and
        /// measuring only the plate left the board drifting down into the card tray.
        /// </summary>
        static float BandPixels(RectTransform band, bool fromTop)
        {
            if (band == null || !band.gameObject.activeInHierarchy) return 0f;
            var corners = new Vector3[4];
            band.GetWorldCorners(corners);          // 0 BL, 1 TL, 2 TR, 3 BR
            var canvas = band.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
            float bottom = RectTransformUtility.WorldToScreenPoint(cam, corners[0]).y;
            float top = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y;
            return fromTop ? Mathf.Max(0f, Screen.height - Mathf.Min(bottom, top))
                           : Mathf.Max(0f, Mathf.Max(bottom, top));
        }

        /// <summary>
        /// The brief: what the stage is, what wins a star, and what is coming. The battle is
        /// not running yet, so the board shows the routes rather than the fight.
        /// </summary>
        void ShowBrief()
        {
            _running = false;

            if (briefTitleLabel != null) briefTitleLabel.text = $"{_stage.No} {_stage.Name}";

            int dust = _stage.Schedule != null ? _stage.Schedule.Length : 0;
            if (briefSubLabel != null)
                briefSubLabel.text = $"웨이브 {_sim.WaveCount} · 먼지 {dust}";

            if (briefDescLabel != null)
                briefDescLabel.text = string.IsNullOrEmpty(_stage.Gimmick)
                    ? _stage.Desc
                    : $"{_stage.Desc}\n기믹 · {_stage.Gimmick}";

            FillStarConditions();
            FillEnemyList();

            board.ShowBriefRoutes(true);
            if (briefRoot != null) briefRoot.SetActive(true);
            // The whole tray goes: there is no power to read before the battle starts.
            if (hudRect != null) hudRect.gameObject.SetActive(false);
            if (trayRect != null) trayRect.gameObject.SetActive(false);
            // Reframe now that the bands changed, or the board sits under the panel.
            FrameCamera();
            RequestReframe();
        }

        /// <summary>
        /// Star conditions come from config.starLifeMin, not from the mockup. The design's
        /// chips guessed at "과열 0회"; the table says each star is a cleanliness floor, so
        /// that is what the player is told.
        /// </summary>
        void FillStarConditions()
        {
            if (briefStarLabels == null) return;
            var mins = _svc.Db.Config.starLifeMin;
            int lifeMax = Mathf.Max(1, _svc.Db.Config.lifeMax);

            for (int i = 0; i < briefStarLabels.Length; i++)
            {
                var chip = briefStarLabels[i];
                if (chip == null) continue;
                bool defined = mins != null && i < mins.Length;
                chip.transform.parent.gameObject.SetActive(defined);
                if (!defined) continue;

                int need = mins[i];
                // 10.16-n 은 청결도를 **남은 생명 수**로 쓴다. 퍼센트로 바꿔 쓰면
                // HUD(숫자)와 브리핑(%)이 서로 다른 말을 하게 된다.
                chip.text = need >= lifeMax ? $"★ 청결도 {lifeMax} (무실점)"
                          : $"★ 청결도 {need} 이상";
            }
        }

        void FillEnemyList()
        {
            if (briefEnemyLabel == null) return;

            // Count what the schedule actually spawns rather than listing the stage's roster.
            var counts = new Dictionary<string, int>();
            foreach (var e in _stage.Schedule)
                counts[e.EnemyId] = counts.TryGetValue(e.EnemyId, out var n) ? n + 1 : 1;

            var parts = new List<string>();
            foreach (var kv in counts)
            {
                var def = _svc.Db.Enemy(kv.Key);
                if (def == null) continue;
                string tag = def.Fly ? "(비행)" : def.Breaker ? "(직진)" : "";
                parts.Add($"{def.Name}{tag} ×{kv.Value}");
            }
            briefEnemyLabel.text = string.Join("   ", parts);
        }

        /// <summary>Leaves the brief and starts the fight.</summary>
        void StartBattle()
        {
            _svc?.Guide?.ReportReached("battle.start");
            board.ShowBriefRoutes(false);
            if (briefRoot != null) briefRoot.SetActive(false);
            if (hudRect != null) hudRect.gameObject.SetActive(true);
            if (trayRect != null) trayRect.gameObject.SetActive(true);
            if (deckRoot != null) deckRoot.gameObject.SetActive(true);
            if (hintLabel != null) hintLabel.gameObject.SetActive(true);
            _running = true;
            SetHint(null);
            FrameCamera();
            RequestReframe();
        }

        void Update()
        {
            if (_sim == null) return;

            float dt = Time.unscaledDeltaTime;
            _presentationTime += dt;

            // Reframe when the strip changes shape, not when some other code touches aspect.
            if (boardCamera != null)
            {
                BoardStrip(out float nowBottom, out float nowFree, out _);
                var now = new Vector3(Screen.width, Screen.height, nowBottom + nowFree);
                if ((now - _framedAt).sqrMagnitude > 0.5f) FrameCamera();
            }

            if (_running && !_sim.Finished)
            {
                _tutorial.SelectedCard = _selectedCard;
                _tutorial.Tick(_presentationTime);
                SyncTutorial();

                // A tutorial step marked pause holds the simulation but not the visuals.
                bool hold = _paused || _tutorial.HoldsSimulation;
                if (!hold)
                {
                    _accumulator += Mathf.Min(dt, 0.05f);
                    while (_accumulator >= SimStep)
                    {
                        _accumulator -= SimStep;
                        for (int i = 0; i < _speed; i++)
                        {
                            // 협동은 CoopSim 이 돌린다 — 웨이브 잇기·고리 세기·봇이
                            // 전투 한 칸마다 같이 돌아야 하기 때문이다.
                            if (_coop != null) _coop.Tick(SimStep);
                            else _sim.Tick(SimStep);
                            if (_sim.Finished) break;

                            // 튜토리얼은 **스텝마다** 본다. 프레임당 한 번만 보면 한 프레임에
                            // 쌓인 스텝이 1~2개 더 돌아, 단계가 뜨는 시점의 전력이 그만큼
                            // 흔들린다. 1단계는 전력이 청소기 값(8)과 정확히 같은 지점에서
                            // 뜨므로 한 스텝 차이로 배치가 안 돼 **교착**이 됐다.
                            _tutorial.Tick(_presentationTime);
                            if (_tutorial.HoldsSimulation) break;
                        }
                        if (_sim.Finished || _tutorial.HoldsSimulation) break;
                    }
                    SyncTutorial();
                }
            }

            if (_selectedUnit != null)
            {
                // Retreat and death both set Dead before the unit leaves the list.
                if (_selectedUnit.Dead) DeselectUnit();
                else { unitActionBar.Refresh(); board.ShowUnitRange(_selectedUnit); }
            }

            // 라벨이 판 밖으로 흘러나가지 않게 가로 범위를 알려 준다(원본 `clampX`).
            if (_stage != null)
            {
                fx.LabelClampMinX = board.Origin.x;
                fx.LabelClampMaxX = board.Origin.x + _stage.Cols * BoardView.TileSize;
            }
            fx.Consume(_bus, board.Origin, BoardView.TileSize);
            _bus.Clear();

            board.Sync(_presentationTime);
            // Presentation time, not simulation time: the border keeps pulsing while
            // paused, exactly as a render-time effect does in the original.
            board.SyncSpawnWarnings(_svc.Db, _svc.Db.Config.spawnWarnSec, _presentationTime);
            HandleInput();
            SyncHud();

            if (_sim.Finished && !_settled) Settle();
        }

        // ------------------------------------------------------------------ input

        void HandleInput()
        {
            if (!_running || _sim.Finished) return;

            // Touch first, then mouse, so the editor is playable with either.
            Vector2? screenPoint = null;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                screenPoint = Touchscreen.current.primaryTouch.position.ReadValue();
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                screenPoint = Mouse.current.position.ReadValue();

            if (screenPoint == null) return;
            // Ignore taps that landed on UI (deck cards, HUD buttons).
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var world = boardCamera.ScreenToWorldPoint(
                new Vector3(screenPoint.Value.x, screenPoint.Value.y, -boardCamera.transform.position.z));
            var cell = board.ToBoard(world);
            int c = Mathf.FloorToInt(cell.x), r = Mathf.FloorToInt(cell.y);
            if (!_sim.Field.InBounds(c, r)) return;

            // 협동은 카드를 골라 놓는 게 아니라 칸을 고르고 소환한다.
            if (IsCoop) { HandleCoopTap(c, r); return; }

            if (_selectedCard != null)
            {
                var forced = ForcedTile(_selectedCard);
                if (forced.HasValue && (forced.Value.X != c || forced.Value.Y != r))
                {
                    SetHint("튜토리얼 중에는 빛나는 칸에만 놓을 수 있어요.");
                    return;
                }
                if (_sim.CanPlace(_selectedCard, c, r) && _sim.Affordable(_selectedCard))
                {
                    _sim.Deploy(_selectedCard, c, r);
                    SelectCard(null);
                }
                else SetHint("이 칸에는 배치할 수 없어요. 빛나는 칸을 눌러주세요.");
                return;
            }

            var here = _sim.UnitAt(c, r);
            if (here != null) SelectUnit(here);
            else DeselectUnit();
        }

        /// <summary>
        /// Tapping a deployed unit opens the action bar over the card row: boost, multitap,
        /// retreat. The battle keeps running underneath - the bar is a tray swap, not a modal.
        /// </summary>
        void SelectUnit(BattleUnit u)
        {
            SelectCard(null);
            _selectedUnit = u;
            if (unitActionBar != null) unitActionBar.Show(_sim, u);
            if (deckRoot != null) deckRoot.gameObject.SetActive(u == null);
            if (hintLabel != null) hintLabel.gameObject.SetActive(u == null);
            board.ShowUnitRange(u);
        }

        /// <summary>
        /// Hands the tray back to the cards. Routed through the bar's Closed event so the
        /// bar's own close button restores the deck too, not just a tap elsewhere.
        /// </summary>
        void DeselectUnit()
        {
            if (unitActionBar != null && unitActionBar.gameObject.activeSelf)
            {
                unitActionBar.Close();   // raises Closed, which runs RestoreDeck
                return;
            }
            RestoreDeck();
        }

        void RestoreDeck()
        {
            _selectedUnit = null;
            if (deckRoot != null) deckRoot.gameObject.SetActive(true);
            if (hintLabel != null) hintLabel.gameObject.SetActive(true);
            board.ShowUnitRange(null);
        }

        // ------------------------------------------------------------------ deck

        void BuildDeck()
        {
            foreach (var c in _cards) if (c != null) Destroy(c.gameObject);
            _cards.Clear();

            foreach (var type in _sim.Deck)
            {
                var def = _svc.Db.Unit(type);
                var card = Instantiate(deckCardPrefab, deckRoot);
                card.Bind(def, _svc.Db, () => SelectCard(type == _selectedCard ? null : type));
                _cards.Add(card);
            }
            LayOutDeck();
        }

        /// <summary>
        /// 10.16-n sizes the hand to fit rather than letting it scroll: up to six cards go in
        /// one row, more than six fold into two. The cells then share the width equally, so
        /// a big hand gets narrow cards instead of cards that fall off the screen.
        /// </summary>
        void LayOutDeck()
        {
            var grid = deckRoot != null ? deckRoot.GetComponent<GridLayoutGroup>() : null;
            if (grid == null || _cards.Count == 0) return;

            int n = _cards.Count;
            int cols = n > 6 ? Mathf.CeilToInt(n / 2f) : Mathf.Max(3, n);
            int rows = Mathf.CeilToInt(n / (float)cols);
            grid.constraintCount = cols;

            float w = deckRoot.rect.width, h = deckRoot.rect.height;
            // minmax(0,1fr): the columns share the width and shrink, but the card KEEPS its
            // height and its type size. Scaling height too would shrink the labels out of
            // their boxes, and TMP answers that by drawing nothing at all.
            // 원본 카드는 95.8x87.9 — 16 / 116.8 / 217.5 / 318.3 에 놓여 줄을 꽉 채운다.
            float cellW = Mathf.Min(UiTheme.Px(95.8f), (w - grid.spacing.x * (cols - 1)) / cols);
            float cellH = Mathf.Min(UiTheme.Py(87.9f), (h - grid.spacing.y * (rows - 1)) / rows);
            grid.cellSize = new Vector2(Mathf.Max(1f, cellW), Mathf.Max(1f, cellH));
        }

        /// <summary>The tile the active tutorial step pins this card to, if any.</summary>
        (int X, int Y)? ForcedTile(string type)
        {
            var a = _tutorial?.Active;
            if (a?.ForcedTile == null) return null;
            // Only constrain the unit the step is actually teaching.
            return a.Target == null || a.Target == type ? a.ForcedTile : null;
        }

        void SelectCard(string type)
        {
            if (type != null)
            {
                // A forced-tile step pins the deck to the unit it teaches: the simulation is
                // held still while it shows, so power spent elsewhere never comes back.
                var locked = _tutorial?.Active;
                if (locked != null && locked.LockCard && locked.Target != null && locked.Target != type)
                {
                    SetHint($"지금은 {_svc.Db.Unit(locked.Target).Name} 카드를 눌러 주세요.");
                    return;
                }
                if (_sim.Units.Count >= _sim.MaxUnits)
                {
                    SetHint($"배치 한도({_sim.MaxUnits}대)에 도달했어요. 기존 가전을 철수시키세요.");
                    return;
                }
                if (_sim.CooldownOf(type) > 0f)
                {
                    SetHint($"{_svc.Db.Unit(type).Name} 재배치 대기 중이에요.");
                    return;
                }
                if (_sim.Power < _svc.Db.Unit(type).Cost)
                {
                    SetHint($"전력이 부족해요. {_svc.Db.Unit(type).Cost * 100:N0}W가 필요합니다.");
                    return;
                }
            }

            if (type != null && _selectedUnit != null) DeselectUnit();
            _selectedCard = type;
            board.ShowPlacement(type, ForcedTile(type));

            if (type == null) SetHint(null);
            else
            {
                var d = _svc.Db.Unit(type);
                SetHint(d.Tile == TileKind.Ground
                    ? $"{d.Name}: 바닥(적 이동 경로) 칸에 배치"
                    : $"{d.Name}: 흰색 가구 위 칸에 배치");
            }
        }

        // ------------------------------------------------------------------ hud

        void SetHint(string msg)
        {
            if (hintLabel == null) return;
            hintLabel.text = msg ?? (string.IsNullOrEmpty(_stage.Gimmick)
                ? "카드를 고른 뒤 빛나는 칸을 눌러 배치하세요. 스킬은 자동 발동됩니다."
                : $"기믹 · {_stage.Gimmick}");
        }

        void SyncHud()
        {
            // 10.16-n reads cleanliness as a life COUNT, not a percentage: the number is how
            // many more leaks you can afford, and it turns red at 3 or fewer.
            if (cleanLabel != null && IsCoop)
            {
                // 협동에는 청결도가 없다. 이 자리에 고리 위 먼지를 센다.
                int load = _coop.RingLoad;
                cleanLabel.text = load.ToString();
                cleanLabel.color = load >= _svc.Db.Config.coopLoopWarn ? UiTheme.Danger : UiTheme.LedLife;
                UiTheme.FitToText(cleanLabel.transform.parent.GetComponent<LayoutElement>(),
                                  cleanLabel, UiTheme.Px(30f), cleanCaptionLabel);
            }
            else if (cleanLabel != null)
            {
                int life = Mathf.Max(0, _sim.Life);
                cleanLabel.text = life.ToString();
                cleanLabel.color = life <= 3 && _running ? UiTheme.Danger : UiTheme.LedLife;
                UiTheme.FitToText(cleanLabel.transform.parent.GetComponent<LayoutElement>(),
                                  cleanLabel, UiTheme.Px(30f), cleanCaptionLabel);
            }

            // Power is stored in whole units; the HUD quotes watts, which is the unit x100.
            if (powerValueLabel != null)
                powerValueLabel.text = UiTheme.Compact(Mathf.Floor(_sim.Power) * 100f) + "W";
            if (regenLabel != null)
                regenLabel.text = $"전력 · 초당 +{_sim.PowerRegen() * 100f:N0}W";

            // The meter is tick-to-tick progress toward the next whole unit - NOT power out
            // of the cap. At the cap it pins full so it stops looking like it is still filling.
            float powerMax = Mathf.Max(1f, _svc.Db.Config.powerMax);
            SetFill(powerFill, _sim.Power >= powerMax ? 1f : _sim.Power - Mathf.Floor(_sim.Power));

            if (remainLabel != null)
            {
                remainLabel.text = IsCoop
                    ? _coop.WavesCleared.ToString()
                    : Mathf.Max(0, _sim.Remaining).ToString();
                UiTheme.FitToText(remainLabel.transform.parent.GetComponent<LayoutElement>(),
                                  remainLabel, UiTheme.Px(34f), remainCaptionLabel);
            }

            SyncSlots();
            foreach (var card in _cards) card.Sync(_sim, _selectedCard);
        }

        /// <summary>
        /// The deploy counter and the multi-tap offer. The button is not just disabled when
        /// taps run out - it is removed, because an offer that can never be taken is noise.
        /// </summary>
        void SyncSlots()
        {
            if (slotCountLabel != null)
                slotCountLabel.text = $"배치 {_sim.Units.Count}/{_sim.MaxUnits}";

            float? cost = _sim.NextTapCost;
            if (multitapButton != null) multitapButton.gameObject.SetActive(cost.HasValue);
            if (!cost.HasValue) return;

            if (multitapLabel != null)
                multitapLabel.text = $"멀티탭 +1칸 · {UiTheme.Watts(cost.Value)}";
            bool afford = _sim.Power >= cost.Value;
            multitapButton.State = afford && _running && !_paused
                                 ? UiButtonState.Normal : UiButtonState.Disabled;
            // "hot": you are at the limit and can pay for the fix right now.
            multitapButton.SetKind(afford && _sim.Units.Count >= _sim.MaxUnits
                                   ? UiButtonKind.Power : UiButtonKind.Secondary);
        }

        /// <summary>Drives a RectMask2D bar's fill by its right anchor, keeping the round cap.</summary>
        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
        }

        void SyncTutorial()
        {
            if (tutorialRoot == null) return;
            var active = _tutorial.Active;
            tutorialRoot.SetActive(active != null);
            if (active == null)
            {
                board.SetTutorialFocus(null, null, false, _presentationTime);
                return;
            }
            if (tutorialText != null) tutorialText.text = active.Text;
            if (tutorialOk != null) tutorialOk.gameObject.SetActive(active.NeedsTap);

            // The spotlight is what "빛나는 칸" refers to, so it must be lit before the player
            // picks a card. A heat step with no tile spotlights the hottest blocker instead.
            (int X, int Y)? hole = active.ForcedTile;
            if (hole == null && _tutorial.HighlightUnit != null)
            {
                var hu = _tutorial.HighlightUnit;
                hole = (hu.Col, hu.Row);
            }
            board.SetTutorialFocus(active.ForcedTile, hole, active.Pauses, _presentationTime);
        }

        void ToggleSpeed()
        {
            _speed = _speed == 1 ? 2 : 1;
            _svc.Profile.Data.speed = _speed;
            _svc.Persist();
            SyncSpeedLabel();
        }

        void SyncSpeedLabel()
        {
            // "1×" / "2×", exactly as the build writes it. The control keeps its dark plate
            // either way; swapping the whole button to a light kind made it read as a
            // separate widget floating off the HUD.
            if (speedButtonLabel != null) speedButtonLabel.text = _speed + "×";
            if (speedButtonLabel != null)
                speedButtonLabel.color = _speed == 2 ? UiTheme.Led : UiTheme.DarkInk;
        }

        /// <summary>
        /// The HUD's 일시정지 button: stops the simulation without opening anything. The
        /// presentation keeps animating, so the board does not freeze solid.
        /// </summary>
        void TogglePause()
        {
            if (!_running || _sim.Finished || _pauseMenu) return;
            SetPaused(!_paused);
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            if (pauseButtonLabel != null) pauseButtonLabel.text = paused ? "계속" : "일시정지";
        }

        /// <summary>
        /// 10.17-c: 나가기 during a battle opens the pause menu instead of arming a second
        /// tap. Two-step confirm looked identical to a misfire and gave no way to simply
        /// restart. Before the battle starts and after it ends there is nothing to confirm,
        /// so the button goes straight back to the lobby.
        /// </summary>
        void OnExitPressed()
        {
            if (!_running || _sim.Finished) { LeaveToLobby(); return; }
            if (_pauseMenu) ClosePauseMenu(); else OpenPauseMenu();
        }

        void OpenPauseMenu()
        {
            _pauseMenu = true;
            SetPaused(true);
            SelectCard(null);
            DeselectUnit();
            // The tutorial bubble would otherwise sit on top of the menu.
            if (tutorialRoot != null) tutorialRoot.SetActive(false);
            SetMenuDim(true);

            if (pauseTitleLabel != null) pauseTitleLabel.text = _svc.Db.Text("battle.pause.title");
            if (pauseDescLabel != null) pauseDescLabel.text = _svc.Db.Text("battle.pause.desc");
            if (pauseResumeButton != null) pauseResumeButton.Text = _svc.Db.Text("battle.pause.resume");
            if (pauseRetryButton != null) pauseRetryButton.Text = _svc.Db.Text("battle.pause.retry");
            if (pauseExitButton != null) pauseExitButton.Text = _svc.Db.Text("battle.pause.exit");
            if (pauseOverlay != null) pauseOverlay.SetActive(true);
        }

        void ClosePauseMenu()
        {
            _pauseMenu = false;
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            SetMenuDim(false);
            SyncTutorial();
            SetPaused(false);
        }

        /// <summary>The tray and the HUD's own controls go half-lit and untappable.</summary>
        void SetMenuDim(bool on)
        {
            if (trayGroup == null) return;
            trayGroup.alpha = on ? 0.5f : 1f;
            trayGroup.interactable = !on;
            trayGroup.blocksRaycasts = !on;
        }

        /// <summary>Replays the stage from the top. The run in progress is simply dropped.</summary>
        void RetryStage()
        {
            var stage = _stage;
            ClosePauseMenu();
            if (stage == null) { LeaveToLobbyCore(); return; }
            Begin(_svc, stage);
        }

        void LeaveFromPauseMenu()
        {
            ClosePauseMenu();
            LeaveToLobby();
        }

        void LeaveToLobby()
        {
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            SetMenuDim(false);
            _pauseMenu = false;
            LeaveToLobbyCore();
        }

        void LeaveToLobbyCore()
        {
            if (result != null) result.gameObject.SetActive(false);
            ShowBoard();
            _running = false;
            _tutorial?.Hide();
            board.ClearAll();
            fx.ClearAll();
            HideOverlay();
            _sim = null;
            Exited?.Invoke();
        }

        // ------------------------------------------------------------------ result

        /// <summary>
        /// 협동은 이기는 길이 없다. 고리가 가득 차면 끝나고, 점수는 **버틴 웨이브**다.
        /// 일지 점수는 판 안에서 이미 들어갔으므로 여기서는 보여 주기만 한다.
        /// </summary>
        void SettleCoop()
        {
            var r = _svc.Profile.FinishCoop(_coop.Partner.id, _coop.WavesCleared,
                                            _coop.Coin, _coop.PoolPoints);
            _svc.Persist();
            OnPartnerSpoke("end");

            var body = new System.Text.StringBuilder();
            body.AppendLine($"버틴 웨이브 {r.Waves} · 처치 {_sim.Killed} · 최고 기록 {r.Best}");

            var paid = new System.Collections.Generic.List<string>();
            if (r.PoolPoints > 0) paid.Add($"청소 일지 +{r.PoolPoints}점");
            if (r.Coin > 0) paid.Add($"코인 {UiTheme.Compact(r.Coin)}");
            if (r.PassXp > 0) paid.Add($"시즌 패스 경험치 +{r.PassXp}");
            body.AppendLine(paid.Count > 0
                                ? "보상: " + string.Join(", ", paid)
                                : "버틴 웨이브가 없어 보상이 없어요.");

            var (pts, max) = _svc.Profile.SurvivorPoolToday();
            body.Append($"청소 일지 {pts}/{max}");

            ShowOverlay(r.NewRecord ? "최고 기록 갱신!" : "먼지가 가득 찼어요",
                        body.ToString(),
                        "다시 하기", () => BeginCoop(_svc, _coop.Partner),
                        "로비로", () => Exited?.Invoke());
        }

        void HookActionBar()
        {
            if (unitActionBar == null || _barHooked) return;
            _barHooked = true;
            unitActionBar.Closed += RestoreDeck;
            // Boosting or buying a slot changes power, so the HUD needs a repaint.
            unitActionBar.Acted += SyncHud;
        }

        // ------------------------------------------------------------------ 협동

        CoopSim _coop;

        /// <summary>
        /// 스테이지 전용 표시를 협동용으로 바꿔 단다. 원본이 `coopHud(on)` 에서
        /// 청결도 → "고리 먼지 / 40", 남은 적 → "버틴 웨이브" 로 글자를 바꾸는 것과 같다.
        /// </summary>
        void SetCoopChrome(bool on)
        {
            if (coopBars != null) coopBars.gameObject.SetActive(on);
            // 원본 `.battle.rd` 는 slots · psbar · deck 을 감춘다. 협동은 카드로 놓는 게
            // 아니라 칸을 골라 뽑는 모드라 그 줄들이 쓸모가 없다.
            if (trayRoot != null) trayRoot.gameObject.SetActive(!on);
            if (slotsRow != null) slotsRow.gameObject.SetActive(!on);
            if (on && briefRoot != null) briefRoot.SetActive(false);

            // 읽기칸 폭은 여기서 정하지 않는다. `SyncHud` 가 매 프레임 `FitToText` 로
            // 다시 재므로 여기서 넣어 봐야 한 프레임 만에 덮인다 — 협동 머리글이
            // '고리 …' 로 잘리던 원인이 **두 곳이 같은 값을 쓰던 것**이었다.
            // 이제 `FitToText` 가 머리글까지 보고 한 곳에서 정한다.
            if (cleanCaptionLabel != null) cleanCaptionLabel.text = on ? $"고리 먼지 / {_svc.Db.Config.coopLoopMax}" : "청결도";
            if (remainCaptionLabel != null) remainCaptionLabel.text = on ? "버틴 웨이브" : "남은 적";
            if (on && coopBars != null) coopBars.Build(_svc, _coop);
        }

        void OnPartnerSpoke(string key)
        {
            if (_coop == null || coopBars == null) return;
            coopBars.Say(_svc.Db.Text($"coop.{_coop.Partner.id}.{key}"));
        }

        void ShowCoopIntro(CoopPartnerRow partner)
        {
            var cfg = _svc.Db.Config;
            string body =
                $"{CoopName(partner)}와 함께 거실 러그를 빙글빙글 도는 먼지를 치워요. " +
                $"고리 위 먼지가 {cfg.coopLoopMax}마리가 되면 끝이에요.\n" +
                $"고리 위(근접)는 {cfg.coopLineMax}대까지, 옮긴 가전은 {cfg.coopMoveCd}초 동안 다시 못 옮겨요.";
            ShowOverlay("협동 청소", body, "시작", () =>
            {
                HideOverlay();
                _running = true;
                OnPartnerSpoke("start");
                SetHint(null);
            }, null, null);
        }

        /// <summary>
        /// 협동의 보드 탭. 배치 카드를 고르는 본편과 달리 **칸을 고르고 소환**하거나
        /// 가전을 눌러 합치고 옮긴다. 안내문은 CoopSim 이 돌려준 결과로 정한다.
        /// </summary>
        void HandleCoopTap(int c, int r)
        {
            var before = _coop.Selected;
            var result = _coop.Tap(c, r);
            var cfg = _svc.Db.Config;

            switch (result)
            {
                case CoopTapResult.PartnersUnit:
                    SetHint($"{CoopName(_coop.Partner)}의 가전이에요. 파트너가 알아서 관리해요.");
                    break;
                case CoopTapResult.WrongTile:
                    SetHint(before != null && _svc.Db.Unit(before.Type).Tile == TileKind.Ground
                                ? "근접 가전은 고리 칸으로만 옮길 수 있어요."
                                : "원거리 가전은 가구 칸으로만 옮길 수 있어요.");
                    break;
                case CoopTapResult.MoveOnCooldown:
                    SetHint($"방금 옮긴 가전이에요. {Mathf.Ceil(_coop.MoveCooldownOf(before))}초 뒤에 다시 옮길 수 있어요.");
                    break;
                case CoopTapResult.RingFull:
                    SetHint($"고리 위 가전이 꽉 찼어요({cfg.coopLineMax}대). 합치거나 원거리 칸을 골라요.");
                    break;
                case CoopTapResult.Moved:
                    SetHint($"옮겼어요. {cfg.coopMoveCd}초 동안은 다시 못 옮겨요.");
                    break;
                case CoopTapResult.Merged:
                    SetHint(null);
                    break;
                case CoopTapResult.SpotPicked:
                    var tile = _coop.TileAt(c, r);
                    SetHint(tile == TileKind.Ground
                                ? $"고리 칸 · [가전 소환]을 누르면 여기에 근접 가전이 나와요(고리 위 {_coop.RingCount(CoopSide.Player)}/{cfg.coopLineMax})."
                                : "가구 칸 · [가전 소환]을 누르면 여기에 원거리 가전이 나와요.");
                    break;
                case CoopTapResult.Selected:
                    var u = _coop.Selected;
                    int mates = _coop.MergeMatesFor(u);
                    string mv = _coop.MoveCooldownOf(u) > 0f
                        ? $" · 옮기기 {Mathf.Ceil(_coop.MoveCooldownOf(u))}초 뒤"
                        : " · 빈칸을 누르면 옮겨요";
                    SetHint(_coop.StarOf(u) >= cfg.coopStarMax
                                ? $"{u.Def.Name} ★{_coop.StarOf(u)} · 최고 단계예요{mv}."
                                : mates > 0
                                    ? $"{u.Def.Name} ★{_coop.StarOf(u)} · 빛나는 ★{_coop.StarOf(u)}을 누르면 합쳐져요{mv}."
                                    : $"{u.Def.Name} ★{_coop.StarOf(u)} · ★{_coop.StarOf(u)}이 하나 더 있어야 합칠 수 있어요{mv}.");
                    break;
                default:
                    SetHint(null);
                    break;
            }
            coopBars?.Refresh();
        }

        void Settle()
        {
            _settled = true;
            _running = false;
            _tutorial.Hide();
            if (tutorialRoot != null) tutorialRoot.SetActive(false);
            board.ClearHighlights();

            if (IsCoop) { SettleCoop(); return; }

            bool won = _sim.Outcome == BattleOutcome.Won;
            var result_ = _svc.Profile.ApplyStageResult(_stage, won, _sim.Life);

            var body = new System.Text.StringBuilder();
            if (won) body.AppendLine(new string('★', result_.Stars) + new string('☆', 3 - result_.Stars));

            if (result_.FirstClear)
            {
                body.AppendLine(result_.PreviousIdleStage == null
                    ? $"자동 청소 시작! 시급 코인 {result_.NewIdleStage.IdleCoin:N0}"
                    : $"시급 코인 {result_.PreviousIdleStage.IdleCoin:N0} → {result_.NewIdleStage.IdleCoin:N0}");
            }
            else if (won)
            {
                body.AppendLine(result_.NewRecord
                    ? $"최고 기록 갱신! 별 {result_.PreviousStars}개 → {result_.Stars}개"
                    : $"이미 클리어한 스테이지예요. 최고 기록은 별 {result_.PreviousStars}개입니다.");
            }
            else body.AppendLine("청결도가 0이 됐어요. 자동 청소로 모은 재화로 가전을 육성하고 다시 도전해보세요.");

            if (won && result_.Stars < 3)
            {
                var cfg = _svc.Db.Config;
                if (result_.Stars < cfg.starLifeMin.Length)
                    body.AppendLine($"청결도 {cfg.starLifeMin[result_.Stars]} 이상으로 지키면 별 {result_.Stars + 1}개예요.");
            }
            if (result_.UnlockedStarReward)
                body.AppendLine($"챕터 {_stage.Chapter} 별 보상을 받을 수 있어요.");

            body.AppendLine();
            body.AppendLine($"{_sim.Killed}처치  ·  청결도 {Mathf.Max(0, _sim.Life)}  ·  {Mathf.Floor(_sim.Time)}초");
            if (result_.Rewards.Count > 0)
                body.AppendLine($"보상: {_svc.Profile.DescribeRewards(result_.Rewards)}");

            // Redesign 2g replaces the generic overlay. The text version stays below as the
            // fallback for a scene built before the result screen existed.
            if (result != null)
            {
                // 판은 그대로 둔다. 결과는 **그늘 위에 뜨는 카드**이지 화면을 다 덮는 장면이
                // 아니다 — 원본도 끝난 판이 카드 뒤로 보인다. 끄면 그 자리가 통째로 비어
                // 무엇을 하다 끝났는지가 사라진다. 연출만 멈춘다.
                if (fx != null) fx.ClearAll();
                result.Show(_svc, _stage, _sim, result_, won);
                return;
            }

            var next = won ? _svc.Db.StageByOrder(_stage.Order + 1) : null;
            bool nextOpen = next != null && _svc.Profile.ConditionMet(next.UnlockCond);

            ShowOverlay(
                won ? $"{_stage.No} {_stage.Name} 사수 성공" : $"{_stage.No} {_stage.Name} 함락",
                body.ToString(),
                nextOpen ? $"다음 {next.No}" : "다시 하기",
                () =>
                {
                    HideOverlay();
                    board.ClearAll();
                    fx.ClearAll();
                    Begin(_svc, nextOpen ? next : _stage);
                },
                "로비로", LeaveToLobby);
        }

        /// <summary>Brings the board back after a screen that covered it.</summary>
        void ShowBoard()
        {
            if (board != null) board.gameObject.SetActive(true);
            if (fx != null) fx.gameObject.SetActive(true);
        }

        /// <summary>Clears the board and starts a stage again from the brief.</summary>
        void Restart(StageDef stage)
        {
            if (result != null) result.gameObject.SetActive(false);
            ShowBoard();
            HideOverlay();
            board.ClearAll();
            fx.ClearAll();
            Begin(_svc, stage);
        }

        // ------------------------------------------------------------------ overlay

        void ShowOverlay(string title, string body, string primary, System.Action onPrimary,
                         string secondary, System.Action onSecondary)
        {
            if (overlayRoot == null) return;
            overlayRoot.SetActive(true);
            if (overlayTitle != null) overlayTitle.text = title;
            if (overlayBody != null) overlayBody.text = body;
            FitOverlayCard();

            if (overlayPrimary != null)
            {
                overlayPrimary.gameObject.SetActive(primary != null);
                overlayPrimary.onClick.RemoveAllListeners();
                if (primary != null)
                {
                    if (overlayPrimaryLabel != null) overlayPrimaryLabel.text = primary;
                    overlayPrimary.onClick.AddListener(() => onPrimary?.Invoke());
                }
            }
            if (overlaySecondary != null)
            {
                overlaySecondary.gameObject.SetActive(secondary != null);
                overlaySecondary.onClick.RemoveAllListeners();
                if (secondary != null)
                {
                    if (overlaySecondaryLabel != null) overlaySecondaryLabel.text = secondary;
                    overlaySecondary.onClick.AddListener(() => onSecondary?.Invoke());
                }
            }
        }

        /// <summary>
        /// 원본 `.ov-card` 는 flex column 이라 **내용만큼만** 높다. 우리 카드는 고정
        /// 높이라 글이 짧으면 아래가 크게 비었다. 본문이 실제로 차지하는 높이를 재서
        /// 카드를 거기에 맞춘다.
        /// </summary>
        void FitOverlayCard()
        {
            if (overlayCard == null || overlayBody == null) return;

            // 버튼이 하나뿐이면 폭을 다 쓴다. 둘을 가정한 자리에 하나만 두면 한쪽으로 쏠린다.
            if (overlayPrimary != null && overlaySecondary != null)
            {
                bool both = overlayPrimary.gameObject.activeSelf && overlaySecondary.gameObject.activeSelf;
                var prt = (RectTransform)overlayPrimary.transform;
                prt.anchorMin = new Vector2(both ? 0.5f : 0f, 0f);
                prt.offsetMin = new Vector2(both ? UiTheme.S4 : UiTheme.Px(18f), prt.offsetMin.y);
            }

            float pad = UiTheme.Py(18f);
            float titleH = UiTheme.Line(UiTheme.Py(20f));
            float gap = UiTheme.Py(10f);
            float buttons = UiTheme.ButtonTall + UiTheme.Py(16f) + UiTheme.Py(12f);

            float inner = overlayCard.rect.width - UiTheme.Px(18f) * 2f;
            float bodyH = overlayBody.GetPreferredValues(overlayBody.text, inner, 0f).y;

            overlayCard.sizeDelta = new Vector2(overlayCard.sizeDelta.x,
                                                pad + titleH + gap + bodyH + buttons);
        }

        void HideOverlay()
        {
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        void OnApplicationPause(bool paused)
        {
            // Losing focus mid-battle should not cost the player progress.
            if (paused && _running && !_paused) TogglePause();
        }
    }
}
