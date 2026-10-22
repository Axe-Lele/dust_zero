// The home tab from redesign 1a: the room, the idle meter and the two ways into a run.
//
// Split out of LobbyScreen so the shell owns navigation and each tab owns its own content.
// Everything it shows comes from PlayerProfile; it holds no rules of its own.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    public sealed class HomeTab : MonoBehaviour
    {
        [Header("방")]
        [SerializeField] RoomView room;

        [OptionalReference("개발 빌드에서만 존재합니다")]
        [SerializeField] UiButton idleTestButton;

        [Header("행동")]
        [SerializeField] UiButton survivorButton;
        /// <summary>The second line of the survivor row: the call to action, or why it is shut.</summary>
        [SerializeField] TextMeshProUGUI survivorTitleLabel;
        [SerializeField] UiButton nextStageButton;
        [SerializeField] TextMeshProUGUI nextStageChip;
        /// <summary>The capsule over the CTAs that says how many appliances are sweeping.</summary>
        [SerializeField] TextMeshProUGUI roomNoteLabel;

        GameServices _svc;
        float _idleRefreshAt;

        /// <summary>Raised with the stage the player chose from the home CTA.</summary>
        public event Action<StageDef> StageChosen;
        /// <summary>Raised when the player taps 대청소. No screen exists for it yet.</summary>
        public event Action SurvivorChosen;
        /// <summary>Raised after a claim so the shell can repaint the wallet.</summary>
        public event Action<string> Claimed;

        /// <summary>The lobby shell owns the 자동 청소 pill, but the quip belongs to the room.</summary>
        public RoomView Room => room;

        void Awake()
        {
            if (idleTestButton != null)
            {
                idleTestButton.SetAction(AddTestHour);
                // Test-only affordance, as the original keeps it out of a release build.
                idleTestButton.gameObject.SetActive(Debug.isDebugBuild);
            }
            if (survivorButton != null) survivorButton.SetAction(() => SurvivorChosen?.Invoke());
            if (nextStageButton != null) nextStageButton.SetAction(PlayNextStage);
        }

        public void Bind(GameServices services)
        {
            _svc = services;
            if (room != null) room.Bind(services);
            Refresh();
        }

        void OnEnable()
        {
            if (_svc != null) Refresh();
        }

        void Update()
        {
            // The room note tracks the cast as it wanders; once a second is enough.
            if (_svc == null || Time.unscaledTime < _idleRefreshAt) return;
            _idleRefreshAt = Time.unscaledTime + 1f;
            RefreshRoomNote();
        }

        public void Refresh()
        {
            if (_svc == null) return;
            if (room != null) room.RefreshCast();
            RefreshActions();
        }

        // ------------------------------------------------------------------ idle

        void AddTestHour()
        {
            if (_svc.Profile.Data.idleSince == 0) return;
            _svc.Profile.Data.idleSince -= 3_600_000L;
            _svc.Persist();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>
        /// The stage the CTA sends the player to: the first unlocked one they have not
        /// three-starred, falling back to the last unlocked stage once everything is done.
        /// </summary>
        /// <summary>The lobby guide's "home.cta" spotlight target.</summary>
        public RectTransform NextStageTarget => nextStageButton != null ? (RectTransform)nextStageButton.transform : null;

        public StageDef NextStage()
        {
            if (_svc == null) return null;
            StageDef lastOpen = null;
            foreach (var st in _svc.Db.Stages)
            {
                if (!_svc.Profile.ConditionMet(st.UnlockCond)) continue;
                lastOpen = st;
                if (_svc.Profile.Data.StarsOf(st.Id) < 3) return st;
            }
            return lastOpen;
        }

        void RefreshActions()
        {
            // 10.16-n writes the stage straight onto the orange row - "1-2 거실" under the
            // kicker - instead of putting it in a chip, so the row's own label is the name.
            var next = NextStage();
            if (nextStageButton != null)
            {
                bool hasDeck = _svc.Profile.Data.deck.Count > 0;
                nextStageButton.State = next == null || !hasDeck
                    ? UiButtonState.Disabled : UiButtonState.Normal;
            }
            if (nextStageChip != null)
                nextStageChip.text = next == null ? "모든 스테이지 완료" : $"{next.No} {next.Name}";

            if (survivorButton != null)
            {
                bool open = _svc.Profile.SurvivorUnlocked;
                survivorButton.State = open ? UiButtonState.Normal : UiButtonState.Locked;
                if (survivorTitleLabel != null)
                    survivorTitleLabel.text = open
                        ? "리더를 골라 출동"
                        : "1-1을 클리어하면 열려요";
            }

            RefreshRoomNote();
        }

        /// <summary>
        /// The capsule the build floats over the CTAs. It counts the appliances actually
        /// walking the room against the roster, which is what the original reports.
        /// </summary>
        void RefreshRoomNote()
        {
            if (roomNoteLabel == null) return;
            int walking = room != null && room.Sim != null ? room.Sim.Agents.Count : 0;
            int total = _svc.Db.Units.Count;
            roomNoteLabel.text = walking > 0
                ? $"가전 {walking}/{total}이 방을 청소하고 있어요 · 눌러서 말 걸기"
                : "가전을 영입하면 방을 돌아다녀요";
        }

        void PlayNextStage()
        {
            var next = NextStage();
            if (next == null) return;
            _svc?.Guide?.ReportReached("home.cta");
            StageChosen?.Invoke(next);
        }

        static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
        }
    }
}
