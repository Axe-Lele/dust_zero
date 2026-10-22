// The 상점 tab, redesign 2e.
//
// Category chips over a grid of product cards. The price button carries the payment method:
// orange for cash, cream with a currency mark for crystal, grey when sold out. Cash and ads
// are the original's test providers - no store or ad SDK is involved anywhere here.
using System;
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class ShopTab : MonoBehaviour
    {
        [SerializeField] RectTransform tabRoot;
        [SerializeField] UiToggleChip tabChipPrefab;
        [SerializeField] RectTransform gridRoot;
        /// <summary>Gap between cards in one row. 원본 .shop-grid 의 gap 10 에 해당한다.</summary>
        [SerializeField] float rowSpacing = 25f;
        [SerializeField] ShopCard cardPrefab;
        [SerializeField] TextMeshProUGUI emptyLabel;

        GameServices _svc;
        readonly List<UiToggleChip> _chips = new();
        readonly List<ShopCard> _cards = new();
        readonly List<int> _tabs = new();
        int _tab;

        public event Action<string> Message;

        public void Bind(GameServices services)
        {
            _svc = services;
            BuildTabs();
            BuildCards();
            Refresh();
        }

        void OnEnable()
        {
            if (_svc != null) Refresh();
        }

        void BuildTabs()
        {
            foreach (var c in _chips) if (c != null) Destroy(c.gameObject);
            _chips.Clear();
            _tabs.Clear();

            foreach (var p in _svc.Db.ShopProducts)
                if (!_tabs.Contains(p.tab)) _tabs.Add(p.tab);
            _tabs.Sort();
            if (_tabs.Count > 0) _tab = _tabs[0];

            if (tabRoot == null || tabChipPrefab == null) return;
            foreach (var t in _tabs)
            {
                int which = t;
                var chip = Instantiate(tabChipPrefab, tabRoot);
                // 칩은 제 글자에 맞춰 줄어드는 물건이다(스테이지 구역 줄에서 그렇게 쓴다).
                // 상점 탭은 격자가 폭을 정해 주므로 그 맞춤자를 떼야 글자가 칸 가운데에 온다 —
                // 안 떼면 격자가 칸을 넓혀도 글자는 왼쪽에 붙어 16~27 어긋난다.
                if (chip.TryGetComponent<ContentSizeFitter>(out var fitter)) Destroy(fitter);
                chip.Bind(TabName(which), () => { _tab = which; Refresh(); });
                _chips.Add(chip);
            }
        }

        /// <summary>enums.ShopTab: 1 crystal, 2 daily, 3 package.</summary>
        string TabName(int tab)
        {
            if (tab == _svc.Db.Enum("ShopTab", "Crystal")) return "크리스탈";
            if (tab == _svc.Db.Enum("ShopTab", "Daily")) return "일일 상점";
            if (tab == _svc.Db.Enum("ShopTab", "Package")) return "패키지";
            return $"분류 {tab}";
        }

        /// <summary>
        /// 원본 `.shop-grid` 는 2열 CSS 그리드이고, `.sp.wide` 한 장이 `grid-column:1/-1`
        /// 로 두 칸을 통째로 먹는다. GridLayoutGroup 으로는 그 폭을 못 만들기 때문에
        /// **행을 직접 쌓는다** — 전체폭 카드는 그 행에 혼자, 보통 카드는 둘씩.
        ///
        /// 전체폭 조건은 원본 그대로다:
        ///   p.tab === 패키지  ||  (광고 상품 && p.tab === 크리스탈)
        /// </summary>
        void BuildCards()
        {
            foreach (var c in _cards) if (c != null) Destroy(c.gameObject);
            _cards.Clear();
            foreach (var r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
            if (gridRoot == null || cardPrefab == null) return;

            int packageTab = _svc.Db.Enum("ShopTab", "Package");
            int crystalTab = _svc.Db.Enum("ShopTab", "Crystal");

            RectTransform row = null;
            int inRow = 0;

            foreach (var p in _svc.Db.ShopProducts)
            {
                var product = p;
                bool wide = product.tab == packageTab
                            || (_svc.Profile.IsAdProduct(product) && product.tab == crystalTab);

                if (wide || row == null || inRow >= 2)
                {
                    row = NewRow();
                    inRow = 0;
                }

                var card = Instantiate(cardPrefab, row);
                card.Bind(product, _svc, () => Buy(product));
                _cards.Add(card);
                inRow++;

                // 전체폭 카드는 그 행을 혼자 쓴다. 다음 상품은 새 행에서 시작한다.
                if (wide) { row = null; inRow = 0; }
            }
        }

        readonly List<RectTransform> _rows = new();

        /// <summary>One grid row. Cards inside share the width evenly, as CSS 1fr does.</summary>
        RectTransform NewRow()
        {
            var go = new GameObject("Row", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(gridRoot, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = rowSpacing;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlHeight = true;

            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _rows.Add(rt);
            return rt;
        }

        void Buy(ShopProductRow product)
        {
            switch (_svc.Profile.BlockOn(product))
            {
                case PurchaseBlock.Locked: Message?.Invoke("아직 열리지 않은 상품이에요."); return;
                case PurchaseBlock.SoldOut: Message?.Invoke("이번 기간에는 다 샀어요."); return;
                case PurchaseBlock.TooPoor: Message?.Invoke("크리스탈이 모자라요."); return;
                case PurchaseBlock.Cooling:
                    Message?.Invoke($"{Mathf.CeilToInt((float)_svc.Profile.CooldownLeft(product))}초 뒤에 다시 받을 수 있어요.");
                    return;
            }

            var got = _svc.Profile.Buy(product);
            _svc.Persist();
            Refresh();
            Message?.Invoke(got.Count > 0
                ? $"{_svc.Profile.DescribeRewards(got)} 받았어요!"
                : "적용했어요.");
        }

        public void Refresh()
        {
            if (_svc == null) return;

            for (int i = 0; i < _chips.Count; i++)
                _chips[i].State = _tabs[i] == _tab ? ChipState.Selected : ChipState.Normal;

            int shown = 0;
            foreach (var card in _cards)
            {
                bool mine = card.Product != null && card.Product.tab == _tab;
                card.gameObject.SetActive(mine);
                if (!mine) continue;
                card.Sync();
                shown++;
            }
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(shown == 0);
        }
    }
}
