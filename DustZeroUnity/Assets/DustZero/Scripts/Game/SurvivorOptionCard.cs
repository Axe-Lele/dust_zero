// One level-up choice card for 대청소: an icon (or glyph for a passive), what it does, and
// a tap target. SurvivorScreen rebuilds up to three of these each time a level-up is pending.
using System;
using DustZero.Core.Survivor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class SurvivorOptionCard : MonoBehaviour
    {
        [SerializeField] Image frame;
        [SerializeField] Image icon;
        [SerializeField] Image icon2;
        [SerializeField] TextMeshProUGUI glyphLabel;
        [SerializeField] TextMeshProUGUI tagLabel;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI descLabel;
        [SerializeField] Button button;

        public void Bind(SurvOptionView view, SpriteLibrary sprites, Action onPick)
        {
            if (tagLabel != null) tagLabel.text = view.Tag ?? string.Empty;
            if (nameLabel != null) nameLabel.text = view.Name ?? string.Empty;
            if (descLabel != null) descLabel.text = view.Desc ?? string.Empty;

            bool hasIcon = !string.IsNullOrEmpty(view.Icon);
            if (icon != null)
            {
                icon.sprite = hasIcon && sprites != null ? sprites.Unit(view.Icon) : null;
                icon.enabled = icon.sprite != null;
            }
            if (glyphLabel != null)
            {
                glyphLabel.text = view.Glyph ?? string.Empty;
                glyphLabel.gameObject.SetActive(!hasIcon);
            }
            if (icon2 != null)
            {
                bool hasIcon2 = !string.IsNullOrEmpty(view.Icon2);
                icon2.sprite = hasIcon2 && sprites != null ? sprites.Unit(view.Icon2) : null;
                icon2.gameObject.SetActive(hasIcon2 && icon2.sprite != null);
            }
            if (frame != null) frame.color = view.Hot ? UiTheme.Premium : UiTheme.Panel;

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onPick?.Invoke());
            }
        }
    }
}
