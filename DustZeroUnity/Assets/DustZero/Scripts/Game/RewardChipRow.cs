// 원본 `.rwc` 칩 줄 — 보상 묶음을 "아이콘 + 숫자" 로 보여 준다.
//
// 10.19-e 를 430x932 에서 재면 아이콘 18x18, 아이콘과 숫자 사이 3, 칩 사이 6, 숫자는 12px
// 굵게다. 원본은 글자 나열("코인 3,200 · 필터 5 · 열선 5 …")을 쓰지 않는다 — 좁은 칸에서
// 말줄임으로 잘려 무엇을 주는지 알 수 없기 때문이다.
using System.Collections.Generic;
using DustZero.Core.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class RewardChipRow : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset font;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] float iconPage = 18f;
        [SerializeField] float fontPage = 12f;
        [SerializeField] Color ink = Color.black;

        readonly List<GameObject> _chips = new();

        /// <summary>Repaints the row from a reward group. An empty group clears it.</summary>
        public void Show(IReadOnlyList<PlayerProfile.RewardChip> chips)
        {
            foreach (var go in _chips) if (go != null) Destroy(go);
            _chips.Clear();
            if (chips == null) return;

            foreach (var chip in chips) Build(chip);
        }

        void Build(PlayerProfile.RewardChip chip)
        {
            var go = new GameObject("Chip", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.spacing = UiTheme.Px(3f);
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            float side = UiTheme.Sq(iconPage);
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(rt, false);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = sprites != null ? sprites.Icon(chip.Icon) : null;
            icon.enabled = icon.sprite != null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.minWidth = iconLe.preferredWidth = side;
            iconLe.minHeight = iconLe.preferredHeight = side;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rt, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = UiTheme.Py(fontPage);
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = ink;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.text = chip.Text;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            var textLe = textGo.AddComponent<LayoutElement>();
            // 글자 칸이 줄 높이보다 낮으면 TMP 는 글자를 아예 그리지 않는다.
            textLe.minHeight = textLe.preferredHeight = UiTheme.Line(UiTheme.Py(fontPage));
            textLe.preferredWidth = tmp.GetPreferredValues(chip.Text).x;

            _chips.Add(go);
        }

        /// <summary>Lets a screen tint the numbers without rebuilding the row's wiring.</summary>
        public Color Ink { get => ink; set => ink = value; }

        /// <summary>
        /// 런타임에 만든 줄에 글꼴과 그림집을 넣는다. 빌더가 프리팹으로 굽지 않고
        /// 화면 쪽에서 직접 만드는 자리(스테이지 탭의 챕터 별 보상)에서 쓴다.
        /// </summary>
        /// <example><code>
        /// var row = go.AddComponent&lt;RewardChipRow&gt;();
        /// row.Configure(numberFont, spriteLibrary);
        /// row.Show(profile.GroupChips(groupId));
        /// </code></example>
        public void Configure(TMP_FontAsset numberFont, SpriteLibrary library)
        {
            if (numberFont != null) font = numberFont;
            if (library != null) sprites = library;
        }
    }
}
