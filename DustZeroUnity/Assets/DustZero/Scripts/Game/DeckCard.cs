// One deployable appliance in the battle deck strip.
using System;
using DustZero.Core.Defense;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class DeckCard : MonoBehaviour
    {
        [SerializeField] Button button;
        /// <summary>The card face. Flat in 10.16-n; the state lives in opacity and the ring.</summary>
        [SerializeField] Image frame;
        /// <summary>Tinted gradient down the top of the face, carrying the rarity.</summary>
        [SerializeField] Image rarityWash;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI costLabel;
        [SerializeField] GameObject cooldownRoot;
        [SerializeField] TextMeshProUGUI cooldownLabel;
        /// <summary>3px along the bottom edge: how close the player is to affording this.</summary>
        [SerializeField] Image chargeFill;
        [SerializeField] Image selectedOutline;
        [SerializeField] CanvasGroup group;
        [SerializeField] SpriteLibrary sprites;

        string _type;

        public void Bind(UnitDef def, GameDatabase db, Action onClick)
        {
            _type = def.Id;
            if (nameLabel != null) nameLabel.text = def.ShortName;
            if (costLabel != null) costLabel.text = UiTheme.Watts(def.Cost);
            if (portrait != null && sprites != null) portrait.sprite = sprites.Unit(def.Id);
            if (rarityWash != null)
            {
                var wash = RarityWash(def.Rarity);
                // Common appliances get no wash at all - the build only gradients SR and SSR.
                rarityWash.enabled = wash.a > 0f;
                rarityWash.color = wash;
            }
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClick?.Invoke());
            }
        }

        /// <summary>The build's --sr-bg / --ssr-bg, which wash the card rather than tint a stripe.</summary>
        public static Color RarityWash(Rarity r) => r switch
        {
            Rarity.SSR => new Color32(0xFF, 0xE6, 0xCF, 0xFF),   // --ssr-bg
            Rarity.SR => new Color32(0xDC, 0xE9, 0xF7, 0xFF),    // --sr-bg
            _ => new Color(0f, 0f, 0f, 0f),
        };

        public void Sync(BattleSim sim, string selected)
        {
            if (_type == null) return;

            var def = sim.Db.Unit(_type);
            float cd = sim.CooldownOf(_type);
            bool affordable = sim.Affordable(_type);
            bool isSelected = selected == _type;
            bool full = sim.Units.Count >= sim.MaxUnits;

            // 10.16-n fades rather than reframes: .off is 55% when you cannot pay or are out
            // of slots, and selection is a --cool ring on top of whatever that opacity is.
            if (group != null) group.alpha = (!affordable || full) ? 0.55f : 1f;
            if (selectedOutline != null) selectedOutline.enabled = isSelected;

            if (chargeFill != null)
                chargeFill.fillAmount = Mathf.Clamp01(sim.Power / Mathf.Max(0.001f, def.Cost));

            // Toggle the veil, not just its text, so the dimming disappears with the countdown.
            bool onCooldown = cd > 0f;
            if (cooldownRoot != null) cooldownRoot.SetActive(onCooldown);
            if (cooldownLabel != null && onCooldown) cooldownLabel.text = $"{Mathf.Ceil(cd):0}s";
        }
    }
}
