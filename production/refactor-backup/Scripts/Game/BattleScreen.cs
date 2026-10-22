// Drives one defense battle: input, HUD, overlays and the simulation clock.
//
// Speed and pause are handled here, not with Time.timeScale, because only the simulation
// should change rate. At 2x the sim is ticked twice with the same dt the original used, so
// per-tick rules (cooldowns, heat, skill charge) behave identically at either speed.
using System.Collections.Generic;
using DustZero.Core;
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
        [SerializeField] FxDirector fx;
        [SerializeField] Camera boardCamera;

        [Header("일시정지")]
        [SerializeField] GameObject pauseOverlay;
        [SerializeField] UiButton pauseResumeButton;

        [Header("레이아웃")]
        /// <summary>Top band; the board viewport starts below it.</summary>
        [SerializeField] RectTransform hudRect;
        /// <summary>Bottom tray; the board viewport ends above it.</summary>
        [SerializeField] RectTransform trayRect;

        [Header("HUD 상단")]
        /// <summary>"웨이브 3" - the count the player tracks, big.</summary>
        [SerializeField] TextMeshProUGUI waveLabel;
        /// <summary>"/ 10 · 1-3 거실" - total and stage, small, beside it.</summary>
        [SerializeField] TextMeshProUGUI waveSubLabel;
        /// <summary>Ten cells that fill as waves arrive, so progress is visible at a glance.</summary>
        [SerializeField] Image[] waveSegments;
        [SerializeField] TextMeshProUGUI remainLabel;
        [SerializeField] UiButton pauseButton;
        [SerializeField] UiButton speedButton;
        [SerializeField] UiButton exitButton;

        [Header("HUD 청결도")]
        [SerializeField] RectTransform cleanFill;
        [SerializeField] TextMeshProUGUI cleanLabel;

        [Header("HUD 전력")]
        [SerializeField] TextMeshProUGUI powerValueLabel;
        [SerializeField] TextMeshProUGUI powerMaxLabel;
        [SerializeField] RectTransform powerFill;
        [SerializeField] TextMeshProUGUI regenLabel;
        [SerializeField] TextMeshProUGUI hintLabel;

        [Header("덱")]
        [SerializeField] RectTransform deckRoot;
        [SerializeField] DeckCard deckCardPrefab;
        /// <summary>Replaces the card row while a deployed unit is selected (redesign 1c).</summary>
        [SerializeField] UnitActionBar unitActionBar;

        [Header("오버레이")]
        [SerializeField] GameObject overlayRoot;
        [SerializeField] TextMeshProUGUI overlayTitle;
        [SerializeField] TextMeshProUGUI overlayBody;
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
        float _exitArmedAt = -99f;

        /// <summary>Raised when the player leaves, so the host can return to the lobby.</summary>
        public event System.Action Exited;

        /// <summary>Simulation step. Fixed so battles play the same regardless of frame rate.</summary>
        const float SimStep = 1f / 60f;
        float _accumulator;

        public BattleSim Sim => _sim;

        void Awake()
        {
            if (speedButton != null) speedButton.SetAction(ToggleSpeed);
            if (pauseButton != null) pauseButton.SetAction(TogglePause);
            if (exitButton != null) exitButton.SetAction(OnExitPressed);
            if (pauseResumeButton != null) pauseResumeButton.SetAction(TogglePause);
            if (tutorialOk != null) tutorialOk.onClick.AddListener(() => _tutorial?.Complete());
            if (tutorialOff != null) tutorialOff.onClick.AddListener(() => _tutorial?.Complete(true));
        }

        // ------------------------------------------------------------------ lifecycle

        public void Begin(GameServices services, StageDef stage)
        {
            _svc = services;
            _stage = stage;
            _bus = new PresentationBus();
            _settled = false;
            _presentationTime = 0f;
            _accumulator = 0f;

            _sim = new BattleSim(services.Db, stage, services.Profile.Data.deck,
                                 services.Profile, services.Rng, _bus);

            if (unitActionBar != null && !_barHooked)
            {
                _barHooked = true;
                unitActionBar.Closed += RestoreDeck;
                // Boosting or buying a slot changes power, so the HUD needs a repaint.
                unitActionBar.Acted += SyncHud;
            }

            _tutorial = new TutorialDirector(services.Db, services.Profile.Data, _sim);
            _tutorial.SaveDirty += services.Persist;

            _speed = services.Profile.Data.speed == 2 ? 2 : 1;
            _paused = false;
            _running = false;
            _selectedCard = null;

            board.Build(_sim);
            fx.ClearAll();
            BuildDeck();
            FrameCamera();

            if (waveSubLabel != null)
                waveSubLabel.text = $"/ {_sim.WaveCount} · {stage.No} {stage.Name}";
            SetHint(null);
            SyncSpeedLabel();
            if (pauseButton != null) pauseButton.Text = "⏸";
            if (tutorialRoot != null) tutorialRoot.SetActive(false);

            ShowIntro();
        }

        float _framedAspect = -1f;

        /// <summary>
        /// Fits the board into the strip the HUD and tray leave behind. The camera keeps the
        /// full viewport - carving it with Camera.rect fights anything that renders the scene
        /// at another resolution - so the strip is honoured by choosing the orthographic size
        /// and sliding the camera instead. The guide states the sizing rule directly: a tile
        /// is min(width / cols, remaining height / rows).
        /// </summary>
        void FrameCamera()
        {
            if (boardCamera == null || _stage == null) return;
            boardCamera.rect = new Rect(0f, 0f, 1f, 1f);

            float screenW = Mathf.Max(1f, Screen.width);
            float screenH = Mathf.Max(1f, Screen.height);
            float topPx = BandPixels(hudRect);
            float bottomPx = BandPixels(trayRect);
            float freePx = Mathf.Clamp(screenH - topPx - bottomPx, screenH * 0.2f, screenH);
            float freeFrac = freePx / screenH;

            float aspect = screenW / screenH;
            float rows = _stage.Rows * BoardView.TileSize;
            float cols = _stage.Cols * BoardView.TileSize;

            // Half-height that leaves the board inside the free strip, and half-height that
            // keeps it inside the screen width. The larger wins so neither is cropped.
            float sizeForHeight = rows * 0.5f / freeFrac;
            float sizeForWidth = cols * 0.5f / aspect;
            float size = Mathf.Max(sizeForHeight, sizeForWidth) * 1.04f;
            boardCamera.orthographicSize = size;

            // Slide the camera so the board sits in the middle of the strip rather than the
            // middle of the screen.
            float bandCentre = (bottomPx + freePx * 0.5f) / screenH;
            float boardCentreY = board.Origin.y - rows * 0.5f;
            var p = boardCamera.transform.position;
            boardCamera.transform.position =
                new Vector3(p.x, boardCentreY + (0.5f - bandCentre) * 2f * size, p.z);

            _framedAspect = CurrentBandAspect();
        }

        /// <summary>The aspect of the strip the board currently gets.</summary>
        float CurrentBandAspect()
        {
            float screenH = Mathf.Max(1f, Screen.height);
            float freePx = Mathf.Max(screenH * 0.2f, screenH - BandPixels(hudRect) - BandPixels(trayRect));
            return Screen.width / freePx;
        }

        /// <summary>A UI band's height in screen pixels, for carving out the board viewport.</summary>
        static float BandPixels(RectTransform band)
        {
            if (band == null || !band.gameObject.activeInHierarchy) return 0f;
            var canvas = band.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            return band.rect.height * scale;
        }

        void ShowIntro()
        {
            var enemies = new List<string>();
            foreach (var id in _stage.EnemyIds)
            {
                var e = _svc.Db.Enemy(id);
                enemies.Add(e.Name + (e.Fly ? "(비행)" : e.Breaker ? "(직진)" : "(우회)"));
            }

            ShowOverlay(
                $"{_stage.No} {_stage.Name}",
                $"{_stage.Desc}\n\n출현: {string.Join(", ", enemies)}" +
                (string.IsNullOrEmpty(_stage.Gimmick) ? "" : $"\n기믹 · {_stage.Gimmick}"),
                "시작", () => { HideOverlay(); _running = true; SetHint(null); },
                null, null);
        }

        void Update()
        {
            if (_sim == null) return;

            float dt = Time.unscaledDeltaTime;
            _presentationTime += dt;

            // Reframe when the strip changes shape, not when some other code touches aspect.
            if (boardCamera != null && !Mathf.Approximately(CurrentBandAspect(), _framedAspect))
                FrameCamera();

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
                            _sim.Tick(SimStep);
                            if (_sim.Finished) break;
                        }
                        if (_sim.Finished) break;
                    }
                }
            }

            if (_selectedUnit != null)
            {
                // Retreat and death both set Dead before the unit leaves the list.
                if (_selectedUnit.Dead) DeselectUnit();
                else { unitActionBar.Refresh(); board.ShowUnitRange(_selectedUnit); }
            }

            fx.Consume(_bus, board.Origin, BoardView.TileSize);
            _bus.Clear();

            board.Sync(_presentationTime);
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
            int wave = _sim.WaveIndex;
            if (waveLabel != null) waveLabel.text = $"웨이브 {wave}";
            SyncWaveSegments(wave);

            // Cleanliness is the life bar read as a percentage, which is how the design frames it.
            float cleanRatio = _svc.Db.Config.lifeMax > 0
                ? Mathf.Clamp01(_sim.Life / (float)_svc.Db.Config.lifeMax) : 0f;
            SetFill(cleanFill, cleanRatio);
            if (cleanLabel != null)
            {
                cleanLabel.text = Mathf.RoundToInt(cleanRatio * 100f) + "%";
                cleanLabel.color = cleanRatio <= 0.3f ? UiTheme.Danger : UiTheme.Ink2;
            }

            float powerMax = Mathf.Max(1f, _svc.Db.Config.powerMax);
            if (powerValueLabel != null) powerValueLabel.text = UiTheme.Compact(Mathf.Floor(_sim.Power) * 100f);
            if (powerMaxLabel != null) powerMaxLabel.text = "/ " + UiTheme.Watts(powerMax);
            SetFill(powerFill, Mathf.Clamp01(_sim.Power / powerMax));
            if (regenLabel != null) regenLabel.text = $"+{_sim.PowerRegen() * 100f:N0}/s";

            if (remainLabel != null) remainLabel.text = Mathf.Max(0, _sim.Remaining).ToString();

            foreach (var card in _cards) card.Sync(_sim, _selectedCard);
        }

        /// <summary>
        /// Paints the wave strip: finished waves solid, the one arriving highlighted, the rest
        /// idle. When a stage has fewer waves than cells the spare cells are hidden rather
        /// than shown empty, so the strip always reads as "this many waves".
        /// </summary>
        void SyncWaveSegments(int wave)
        {
            if (waveSegments == null) return;
            int total = Mathf.Max(1, _sim.WaveCount);
            for (int i = 0; i < waveSegments.Length; i++)
            {
                var seg = waveSegments[i];
                if (seg == null) continue;
                bool used = i < total;
                seg.gameObject.SetActive(used);
                if (!used) continue;
                seg.color = i < wave - 1 ? UiTheme.Primary
                          : i == wave - 1 ? new Color(0.96f, 0.71f, 0.48f)
                          : new Color(0.89f, 0.84f, 0.78f);
            }
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
            if (speedButton == null) return;
            speedButton.Text = "▶▶ ×" + _speed;
            // 2x is a state, not just a label, so it takes the selected treatment.
            speedButton.Kind = _speed == 2 ? UiButtonKind.Primary : UiButtonKind.Secondary;
        }

        /// <summary>
        /// Pausing opens the overlay: with no exit button in the HUD, this is where leaving
        /// and resuming live. The simulation stops; the presentation keeps animating.
        /// </summary>
        void TogglePause()
        {
            if (!_running || _sim.Finished) return;
            _paused = !_paused;
            if (pauseButton != null) pauseButton.Text = _paused ? "▶" : "⏸";
            if (pauseOverlay != null) pauseOverlay.SetActive(_paused);
        }

        void OnExitPressed()
        {
            if (!_running || _sim.Finished) { LeaveToLobby(); return; }
            // Two-step confirm so a stray tap cannot throw a run away.
            if (Time.unscaledTime - _exitArmedAt < 3f) { LeaveToLobby(); return; }
            _exitArmedAt = Time.unscaledTime;
            if (exitButton != null) exitButton.Text = "포기?";
            CancelInvoke(nameof(DisarmExit));
            Invoke(nameof(DisarmExit), 3f);
        }

        void DisarmExit()
        {
            if (exitButton != null) exitButton.Text = "나가기";
        }

        void LeaveToLobby()
        {
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            LeaveToLobbyCore();
        }

        void LeaveToLobbyCore()
        {
            _running = false;
            _tutorial?.Hide();
            board.ClearAll();
            fx.ClearAll();
            HideOverlay();
            _sim = null;
            Exited?.Invoke();
        }

        // ------------------------------------------------------------------ result

        void Settle()
        {
            _settled = true;
            _running = false;
            _tutorial.Hide();
            if (tutorialRoot != null) tutorialRoot.SetActive(false);
            board.ClearHighlights();

            bool won = _sim.Outcome == BattleOutcome.Won;
            var result = _svc.Profile.ApplyStageResult(_stage, won, _sim.Life);

            var body = new System.Text.StringBuilder();
            if (won) body.AppendLine(new string('★', result.Stars) + new string('☆', 3 - result.Stars));

            if (result.FirstClear)
            {
                body.AppendLine(result.PreviousIdleStage == null
                    ? $"자동 청소 시작! 시급 코인 {result.NewIdleStage.IdleCoin:N0}"
                    : $"시급 코인 {result.PreviousIdleStage.IdleCoin:N0} → {result.NewIdleStage.IdleCoin:N0}");
            }
            else if (won)
            {
                body.AppendLine(result.NewRecord
                    ? $"최고 기록 갱신! 별 {result.PreviousStars}개 → {result.Stars}개"
                    : $"이미 클리어한 스테이지예요. 최고 기록은 별 {result.PreviousStars}개입니다.");
            }
            else body.AppendLine("청결도가 0이 됐어요. 자동 청소로 모은 재화로 가전을 육성하고 다시 도전해보세요.");

            if (won && result.Stars < 3)
            {
                var cfg = _svc.Db.Config;
                if (result.Stars < cfg.starLifeMin.Length)
                    body.AppendLine($"청결도 {cfg.starLifeMin[result.Stars]} 이상으로 지키면 별 {result.Stars + 1}개예요.");
            }
            if (result.UnlockedStarReward)
                body.AppendLine($"챕터 {_stage.Chapter} 별 보상을 받을 수 있어요.");

            body.AppendLine();
            body.AppendLine($"{_sim.Killed}처치  ·  청결도 {Mathf.Max(0, _sim.Life)}  ·  {Mathf.Floor(_sim.Time)}초");
            if (result.Rewards.Count > 0)
                body.AppendLine($"보상: {_svc.Profile.DescribeRewards(result.Rewards)}");

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

        // ------------------------------------------------------------------ overlay

        void ShowOverlay(string title, string body, string primary, System.Action onPrimary,
                         string secondary, System.Action onSecondary)
        {
            if (overlayRoot == null) return;
            overlayRoot.SetActive(true);
            if (overlayTitle != null) overlayTitle.text = title;
            if (overlayBody != null) overlayBody.text = body;

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
