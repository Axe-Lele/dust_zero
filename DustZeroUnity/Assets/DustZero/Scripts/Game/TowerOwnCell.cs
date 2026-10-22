// 먼지 타워 출전 목록의 한 칸 — 원본 `.tw-oc`.
//
// 430x932 실측: 62.16x54 · 모서리 10 · 테두리 1.5 · 아이콘 34 ·
// `Lv` 10.5px LED(아래 2) · ★ 왼쪽 위 8.5px. 출전 중이면 테두리가 파래지고 흐려지며
// '출전' 띠가 붙고, 추천이면 테두리가 붉어지며 오른쪽 위에 '추천' 이 붙는다.
// 자세한 수치는 production/qa/audit/original/TOWER-MEASUREMENTS.md 1절.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class TowerOwnCell : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image outline;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] TextMeshProUGUI starLabel;
        [SerializeField] GameObject inBadge;
        [SerializeField] GameObject hotBadge;
        [SerializeField] UiButton button;

        /// <summary>이 칸이 들고 있는 가전 id. 눌렀을 때 화면이 이것으로 넣고 뺀다.</summary>
        public string UnitId { get; private set; }

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.SetAction(() => Tapped?.Invoke());
        }

        /// <summary>
        /// 한 칸을 그린다.
        /// </summary>
        /// <param name="unitId">가전 id.</param>
        /// <param name="sprites">그림 묶음.</param>
        /// <param name="rarity">희귀도 — SR·SSR 은 바탕이 옅게 물든다.</param>
        /// <param name="levelText">`Lv5` 또는 빌려 주는 가전이면 `손님`.</param>
        /// <param name="star">돌파 ★ 수. 0이면 안 보인다.</param>
        /// <param name="placed">판에 올라가 있다.</param>
        /// <param name="recommended">이 층에서 쓸모가 큰 가전이다(원본 `twHot`).</param>
        /// <param name="interactable">준비 단계에서만 누를 수 있다.</param>
        public void Show(string unitId, SpriteLibrary sprites, Rarity rarity, string levelText,
                         int star, bool placed, bool recommended, bool interactable)
        {
            UnitId = unitId;

            if (face != null)
                face.color = rarity switch
                {
                    Rarity.SSR => UiTheme.RarityBgSSR,
                    Rarity.SR => UiTheme.RarityBgSR,
                    _ => Color.white,
                };
            if (outline != null)
                outline.color = placed ? UiTheme.Navy
                              : recommended ? UiTheme.Danger
                              : UiTheme.PanelLine;
            if (portrait != null)
            {
                portrait.sprite = sprites != null ? sprites.Unit(unitId) : null;
                portrait.enabled = portrait.sprite != null;
                // 원본은 출전 중인 칸을 .55 로 흐린다.
                portrait.color = new Color(1f, 1f, 1f, placed ? 0.55f : 1f);
            }
            if (levelLabel != null) levelLabel.text = levelText;
            if (starLabel != null)
            {
                starLabel.gameObject.SetActive(star > 0);
                if (star > 0) starLabel.text = new string('★', star);
            }
            if (inBadge != null) inBadge.SetActive(placed);
            if (hotBadge != null) hotBadge.SetActive(recommended && !placed);
            if (button != null)
                button.State = interactable ? UiButtonState.Normal : UiButtonState.Disabled;
        }
    }
}
