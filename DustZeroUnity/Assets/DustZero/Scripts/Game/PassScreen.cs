// Season pass, redesign 2f.
//
// A level pillar down the middle with the free track on its left and the premium track on
// its right. Claimed cells dim and check, claimable cells wear a green rim, and premium
// cells stay locked until the season pass product is bought.
using System;
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class PassScreen : MonoBehaviour
    {
        [Header("머리말")]
        [SerializeField] UiButton backButton;
        [SerializeField] TextMeshProUGUI daysLabel;
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] TextMeshProUGUI seasonLabel;
        [SerializeField] TextMeshProUGUI xpLabel;
        [SerializeField] RectTransform xpFill;

        [Header("프리미엄")]
        [SerializeField] GameObject premiumRoot;
        [SerializeField] UiButton premiumButton;
        [SerializeField] TextMeshProUGUI premiumSubLabel;

        [Header("목록")]
        [SerializeField] RectTransform listRoot;
        [SerializeField] PassRow rowPrefab;
        [SerializeField] RectTransform pillarFill;

        [Header("한 번에")]
        [SerializeField] UiButton claimAllButton;
        [SerializeField] TextMeshProUGUI claimAllBadge;

        GameServices _svc;
        readonly List<PassRow> _rows = new();

        public event Action Closed;
        public event Action<string> Message;

        void Awake()
        {
            if (backButton != null) backButton.SetAction(Close);
            if (claimAllButton != null) claimAllButton.SetAction(ClaimAll);
            if (premiumButton != null) premiumButton.SetAction(BuyPremium);
        }

        public void Open(GameServices services)
        {
            _svc = services;
            gameObject.SetActive(true);
            BuildRows();
            Refresh();
        }

        void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void BuildRows()
        {
            foreach (var r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
            if (listRoot == null || rowPrefab == null) return;

            foreach (var level in _svc.Db.PassLevels)
            {
                var row = Instantiate(rowPrefab, listRoot);
                row.Bind(level, _svc, Claim);
                _rows.Add(row);
            }
        }

        void Claim(PassLevelRow level, bool paid)
        {
            var got = _svc.Profile.ClaimPass(level, paid);
            if (got.Count == 0) return;
            _svc.Persist();
            Refresh();
            Message?.Invoke($"{_svc.Profile.DescribeRewards(got)} 받았어요!");
        }

        void ClaimAll()
        {
            var got = _svc.Profile.ClaimAllPass();
            if (got.Count == 0) { Message?.Invoke("받을 보상이 없어요."); return; }
            _svc.Persist();
            Refresh();
            Message?.Invoke($"{_svc.Profile.DescribeRewards(got)} 받았어요!");
        }

        /// <summary>
        /// The premium track unlocks through the shop's "pass:season" product, which is the
        /// original's test cash provider. Nothing here contacts a store.
        /// </summary>
        void BuyPremium()
        {
            ShopProductRow product = null;
            foreach (var p in _svc.Db.ShopProducts) if (p.special == "pass:season") product = p;
            if (product == null) { Message?.Invoke("시즌 패스 상품이 상점에 없어요."); return; }

            var got = _svc.Profile.Buy(product);
            if (!_svc.Profile.PassPaid) { Message?.Invoke("지금은 구매할 수 없어요."); return; }
            _svc.Persist();
            Refresh();
            Message?.Invoke("프리미엄 패스를 켰어요. 테스트 결제입니다.");
        }

        public void Refresh()
        {
            if (_svc == null) return;
            var profile = _svc.Profile;

            if (daysLabel != null) daysLabel.text = $"D-{profile.SeasonDaysLeft()}";
            if (levelLabel != null) levelLabel.text = profile.PassLevel.ToString();
            if (seasonLabel != null) seasonLabel.text = $"시즌 {profile.CurrentSeason().Id}";

            var (into, span) = profile.PassLevelProgress();
            if (xpLabel != null)
                xpLabel.text = span <= 0 ? "최고 레벨" : $"다음 레벨까지 {into} / {span}";
            if (xpFill != null)
                xpFill.anchorMax = new Vector2(span <= 0 ? 1f : Mathf.Clamp01(into / (float)span),
                                               xpFill.anchorMax.y);

            // The pillar fills to the share of levels already reached.
            if (pillarFill != null)
            {
                int max = Mathf.Max(1, profile.PassMaxLevel);
                pillarFill.anchorMax = new Vector2(pillarFill.anchorMax.x,
                                                   Mathf.Clamp01(profile.PassLevel / (float)max));
            }

            bool paid = profile.PassPaid;
            if (premiumRoot != null) premiumRoot.SetActive(!paid);
            if (premiumSubLabel != null && !paid)
            {
                ShopProductRow product = null;
                foreach (var p in _svc.Db.ShopProducts) if (p.special == "pass:season") product = p;
                premiumSubLabel.text = product != null
                    ? $"₩{product.price:N0} · 테스트 결제"
                    : "상점에 상품이 없어요";
            }

            foreach (var row in _rows) row.Sync();

            int claimable = profile.PassClaimableCount();
            if (claimAllButton != null)
                claimAllButton.State = claimable > 0 ? UiButtonState.Normal : UiButtonState.Disabled;
            if (claimAllBadge != null)
            {
                claimAllBadge.gameObject.SetActive(claimable > 0);
                claimAllBadge.text = claimable.ToString();
            }
        }
    }
}
