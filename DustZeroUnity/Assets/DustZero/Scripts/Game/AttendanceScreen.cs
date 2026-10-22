// Lobby popup for the seven-day attendance wheel.
//
// LobbyScreen's rail button already claims today's square directly; this screen is the
// calendar view the redesign calls for, showing all seven squares at once so the player can
// see what is coming rather than only what they just took.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class AttendanceScreen : MonoBehaviour
    {
        public const int Days = 7;

        [SerializeField] UiButton backButton;
        [SerializeField] UiButton claimButton;

        [Header("칸 (1~7일차 순서)")]
        [SerializeField] Image[] faces;
        [SerializeField] TextMeshProUGUI[] dayLabels;
        [SerializeField] TextMeshProUGUI[] rewardLabels;
        [SerializeField] GameObject[] checks;
        [SerializeField] GameObject[] locks;

        GameServices _svc;

        public event System.Action Closed;
        public event System.Action<string> Message;

        void Awake()
        {
            if (backButton != null) backButton.SetAction(Close);
            if (claimButton != null) claimButton.SetAction(Claim);
        }

        public void Open(GameServices services)
        {
            _svc = services;
            gameObject.SetActive(true);
            Refresh();
        }

        void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void Claim()
        {
            var got = _svc.Profile.ClaimAttendance();
            if (got.Count == 0) { Message?.Invoke("오늘 출석은 이미 받았어요."); return; }
            _svc.Persist();
            Refresh();
            Message?.Invoke($"출석 보상, {_svc.Profile.DescribeRewards(got)} 챙겨두었어요!");
        }

        public void Refresh()
        {
            if (_svc == null || faces == null) return;
            var p = _svc.Profile;

            // Data.att.n never resets, so how many squares are done THIS lap is its remainder
            // against the table length - the same arithmetic AttendanceNextDay already uses.
            int cycleLen = _svc.Db.Attendance.Count;
            int doneInCycle = cycleLen > 0 ? p.AttendanceTaken % cycleLen : 0;
            int next = p.AttendanceNextDay;
            bool canClaimToday = p.AttendanceAvailable;

            int slots = Mathf.Min(Days, faces.Length, cycleLen);
            for (int i = 0; i < slots; i++)
            {
                int day = i + 1;
                var row = p.AttendanceRowFor(day);
                bool done = day <= doneInCycle;
                bool ready = day == next && canClaimToday;

                if (dayLabels != null && i < dayLabels.Length && dayLabels[i] != null)
                    dayLabels[i].text = $"{day}일차";
                if (rewardLabels != null && i < rewardLabels.Length && rewardLabels[i] != null)
                    rewardLabels[i].text = row != null ? p.DescribeGroup(row.rewardGroup) : string.Empty;
                if (checks != null && i < checks.Length && checks[i] != null)
                    checks[i].SetActive(done);
                if (locks != null && i < locks.Length && locks[i] != null)
                    locks[i].SetActive(!done && !ready);
                if (faces[i] != null)
                    faces[i].color = done ? UiTheme.Disabled : ready ? UiTheme.Success : UiTheme.Panel2;
            }

            if (claimButton != null)
            {
                claimButton.State = canClaimToday ? UiButtonState.Normal : UiButtonState.Disabled;
                claimButton.Text = canClaimToday ? $"{next}일차 받기" : "오늘은 이미 받았어요";
            }
        }
    }
}
