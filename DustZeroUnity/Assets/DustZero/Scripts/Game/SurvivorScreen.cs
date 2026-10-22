// Drives one 대청소 (survival) run: leader pick, the open arena, level-up cards and the result.
//
// Mirrors BattleScreen's shape (fixed-step sim clock, pause as a flag rather than Time.timeScale,
// PresentationBus drained through the same FxDirector) but trades the tile board and deck tray
// for a free-roaming arena the camera follows, and card placement for a joystick-style drag.
using DustZero.Core;
using DustZero.Core.Survivor;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class SurvivorScreen : MonoBehaviour
    {
        [Header("아레나")]
        [SerializeField] SurvivorView view;
        [SerializeField] FxDirector fx;
        [SerializeField] Camera arenaCamera;

        /// <summary>대청소 월드를 찍는 카메라. 화면 전환 때 깊이를 조정한다.</summary>
        public Camera ArenaCamera => arenaCamera;
        [SerializeField] float cameraSize = 4.6f;

        [Header("리더 선택")]
        [SerializeField] GameObject leaderSelectRoot;
        [SerializeField] RectTransform leaderListRoot;
        [SerializeField] UnitCard leaderCardPrefab;
        /// <summary>`.sv-sel` — the picked leader's survival skill name and filled-in description.</summary>
        [SerializeField] TextMeshProUGUI leaderSkillName;
        [SerializeField] TextMeshProUGUI leaderSkillDesc;
        /// <summary>`.deckstrip .minis` — the deck minus the leader, as 34x34 portraits.</summary>
        [SerializeField] RectTransform leaderCompRoot;
        [SerializeField] TextMeshProUGUI leaderCompEmpty;
        /// <summary>`.sv-rw` — 청소 일지 / 보상 판 / 부품·코인 lines.</summary>
        [SerializeField] TextMeshProUGUI leaderRewardLabel;
        [SerializeField] UiButton leaderCloseButton;
        [SerializeField] UiButton leaderGoButton;
        /// <summary>Rounded 8px plate behind each companion mini.</summary>
        [SerializeField] Sprite miniSprite;

        [Header("HUD")]
        [SerializeField] GameObject hudRoot;
        /// <summary>The dark plate, so the arena camera knows how much of the top it loses.</summary>
        [SerializeField] RectTransform hudRect;
        [SerializeField] TextMeshProUGUI timeLabel;
        [SerializeField] TextMeshProUGUI killLabel;
        [SerializeField] UiButton pauseButton;
        [SerializeField] TextMeshProUGUI pauseButtonLabel;
        [SerializeField] UiButton exitButton;
        /// <summary>"Lv.3 · 버틴 시간 · 다음 2:00" - the level is part of the caption, not a pill.</summary>
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] RectTransform xpFill;
        [SerializeField] RectTransform hpFill;
        /// <summary>Durability is an LED count, not "74 / 100".</summary>
        [SerializeField] TextMeshProUGUI hpLabel;

        [Header("팀")]
        [SerializeField] RectTransform teamRoot;
        /// <summary>Bottom band, so the arena camera knows how much of the bottom it loses.</summary>
        [SerializeField] RectTransform teamRect;
        [SerializeField] TextMeshProUGUI hintLabel;
        [SerializeField] TMP_FontAsset bodyFont;
        /// <summary>Slot art is handed in: the generator that makes it is editor-only.</summary>
        [SerializeField] Sprite slotSprite;
        [SerializeField] Sprite slotOutlineSprite;

        [Header("보스")]
        [SerializeField] GameObject bossRoot;
        [SerializeField] TextMeshProUGUI bossNameLabel;
        [SerializeField] RectTransform bossHpFill;

        [Header("일시정지")]
        [SerializeField] GameObject pauseOverlay;
        [SerializeField] UiButton pauseResumeButton;
        [SerializeField] UiButton pauseExitButton;

        [Header("레벨업")]
        [SerializeField] GameObject optionRoot;
        [SerializeField] RectTransform optionListRoot;
        [SerializeField] SurvivorOptionCard optionCardPrefab;

        [Header("결과")]
        [SerializeField] SurvivorResultScreen result;

        [SerializeField] SpriteLibrary sprites;

        GameServices _svc;
        SurvivorSim _sim;
        PresentationBus _bus;
        string _lastLeaderId;
        string _pickLeader;
        readonly System.Collections.Generic.List<string> _leaders = new();
        readonly System.Collections.Generic.List<UnitCard> _leaderCards = new();
        bool _paused;
        bool _cardsShown;
        bool _settled;
        Vector2 _dragOrigin;
        bool _dragging;

        const float SimStep = 1f / 60f;
        float _accumulator;

        /// <summary>Raised when the player returns to the lobby.</summary>
        public event System.Action Exited;

        void Awake()
        {
            if (pauseButton != null) pauseButton.SetAction(TogglePause);
            if (pauseResumeButton != null) pauseResumeButton.SetAction(TogglePause);
            if (pauseExitButton != null) pauseExitButton.SetAction(QuitRun);
            if (leaderGoButton != null) leaderGoButton.SetAction(() => StartRun(_pickLeader));
            if (leaderCloseButton != null)
                leaderCloseButton.SetAction(() =>
                {
                    if (leaderSelectRoot != null) leaderSelectRoot.SetActive(false);
                    Exited?.Invoke();
                });
            if (result != null)
            {
                result.HomeRequested += () => { result.gameObject.SetActive(false); Exited?.Invoke(); };
                result.RetryRequested += () => { result.gameObject.SetActive(false); StartRun(_lastLeaderId); };
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Opens the leader-select panel; the run itself starts once one is tapped.</summary>
        public void Begin(GameServices services)
        {
            _svc = services;
            if (hudRoot != null) hudRoot.SetActive(false);
            if (bossRoot != null) bossRoot.SetActive(false);
            if (optionRoot != null) optionRoot.SetActive(false);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            if (result != null) result.gameObject.SetActive(false);
            BuildLeaderSelect();
            if (leaderSelectRoot != null) leaderSelectRoot.SetActive(true);
            // 리더를 고르는 동안에는 아레나를 그리지 않는다. 원본은 로비 위에 시트를
            // 띄울 뿐이다. 켜 두면 아직 뷰포트를 잡기 전이라 바닥이 **화면 전체**를 덮는다.
            if (arenaCamera != null) arenaCamera.enabled = false;
        }

        /// <summary>
        /// 원본 `openSurvPick`: 리더 칸을 누르면 시트가 **다시 그려질 뿐** 출발하지 않는다.
        /// 고른 리더의 스킬 설명·동료 후보·오늘 보상 줄이 같이 바뀌고, 출동 버튼이 시작한다.
        /// </summary>
        void BuildLeaderSelect()
        {
            if (leaderListRoot == null || leaderCardPrefab == null) return;
            _leaders.Clear();
            foreach (var id in SurvivorSim.Leaders(_svc.Db))
                if (_svc.Profile.Data.Own(id) != null) _leaders.Add(id);

            // 원본은 저장된 리더를 먼저 쓰고, 없으면 첫 번째다.
            string saved = _svc.Profile.Data.surv != null ? _svc.Profile.Data.surv.leader : null;
            _pickLeader = !string.IsNullOrEmpty(saved) && _leaders.Contains(saved)
                ? saved
                : _leaders.Count > 0 ? _leaders[0] : null;

            for (int i = leaderListRoot.childCount - 1; i >= 0; i--)
                Destroy(leaderListRoot.GetChild(i).gameObject);
            _leaderCards.Clear();

            foreach (var id in _leaders)
            {
                if (!_svc.Db.Units.TryGetValue(id, out var unit)) continue;
                var card = Instantiate(leaderCardPrefab, leaderListRoot);
                card.Bind(unit, sprites);
                card.Tapped += u => PickLeader(u.Id);
                _leaderCards.Add(card);
            }
            PickLeader(_pickLeader);
        }

        void PickLeader(string id)
        {
            if (!string.IsNullOrEmpty(id)) _pickLeader = id;

            foreach (var card in _leaderCards)
            {
                if (card == null || card.Unit == null) continue;
                // 원본 `unitTile(t, t===sel?{tag:'리더'}:null)` — 고르지 않은 칸은
                // 편성 표시도 달지 않는다. 빈 문자열이 "표시 없음" 이다.
                card.TagOverride = card.Unit.Id == _pickLeader ? "리더" : "";
                card.Sync(_svc.Profile);
            }

            if (leaderSkillName != null)
                leaderSkillName.text = _pickLeader != null
                    ? _svc.Db.Text($"surv.leader.{_pickLeader}.name") : string.Empty;
            if (leaderSkillDesc != null)
                leaderSkillDesc.text = _pickLeader != null
                    ? SurvivorSim.FillDesc(_svc.Db.Text($"surv.leader.{_pickLeader}.desc"),
                                           _svc.Db.SurvParamsOf(_pickLeader, "leader"))
                    : string.Empty;

            BuildCompanionMinis();

            if (leaderRewardLabel != null)
            {
                var cfg = _svc.Db.Config;
                var (pts, max) = _svc.Profile.SurvivorPoolToday();
                leaderRewardLabel.text =
                    $"청소 일지 <b>{pts}/{max}</b> · 1분 +{cfg.poolSurvPerMin}   " +
                    $"보상 판 <b>{_svc.Profile.SurvivorRunsLeft()}/{cfg.survDailyRewards}</b>\n" +
                    $"30초마다 부품 {cfg.survPartPer30s} · 처치당 코인 {cfg.survCoinPerKill}";
            }

            if (leaderGoButton != null)
                leaderGoButton.State = _pickLeader != null ? UiButtonState.Normal : UiButtonState.Locked;
        }

        /// <summary>`#svComps` — the deck without the leader, or a note when it is empty.</summary>
        void BuildCompanionMinis()
        {
            if (leaderCompRoot == null) return;
            for (int i = leaderCompRoot.childCount - 1; i >= 0; i--)
                Destroy(leaderCompRoot.GetChild(i).gameObject);

            int shown = 0;
            foreach (var id in _svc.Profile.Data.deck)
            {
                if (string.IsNullOrEmpty(id) || id == _pickLeader) continue;
                if (!_svc.Db.Units.TryGetValue(id, out var unit)) continue;

                var cell = new GameObject(id, typeof(RectTransform));
                var cellRt = (RectTransform)cell.transform;
                cellRt.SetParent(leaderCompRoot, false);
                cellRt.sizeDelta = new Vector2(UiTheme.Sq(34f), UiTheme.Sq(34f));
                var plate = cell.AddComponent<Image>();
                plate.sprite = miniSprite;
                plate.type = Image.Type.Sliced;
                plate.color = UiTheme.RarityBg(UiTheme.Tier(unit.Rarity));
                plate.raycastTarget = false;

                var art = new GameObject("Icon", typeof(RectTransform));
                var artRt = (RectTransform)art.transform;
                artRt.SetParent(cellRt, false);
                artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 0.5f);
                artRt.sizeDelta = new Vector2(UiTheme.Sq(28f), UiTheme.Sq(28f));
                var icon = art.AddComponent<Image>();
                icon.sprite = sprites != null ? sprites.Unit(id) : null;
                icon.enabled = icon.sprite != null;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                shown++;
            }

            if (leaderCompEmpty != null) leaderCompEmpty.gameObject.SetActive(shown == 0);
        }

        void StartRun(string leaderId)
        {
            if (string.IsNullOrEmpty(leaderId)) return;
            _lastLeaderId = leaderId;
            _bus = new PresentationBus();
            _settled = false;
            _paused = false;
            _cardsShown = false;
            _accumulator = 0f;
            _dragging = false;

            _sim = new SurvivorSim(_svc.Db, _svc.Profile, leaderId, _svc.Profile.Data.deck, _svc.Rng, _bus);

            if (leaderSelectRoot != null) leaderSelectRoot.SetActive(false);
            if (optionRoot != null) optionRoot.SetActive(false);
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            if (hudRoot != null) hudRoot.SetActive(true);
            if (bossRoot != null) bossRoot.SetActive(false);

            view.Build(_sim.Arena);
            fx.ClearAll();
            if (arenaCamera != null) arenaCamera.enabled = true;
            FrameArena();
            MoveCameraTo(_sim.Leader.X, _sim.Leader.Y);
        }

        void QuitRun()
        {
            if (_sim == null) return;
            if (pauseOverlay != null) pauseOverlay.SetActive(false);
            _sim.Quit();
        }

        void TogglePause()
        {
            if (_sim == null || _sim.Outcome != SurvOutcome.Running) return;
            _paused = !_paused;
            if (pauseOverlay != null) pauseOverlay.SetActive(_paused);
            if (pauseButton != null) pauseButton.Text = _paused ? "▶" : "⏸";
        }

        // ------------------------------------------------------------------ frame loop

        /// <summary>

        /// 이 화면이 보이는 동안 자기 카메라를 위로 올린다.

        ///

        /// 전투판과 대청소판은 각자 카메라를 들고 **둘 다 켜져 있다.** 둘 다 깊이 0 에

        /// 배경까지 칠하므로 누가 나중에 그려질지 정해지지 않는다 — 실제로 전투

        /// 카메라가 대청소를 덮어 빈 화면이 됐다. 카메라를 끄면 Camera.main 이

        /// null 이 되어 캡처가 통째로 비므로, 깊이만 바꾼다.

        /// </summary>

        /// <summary>
        /// 원본 `#svCv` 는 화면 전체가 아니라 16,135 에 놓인 398x486 패널이다. 카메라의
        /// 뷰포트를 그 자리에만 준다 — 전에는 화면 전체를 썼다.
        /// </summary>
        /// <summary>
        /// 화면 내용은 가운데 886px 기둥에 담기므로, 카메라 뷰포트도 그 기둥 기준으로 잡는다.
        /// 화면 전체(1080) 기준으로 잡으면 아레나가 기둥 밖으로 밀린다.
        /// </summary>
        static Rect ArenaViewport => UiTheme.PageViewport(16f, 135f, 398f, 486f);

        /// <summary>원본 `survRender` 의 `k = SW/8` — 패널 가로에 아레나 8칸이 들어간다.</summary>
        const float ArenaTilesAcross = 8f;

        /// <summary>
        /// 아레나 패널을 잡는다. 세로로 몇 칸이 보이는지는 뷰포트 비율이 정한다 —
        /// 원본은 398x486 이라 9.77칸이지만 우리 기준 칸(가로 2.512 / 세로 2.060)에서는
        /// 패널이 거의 정사각이라 8칸쯤 보인다. **가로 8칸**을 맞추는 쪽을 택했다.
        /// 그래야 가전과 먼지가 바닥 격자에 대해 원본과 같은 크기로 보인다.
        /// </summary>
        void FrameArena()
        {
            if (arenaCamera == null) return;
            arenaCamera.rect = ArenaViewport;
            float aspect = Mathf.Max(0.01f, arenaCamera.aspect);
            arenaCamera.orthographicSize = ArenaTilesAcross / (2f * aspect);
        }

        /// <summary>
        /// 아레나는 리더를 따라가되 **벽 밖으로는 나가지 않는다** — 원본 `survRender` 가
        /// 보는 자리를 `0 ~ A-vw` 로 자르는 것과 같다. 자르지 않으면 가장자리에서
        /// 벽 너머 빈 공간이 화면 절반을 차지한다.
        ///
        /// 카메라 좌표를 직접 쓰지 않고 <see cref="FxDirector.CameraHome"/> 를 옮기는 이유는,
        /// 흔들기가 매 프레임 자기 자리로 카메라를 되돌리기 때문이다 — 둘이 같이 쓰면
        /// 늦게 도는 쪽이 이겨 화면이 원점에 멈춘다(실제로 빈 화면이었다).
        /// </summary>
        void MoveCameraTo(float simX, float simY)
        {
            if (arenaCamera == null || _sim == null) return;

            float vh = 2f * arenaCamera.orthographicSize;
            float vw = vh * arenaCamera.aspect;
            float a = _sim.Arena;

            float cx = a > vw ? Mathf.Clamp(simX - vw / 2f, 0f, a - vw) : (a - vw) / 2f;
            float cy = a > vh ? Mathf.Clamp(simY - vh / 2f, 0f, a - vh) : (a - vh) / 2f;

            var p = SurvivorView.ToWorld(cx + vw / 2f, cy + vh / 2f);
            var here = arenaCamera.transform.localPosition;
            var home = new Vector3(p.x, p.y, here.z);
            if (fx != null) fx.CameraHome = home;
            else arenaCamera.transform.localPosition = home;
        }

        void OnEnable()
        {
            if (arenaCamera != null) arenaCamera.depth = 10f;
        }

        void OnDisable()
        {
            if (arenaCamera != null)
            {
                arenaCamera.depth = 0f;
                arenaCamera.enabled = false;
            }
        }


        void Update()
        {
            if (_sim == null) return;

            float dt = Time.unscaledDeltaTime;

            if (_sim.Outcome == SurvOutcome.Running)
            {
                if (_sim.PendingLevelUps > 0 && !_cardsShown) ShowLevelUp();

                bool frozen = _paused || _cardsShown;
                if (!frozen)
                {
                    _accumulator += Mathf.Min(dt, 0.05f);
                    var move = ReadMove();
                    while (_accumulator >= SimStep)
                    {
                        _accumulator -= SimStep;
                        _sim.Tick(SimStep, new Vec2(move.x, move.y));
                        if (_sim.Outcome != SurvOutcome.Running) break;
                    }
                }
            }

            MoveCameraTo(_sim.Leader.X, _sim.Leader.Y);

            fx.Consume(_bus, Vector2.zero, 1f);
            _bus.Clear();
            view.Sync(_sim);
            SyncHud();

            if (_sim.Outcome != SurvOutcome.Running && !_settled) Settle();
        }

        /// <summary>WASD/arrows first, then a drag-to-move joystick anchored where the touch/click started.</summary>
        Vector2 ReadMove()
        {
            var k = Keyboard.current;
            if (k != null)
            {
                float kx = 0f, ky = 0f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) kx -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) kx += 1f;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) ky -= 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) ky += 1f;
                if (kx != 0f || ky != 0f) { _dragging = false; return new Vector2(kx, ky); }
            }

            bool held;
            bool pressedNow;
            Vector2 pos;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                held = true;
                pressedNow = Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                held = true;
                pressedNow = Mouse.current.leftButton.wasPressedThisFrame;
                pos = Mouse.current.position.ReadValue();
            }
            else { held = false; pressedNow = false; pos = default; }

            if (!held) { _dragging = false; return Vector2.zero; }

            if (pressedNow)
            {
                if (UnityEngine.EventSystems.EventSystem.current != null &&
                    UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    return Vector2.zero;
                _dragging = true;
                _dragOrigin = pos;
            }
            if (!_dragging) return Vector2.zero;

            const float deadzone = 10f, range = 70f;
            Vector2 delta = pos - _dragOrigin;
            float len = delta.magnitude;
            if (len < deadzone) return Vector2.zero;
            Vector2 dir = delta / len;
            float mag = Mathf.Clamp01((len - deadzone) / range);
            // Screen Y is up; the sim's Y (and the arena camera) treats down as positive.
            return new Vector2(dir.x, -dir.y) * mag;
        }

        // ------------------------------------------------------------------ HUD

        void SyncHud()
        {
            if (timeLabel != null) timeLabel.text = SurvivorSim.Mmss(_sim.Time);
            if (killLabel != null)
            {
                killLabel.text = _sim.Kills.ToString("N0");
                UiTheme.FitToText(killLabel.transform.parent.GetComponent<LayoutElement>(),
                                  killLabel, UiTheme.Px(34f));
            }

            // The level, what the clock means, and when the next payout lands all share one
            // caption line - 10.16-n has no level pill and no separate milestone row.
            // 원본 `survSync`: `Lv.N · 버틴 시간 · 다음 M:SS` — 다음 **분**까지 남은 시간이지
            // 일지 점수 안내가 아니다.
            if (levelLabel != null)
                levelLabel.text = $"Lv.{_sim.Level} · 버틴 시간 · 다음 "
                                + SurvivorSim.Mmss((Mathf.Floor(_sim.Time / 60f) + 1f) * 60f);
            SetFill(xpFill, _sim.XpNeeded > 0f ? _sim.Xp / _sim.XpNeeded : 0f);

            var leader = _sim.Leader;
            SetFill(hpFill, leader.MaxHp > 0f ? leader.Hp / leader.MaxHp : 0f);
            if (hpLabel != null)
            {
                hpLabel.text = Mathf.CeilToInt(leader.Hp).ToString();
                UiTheme.FitToText(hpLabel.transform.parent.GetComponent<LayoutElement>(),
                                  hpLabel, UiTheme.Px(30f));
                // Below 30% it goes danger-red, the same threshold the build uses.
                hpLabel.color = leader.Hp < leader.MaxHp * 0.3f ? UiTheme.Danger : UiTheme.LedLife;
            }

            SyncTeam();

            bool bossActive = _sim.Boss != null;
            if (bossRoot != null) bossRoot.SetActive(bossActive);
            if (bossActive)
            {
                if (bossNameLabel != null) bossNameLabel.text = _sim.Boss.Def?.Name ?? "";
                SetFill(bossHpFill, _sim.Boss.MaxHp > 0f ? _sim.Boss.Hp / _sim.Boss.MaxHp : 0f);
            }
        }

        /// <summary>
        /// The five-slot team strip: the leader, then one tile per companion, then dashed
        /// empties for the rest. 10.16-n rebuilds this whenever the roster changes rather
        /// than keeping fixed widgets, because the slot count comes from config.
        /// </summary>
        void SyncTeam()
        {
            if (teamRoot == null) return;
            int slots = 1 + _svc.Db.Config.survMaxComp;
            if (_teamSlots.Count != slots)
            {
                foreach (var t in _teamSlots) if (t.Root != null) Destroy(t.Root);
                _teamSlots.Clear();
                for (int i = 0; i < slots; i++) _teamSlots.Add(BuildTeamSlot(i == 0));
            }

            var leader = _sim.Leader;
            _teamSlots[0].Fill(sprites, leader.Type, "리더 " + _sim.Level);
            for (int i = 1; i < _teamSlots.Count; i++)
            {
                var c = i - 1 < _sim.Companions.Count ? _sim.Companions[i - 1] : null;
                if (c == null) _teamSlots[i].Clear();
                else _teamSlots[i].Fill(sprites, c.Type, _svc.Db.Unit(c.Type).ShortName + " " + c.Level);
            }
        }

        readonly System.Collections.Generic.List<TeamSlot> _teamSlots = new();

        /// <summary>One tile in the team strip: a portrait over a 10pt caption.</summary>
        sealed class TeamSlot
        {
            public GameObject Root;
            public Image Plate;
            public Image Portrait;
            public TextMeshProUGUI Label;

            public void Fill(SpriteLibrary sprites, string unitId, string caption)
            {
                if (Plate != null) { Plate.enabled = true; Plate.color = UiTheme.Panel2; }
                if (Portrait != null)
                {
                    Portrait.sprite = sprites != null ? sprites.Unit(unitId) : null;
                    Portrait.enabled = Portrait.sprite != null;
                }
                if (Label != null) { Label.text = caption; Label.color = UiTheme.Ink; }
            }

            public void Clear()
            {
                if (Plate != null) Plate.enabled = false;
                if (Portrait != null) Portrait.enabled = false;
                if (Label != null) { Label.text = "빈 자리"; Label.color = UiTheme.Ink2; }
            }
        }

        TeamSlot BuildTeamSlot(bool leader)
        {
            var go = new GameObject(leader ? "Leader" : "Slot", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(teamRoot, false);
            rt.sizeDelta = new Vector2(UiTheme.Px(58f), UiTheme.Py(57f));

            var plate = go.AddComponent<Image>();
            plate.sprite = slotSprite;
            plate.type = Image.Type.Sliced;
            plate.color = UiTheme.Panel2;
            plate.raycastTarget = false;

            // Empty slots still show their outline, so the dashed ring is its own image that
            // stays on while the filled plate switches off.
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.SetParent(rt, false);
            ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero; ringRt.offsetMax = Vector2.zero;
            var ring = ringGo.AddComponent<Image>();
            ring.sprite = slotOutlineSprite;
            ring.type = Image.Type.Sliced;
            ring.color = UiTheme.PanelLine;
            ring.raycastTarget = false;

            var picGo = new GameObject("Portrait", typeof(RectTransform));
            var picRt = (RectTransform)picGo.transform;
            picRt.SetParent(rt, false);
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 1f);
            picRt.pivot = new Vector2(0.5f, 1f);
            picRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(3f));
            picRt.sizeDelta = new Vector2(UiTheme.Px(30f), UiTheme.Px(30f));
            var pic = picGo.AddComponent<Image>();
            pic.preserveAspect = true;
            pic.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 0f);
            labelRt.offsetMin = new Vector2(UiTheme.Px(2f), UiTheme.Py(4f));
            // 10pt needs ~22px of box; the build's 15px row would make TMP drop the line.
            labelRt.offsetMax = new Vector2(-UiTheme.Px(2f), UiTheme.Py(4f) + UiTheme.Py(22f));
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = bodyFont;
            label.fontSize = UiTheme.Py(10f);
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.color = UiTheme.Ink2;
            label.text = "빈 자리";

            return new TeamSlot { Root = go, Plate = plate, Portrait = pic, Label = label };
        }

        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
        }

        // ------------------------------------------------------------------ level-up

        void ShowLevelUp()
        {
            _cardsShown = true;
            var options = _sim.RollOptions();
            BuildOptionCards(options);
            if (optionRoot != null) optionRoot.SetActive(true);
        }

        void BuildOptionCards(System.Collections.Generic.List<SurvOption> options)
        {
            if (optionListRoot == null || optionCardPrefab == null) return;
            for (int i = optionListRoot.childCount - 1; i >= 0; i--)
                Destroy(optionListRoot.GetChild(i).gameObject);

            foreach (var o in options)
            {
                var view = _sim.Describe(o);
                var card = Instantiate(optionCardPrefab, optionListRoot);
                card.Bind(view, sprites, () => PickOption(o));
            }
        }

        void PickOption(SurvOption o)
        {
            _sim.Apply(o);
            if (optionRoot != null) optionRoot.SetActive(false);
            _cardsShown = false;
        }

        // ------------------------------------------------------------------ settlement

        void Settle()
        {
            _settled = true;
            var r = _sim.Settle();
            _svc.Persist();
            if (hudRoot != null) hudRoot.SetActive(false);
            if (bossRoot != null) bossRoot.SetActive(false);
            if (result != null) result.Show(_svc, r);
        }
    }
}
