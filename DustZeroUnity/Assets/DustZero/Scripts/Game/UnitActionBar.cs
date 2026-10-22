// The tray contents while a deployed appliance is selected.
//
// Redesign 1c: tapping a unit swaps the card row for this bar - a read-out of what is
// selected on the left, then boost, multitap and retreat, then a close button. The costs
// move as power accrues, so the bar re-reads the simulation every frame rather than only on
// open; nothing here decides anything, it just shows what BattleSim will allow.
using DustZero.Core.Defense;
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>Boost / multitap / retreat for the selected unit.</summary>
    public sealed class UnitActionBar : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI metaLabel;
        [SerializeField] TextMeshProUGUI statusLabel;
        [SerializeField] UiButton boostButton;
        [SerializeField] UiButton multitapButton;
        [SerializeField] UiButton retreatButton;
        [SerializeField] UiButton closeButton;

        BattleSim _sim;
        BattleUnit _unit;

        /// <summary>The unit on show, or null when the bar is closed.</summary>
        public BattleUnit Unit => _unit;

        /// <summary>Raised when the player dismisses the bar.</summary>
        public event System.Action Closed;
        /// <summary>Raised after a successful action, so the screen can refresh the rest of the HUD.</summary>
        public event System.Action Acted;

        public void Bind(TextMeshProUGUI unitName, TextMeshProUGUI meta, TextMeshProUGUI status,
                         UiButton boost, UiButton multitap, UiButton retreat, UiButton close)
        {
            nameLabel = unitName; metaLabel = meta; statusLabel = status;
            boostButton = boost; multitapButton = multitap;
            retreatButton = retreat; closeButton = close;
            Hook();
        }

        void Awake() => Hook();

        bool _hooked;

        void Hook()
        {
            if (_hooked || boostButton == null) return;
            _hooked = true;
            boostButton.SetAction(() => { if (_sim != null && _sim.Boost(_unit)) Acted?.Invoke(); });
            multitapButton.SetAction(() => { if (_sim != null && _sim.BuyMultitap()) Acted?.Invoke(); });
            retreatButton.SetAction(() =>
            {
                if (_sim == null || _unit == null) return;
                _sim.Retreat(_unit);
                Acted?.Invoke();
                Close();
            });
            closeButton.SetAction(Close);
        }

        /// <summary>Opens the bar on a unit.</summary>
        public void Show(BattleSim sim, BattleUnit unit)
        {
            _sim = sim;
            _unit = unit;
            gameObject.SetActive(unit != null);
            Refresh();
        }

        public void Close()
        {
            _unit = null;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Re-reads the simulation. Cheap enough to call every frame.</summary>
        public void Refresh()
        {
            if (_sim == null || _unit == null || _unit.Dead) return;
            var def = _unit.Def;

            // 10.17-d: name carries the boost stars, so the panel, the boost button and the
            // badge on the board all show the same count. Lv. is the ROSTER level - it used
            // to print the boost tier, which made a tier-2 appliance read as "Lv.2".
            if (nameLabel != null)
                nameLabel.text = _unit.Tier > 1
                    ? $"{def.Name} {new string('★', _unit.Tier)}" : def.Name;
            if (metaLabel != null)
                metaLabel.text = $"Lv.{_sim.LevelOf(_unit.Type)} · " +
                                 (_unit.IsBlocker ? "지상 저지" : "가구 지원");
            if (statusLabel != null)
            {
                statusLabel.text = _unit.Overheated ? "과열 - 냉각 중"
                                 : _unit.SkillActive ? "스킬 발동 중"
                                 : "정상 작동";
                statusLabel.color = _unit.Overheated ? UiTheme.Danger : UiTheme.Ink2;
            }

            RefreshBoost();
            RefreshMultitap();
            RefreshRetreat();
        }

        void RefreshBoost()
        {
            if (boostButton == null) return;
            float? cost = _sim.BoostCost(_unit);
            if (cost == null)
            {
                // Already at the top tier the table defines.
                boostButton.Text = "강화 MAX";
                boostButton.SubText = string.Empty;
                boostButton.State = UiButtonState.Disabled;
                return;
            }
            // 원본과 다른 점: the build writes "강화 ★★ · 2,000W" across a full-width button.
            // This one is a small square - "강화 ★★" ellipsized to "강화 …", and "★★ 500W" on
            // the sub line needs 148px of 133px. The star count lives where it fits and where
            // it matters: the board badge and the panel name, which agree with each other.
            boostButton.Text = "강화";
            boostButton.SubText = UiTheme.Watts(cost.Value);
            boostButton.State = _sim.CanBoost(_unit) ? UiButtonState.Normal : UiButtonState.Disabled;
        }

        void RefreshMultitap()
        {
            if (multitapButton == null) return;
            float? cost = _sim.NextTapCost;
            multitapButton.Text = "멀티탭";
            if (cost == null)
            {
                multitapButton.SubText = "남은 0";
                multitapButton.State = UiButtonState.Disabled;
                return;
            }
            // The guide left the multitap's effect as a guess; 10.15-h's buyTap raises the
            // deploy cap, so the cost is what the player needs to see here.
            multitapButton.SubText = UiTheme.Watts(cost.Value);
            multitapButton.State = _sim.Power >= cost.Value ? UiButtonState.Normal : UiButtonState.Disabled;
        }

        void RefreshRetreat()
        {
            if (retreatButton == null) return;
            retreatButton.Text = "철수";
            retreatButton.SubText = "+" + UiTheme.Watts(_sim.RetreatRefund(_unit));
            retreatButton.State = UiButtonState.Normal;
        }
    }
}
