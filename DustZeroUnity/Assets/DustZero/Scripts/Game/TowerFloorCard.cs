// 층 목록의 한 줄 — 원본 `.tw-fl`.
//
// 430x932 실측: 346.9x47 · 모서리 10 · 30/132/1fr 격자에 간격 7 ·
// 번호 16px LED · 창 그림 132x40 · 이름줄 12.5px 굵게 · 보상줄 11px.
// 상태별 색: 기본 #6E7B84 · 완료 #DCE7EE · 현재 #FFF3E6 + led 링 · 보스 #4A2A2A.
// 자세한 수치는 production/qa/audit/original/TOWER-MEASUREMENTS.md 2절.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>층 한 줄의 상태. 원본의 `done`/`cur`/`lock`/`open` 과 같다.</summary>
    public enum TowerFloorState { Open, Current, Cleared, Locked }

    public sealed class TowerFloorCard : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image ring;
        [SerializeField] TextMeshProUGUI numberLabel;
        [SerializeField] TowerWindowView window;
        [SerializeField] TextMeshProUGUI tagLabel;
        [SerializeField] Image tagFace;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI rewardLabel;
        [SerializeField] RectTransform nameRow;
        [SerializeField] GameObject goBadge;
        [SerializeField] UiButton button;

        public int Floor { get; private set; }
        public TowerWindowView Window => window;

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.SetAction(() => Tapped?.Invoke());
        }

        /// <summary>
        /// 한 줄을 그린다. 색과 띠는 상태가, 글은 표가 정한다.
        /// </summary>
        /// <param name="floor">층 번호.</param>
        /// <param name="state">이 층의 상태.</param>
        /// <param name="boss">보스 층이면 바탕이 어두운 붉은색이 된다.</param>
        /// <param name="tag">규칙 이름. 없으면 꼬리표를 숨긴다.</param>
        /// <param name="tagColor">규칙 종류 색(판·목표·편성·보스).</param>
        /// <param name="name">층 이름.</param>
        /// <param name="reward">보상 또는 완료 표시 줄.</param>
        public void Show(int floor, TowerFloorState state, bool boss,
                         string tag, Color tagColor, string name, string reward)
        {
            Floor = floor;

            bool cleared = state == TowerFloorState.Cleared;
            bool current = state == TowerFloorState.Current;

            Color bg = cleared ? UiTheme.TowerFloorDone
                     : current ? UiTheme.TowerFloorCurrent
                     : boss ? UiTheme.TowerFloorBoss
                     : UiTheme.TowerFloorOpen;
            Color ink = cleared || current ? UiTheme.Ink : UiTheme.DarkInk;

            if (face != null)
            {
                // 잠긴 층은 원본처럼 .78 로 흐려진다.
                float a = state == TowerFloorState.Locked ? 0.78f : 1f;
                face.color = new Color(bg.r, bg.g, bg.b, a);
            }
            if (ring != null)
            {
                ring.gameObject.SetActive(current || boss);
                ring.color = current ? UiTheme.Led : UiTheme.TowerBossRing;
            }
            if (numberLabel != null)
            {
                numberLabel.text = floor.ToString();
                numberLabel.color = cleared ? UiTheme.Success
                                  : current ? UiTheme.Led
                                  : boss ? UiTheme.TowerBossNumber
                                  : UiTheme.TowerFloorNumber;
            }
            bool hasTag = !string.IsNullOrEmpty(tag);
            if (tagFace != null)
            {
                tagFace.gameObject.SetActive(hasTag);
                tagFace.color = tagColor;
            }
            if (tagLabel != null)
            {
                tagLabel.gameObject.SetActive(hasTag);
                tagLabel.text = tag ?? string.Empty;
                // 규칙 이름은 '보스'부터 '도망치는 황금 먼지'까지 길이가 제각각이다.
                // 폭을 글자에 맞추지 않으면 긴 이름이 잘린다.
                if (hasTag && tagFace != null
                    && tagFace.TryGetComponent<LayoutElement>(out var tagLayout))
                    tagLayout.preferredWidth =
                        tagLabel.GetPreferredValues(tag).x + UiTheme.Px(10f);
            }
            if (nameLabel != null) { nameLabel.text = name; nameLabel.color = ink; }
            if (rewardLabel != null)
            {
                rewardLabel.text = reward;
                rewardLabel.color = cleared || current ? UiTheme.Ink2 : UiTheme.TowerFloorReward;
            }
            if (goBadge != null) goBadge.SetActive(current);
            // 원본 `.tw-fl.cur .inf{padding-right:52px}` — '도전' 띠가 글자를 덮지 않게.
            if (nameRow != null)
                nameRow.offsetMax = new Vector2(current ? -UiTheme.Px(52f) : -UiTheme.Px(7f),
                                                nameRow.offsetMax.y);
            if (rewardLabel != null)
            {
                var rrt = (RectTransform)rewardLabel.transform;
                rrt.offsetMax = new Vector2(current ? -UiTheme.Px(52f) : -UiTheme.Px(7f),
                                            rrt.offsetMax.y);
            }
        }
    }
}
