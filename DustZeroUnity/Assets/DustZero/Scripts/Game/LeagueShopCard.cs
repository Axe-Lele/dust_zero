// 상점 카드 한 장 — 원본 `.lg-sc`.
//
// 74.8x85, 모서리 12, --card 바탕에 1.5 테두리. 등급이 SR·SSR 이면 위쪽이 옅게 물든다.
// 오른쪽 위에 값, 왼쪽 위에 '체험', 아래에 '사면 ★2!' 띠. 팔린 칸은 흐려진다.
using System;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class LeagueShopCard : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image outline;
        [SerializeField] Image wash;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI familyLabel;
        [SerializeField] TextMeshProUGUI costLabel;
        [SerializeField] GameObject costPlate;
        [SerializeField] GameObject trialTag;
        [SerializeField] GameObject ribbon;
        [SerializeField] TextMeshProUGUI ribbonLabel;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button button;

        public event Action Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke());
        }

        /// <summary>
        /// 한 칸을 다시 그린다. <paramref name="unit"/> 가 null 이면 팔린 칸이다.
        /// </summary>
        public void Show(string unitId, UnitDef unit, SpriteLibrary sprites, int cost,
                         bool hot, bool poor, bool trial, string familyName)
        {
            bool has = unit != null;

            if (group != null) group.alpha = !has ? 0.3f : poor ? 0.55f : 1f;
            if (portrait != null)
            {
                portrait.sprite = has && sprites != null ? sprites.Unit(unitId) : null;
                portrait.enabled = portrait.sprite != null;
            }
            if (nameLabel != null) nameLabel.text = has ? unit.Name : string.Empty;
            if (familyLabel != null)
                familyLabel.text = has
                    ? $"{(familyName ?? string.Empty).Replace("가전", string.Empty)}·" +
                      (unit.Tile == TileKind.Ground ? "근접" : "원거리")
                    : "팔렸어요";

            if (costPlate != null) costPlate.SetActive(has);
            if (costLabel != null && has) costLabel.text = cost.ToString();

            if (wash != null)
            {
                wash.gameObject.SetActive(has && unit.Rarity != Rarity.R);
                if (has) wash.color = UiTheme.RarityBg(UiTheme.Tier(unit.Rarity));
            }
            if (outline != null) outline.color = hot ? UiTheme.Led : UiTheme.PanelLine;
            if (trialTag != null) trialTag.SetActive(has && trial);
            if (ribbon != null) ribbon.SetActive(hot);
            if (ribbonLabel != null && hot) ribbonLabel.text = "사면 ★2!";
        }
    }
}
