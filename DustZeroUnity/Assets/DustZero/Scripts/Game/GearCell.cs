// One piece in the 장비 가방 grid.
//
// The build draws a bare icon with a caption under it - no card, no frame. The grade shows
// in the icon's colour, which is why the cell tints rather than swapping art.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class GearCell : MonoBehaviour
    {
        [SerializeField] Image plate;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI caption;
        [SerializeField] Button button;
        [SerializeField] Sprite gearAtkIcon;
        [SerializeField] Sprite gearHeatIcon;
        [SerializeField] Sprite gearSpIcon;

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke());
        }

        /// <summary>
        /// Shows a piece. <paramref name="portrait"/> is set only for exclusives, which the
        /// build draws as the appliance itself rather than as a gear type.
        /// </summary>
        public void Bind(GearRow row, string text, Sprite portrait)
        {
            if (caption != null) caption.text = text;

            Color grade = row != null ? UiTheme.GearGrade(row.grade) : UiTheme.Ink3;
            if (plate != null) plate.color = new Color(grade.r, grade.g, grade.b, 0.16f);
            if (icon == null) return;

            if (portrait != null)
            {
                icon.sprite = portrait;
                icon.color = Color.white;
            }
            else
            {
                icon.sprite = row?.type switch
                {
                    "heat" => gearHeatIcon,
                    "sp" => gearSpIcon,
                    _ => gearAtkIcon,
                };
                icon.color = grade;
            }
            icon.enabled = icon.sprite != null;
        }
    }
}
