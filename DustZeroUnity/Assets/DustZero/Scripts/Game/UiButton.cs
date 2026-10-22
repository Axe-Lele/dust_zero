// A button that swaps the art pack's faces for its states.
//
// The pack bakes each button's face and the solid band beneath it into one 9-slice sprite -
// btn_primary, btn_primary_pressed and so on - so depth and the press feel come from the art
// rather than from a second Image being resized. Pressing swaps the sprite and drops the
// contents by 8px, which is what the pack's own notes specify.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>The states the guide's button board defines.</summary>
    public enum UiButtonState
    {
        Normal,
        /// <summary>Chosen among siblings.</summary>
        Selected,
        /// <summary>Condition not met. Greyed, still readable, not tappable.</summary>
        Disabled,
        /// <summary>Not yet unlocked. Shows a padlock and the unlock condition.</summary>
        Locked,
        /// <summary>Something is waiting to be collected: green with a red dot.</summary>
        Claimable,
    }

    /// <summary>Which face set the button draws from.</summary>
    public enum UiButtonKind
    {
        Primary, Secondary, Navy, Success, Icon, TextDanger,
        /// <summary>An item in the dark bottom bar: no ground of its own, light ink.</summary>
        Nav,
        /// <summary>A translucent dark pill floating on the room - 출석부, 자동 청소, 설정.</summary>
        Dark,
        /// <summary>An opaque dark plate - the 대청소 row above the next-stage CTA.</summary>
        DarkSolid,
        /// <summary>
        /// 어두운 카드 **위에** 얹는 판. 원본 `--panel-2`(#2A3136) 다 — 소환 배너의
        /// [1회 소환] 처럼 카드와 같은 어두운 색(`--panel`)을 쓰면 버튼이 보이지 않는다.
        /// </summary>
        DarkRaised,
        /// <summary>
        /// Keeps whatever surface the builder gave it. Bind receives no background, so
        /// Repaint never recolours or re-sprites it - that is what lets the art viewer's
        /// translucent white discs and its transparent tabs survive Awake.
        /// </summary>
        Glass,
        /// <summary>The amber "watch an ad" button; its ink is PowerInk, not white.</summary>
        Power,
    }

    [RequireComponent(typeof(RectTransform))]
    public sealed class UiButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                   IPointerClickHandler, IPointerExitHandler
    {
        [Header("그림")]
        [OptionalReference("납작한 탭 항목에는 배경이 없습니다")]
        [SerializeField] Image background;
        [OptionalReference("납작한 탭 항목에는 배경이 없습니다")]
        [SerializeField] Sprite normalSprite;
        [OptionalReference("납작한 탭 항목에는 배경이 없습니다")]
        [SerializeField] Sprite pressedSprite;
        [OptionalReference("납작한 탭 항목에는 배경이 없습니다")]
        [SerializeField] Sprite disabledSprite;
        [OptionalReference("납작한 탭 항목에는 배경이 없습니다")]
        [SerializeField] Sprite selectedSprite;

        [Header("내용")]
        /// <summary>Holds the label and icon so a press moves them together.</summary>
        [SerializeField] RectTransform content;
        [SerializeField] TextMeshProUGUI label;
        [OptionalReference("보조 문구 없는 버튼이 있습니다")]
        [SerializeField] TextMeshProUGUI subLabel;
        [OptionalReference("아이콘 없는 글자 버튼이 있습니다")]
        [SerializeField] Image icon;
        [OptionalReference("잠금 표시가 없는 버튼이 있습니다")]
        [SerializeField] GameObject lockIcon;
        [OptionalReference("알림 점이 없는 버튼이 있습니다")]
        [SerializeField] GameObject dot;

        [SerializeField] UiButtonKind kind = UiButtonKind.Primary;
        [SerializeField] float subBand;

        /// <summary>The pack's press offset.</summary>
        public const float PressDrop = 8f;

        UiButtonState _state = UiButtonState.Normal;
        bool _held;

        public event Action Clicked;

        public UiButtonKind Kind
        {
            get => kind;
            set { kind = value; Repaint(); }
        }

        public UiButtonState State
        {
            get => _state;
            set { _state = value; Repaint(); }
        }

        public bool Interactable => _state != UiButtonState.Disabled && _state != UiButtonState.Locked;

        public string Text
        {
            get => label != null ? label.text : string.Empty;
            set { if (label != null) label.text = value; }
        }

        public string SubText
        {
            get => subLabel != null ? subLabel.text : string.Empty;
            set
            {
                if (subLabel == null) return;
                bool show = !string.IsNullOrEmpty(value);
                subLabel.text = value ?? string.Empty;
                subLabel.gameObject.SetActive(show);
                if (label != null)
                {
                    var lrt = label.rectTransform;
                    lrt.offsetMin = new Vector2(lrt.offsetMin.x, show ? subBand : 0f);
                }
            }
        }

        /// <summary>
        /// Swaps the baked face set, for a button whose role depends on data rather than on
        /// where it sits - a shop price is orange for cash and cream for crystal.
        /// </summary>
        /// <summary>Switches the palette, for a button whose role depends on its data.</summary>
        public void SetKind(UiButtonKind buttonKind)
        {
            kind = buttonKind;
            Repaint();
        }

        public void SetFaces(Sprite normal, Sprite pressed, Sprite disabled, Sprite selected,
                             UiButtonKind buttonKind)
        {
            normalSprite = normal; pressedSprite = pressed;
            disabledSprite = disabled; selectedSprite = selected;
            kind = buttonKind;
            Repaint();
        }

        /// <summary>Keeps the main text clear of something overlaid on the right.</summary>
        public void SetLabelInset(float right)
        {
            if (label == null) return;
            var lrt = label.rectTransform;
            lrt.offsetMax = new Vector2(-right, lrt.offsetMax.y);
        }

        public void SetSubBand(float band) => subBand = band;

        /// <summary>Wires the parts. Called by the builder; not meant for gameplay code.</summary>
        public void Bind(Image bg, Sprite normal, Sprite pressed, Sprite disabled, Sprite selected,
                         RectTransform contentRoot, TextMeshProUGUI mainLabel, TextMeshProUGUI sub,
                         Image iconImage, GameObject padlock, GameObject notifyDot,
                         UiButtonKind buttonKind)
        {
            background = bg;
            normalSprite = normal; pressedSprite = pressed;
            disabledSprite = disabled; selectedSprite = selected;
            content = contentRoot;
            label = mainLabel; subLabel = sub; icon = iconImage;
            lockIcon = padlock; dot = notifyDot; kind = buttonKind;
            Repaint();
        }

        float _restY;
        bool _restKnown;

        void Awake() => Repaint();

        // ------------------------------------------------------------------ painting

        /// <summary>Text and icon colour for the current kind.</summary>
        Color Ink() => kind switch
        {
            UiButtonKind.Primary or UiButtonKind.Navy or UiButtonKind.Success => Color.white,
            UiButtonKind.TextDanger => UiTheme.Danger,
            _ => UiTheme.Ink,
        };

        void Repaint()
        {
            // 10.16-n paints one flat rounded shape and changes its colour per state, so the
            // four baked faces the earlier art pack supplied are no longer swapped between.
            // A flat icon item has no ground at all and marks selection by ink alone.
            var ink = kind == UiButtonKind.Icon && _state == UiButtonState.Selected
                ? UiTheme.Primary
                : UiTheme.ButtonInk(kind, _state);

            if (background != null)
            {
                background.color = UiTheme.ButtonFill(kind, _state);
                if (normalSprite != null) background.sprite = normalSprite;
            }
            if (label != null) label.color = ink;
            if (subLabel != null) subLabel.color = ink;
            if (icon != null) icon.color = ink;
            if (lockIcon != null) lockIcon.SetActive(_state == UiButtonState.Locked);
            if (dot != null) dot.SetActive(_state == UiButtonState.Claimable);

            SetPressed(_held && Interactable);
        }

        void SetPressed(bool pressed)
        {
            // Pressing darkens the flat ground instead of swapping to a pre-painted face.
            if (background != null && Interactable)
            {
                var fill = UiTheme.ButtonFill(kind, _state);
                background.color = pressed ? fill * new Color(0.88f, 0.88f, 0.88f, 1f) : fill;
            }

            if (content == null) return;
            // The rest position is captured rather than assumed to be zero: a content rect
            // whose position a layout group owns would otherwise be dragged to the origin
            // every time the button repainted.
            if (!_restKnown) { _restY = content.anchoredPosition.y; _restKnown = true; }
            content.anchoredPosition = new Vector2(content.anchoredPosition.x,
                                                   pressed ? _restY - PressDrop : _restY);
        }

        // ------------------------------------------------------------------ input

        public void OnPointerDown(PointerEventData _)
        {
            if (!Interactable) return;
            _held = true;
            SetPressed(true);
        }

        public void OnPointerUp(PointerEventData _)
        {
            _held = false;
            SetPressed(false);
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (!_held) return;
            _held = false;
            SetPressed(false);
        }

        public void OnPointerClick(PointerEventData _)
        {
            if (Interactable) Clicked?.Invoke();
        }

        /// <summary>Replaces every listener. Screens rebuild rows, so this is the common case.</summary>
        public void SetAction(Action action)
        {
            Clicked = null;
            if (action != null) Clicked += action;
        }
    }
}
