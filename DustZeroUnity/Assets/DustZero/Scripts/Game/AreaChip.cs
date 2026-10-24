// 스테이지 탭의 구역 칩. 원본 `.atab` 을 그대로 옮겼다.
//
// 예전에는 챕터 칩(한 줄짜리 어두운 칩)을 빌려 쓰면서 글자 세 줄을 서식으로 욱여넣었다.
// 그래서 고른 구역이 **검게** 칠해졌고(원본은 그 구역의 색), 안 고른 구역은 테두리 없는
// 맨 글자가 되었다. 칩은 구역마다 색이 다르므로 색을 밖에서 받는다.
//
// 원본 CSS (10.22-g index.html 137~146줄):
//   .atab        테두리 1.5 --line · 바탕 --card · 반경 11 · 안쪽 7/2/6 · 세로 · 가운데 · 간격 1
//   .atab small  Share Tech Mono 11 · --muted
//   .atab b      Do Hyeon 14 · 줄높이 1.15 · 한 줄
//   선택됨       테두리·바탕이 그 구역 색, 글자 #fff, small 은 흰색 85%
//   .locked      선택되지 않았을 때만 불투명도 .55
//   .lk          오른쪽 위 자물쇠, 지금 글자색의 60%
//   .here        칩 **아래로 7 내려** 붙는 주황(--ssr) 알약, 9.5px
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class AreaChip : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image edge;
        [SerializeField] TextMeshProUGUI numberLabel;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button button;
        [SerializeField] RectTransform lockBadge;
        [SerializeField] Image lockBody;
        [SerializeField] Image lockShackle;
        [SerializeField] RectTransform herePill;
        [SerializeField] TextMeshProUGUI hereLabel;

        /// <summary>원본 `.atab small` 과 `.atab b` 의 평소 색.</summary>
        [SerializeField] Color inkNormal = Color.black;
        [SerializeField] Color mutedNormal = Color.grey;
        [SerializeField] Color faceNormal = Color.white;
        [SerializeField] Color edgeNormal = Color.grey;

        /// <summary>
        /// 칩 한 개를 채운다. <paramref name="accent"/> 는 area 표의 color 다 —
        /// 구역마다 다르므로 테마에 박지 않고 표에서 받는다.
        /// </summary>
        /// <example><code>
        /// chip.Bind("1구역", "1층", accent, selected: true, locked: false, here: true, onTap);
        /// </code></example>
        public void Bind(string number, string name, Color accent,
                         bool selected, bool locked, bool here, Action onTap)
        {
            if (numberLabel != null) numberLabel.text = number;
            if (nameLabel != null) nameLabel.text = name;

            if (face != null) face.color = selected ? accent : faceNormal;
            if (edge != null) edge.color = selected ? accent : edgeNormal;
            // 원본은 선택됐을 때 small 을 흰색 85% 로 낮춘다. 흰색 그대로 두면 두 줄의
            // 무게가 같아져 어느 쪽이 이름인지 흐려진다.
            if (nameLabel != null) nameLabel.color = selected ? Color.white : inkNormal;
            if (numberLabel != null)
                numberLabel.color = selected ? new Color(1f, 1f, 1f, 0.85f) : mutedNormal;

            // .atab.locked:not([aria-selected=true]) — 고른 구역은 잠겨 있어도 또렷하다.
            if (group != null) group.alpha = locked && !selected ? 0.55f : 1f;
            if (button != null)
            {
                button.interactable = true;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onTap?.Invoke());
            }

            if (lockBadge != null) lockBadge.gameObject.SetActive(locked);
            // `background:currentColor` 라 자물쇠는 **그 줄 글자색**을 따라간다.
            var lockInk = selected ? Color.white : inkNormal;
            lockInk.a = 0.6f;
            if (lockBody != null) lockBody.color = lockInk;
            if (lockShackle != null) lockShackle.color = lockInk;

            if (herePill != null) herePill.gameObject.SetActive(here);
            if (here && hereLabel != null)
            {
                hereLabel.ForceMeshUpdate();
                // padding:0 5px — 알약은 글자 폭에 좌우 5씩 더한 만큼이다.
                var size = hereLabel.GetPreferredValues();
                herePill.sizeDelta = new Vector2(size.x + UiTheme.Px(10f), herePill.sizeDelta.y);
            }
        }
    }
}
