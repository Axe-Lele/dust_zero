// One of the six 편성 slots.
//
// Not a small UnitCard: the build draws `.slot` as a 3:4 box that is a dashed --line outline
// over nothing while empty and reads "빈 슬롯", and switches to a solid outline over the
// rarity tint with a 34px portrait and the appliance's short name once filled.
using System;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class DeckSlot : MonoBehaviour
    {
        [SerializeField] Image fill;
        [SerializeField] Image outline;
        [SerializeField] Sprite solidOutline;
        [SerializeField] Sprite dashedOutline;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] Button button;
        [SerializeField] SpriteLibrary sprites;

        public UnitDef Unit { get; private set; }

        /// <summary>Raised on a tap. The appliance is null when the slot is empty.</summary>
        public event Action<UnitDef> Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke(Unit));
        }

        /// <summary>Page-space metrics: a filled slot is 40x62, an empty one 38x50.7.</summary>
        const float FilledW = 40f, FilledH = 62f, EmptyW = 38f, EmptyH = 50.7f;

        public void Bind(UnitDef unit, SpriteLibrary library)
        {
            Unit = unit;
            if (library != null) sprites = library;
            bool filled = unit != null;

            // An empty slot has no content to stretch it, so the build draws it smaller.
            var rt = (RectTransform)transform;
            rt.sizeDelta = filled
                ? new Vector2(UiTheme.Px(FilledW), UiTheme.Py(FilledH))
                : new Vector2(UiTheme.Px(EmptyW), UiTheme.Py(EmptyH));

            // Only SR and SSR carry a tint here; a filled R slot stays on plain --card.
            if (fill != null)
                fill.color = !filled ? new Color(0f, 0f, 0f, 0f)
                    : unit.Rarity >= Rarity.SR ? UiTheme.RarityBg(UiTheme.Tier(unit.Rarity))
                    : UiTheme.Panel;
            if (outline != null)
            {
                var want = filled ? solidOutline : dashedOutline;
                if (want != null) outline.sprite = want;
                outline.color = UiTheme.PanelLine;
            }
            if (portrait != null)
            {
                portrait.sprite = filled && sprites != null ? sprites.Unit(unit.Id) : null;
                portrait.enabled = portrait.sprite != null;
            }
            if (nameLabel != null)
            {
                nameLabel.text = filled ? unit.ShortName : "빈 슬롯";
                nameLabel.color = filled ? UiTheme.Ink : UiTheme.Ink2;
                nameLabel.fontSize = UiTheme.Py(filled ? 12.5f : 11f);
            }
        }
    }
}
