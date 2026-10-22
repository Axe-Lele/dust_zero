// 원본 `.mode-card` 한 장 — 콘텐츠 탭의 모드 카드.
//
// 10.19-e 를 430x932 에서 직접 재서 맞췄다: 어두운 카드(--panel) 안쪽 여백 14, 모서리 16.
// 위에서부터 아이콘 44 + 꼭지말(11, --led) + 제목(26), 소개(13.5), 규칙 두 줄(12.5),
// 통계 세 칸(118x55~59), 보충 줄(12), 그리고 46 높이의 CTA.
//
// 대청소 카드는 시간 보상 띠가 따로 있어 `ContentTab` 이 직접 들고 있고, 이 부품은
// 먼지 타워·가전 대청소 리그처럼 같은 틀을 그대로 쓰는 카드가 쓴다.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class ModeCard : MonoBehaviour
    {
        /// <summary>원본 `.mc-stats` 는 늘 세 칸이다.</summary>
        public const int StatCount = 3;
        /// <summary>원본 `.mc-rules` 는 늘 두 줄이다.</summary>
        public const int RuleCount = 2;

        [SerializeField] TextMeshProUGUI kickerLabel;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI leadLabel;
        [SerializeField] TextMeshProUGUI[] ruleLabels = new TextMeshProUGUI[RuleCount];
        [SerializeField] TextMeshProUGUI[] statKeys = new TextMeshProUGUI[StatCount];
        [SerializeField] TextMeshProUGUI[] statValues = new TextMeshProUGUI[StatCount];
        [OptionalReference("협동 카드에는 보충 줄이 없습니다")]
        [SerializeField] TextMeshProUGUI subLabel;
        /// <summary>대청소 카드만 보충 줄이 둘이다(`.mc-sub` 두 개).</summary>
        [OptionalReference("보충 줄이 둘인 카드에만 있습니다")]
        [SerializeField] TextMeshProUGUI subLabel2;
        /// <summary>`.mc-leaders` — 대청소 카드 제목 옆의 리더 얼굴들이 들어갈 자리.</summary>
        [OptionalReference("대청소 카드에만 있습니다")]
        [SerializeField] RectTransform portraitRow;
        [SerializeField] UiButton goButton;
        [SerializeField] TextMeshProUGUI goLabel;

        /// <summary>
        /// 보충 줄이 비면 그 줄을 **접는다.** 원본은 흐름 배치라 내용이 없는 `.mc-sub` 가
        /// 높이 0 이 되고 CTA 가 그만큼 올라오며 카드도 짧아진다(잠긴 먼지 타워 312.8,
        /// 열리면 331.4). 우리 줄은 자리를 고정해 두므로 직접 옮겨 준다.
        /// </summary>
        [OptionalReference("접을 줄이 없는 카드도 있습니다")]
        [SerializeField] RectTransform subRow;
        [OptionalReference("접을 줄이 없는 카드도 있습니다")]
        [SerializeField] RectTransform goRow;
        [OptionalReference("접을 줄이 없는 카드도 있습니다")]
        [SerializeField] LayoutElement cardHeight;

        float _goBaseY, _cardBaseH;
        bool _baseRead;

        /// <summary>CTA, so the screen that owns this card can hang its action on it.</summary>
        public UiButton Go => goButton;

        /// <summary>Where the 대청소 card's leader faces go; null on the other cards.</summary>
        public RectTransform PortraitRow => portraitRow;

        /// <summary>
        /// Writes the whole card in one go. <paramref name="stats"/> is three key/value
        /// pairs, shorter arrays simply leave the remaining cells blank.
        /// </summary>
        public void Set(string kicker, string title, string lead,
                        string[] rules, (string key, string value)[] stats,
                        string sub, string cta, bool unlocked, string sub2 = null)
        {
            if (kickerLabel != null) kickerLabel.text = kicker;
            if (titleLabel != null) titleLabel.text = title;
            if (leadLabel != null) leadLabel.text = lead;

            for (int i = 0; i < ruleLabels.Length; i++)
            {
                if (ruleLabels[i] == null) continue;
                ruleLabels[i].text = rules != null && i < rules.Length ? "· " + rules[i] : string.Empty;
            }

            for (int i = 0; i < StatCount; i++)
            {
                bool has = stats != null && i < stats.Length;
                if (i < statKeys.Length && statKeys[i] != null)
                    statKeys[i].text = has ? stats[i].key : string.Empty;
                if (i < statValues.Length && statValues[i] != null)
                    statValues[i].text = has ? stats[i].value : string.Empty;
            }

            if (subLabel != null) subLabel.text = sub ?? string.Empty;
            if (subLabel2 != null) subLabel2.text = sub2 ?? string.Empty;
            CollapseEmptySub(string.IsNullOrEmpty(sub));
            if (goLabel != null) goLabel.text = cta;
            if (goButton != null)
                goButton.State = unlocked ? UiButtonState.Normal : UiButtonState.Locked;
        }

        /// <summary>
        /// 내용이 없는 보충 줄을 접고 CTA 와 카드 높이를 그만큼 끌어올린다.
        /// 원본은 흐름 배치라 저절로 그렇게 되지만 우리 줄은 자리가 고정이다.
        /// </summary>
        void CollapseEmptySub(bool empty)
        {
            if (subRow == null || goRow == null) return;
            if (!_baseRead)
            {
                _goBaseY = goRow.anchoredPosition.y;
                _cardBaseH = cardHeight != null ? cardHeight.preferredHeight : 0f;
                _baseRead = true;
            }
            // 원본은 보충 줄이 비어도 `<p class="mc-sub">` 가 남아 **여백은 그대로**다.
            // 높이만 0이 되므로 줄 자체(18.6)만 줄이고 위아래 10 씩은 남겨야 한다 —
            // 10 까지 같이 줄이면 카드가 6 짧아져 아래 카드가 통째로 올라온다.
            float shrink = empty ? subRow.rect.height : 0f;
            subRow.gameObject.SetActive(!empty);
            goRow.anchoredPosition = new Vector2(goRow.anchoredPosition.x, _goBaseY + shrink);
            if (cardHeight != null)
                cardHeight.minHeight = cardHeight.preferredHeight = _cardBaseH - shrink;
        }
    }
}
