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
        [SerializeField] Image portrait;
        [SerializeField] Image rarityStripe;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI costLabel;
        [SerializeField] GameObject cooldownRoot;
        [SerializeField] TextMeshProUGUI cooldownLabel;
        [SerializeField] Image chargeFill;
        [SerializeField] Image selectedOutline;
        [SerializeField] CanvasGroup group;
        [SerializeField] SpriteLibrary sprites;

        string _type;

        public void Bind(UnitDef def, GameDatabase db, Action onClick)
        {
            _type = def.Id;
            if (nameLabel != null) nameLabel.text = def.ShortName;
            if (costLabel != null) costLabel.text = $"{def.Cost * 100:N0}W";
            if (portrait != null && sprites != null) portrait.sprite = sprites.Unit(def.Id);
            if (rarityStripe != null) rarityStripe.color = RarityColor(def.Rarity);
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClick?.Invoke());
            }
        }

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.SSR => new Color(1f, 0.60f, 0.24f),
            Rarity.SR => new Color(0.24f, 0.55f, 0.85f),
            _ => new Color(0.65f, 0.69f, 0.73f),
        };

        public void Sync(BattleSim sim, string selected)
        {
            if (_type == null) return;

            var def = sim.Db.Unit(_type);
            float cd = sim.CooldownOf(_type);
            bool affordable = sim.Affordable(_type);

            if (selectedOutline != null) selectedOutline.enabled = selected == _type;
            if (group != null) group.alpha = affordable ? 1f : 0.45f;

            // The fill doubles as a "how close am I to affording this" meter.
            if (chargeFill != null)
                chargeFill.fillAmount = Mathf.Clamp01(sim.Power / Mathf.Max(0.001f, def.Cost));

            // Toggle the veil, not just its text, so the dimming disappears with the countdown.
            bool onCooldown = cd > 0f;
            if (cooldownRoot != null) cooldownRoot.SetActive(onCooldown);
            if (cooldownLabel != null && onCooldown) cooldownLabel.text = $"{Mathf.Ceil(cd):0}s";
        }
    }
}
