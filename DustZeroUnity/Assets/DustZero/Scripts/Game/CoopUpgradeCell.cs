// 협동 조작 줄의 강화 버튼 한 칸 — 원본 `.rd-up`.
//
// 생김새는 ProjectBuilder 가 프리팹으로 짓는다. 여기서는 값만 채운다.
using DustZero.Core.Coop;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    /// <summary>One appliance's type-upgrade button. 그 종류 전체를 올린다.</summary>
    public sealed class CoopUpgradeCell : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] TextMeshProUGUI costLabel;
        [SerializeField] UiButton button;
        [SerializeField] CanvasGroup group;
        [SerializeField] SpriteLibrary sprites;

        [Header("등급 바탕")]
        [SerializeField] Color rareTint = new(0.863f, 0.922f, 0.980f);
        [SerializeField] Color superRareTint = new(1f, 0.902f, 0.780f);

        string _type;

        public void Bind(GameServices svc, string type, System.Action onTap)
        {
            _type = type;
            var def = svc.Db.Unit(type);

            if (icon != null)
            {
                icon.sprite = sprites != null ? sprites.Unit(type) : null;
                icon.enabled = icon.sprite != null;
            }
            // 원본은 SR/SSR 에만 위에서 아래로 흐리는 그라데이션을 깐다. 이 크기에서는
            // 단색이 같은 구실을 한다 — 본편 카드도 같은 처리다.
            if (face != null)
                face.color = def.Rarity switch
                {
                    Rarity.SSR => superRareTint,
                    Rarity.SR => rareTint,
                    _ => Color.white,
                };

            button?.SetAction(onTap);
        }

        public void Refresh(CoopSim coop)
        {
            if (_type == null) return;
            int level = coop.UpgradeLevel(CoopSide.Player, _type);
            var cost = coop.UpgradeCost(CoopSide.Player, _type);

            if (levelLabel != null) levelLabel.text = $"강화 {level}";
            if (costLabel != null)
                costLabel.text = cost == null ? "최고" : $"{UiTheme.Compact(cost.Value * 100f)}W";

            // 원본 `.rd-up.off` 는 opacity .55 로 흐릴 뿐 누를 수는 있다 —
            // 눌러야 "전력이 모자라요" 안내가 나오기 때문이다.
            bool affordable = cost != null && coop.PowerOf(CoopSide.Player) >= cost.Value;
            if (group != null) group.alpha = affordable ? 1f : 0.55f;
        }
    }
}
