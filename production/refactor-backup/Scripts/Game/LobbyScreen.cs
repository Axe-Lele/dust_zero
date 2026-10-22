// Stage select, wallet, idle claim and attendance.
//
// This is the lobby surface the first-stage loop needs. Roster, deck editing, gacha and the
// detail popups are not wired here yet - their rules live in PlayerProfile and are covered by
// tests, but they have no screen. See PORTING-NOTES.md for the current state.
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class LobbyScreen : MonoBehaviour
    {
        [Header("상단")]
        [SerializeField] TextMeshProUGUI coinLabel;
        [SerializeField] TextMeshProUGUI crystalLabel;
        [SerializeField] TextMeshProUGUI repNameLabel;
        [SerializeField] Image repPortrait;
        [SerializeField] SpriteLibrary sprites;

        [Header("자동 청소")]
        [SerializeField] GameObject idleRoot;
        [SerializeField] TextMeshProUGUI idleSubLabel;
        [SerializeField] TextMeshProUGUI idleAmountLabel;
        [SerializeField] Image idleBar;
        [SerializeField] Button idleClaimButton;
        [SerializeField] Button idleTestButton;

        [Header("출석")]
        [SerializeField] Button attendanceButton;
        [SerializeField] TextMeshProUGUI attendanceLabel;

        [Header("스테이지")]
        [SerializeField] RectTransform stageListRoot;
        [SerializeField] StageRow stageRowPrefab;

        [Header("시스템")]
        [SerializeField] Button tutorialReplayButton;
        [SerializeField] Button resetButton;
        [SerializeField] TextMeshProUGUI resetLabel;
        [SerializeField] GameObject toastRoot;
        [SerializeField] TextMeshProUGUI toastLabel;
        [SerializeField] TextMeshProUGUI buildLabel;

        GameServices _svc;
        readonly List<StageRow> _rows = new();
        float _resetArmedAt = -99f;
        float _toastUntil;
        float _idleRefreshAt;

        /// <summary>Raised with the stage the player chose to play.</summary>
        public event System.Action<StageDef> StageChosen;

        void Awake()
        {
            if (idleClaimButton != null) idleClaimButton.onClick.AddListener(ClaimIdle);
            if (idleTestButton != null) idleTestButton.onClick.AddListener(AddTestHour);
            if (attendanceButton != null) attendanceButton.onClick.AddListener(ClaimAttendance);
            if (tutorialReplayButton != null) tutorialReplayButton.onClick.AddListener(ReplayTutorial);
            if (resetButton != null) resetButton.onClick.AddListener(OnResetPressed);

            // Test-only affordances stay out of release builds.
            if (idleTestButton != null) idleTestButton.gameObject.SetActive(Debug.isDebugBuild);
        }

        public void Bind(GameServices services)
        {
            _svc = services;
            if (buildLabel != null)
                buildLabel.text = $"Unity 이식 빌드 {Application.version} · 원본 테스트 빌드 10.14-d";
            BuildStageList();
            Refresh();
        }

        void OnEnable()
        {
            if (_svc != null) Refresh();
        }

        void Update()
        {
            if (_svc == null) return;

            // The idle meter ticks on its own; refreshing once a second is enough.
            if (Time.unscaledTime >= _idleRefreshAt)
            {
                _idleRefreshAt = Time.unscaledTime + 1f;
                RefreshIdle();
            }

            if (toastRoot != null && toastRoot.activeSelf && Time.unscaledTime > _toastUntil)
                toastRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ build

        void BuildStageList()
        {
            foreach (var r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();

            foreach (var st in _svc.Db.Stages)
            {
                var row = Instantiate(stageRowPrefab, stageListRoot);
                var stage = st;
                row.Bind(stage, _svc, () => StageChosen?.Invoke(stage));
                _rows.Add(row);
            }
        }

        public void Refresh()
        {
            if (_svc == null) return;
            var p = _svc.Profile;

            if (coinLabel != null) coinLabel.text = p.Coin.ToString("N0");
            if (crystalLabel != null) crystalLabel.text = p.Crystal.ToString("N0");

            var rep = p.Data.rep;
            if (rep != null && _svc.Db.Units.TryGetValue(rep, out var def))
            {
                if (repNameLabel != null)
                {
                    var owned = p.Data.Own(rep);
                    repNameLabel.text = $"{def.Name}  Lv.{owned?.lv ?? 1}" +
                                        (owned != null && owned.star > 0 ? $" · 돌파 {owned.star}" : "");
                }
                if (repPortrait != null && sprites != null)
                {
                    repPortrait.sprite = sprites.Unit(rep);
                    repPortrait.enabled = repPortrait.sprite != null;
                }
            }

            if (attendanceButton != null)
            {
                bool available = p.AttendanceAvailable;
                attendanceButton.interactable = available;
                if (attendanceLabel != null)
                    attendanceLabel.text = available ? "출석 보상 받기" : "오늘 출석 완료";
            }

            foreach (var row in _rows) row.Sync();
            RefreshIdle();
        }

        void RefreshIdle()
        {
            if (_svc == null || idleRoot == null) return;
            var p = _svc.Profile;
            var pending = p.IdlePendingNow();

            bool on = p.IdleUnlocked && pending != null;
            idleRoot.SetActive(true);

            if (!on)
            {
                if (idleSubLabel != null)
                    idleSubLabel.text = _svc.Db.Unlocks.TryGetValue("idle", out var u)
                        ? _svc.Db.Text(u.lockKey) : "스테이지를 클리어하면 열려요.";
                if (idleAmountLabel != null) idleAmountLabel.text = "";
                if (idleBar != null) idleBar.fillAmount = 0f;
                if (idleClaimButton != null) idleClaimButton.interactable = false;
                return;
            }

            var v = pending.Value;
            var rate = p.IdleRateStage;
            if (idleSubLabel != null)
                idleSubLabel.text = $"최고 {rate.No} 기준 시급 · 코인 {rate.IdleCoin:N0} · 부품 {rate.IdlePart} · 크리스탈 {rate.IdleCrystal}";
            if (idleAmountLabel != null)
                idleAmountLabel.text = $"코인 {Mathf.Floor((float)v.Coin):N0}  ·  부품 {Mathf.Floor((float)v.Part):N0}  ·  크리스탈 {Mathf.Floor((float)v.Crystal):N0}";
            if (idleBar != null) idleBar.fillAmount = Mathf.Clamp01((float)(v.Hours / v.Cap));
            if (idleClaimButton != null) idleClaimButton.interactable = v.Coin >= 1d;
        }

        // ------------------------------------------------------------------ actions

        void ClaimIdle()
        {
            var got = _svc.Profile.ClaimIdle();
            if (got == null) return;
            Toast($"자동 청소로 코인 {got.Value.coin:N0}, 부품 {got.Value.part:N0}씩, 크리스탈 {got.Value.crystal:N0} 모아왔어요!");
            Refresh();
        }

        /// <summary>Development helper that matches the original's "테스트용 +1시간" button.</summary>
        void AddTestHour()
        {
            if (_svc.Profile.Data.idleSince == 0) return;
            _svc.Profile.Data.idleSince -= 3_600_000L;
            _svc.Persist();
            RefreshIdle();
        }

        void ClaimAttendance()
        {
            var got = _svc.Profile.ClaimAttendance();
            if (got.Count == 0) return;
            Toast($"출석 보상, {_svc.Profile.DescribeRewards(got)} 챙겨두었어요!");
            Refresh();
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
                if (resetLabel != null) resetLabel.text = "게임 초기화";
                BuildStageList();
                Refresh();
                Toast("초기화했어요.");
                return;
            }
            _resetArmedAt = Time.unscaledTime;
            if (resetLabel != null) resetLabel.text = "한 번 더 누르면 초기화";
            CancelInvoke(nameof(DisarmReset));
            Invoke(nameof(DisarmReset), 3f);
        }

        void DisarmReset()
        {
            if (resetLabel != null) resetLabel.text = "게임 초기화";
        }

        public void Toast(string msg)
        {
            if (toastLabel == null || toastRoot == null) return;
            toastLabel.text = msg;
            toastRoot.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.6f;
        }
    }
}
