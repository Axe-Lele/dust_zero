// The lobby shell: wallet, side rail, the five-tab bar and the panel each tab shows.
//
// Redesign 1a consolidates the original's seven bottom menus into five tabs; nothing is
// dropped, the extra two become sub-tabs inside 스테이지 and 가전. This class owns navigation
// and the surfaces shared by every tab (wallet, toast, settings). Tab contents live in their
// own components so none of them grows into another monolith.
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>The five bottom-bar destinations, in bar order.</summary>
    /// <summary>
    /// The seven tabs 10.16-n puts along the bottom, in its order. 콘텐츠 is the entry to the
    /// endless mode and 편성 is the deck editor; both used to hang off other screens.
    /// </summary>
    public enum LobbyTab { Home, Stage, Content, Units, Deck, Gacha, Shop }

    public sealed class LobbyScreen : MonoBehaviour
    {
        [Header("상단")]
        [SerializeField] TextMeshProUGUI coinLabel;
        [SerializeField] TextMeshProUGUI crystalLabel;
        /// <summary>The page heading under the bar; 홈 leaves it blank.</summary>
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] SpriteLibrary sprites;

        [Header("탭")]
        /// <summary>One per <see cref="LobbyTab"/>, in the same order.</summary>
        [SerializeField] UiButton[] tabButtons;
        [SerializeField] GameObject[] tabPanels;
        /// <summary>The short bar the mockup hangs under the selected tab.</summary>
        [SerializeField] GameObject[] tabIndicators;

        [Header("콘텐츠 탭")]
        [SerializeField] ContentTab content;

        [Header("탭 내용")]
        [SerializeField] HomeTab home;
        [SerializeField] StageTab stage;
        [SerializeField] UnitsTab units;
        [SerializeField] DeckTab deck;
        [SerializeField] GachaTab gacha;
        [SerializeField] ShopTab shop;

        [Header("전체 화면")]
        [SerializeField] UnitDetailScreen unitDetail;
        [SerializeField] ArtViewerScreen artViewer;
        [SerializeField] GearBagScreen gearBag;
        [SerializeField] PassScreen pass;
        [SerializeField] AttendanceScreen attendance;
        [SerializeField] CoopPickScreen coopPick;

        [Header("홈 클러스터")]
        [SerializeField] UiButton attendanceButton;
        /// <summary>The orange status line under 출석부: which day is waiting.</summary>
        [SerializeField] TextMeshProUGUI attendanceSubLabel;
        [SerializeField] UiButton passButton;
        [SerializeField] TextMeshProUGUI passBadgeLabel;
        [SerializeField] TextMeshProUGUI passSubLabel;
        [SerializeField] UiButton settingsButton;
        /// <summary>10.16-n keeps a reset shortcut on the home screen beside 설정.</summary>
        [SerializeField] UiButton homeResetButton;
        [SerializeField] GameObject attendanceDot;
        /// <summary>자동 청소 lives beside 출석부 in 10.16-n, not in a card on the room.</summary>
        [SerializeField] UiButton idleButton;
        [SerializeField] TextMeshProUGUI idleSubLabel;
        [SerializeField] RectTransform idleFill;
        /// <summary>The rail belongs to 홈; the other tabs need the width for their own content.</summary>
        [SerializeField] GameObject railRoot;

        [Header("설정 팝업")]
        [SerializeField] GameObject settingsRoot;
        [SerializeField] UiButton settingsCloseButton;
        [SerializeField] UiButton tutorialReplayButton;
        [SerializeField] UiButton resetButton;
        [SerializeField] TextMeshProUGUI buildLabel;
        [SerializeField] TextMeshProUGUI bgmValueLabel;
        [SerializeField] UiButton bgmDownButton;
        [SerializeField] UiButton bgmUpButton;
        [SerializeField] UiButton bgmMuteButton;

        [Header("알림")]
        [SerializeField] GameObject toastRoot;
        [SerializeField] TextMeshProUGUI toastLabel;

        GameServices _svc;
        LobbyTab _tab = LobbyTab.Home;
        float _resetArmedAt = -99f;
        float _toastUntil;

        /// <summary>Raised with the stage the player chose, from whichever tab offered it.</summary>
        public event System.Action<StageDef> StageChosen;
        /// <summary>Raised when the player taps 대청소 from the home CTA.</summary>
        public event System.Action SurvivorChosen;
        /// <summary>협동 출동. 고른 파트너 id 를 나른다.</summary>
        public event System.Action<string> CoopChosen;

        /// <summary>가전 대청소 리그로 출동. 콘텐츠 탭의 리그 카드가 올린다.</summary>
        public event System.Action LeagueChosen;

        public LobbyTab CurrentTab => _tab;

        void Awake()
        {
            if (tabButtons != null)
                for (int i = 0; i < tabButtons.Length; i++)
                {
                    var which = (LobbyTab)i;
                    if (tabButtons[i] != null) tabButtons[i].SetAction(() => ShowTab(which));
                }

            if (attendanceButton != null) attendanceButton.SetAction(OpenAttendance);
            if (passButton != null) passButton.SetAction(OpenPass);
            if (idleButton != null) idleButton.SetAction(ClaimIdle);
            if (homeResetButton != null) homeResetButton.SetAction(OnResetPressed);
            if (settingsButton != null) settingsButton.SetAction(() => ToggleSettings(true));
            if (settingsCloseButton != null) settingsCloseButton.SetAction(() => ToggleSettings(false));
            if (tutorialReplayButton != null) tutorialReplayButton.SetAction(ReplayTutorial);
            if (resetButton != null) resetButton.SetAction(OnResetPressed);
            if (content != null)
            {
                content.SurvivorChosen += () => SurvivorChosen?.Invoke();
                content.CoopChosen += OpenCoopPick;
                content.LeagueChosen += () => LeagueChosen?.Invoke();
                content.Claimed += msg => { Toast(msg); Refresh(); };
            }
            if (bgmDownButton != null) bgmDownButton.SetAction(() => StepBgm(-10));
            if (bgmUpButton != null) bgmUpButton.SetAction(() => StepBgm(10));
            if (bgmMuteButton != null) bgmMuteButton.SetAction(ToggleMute);
        }

        public void Bind(GameServices services)
        {
            _svc = services;

            if (home != null)
            {
                home.Bind(services);
                home.StageChosen += OnStageChosen;
                home.SurvivorChosen += () => SurvivorChosen?.Invoke();
                home.Claimed += msg => { Toast(msg); Refresh(); };
            }
            if (content != null) content.Bind(services);
            if (stage != null)
            {
                stage.Bind(services);
                stage.StageChosen += OnStageChosen;
                // 변경 on the sortie strip jumps to the deck tab rather than opening a
                // second editor, which is what the build does.
                stage.DeckRequested += () => ShowTab(LobbyTab.Deck);
            }
            if (units != null)
            {
                units.Bind(services);
                units.Message += Toast;
                units.DetailRequested += OpenUnitDetail;
                // Both shortcuts open screens that are not built yet; say so rather than
                // letting the button look broken.
                units.GalleryRequested += OpenArtViewer;
                units.GearBagRequested += OpenGearBag;
            }
            if (deck != null)
            {
                deck.Bind(services);
                deck.Message += Toast;
            }
            if (gacha != null)
            {
                gacha.Bind(services);
                gacha.Message += Toast;
                gacha.Pulled += OnPulled;
            }
            if (shop != null)
            {
                shop.Bind(services);
                shop.Message += msg => { Toast(msg); Refresh(); };
            }
            if (gearBag != null)
            {
                // Closing repaints the catalogue: equipping or dismantling changes what the
                // cards show, so a stale grid would be the first thing the player sees.
                gearBag.Closed += OnDetailClosed;
                gearBag.Message += Toast;
            }
            if (artViewer != null)
            {
                // Closing the viewer repaints the catalogue: a limit-break taken from the
                // detail screen it hands off to would otherwise leave stale cards behind.
                artViewer.Closed += OnDetailClosed;
                artViewer.DetailRequested += OpenUnitDetail;
            }
            if (unitDetail != null)
            {
                unitDetail.Message += Toast;
                unitDetail.Closed += OnDetailClosed;
            }
            if (pass != null)
            {
                pass.Message += Toast;
                pass.Closed += () => { Refresh(); };
            }
            if (coopPick != null)
            {
                coopPick.Closed += Refresh;
                coopPick.Chosen += id => CoopChosen?.Invoke(id);
            }

            if (attendance != null)
            {
                attendance.Message += Toast;
                attendance.Closed += () => { Refresh(); };
            }

            if (buildLabel != null)
                buildLabel.text = $"Unity 이식 빌드 {Application.version} · 원본 10.15-h";

            ToggleSettings(false);
            ShowTab(LobbyTab.Home);
            Refresh();
        }

        void OnStageChosen(StageDef s) => StageChosen?.Invoke(s);

        /// <summary>Opens 2b on one appliance, with the roster order behind its arrows.</summary>
        /// <summary>
        /// 일러스트 opens the viewer on the first appliance the player owns, the way the build
        /// does - landing on a silhouette would be a poor first impression of the gallery.
        /// </summary>
        void OpenArtViewer()
        {
            if (artViewer == null) return;
            var order = new List<UnitDef>(_svc.Db.Units.Values);
            order.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (order.Count == 0) return;
            var first = order.Find(u => _svc.Profile.Data.Own(u.Id) != null) ?? order[0];
            artViewer.Open(_svc, first, order);
        }

        void OpenGearBag()
        {
            if (gearBag == null) return;
            gearBag.Open(_svc);
        }

        void OpenUnitDetail(UnitDef unit)
        {
            if (unitDetail == null) return;
            var order = new List<UnitDef>(_svc.Db.Units.Values);
            order.Sort((a, b) => a.Order.CompareTo(b.Order));
            unitDetail.Open(_svc, unit, order);
            if (unit != null) _svc.Guide?.ReportReached("roster.unit:" + unit.Id);
        }

        /// <summary>
        /// Resolves one of the lobby guide's spotlight targets to the RectTransform it names,
        /// or null if this screen does not own that element right now (wrong tab, detail
        /// closed, etc). AppRoot polls this each frame while a guide step is active.
        /// </summary>
        public RectTransform ResolveGuideTarget(string key)
        {
            if (key == "home.cta")
                return home != null && home.gameObject.activeInHierarchy ? home.NextStageTarget : null;
            if (key == "tab.roster")
                return tabButtons != null && tabButtons.Length > (int)LobbyTab.Units
                    ? (RectTransform)tabButtons[(int)LobbyTab.Units].transform : null;
            if (key == "tab.home")
                return tabButtons != null && tabButtons.Length > (int)LobbyTab.Home
                    ? (RectTransform)tabButtons[(int)LobbyTab.Home].transform : null;
            if (key == "detail.levelup" || key == "detail.close")
                return unitDetail != null && unitDetail.gameObject.activeInHierarchy
                    ? unitDetail.ResolveGuideTarget(key) : null;
            if (key != null && key.StartsWith("roster.unit:") && units != null && units.gameObject.activeInHierarchy)
                return units.CardFor(key.Substring("roster.unit:".Length));
            return null;
        }

        void OnDetailClosed()
        {
            if (units != null) units.Refresh();
            Refresh();
        }

        void OpenPass()
        {
            if (pass == null) { Toast("시즌 패스 화면이 없어요."); return; }
            pass.Open(_svc);
        }

        /// <summary>
        /// 파트너 고르기를 띄운다. 잠겨 있으면 안내만 하고 열지 않는다 —
        /// 원본 `coopEnter` 가 `coopOn()` 을 먼저 보는 것과 같다.
        /// </summary>
        void OpenCoopPick()
        {
            if (!_svc.Profile.CoopUnlocked) { Toast(_svc.Profile.CoopLockMessage); return; }
            if (_svc.Profile.Data.deck.Count == 0) { Toast("먼저 편성을 짜주세요."); return; }
            if (coopPick == null) { Toast("협동 화면이 없어요."); return; }
            coopPick.Open(_svc);
        }

        void OpenAttendance()
        {
            if (attendance == null) { Toast("출석부 화면이 없어요."); return; }
            attendance.Open(_svc);
        }

        void OnPulled(List<(string unitId, Core.Meta.DuplicateResult dup)> got)
        {
            if (got == null || got.Count == 0) return;
            var names = new List<string>();
            foreach (var (id, dup) in got)
            {
                var name = _svc.Db.Units[id].Name;
                names.Add(dup == Core.Meta.DuplicateResult.New ? $"{name} (신규)" : name);
            }
            Toast(string.Join(" · ", names));
            if (units != null) units.Refresh();
            Refresh();
        }

        void OnEnable()
        {
            if (_svc != null) Refresh();
        }

        void Update()
        {
            if (toastRoot != null && toastRoot.activeSelf && Time.unscaledTime > _toastUntil)
                toastRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>
        /// Switches tabs. Only the chosen panel stays active, so a tab that is not on screen
        /// costs nothing - the room simulation in particular stops ticking.
        /// </summary>
        public void ShowTab(LobbyTab which)
        {
            _tab = which;
            if (which == LobbyTab.Units) _svc?.Guide?.ReportReached("tab.roster");
            else if (which == LobbyTab.Home) _svc?.Guide?.ReportReached("tab.home");

            // Every tab owns a panel now that 편성 has its own. Hiding all of them before
            // showing one keeps this correct even if two entries ever point at one panel.
            int panel = (int)which;
            if (tabPanels != null)
            {
                foreach (var p in tabPanels)
                    if (p != null) p.SetActive(false);
                if (panel >= 0 && panel < tabPanels.Length && tabPanels[panel] != null)
                    tabPanels[panel].SetActive(true);
            }

            if (tabButtons != null)
                for (int i = 0; i < tabButtons.Length; i++)
                    if (tabButtons[i] != null)
                        tabButtons[i].State = i == (int)which ? UiButtonState.Selected : UiButtonState.Normal;

            if (tabIndicators != null)
                for (int i = 0; i < tabIndicators.Length; i++)
                    if (tabIndicators[i] != null) tabIndicators[i].SetActive(i == (int)which);

            if (which == LobbyTab.Content) RefreshContentTab();

            // The bar carries a fixed wordmark in 10.16-n, so the tab name is a heading on
            // the page below it, and 홈 shows no heading because the room fills that space.
            bool home = which == LobbyTab.Home;
            if (railRoot != null) railRoot.SetActive(home);
            // Each panel draws its own heading, the way the build gives every section its
            // own sec-head. Leaving this one on printed a second title over the panel's.
            if (titleLabel != null) titleLabel.gameObject.SetActive(false);
        }

        static string TabName(LobbyTab which) => which switch
        {
            LobbyTab.Stage => "스테이지",
            LobbyTab.Content => "콘텐츠",
            LobbyTab.Units => "가전",
            LobbyTab.Deck => "편성",
            LobbyTab.Gacha => "소환",
            LobbyTab.Shop => "상점",
            _ => "홈",
        };

        // ------------------------------------------------------------------ shared surfaces

        public void Refresh()
        {
            if (_svc == null) return;
            var p = _svc.Profile;

            if (coinLabel != null) coinLabel.text = UiTheme.Compact(p.Coin);
            if (crystalLabel != null) crystalLabel.text = UiTheme.Compact(p.Crystal);

            RefreshIdlePill();

            // The pills show "there is something here" with a dot and an orange status
            // line, not by greying out - that is how 10.16-n reads at a glance.
            bool canAttend = p.AttendanceAvailable;
            if (attendanceButton != null) attendanceButton.State = UiButtonState.Normal;
            if (attendanceDot != null) attendanceDot.SetActive(canAttend);
            if (attendanceSubLabel != null)
                attendanceSubLabel.text = canAttend
                    ? $"{p.AttendanceNextDay}일째 받기"
                    : $"{p.AttendanceTaken}일째 완료";

            if (passBadgeLabel != null) passBadgeLabel.text = $"Lv.{p.PassLevel}";
            if (passSubLabel != null)
            {
                int ready = p.PassClaimableCount();
                passSubLabel.text = ready > 0 ? $"받기 {ready}" : (p.PassPaid ? "유료" : "무료");
            }

            if (home != null && home.gameObject.activeInHierarchy) home.Refresh();
            if (stage != null && stage.gameObject.activeInHierarchy) stage.Refresh();
            if (units != null && units.gameObject.activeInHierarchy) units.Refresh();
            if (deck != null && deck.gameObject.activeInHierarchy) deck.Refresh();
            if (gacha != null && gacha.gameObject.activeInHierarchy) gacha.Refresh();
            if (shop != null && shop.gameObject.activeInHierarchy) shop.Refresh();
        }

        /// <summary>
        /// The 자동 청소 pill. 10.16-n shows the pending haul on the pill itself and claims on
        /// tap; the sheet the original opens for the detail is not built yet, so the tap
        /// collects directly rather than doing nothing.
        /// </summary>
        void RefreshIdlePill()
        {
            if (_svc == null) return;
            var p = _svc.Profile;
            var pending = p.IdlePendingNow();
            bool on = p.IdleUnlocked && pending != null;

            if (!on)
            {
                if (idleSubLabel != null) idleSubLabel.text = "잠김";
                SetFill(idleFill, 0f);
                if (idleButton != null) idleButton.State = UiButtonState.Locked;
                return;
            }

            var v = pending.Value;
            double hours = v.Hours, cap = v.Cap;
            if (idleSubLabel != null)
                idleSubLabel.text = v.Coin >= 1d
                    ? $"+{UiTheme.Compact(System.Math.Floor(v.Coin))}"
                    : "모으는 중";
            SetFill(idleFill, cap > 0d ? Mathf.Clamp01((float)(hours / cap)) : 0f);
            if (idleButton != null)
                idleButton.State = v.Coin >= 1d ? UiButtonState.Claimable : UiButtonState.Normal;
        }

        void ClaimIdle()
        {
            if (_svc == null) return;
            var got = _svc.Profile.ClaimIdle();
            if (got == null) { Toast("아직 모인 게 없어요."); return; }
            Toast($"자동 청소로 코인 {got.Value.coin:N0}, 부품 {got.Value.part:N0}, 크리스탈 {got.Value.crystal:N0} 모아왔어요!");
            home?.Room?.Sim?.Say("싹 쓸어 담았어요!");
            Refresh();
        }

        /// <summary>
        /// The 콘텐츠 card shows the same record and lock rule the home entry does, so the
        /// two entry points cannot disagree about whether 대청소 is open.
        /// </summary>
        void RefreshContentTab()
        {
            if (content != null) content.Refresh();
        }

        void ToggleSettings(bool on)
        {
            if (settingsRoot != null) settingsRoot.SetActive(on);
            if (on) RefreshAudioRow();
        }

        /// <summary>Raised after the player changes the volume or mute, so the music follows.</summary>
        public event System.Action AudioSettingsChanged;

        void StepBgm(int delta)
        {
            var o = _svc?.Profile?.Data?.opt;
            if (o == null) return;
            o.bgm = Mathf.Clamp(o.bgm + delta, 0, 100);
            // Nudging the volume up is a clear request to hear something.
            if (delta > 0 && o.bgm > 0) o.mute = 0;
            CommitAudioChange();
        }

        void ToggleMute()
        {
            var o = _svc?.Profile?.Data?.opt;
            if (o == null) return;
            o.mute = o.mute != 0 ? 0 : 1;
            CommitAudioChange();
        }

        void CommitAudioChange()
        {
            RefreshAudioRow();
            AudioSettingsChanged?.Invoke();
            _svc?.Persist();
        }

        void RefreshAudioRow()
        {
            var o = _svc?.Profile?.Data?.opt;
            if (o == null) return;
            if (bgmValueLabel != null)
                bgmValueLabel.text = o.mute != 0 ? $"배경음  음소거 ({o.bgm}%)" : $"배경음  {o.bgm}%";
            if (bgmMuteButton != null)
                bgmMuteButton.Text = o.mute != 0 ? "소리 켜기" : "음소거";
            if (bgmDownButton != null) bgmDownButton.State = o.bgm <= 0 ? UiButtonState.Disabled : UiButtonState.Normal;
            if (bgmUpButton != null) bgmUpButton.State = o.bgm >= 100 ? UiButtonState.Disabled : UiButtonState.Normal;
        }

        void ReplayTutorial()
        {
            var d = _svc.Profile.Data;
            d.tut.Clear();
            d.tutOff = 0;
            d.tutOpen = 0;
            _svc.Persist();
            Toast("다음 전투부터 튜토리얼이 다시 나와요.");
        }

        void OnResetPressed()
        {
            // Two presses within three seconds, like the original's reset button.
            if (Time.unscaledTime - _resetArmedAt < 3f)
            {
                _svc.ResetProfile();
                _resetArmedAt = -99f;
                if (resetButton != null) resetButton.Text = "게임 초기화";
                if (stage != null) stage.Bind(_svc);
                if (home != null) home.Refresh();
                Refresh();
                ToggleSettings(false);
                Toast("초기화했어요.");
                return;
            }
            _resetArmedAt = Time.unscaledTime;
            if (resetButton != null) resetButton.Text = "한 번 더 누르면 초기화";
            CancelInvoke(nameof(DisarmReset));
            Invoke(nameof(DisarmReset), 3f);
        }

        void DisarmReset()
        {
            if (resetButton != null) resetButton.Text = "게임 초기화";
        }

        public void Toast(string msg)
        {
            if (toastLabel == null || toastRoot == null) return;
            toastLabel.text = msg;
            toastRoot.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.6f;
        }

        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
        }
    }
}
