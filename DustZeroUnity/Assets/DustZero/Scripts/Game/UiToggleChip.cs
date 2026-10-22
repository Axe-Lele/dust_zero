// A pill that can be off, on, or locked, with its palette chosen at build time.
//
// UiButton paints itself from the sprite pack's baked faces, which cannot express the two
// palettes the redesign uses for selectable pills: the stage tab's chapter row turns the open
// chapter into a dark solid, while its segmented sub-tabs turn the active half white on a
// sunken ground. Both are the same widget with different colours, so the colours are data.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public enum ChipState { Normal, Selected, Locked }

    public sealed class UiToggleChip : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] GameObject lockIcon;
        [SerializeField] Button button;
        [OptionalReference("레이아웃이 여백을 맡으면 필요 없다")]
        [SerializeField] RectTransform labelRect;

        [Header("팔레트")]
        [SerializeField] Color faceNormal = Color.white;
        [SerializeField] Color faceSelected = Color.black;
        [SerializeField] Color faceLocked = Color.grey;
        [SerializeField] Color inkNormal = Color.black;
        [SerializeField] Color inkSelected = Color.white;
        [SerializeField] Color inkLocked = Color.grey;

        /// <summary>Left inset for the text when no padlock is showing.</summary>
        [SerializeField] float textInset = 16f;
        /// <summary>Left inset once the padlock takes the lead position.</summary>
        [SerializeField] float lockedTextInset = 34f;

        ChipState _state = ChipState.Normal;

        public ChipState State
        {
            get => _state;
            set { _state = value; Repaint(); }
        }

        public string Text
        {
            get => label != null ? label.text : string.Empty;
            set { if (label != null) label.text = value; }
        }

        /// <summary>Sets the text and what happens on a tap.</summary>
        /// <summary>
        /// 이 칩을 여러 줄로 쓴다. 기본 칩은 한 줄(NoWrap + Ellipsis)이라 줄바꿈을 넣으면
        /// '1구...' 처럼 잘린다. 구역 칩은 원본이 세 줄(구역 번호 · 이름 · 진행 중)이다.
        /// </summary>
        public void AllowMultiLine(float height)
        {
            if (label != null)
            {
                label.textWrappingMode = TMPro.TextWrappingModes.Normal;
                label.overflowMode = TMPro.TextOverflowModes.Overflow;
                label.alignment = TMPro.TextAlignmentOptions.Center;
            }
            var le = GetComponent<UnityEngine.UI.LayoutElement>();
            if (le == null) le = gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            // 구역 줄은 자식 높이를 제어하지 않는다(childControlHeight = false). 그래서
            // LayoutElement 만으로는 안 커지고 프리팹의 sizeDelta 가 그대로 남는다.
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);

            // 기본 칩은 한 줄짜리라 위아래 여백이 넣넉하다(10+10). 세 줄을 넣으면
            // 여백을 뺀 내용 자리가 글자보다 짧아져 위아래로 넘친다. 줄임으로 줄인다.
            var pad = GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (pad != null)
                pad.padding = new RectOffset(pad.padding.left, pad.padding.right, 2, 2);

            // 줄 간격을 원본만큼 좁힌다. TMP 기본은 글자 크기의 1.2배라 세 줄이면
            // 칩을 넘친다 — 원본 HTML 은 15+16+14 를 47 안에 거의 붙여 넣는다.
            if (label != null) label.lineSpacing = -22f;
            var lrt = label != null ? label.rectTransform : null;
            if (lrt != null)
            {
                var lle = lrt.GetComponent<UnityEngine.UI.LayoutElement>();
                if (lle != null) lle.minHeight = height;
            }
        }

        /// <summary>
        /// 글자를 넣은 **뒤에** 부르면 실제로 필요한 높이로 칩을 키운다.
        /// 몇 줄이 될지는 글꼴과 글자 크기가 정하므로 눈대중으로 적으면 넘치거나 뜬다.
        /// </summary>
        public void FitToText(float minHeight)
        {
            if (label == null) return;
            label.ForceMeshUpdate();
            float padY = 0f;
            var hlg = GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg != null) padY = hlg.padding.top + hlg.padding.bottom;

            float want = Mathf.Max(minHeight, label.preferredHeight + padY);
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, want);

            var le = GetComponent<UnityEngine.UI.LayoutElement>();
            if (le != null) le.minHeight = le.preferredHeight = want;
            var lle = label.rectTransform.GetComponent<UnityEngine.UI.LayoutElement>();
            if (lle != null) lle.minHeight = label.preferredHeight;
        }

        public void Bind(string text, Action onTap)
        {
            Text = text;
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onTap?.Invoke());
        }

        void Awake() => Repaint();

        void Repaint()
        {
            bool locked = _state == ChipState.Locked;
            bool on = _state == ChipState.Selected;

            if (lockIcon != null) lockIcon.SetActive(locked);
            if (button != null) button.interactable = !locked;
            if (face != null) face.color = on ? faceSelected : locked ? faceLocked : faceNormal;
            if (label != null) label.color = on ? inkSelected : locked ? inkLocked : inkNormal;
            if (labelRect != null)
                labelRect.offsetMin = new Vector2(locked ? lockedTextInset : textInset,
                                                  labelRect.offsetMin.y);
        }
    }
}
