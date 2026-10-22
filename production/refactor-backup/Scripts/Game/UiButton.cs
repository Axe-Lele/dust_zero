// A button drawn the way the redesign specifies: a flat face sitting on a solid band.
//
// The guide rules out gradients and gloss. Depth comes from a single band of the edge colour
// under the face; pressing drops the face by 3pt and thins the band to 2pt over 60ms. Because
// the band is a sibling Image rather than a shadow effect, it tints per state with no extra
// material and costs one extra draw call for the whole button set.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>The six states the guide's button board defines.</summary>
    public enum UiButtonState
    {
        Normal,
        /// <summary>Chosen among siblings - cream face, primary border and text.</summary>
        Selected,
        /// <summary>Condition not met. Greyed, still readable, not tappable.</summary>
        Disabled,
        /// <summary>Not yet unlocked. Shows a padlock and the unlock condition.</summary>
        Locked,
        /// <summary>Something is waiting to be collected: green with a red dot.</summary>
        Claimable,
    }

    /// <summary>Which palette the button draws from.</summary>
    public enum UiButtonKind { Primary, Secondary, Navy, Success, Icon, TextDanger }

    /// <summary>
    /// Face + edge button with the redesign's press feel. Build one with
    /// <c>UiKit.Button</c> rather than by hand so the parts stay wired consistently.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                   IPointerClickHandler, IPointerExitHandler
    {
        [SerializeField] RectTransform face;
        [SerializeField] Image faceImage;
        [SerializeField] Image edgeImage;
        [SerializeField] TextMeshProUGUI label;
        [OptionalReference("보조 문구 없는 버튼이 있습니다")]
        [SerializeField] TextMeshProUGUI subLabel;
        [OptionalReference("아이콘 없는 글자 버튼이 있습니다")]
        [SerializeField] Image icon;
        [SerializeField] GameObject lockIcon;
        [SerializeField] GameObject dot;

        [SerializeField] UiButtonKind kind = UiButtonKind.Primary;

        /// <summary>How much of the face the sub line claims, so the main text clears it.</summary>
        [SerializeField] float subBand;

        UiButtonState _state = UiButtonState.Normal;
        bool _held;
        float _restY;
        float _edgeHeight;

        /// <summary>Raised on a completed tap. Ignored unless the button is tappable.</summary>
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

        /// <summary>True when a tap would do something.</summary>
        public bool Interactable => _state != UiButtonState.Disabled && _state != UiButtonState.Locked;

        public string Text
        {
            get => label != null ? label.text : string.Empty;
            set { if (label != null) label.text = value; }
        }

        /// <summary>The smaller line under the main text, e.g. a cost. Hidden when empty.</summary>
        public string SubText
        {
            get => subLabel != null ? subLabel.text : string.Empty;
            set
            {
                if (subLabel == null) return;
                bool show = !string.IsNullOrEmpty(value);
                subLabel.text = value ?? string.Empty;
                subLabel.gameObject.SetActive(show);
                // Lift the main text clear of the sub line rather than letting them overlap.
                if (label != null)
                {
                    var lrt = label.rectTransform;
                    lrt.offsetMin = new Vector2(lrt.offsetMin.x, show ? subBand : UiTheme.S8);
                }
            }
        }

        /// <summary>Sets the sub-line reserve. Called by the builder alongside Bind.</summary>
        public void SetSubBand(float band) => subBand = band;

        /// <summary>Wires the parts. Called by the builder; not meant for gameplay code.</summary>
        public void Bind(RectTransform faceRect, Image faceImg, Image edgeImg,
                         TextMeshProUGUI mainLabel, TextMeshProUGUI sub, Image iconImage,
                         GameObject padlock, GameObject notifyDot, UiButtonKind buttonKind,
                         float edgeHeight)
        {
            face = faceRect; faceImage = faceImg; edgeImage = edgeImg;
            label = mainLabel; subLabel = sub; icon = iconImage;
            lockIcon = padlock; dot = notifyDot; kind = buttonKind;
            _edgeHeight = edgeHeight;
            _restY = 0f;
            Repaint();
        }

        void Awake()
        {
            if (_edgeHeight <= 0f) _edgeHeight = UiTheme.ButtonEdge;
            Repaint();
        }

        // ------------------------------------------------------------------ painting

        (Color face, Color edge, Color text) Palette() => kind switch
        {
            UiButtonKind.Primary => (UiTheme.Primary, UiTheme.PrimaryEdge, Color.white),
            UiButtonKind.Navy => (UiTheme.Navy, UiTheme.NavyEdge, Color.white),
            UiButtonKind.Success => (UiTheme.Success, UiTheme.SuccessEdge, Color.white),
            UiButtonKind.TextDanger => (new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), UiTheme.Danger),
            // Secondary and Icon share the cream face with a line-coloured band.
            _ => (UiTheme.Panel2, UiTheme.PanelLine, UiTheme.Ink),
        };

        void Repaint()
        {
            var (faceColor, edgeColor, textColor) = Palette();

            switch (_state)
            {
                case UiButtonState.Selected:
                    // Cream face, primary border and text - the guide's "선택됨".
                    faceColor = new Color(1f, 0.957f, 0.914f);
                    edgeColor = UiTheme.Primary;
                    textColor = UiTheme.Primary;
                    break;
                case UiButtonState.Disabled:
                case UiButtonState.Locked:
                    faceColor = UiTheme.Disabled;
                    edgeColor = new Color(UiTheme.Disabled.r * 0.9f, UiTheme.Disabled.g * 0.9f,
                                          UiTheme.Disabled.b * 0.9f, 1f);
                    textColor = UiTheme.Ink3;
                    break;
                case UiButtonState.Claimable:
                    faceColor = UiTheme.Success;
                    edgeColor = UiTheme.SuccessEdge;
                    textColor = Color.white;
                    break;
            }

            if (faceImage != null) faceImage.color = faceColor;
            if (edgeImage != null) edgeImage.color = edgeColor;
            if (label != null) label.color = textColor;
            if (subLabel != null) subLabel.color = textColor;
            if (icon != null) icon.color = textColor;
            if (lockIcon != null) lockIcon.SetActive(_state == UiButtonState.Locked);
            if (dot != null) dot.SetActive(_state == UiButtonState.Claimable);

            SetPressed(_held && Interactable);
        }

        /// <summary>Drops the face and thins the band, or restores both.</summary>
        void SetPressed(bool pressed)
        {
            if (face != null)
            {
                var p = face.anchoredPosition;
                face.anchoredPosition = new Vector2(p.x, _restY - (pressed ? UiTheme.PressDrop : 0f));
            }
            if (edgeImage != null)
            {
                var rt = (RectTransform)edgeImage.transform;
                float h = pressed ? UiTheme.PressedEdge : _edgeHeight;
                rt.offsetMin = new Vector2(rt.offsetMin.x, -h);
            }
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
