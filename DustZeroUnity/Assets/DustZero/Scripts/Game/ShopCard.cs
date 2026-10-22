// One product tile in the shop grid.
using System;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] Image face;
        [SerializeField] Image art;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI amountLabel;
        [SerializeField] TextMeshProUGUI limitLabel;
        [SerializeField] GameObject limitRoot;
        [SerializeField] GameObject badgeRoot;
        [SerializeField] Image badgePlate;
        [SerializeField] TextMeshProUGUI badgeLabel;
        /// <summary>원본 `.bd.first` — 아직 안 산 첫 구매 보너스. 빨강이다.</summary>
        [SerializeField] GameObject firstBadgeRoot;
        [SerializeField] Image firstBadgePlate;
        [SerializeField] TextMeshProUGUI firstBadgeLabel;
        [SerializeField] TextMeshProUGUI descLabel;
        [SerializeField] TextMeshProUGUI metaLabel;
        [SerializeField] UiButton priceButton;
        [SerializeField] Image priceCurrencyIcon;
        [SerializeField] CanvasGroup group;

        /// <summary>원본 `.sp.ad` 의 크림 바탕 #FFFBEF.</summary>
        static readonly Color AdCardFace = new(1f, 0.984f, 0.937f);

        [Header("재화 아이콘")]
        [SerializeField] Sprite crystalIcon;
        [SerializeField] Sprite coinIcon;

        [Header("가격 버튼 면")]
        /// <summary>Orange for cash, green for an ad, cream for crystal - the mockup splits
        /// the price button by payment method.</summary>
        [SerializeField] Sprite[] cashFaces;
        [SerializeField] Sprite[] adFaces;
        [SerializeField] Sprite[] crystalFaces;

        GameServices _svc;

        public ShopProductRow Product { get; private set; }

        public void Bind(ShopProductRow product, GameServices services, Action onBuy)
        {
            Product = product;
            _svc = services;
            if (priceButton != null) priceButton.SetAction(() => onBuy?.Invoke());

            var name = services.Db.Text(product.nameKey);
            var contents = services.Profile.DescribeProduct(product);
            if (nameLabel != null) nameLabel.text = name;
            // The contents line is dropped when it only repeats the product name.
            if (amountLabel != null)
            {
                bool useful = !string.IsNullOrEmpty(contents) && contents != name;
                amountLabel.gameObject.SetActive(useful);
                amountLabel.text = useful ? contents : string.Empty;
            }
            if (descLabel != null)
            {
                string desc = services.Db.Text(product.descKey);
                bool hasDesc = !string.IsNullOrEmpty(desc) && desc != product.descKey;
                descLabel.gameObject.SetActive(hasDesc);
                if (hasDesc) descLabel.text = desc;
            }
            // 원본은 배지를 **둘까지** 왼쪽부터 늘어놓는다:
            //   if (first)       → .bd.first  "첫 구매 2배"  (빨강)
            //   if (p.badgeKey)  → .bd.free(광고=노랑) / .bd.hot(그 외=주황)
            if (firstBadgeRoot != null)
            {
                bool first = product.firstBonusGroup != 0
                             && !services.Profile.FirstBonusTaken(product);
                firstBadgeRoot.SetActive(first);
                if (first)
                {
                    if (firstBadgeLabel != null) firstBadgeLabel.text = "첫 구매 2배";
                    if (firstBadgePlate != null) firstBadgePlate.color = UiTheme.Danger;
                    if (firstBadgeLabel != null) firstBadgeLabel.color = Color.white;
                }
            }
            if (badgeRoot != null)
            {
                string badge = string.IsNullOrEmpty(product.badgeKey) ? null
                             : services.Db.Text(product.badgeKey);
                bool hasBadge = !string.IsNullOrEmpty(badge) && badge != product.badgeKey;
                badgeRoot.SetActive(hasBadge);
                if (hasBadge && badgeLabel != null) badgeLabel.text = badge;
                if (hasBadge && badgePlate != null)
                    badgePlate.color = services.Profile.IsAdProduct(product)
                        ? UiTheme.Power : UiTheme.Primary;
            }
            // The tile shows what you get: the first item the reward group pays out. Without
            // this the art slot drew a blank white square on every card.
            if (art != null)
            {
                art.sprite = RewardIcon(product);
                art.enabled = art.sprite != null;
            }
            // 원본 `.sp.ad` 는 크림 바탕(#FFFBEF)에 노란 테두리를 두른다.
            if (face != null)
                face.color = services.Profile.IsAdProduct(product) ? AdCardFace : UiTheme.Panel;

            ApplyPriceFace();
            Sync();
        }

        /// <summary>
        /// The icon for whatever the product mainly pays out. Only currencies have art in
        /// the pack, so anything else falls back to the crystal the shop is priced in.
        /// </summary>
        Sprite RewardIcon(ShopProductRow product)
        {
            if (_svc == null || product == null) return crystalIcon;
            if (!_svc.Db.RewardGroups.TryGetValue(product.rewardGroup, out var rows)) return crystalIcon;
            int kindItem = _svc.Db.Enum("RewardKind", "Item");
            foreach (var r in rows)
            {
                // refId is a scalar because a reward can name a unit as well as an item.
                if (r.kind != kindItem || !r.refId.TryAsFloat(out var id)) continue;
                int itemId = Mathf.RoundToInt(id);
                if (itemId == PlayerProfile.CoinItemId) return coinIcon;
                if (itemId == PlayerProfile.CrystalItemId) return crystalIcon;
            }
            return crystalIcon;
        }

        /// <summary>Picks the price button face from how the product is paid for.</summary>
        void ApplyPriceFace()
        {
            if (priceButton == null || _svc == null) return;
            // 원본 10.19-e 의 `.sp .buy` 를 그대로 따른다. 색이 1:1 로 대응한다:
            //   .buy      var(--ink) #1D2226  → DarkSolid
            //   .buy.ad   #F2C14E (--heat)    → Power
            //   .buy.cry  #3D8BD9 (--cool)    → Navy
            // 현금을 파랑(Navy)으로 칠하고 있었다. 파랑은 **크리스탈 가격**의 색이다.
            var kind = _svc.Profile.IsAdProduct(Product) ? UiButtonKind.Power
                     : _svc.Profile.IsCashProduct(Product) ? UiButtonKind.DarkSolid
                     : UiButtonKind.Navy;
            priceButton.SetKind(kind);
        }

        public void Sync()
        {
            if (Product == null || _svc == null) return;
            var profile = _svc.Profile;
            var block = profile.BlockOn(Product);
            bool sold = block == PurchaseBlock.SoldOut;

            // Sold out dims the whole tile, as the mockup specifies.
            if (group != null) group.alpha = sold ? 0.5f : 1f;
            if (face != null) face.color = sold ? UiTheme.Panel2 : UiTheme.Panel;

            int left = profile.PurchasesLeft(Product);
            bool limited = left != int.MaxValue;
            if (limitRoot != null) limitRoot.SetActive(limited);
            if (limitLabel != null && limited) limitLabel.text = $"{Mathf.Max(0, left)}/{Product.limitCount}";
            // Meta and description share one row, as the build's small cards carry only one
            // of them: how many are left matters more than the blurb once a cap applies.
            if (metaLabel != null)
            {
                metaLabel.gameObject.SetActive(limited);
                if (limited)
                    metaLabel.text = $"오늘 {Mathf.Max(0, left)}/{Product.limitCount}회 남음";
            }
            if (descLabel != null && limited) descLabel.gameObject.SetActive(false);

            if (priceButton == null) return;
            priceButton.Text = PriceText(block);
            priceButton.State = block == PurchaseBlock.None ? UiButtonState.Normal : UiButtonState.Disabled;

            if (priceCurrencyIcon != null)
            {
                var icon = profile.IsCrystalProduct(Product) ? crystalIcon : null;
                priceCurrencyIcon.sprite = icon;
                priceCurrencyIcon.enabled = icon != null && !sold;
            }
        }

        string PriceText(PurchaseBlock block)
        {
            if (block == PurchaseBlock.SoldOut) return "매진";
            if (block == PurchaseBlock.Locked) return "잠김";
            if (block == PurchaseBlock.Cooling)
                return $"{Mathf.CeilToInt((float)_svc.Profile.CooldownLeft(Product))}초";

            // Cash and ads are the original's test providers; the label says what the player
            // does, never that a store or ad network was contacted.
            if (_svc.Profile.IsAdProduct(Product)) return "광고 보고 받기";
            if (_svc.Profile.IsCashProduct(Product)) return $"₩{Product.price:N0}";
            return $"{Product.price:N0}";
        }

        /// <summary>Cash products wear the primary face; the rest stay cream.</summary>
        public bool IsCash => _svc != null && _svc.Profile.IsCashProduct(Product);
    }
}
