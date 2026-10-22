// One appliance's exclusive, offered in the 전용장비 교환 list.
//
// The build draws a wide card: the gear icon on a grade-tinted plate, then three stacked
// lines - who it belongs to and whether you hold it, its name, and what the next star would
// give. A row you cannot take (already maxed, or not enough essence) dims rather than hides,
// so the list always shows the full set of twelve.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class GearExchangeRow : MonoBehaviour
    {
        [SerializeField] Image plate;
        [SerializeField] Image iconPlate;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI ownership;
        [SerializeField] TextMeshProUGUI gearName;
        [SerializeField] TextMeshProUGUI effect;
        [SerializeField] Button button;
        [SerializeField] CanvasGroup group;

        public event Action Tapped;

        public string UnitId { get; private set; }

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke());
        }

        /// <summary>
        /// Fills the row. <paramref name="affordable"/> is false when the row is maxed or the
        /// player is short of essence; the build dims those to 45% and stops them responding.
        /// </summary>
        public void Bind(string unitId, GearRow row, Sprite portrait,
                         string ownershipText, string nameText, string effectText,
                         bool affordable)
        {
            UnitId = unitId;
            if (ownership != null) ownership.text = ownershipText;
            if (gearName != null) gearName.text = nameText;
            if (effect != null) effect.text = effectText;

            Color grade = row != null ? UiTheme.GearGrade(row.grade) : UiTheme.Ink3;
            if (iconPlate != null) iconPlate.color = new Color(grade.r, grade.g, grade.b, 0.16f);
            if (icon != null)
            {
                icon.sprite = portrait;
                icon.enabled = portrait != null;
                icon.color = Color.white;
            }
            if (plate != null) plate.color = UiTheme.Panel;

            if (group != null) group.alpha = affordable ? 1f : 0.45f;
            if (button != null) button.interactable = affordable;
        }
    }
}
