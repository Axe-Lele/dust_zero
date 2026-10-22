// 전투 결과, 10.16-n 기준.
//
// 원본은 보드 위에 330px 카드 하나를 띄운다 — 제목, 별 셋, 무슨 일이 있었는지 한 문단,
// 읽는 값 셋, 보상, 버튼 둘. 예전의 전체 화면 레이아웃(별 조건 체크리스트 + 통계 타일)은
// 원본에 없다. 별 조건은 config.starLifeMin 의 **생명 수**지 퍼센트가 아니다.
using System;
using System.Text;
using DustZero.Core.Defense;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class ResultScreen : MonoBehaviour
    {
        [Header("머리말")]
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] Image[] starImages;
        [SerializeField] Sprite starOn;
        [SerializeField] Sprite starOff;

        [Header("본문")]
        [SerializeField] TextMeshProUGUI bodyLabel;
        [SerializeField] TextMeshProUGUI killsValue;
        [SerializeField] TextMeshProUGUI lifeValue;
        [SerializeField] TextMeshProUGUI timeValue;
        [SerializeField] TextMeshProUGUI rewardLabel;
        [SerializeField] TextMeshProUGUI noteLabel;

        [Header("MVP — 원본에 없는 추가")]
        [SerializeField] GameObject mvpRoot;
        [SerializeField] Image mvpPortrait;
        [SerializeField] TextMeshProUGUI mvpNameLabel;
        [SerializeField] SpriteLibrary sprites;

        [Header("행동")]
        [SerializeField] UiButton homeButton;
        [SerializeField] UiButton nextButton;
        [SerializeField] TextMeshProUGUI nextLabel;

        public event Action HomeRequested;
        public event Action RetryRequested;
        public event Action<StageDef> NextRequested;

        /// <summary>The lobby guide's "result.lobby" spotlight target.</summary>
        public RectTransform ResolveGuideTarget(string key)
            => key == "result.lobby" && homeButton != null ? (RectTransform)homeButton.transform : null;

        GameServices _svc;
        StageDef _next;

        void Awake()
        {
            if (homeButton != null) homeButton.SetAction(() => HomeRequested?.Invoke());
            if (nextButton != null)
                nextButton.SetAction(() =>
                {
                    if (_next != null) NextRequested?.Invoke(_next);
                    else RetryRequested?.Invoke();
                });
        }

        public void Show(GameServices services, StageDef stage, BattleSim sim,
                         PlayerProfile.StageResult outcome, bool won)
        {
            _svc = services;
            gameObject.SetActive(true);

            if (titleLabel != null)
                titleLabel.text = won ? $"{stage.No} {stage.Name} 사수 성공"
                                      : $"{stage.No} {stage.Name} 함락";

            FillStars(won ? outcome.Stars : 0, won);
            FillBody(stage, outcome, won);
            FillStats(sim);
            FillRewards(outcome);
            FillMvp(sim);
            FillActions(stage, won);

            // Rows are shown and hidden per result, and a row enabled this frame has not been
            // through its layout group yet: its width is still the default 100, which makes
            // the MVP caption's stretched rect come out NEGATIVE and draw nothing. Rebuild the
            // card itself - twice, because the ContentSizeFitter on it resizes the card from
            // the rows, and the rows then have to be laid out again inside the new height.
            var card = mvpRoot != null ? mvpRoot.transform.parent as RectTransform : null;
            if (card != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            }
        }

        void FillStars(int stars, bool won)
        {
            if (starImages == null) return;
            // 패배하면 별 줄 자체가 없다. 빈 별 셋을 보여 주는 것은 원본과 다르다.
            foreach (var img in starImages)
            {
                if (img == null) continue;
                img.transform.parent.gameObject.SetActive(won);
            }
            if (!won) return;
            for (int i = 0; i < starImages.Length; i++)
            {
                if (starImages[i] == null) continue;
                bool on = i < stars;
                starImages[i].sprite = on ? starOn : (starOff != null ? starOff : starOn);
                starImages[i].color = on ? UiTheme.Led : UiTheme.PanelLine;
            }
        }

        /// <summary>
        /// 원본이 쓰는 문단 그대로. 첫 클리어면 시급 변화, 다시 깼으면 기록, 졌으면 안내.
        /// 다음 별 조건은 **생명 수**로 쓴다(퍼센트가 아니다).
        /// </summary>
        void FillBody(StageDef stage, PlayerProfile.StageResult outcome, bool won)
        {
            if (bodyLabel == null) return;
            var body = new StringBuilder();

            if (outcome.FirstClear)
                body.Append(outcome.PreviousIdleStage == null
                    ? $"자동 청소 시작! 시급 코인 {outcome.NewIdleStage.IdleCoin:N0}"
                    : $"시급 코인 {outcome.PreviousIdleStage.IdleCoin:N0} → {outcome.NewIdleStage.IdleCoin:N0}");
            else if (won)
                body.Append(outcome.NewRecord
                    ? $"최고 기록 갱신! 별 {outcome.PreviousStars}개 → {outcome.Stars}개"
                    : $"이미 클리어한 스테이지예요. 최고 기록은 별 {outcome.PreviousStars}개입니다.");
            else
                body.Append("청결도가 0이 됐어요. 자동 청소로 모은 재화로 가전을 육성하고 "
                            + "다시 도전해보세요.");

            var mins = _svc.Db.Config.starLifeMin;
            if (won && outcome.Stars < 3 && mins != null && outcome.Stars < mins.Length)
                body.Append($"\n청결도 {mins[outcome.Stars]} 이상으로 지키면 별 {outcome.Stars + 1}개예요.");

            if (outcome.UnlockedStarReward)
                body.Append($"\n챕터 {stage.Chapter} 별 보상을 받을 수 있어요. 스테이지 탭에서 받으세요.");

            bodyLabel.text = body.ToString();
        }

        void FillStats(BattleSim sim)
        {
            if (killsValue != null) killsValue.text = sim.Killed.ToString("N0");
            if (lifeValue != null) lifeValue.text = Mathf.Max(0, sim.Life).ToString();
            if (timeValue != null) timeValue.text = $"{Mathf.FloorToInt(sim.Time)}s";
        }

        void FillRewards(PlayerProfile.StageResult outcome)
        {
            if (rewardLabel == null) return;
            bool any = outcome.Rewards != null && outcome.Rewards.Count > 0;
            rewardLabel.transform.parent.gameObject.SetActive(any);
            if (any) rewardLabel.text = _svc.Profile.DescribeRewards(outcome.Rewards);
            // 원본의 마지막 두 줄: 전력 스킬 해금/강화와 시즌 패스 경험치.
            if (noteLabel == null) return;
            var lines = new System.Collections.Generic.List<string>();
            foreach (var (id, lv) in outcome.PowerSkills)
                lines.Add($"전력 스킬 {(lv > 1 ? "강화" : "해금")} · "
                          + $"{_svc.Db.Text($"ps.{id}.name")} Lv.{lv}");
            if (outcome.PassXp > 0)
                lines.Add($"시즌 패스 경험치 +{outcome.PassXp}"
                          + (outcome.PassLevel > 0 ? $" · Lv.{outcome.PassLevel} 달성!" : ""));
            noteLabel.gameObject.SetActive(lines.Count > 0);
            noteLabel.text = string.Join("\n", lines);
        }

        /// <summary>원본에는 없다. 지우지 않고 카드 맨 아래 한 줄로 남긴다.</summary>
        void FillMvp(BattleSim sim)
        {
            if (mvpRoot == null) return;
            // 시뮬레이션에 MVP 개념이 없다. 처치 수가 가장 많은 가전을 여기서 고른다.
            BattleUnit best = null;
            foreach (var u in sim.Units)
                if (best == null || u.Kills > best.Kills) best = u;
            if (best != null && best.Kills <= 0) best = null;
            mvpRoot.SetActive(best != null);
            if (best == null) return;
            if (mvpPortrait != null && sprites != null)
            {
                mvpPortrait.sprite = sprites.Unit(best.Type);
                mvpPortrait.enabled = mvpPortrait.sprite != null;
            }
            if (mvpNameLabel != null)
                mvpNameLabel.text = $"MVP · {_svc.Db.Unit(best.Type).ShortName}  처치 {best.Kills}";
        }

        void FillActions(StageDef stage, bool won)
        {
            _next = null;
            if (won)
            {
                var candidate = _svc.Db.StageByOrder(stage.Order + 1);
                if (candidate != null && _svc.Profile.ConditionMet(candidate.UnlockCond))
                    _next = candidate;
            }
            if (nextLabel != null)
                nextLabel.text = _next != null ? $"다음 {_next.No}" : "다시 하기";
        }
    }
}
