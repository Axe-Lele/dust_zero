// 대청소 run result: how long the leader lasted, what it paid, and whether it is a new record.
// No star checklist here - the run has one outcome (fell, or the player left) rather than a
// graded clear, so the screen is the numbers plus the payout.
using System;
using DustZero.Core.Survivor;
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    public sealed class SurvivorResultScreen : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI timeValue;
        [SerializeField] TextMeshProUGUI killsValue;
        [SerializeField] TextMeshProUGUI levelValue;
        /// <summary>보상 한 줄. 원본은 "보상: 코인 1,200, 부품 2씩" 처럼 이어 쓴다.</summary>
        [SerializeField] TextMeshProUGUI rewardLabel;
        /// <summary>오늘 크리스탈 · 남은 판 · 오늘 최고 · 역대 최고 한 줄.</summary>
        [SerializeField] TextMeshProUGUI bestLabel;

        [SerializeField] UiButton homeButton;
        [SerializeField] UiButton retryButton;

        public event Action HomeRequested;
        public event Action RetryRequested;

        void Awake()
        {
            if (homeButton != null) homeButton.SetAction(() => HomeRequested?.Invoke());
            if (retryButton != null) retryButton.SetAction(() => RetryRequested?.Invoke());
        }

        public void Show(GameServices services, SurvivorSim.SurvResult result)
        {
            gameObject.SetActive(true);

            // 원본은 제목 한 줄로 셋을 가른다. 기록 갱신을 따로 뱃지로 띄우지 않는다.
            if (titleLabel != null)
                titleLabel.text = result.Quit ? "중간에 나왔어요"
                                : result.NewRecord ? "최고 기록 갱신!"
                                : "리더가 쓰러졌어요";

            if (timeValue != null) timeValue.text = SurvivorSim.Mmss(result.Seconds);
            if (killsValue != null) killsValue.text = result.Kills.ToString("N0");
            if (levelValue != null) levelValue.text = result.Level.ToString();

            if (rewardLabel != null)
            {
                var got = new System.Collections.Generic.List<string>();
                if (result.Crystal > 0) got.Add($"크리스탈 {result.Crystal:N0}");
                if (result.Coin > 0) got.Add($"코인 {result.Coin:N0}");
                if (result.Parts > 0) got.Add($"부품 {result.Parts:N0}씩");
                if (result.LeaderShards > 0 &&
                    services?.Db.Units.TryGetValue(result.LeaderId, out var def) == true)
                    got.Add($"{def.Name} 조각 {result.LeaderShards}");

                // 30초를 못 버티면 보상도 없고 오늘 판 수도 쓰지 않는다 — 원본의 규칙이다.
                rewardLabel.text =
                    got.Count > 0 ? "보상: " + string.Join(", ", got)
                    : !result.Counted ? "30초 미만이라 보상 없이 끝났어요. 오늘 판 수는 그대로예요."
                    : "새로 받은 보상이 없어요."
                    + (result.Counted && !result.Rewarded ? " (부품·코인은 오늘 판 수를 다 썼어요)" : "");
            }

            // 원본의 note 줄 그대로: 오늘 일지 / 남은 판 / 오늘 최고 / 역대 최고.
            if (bestLabel != null)
                bestLabel.text =
                    $"오늘 청소 일지 {result.PoolToday}/{result.PoolTotal}점"
                    + $" · 부품·코인 남은 판 {result.RunsLeft}/{result.RunsTotal}"
                    + $" · 오늘 최고 {SurvivorSim.Mmss(result.BestToday)}"
                    + $" · 역대 최고 {SurvivorSim.Mmss(result.BestEver)}";
        }
    }
}
