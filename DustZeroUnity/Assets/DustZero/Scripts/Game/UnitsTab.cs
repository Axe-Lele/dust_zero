// 가전: the catalogue of every appliance, owned or not.
//
// 10.16-n made this a 도감 rather than a roster - all twelve are listed and the ones you have
// not recruited show as silhouettes, so the tab doubles as the collection screen. 편성 moved
// out to its own bottom tab, which is why the old 보유 가전/편성 sub-tabs are gone.
using System;
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class UnitsTab : MonoBehaviour
    {
        [Header("머리말")]
        [SerializeField] TextMeshProUGUI countLabel;
        [SerializeField] UiButton galleryButton;
        [SerializeField] UiButton gearBagButton;

        [Header("재료")]
        [SerializeField] RectTransform partsRoot;
        [SerializeField] Sprite partPillSprite;
        [SerializeField] Sprite partOutlineSprite;
        /// <summary>Part icons in the order of <see cref="Parts"/>; SpriteLibrary only
        /// carries battle art, so these come from the converted 10.16-n icon set.</summary>
        [SerializeField] Sprite[] partIcons;
        [SerializeField] TMP_FontAsset bodyFont;

        [Header("도감")]
        [SerializeField] RectTransform rosterRoot;
        [SerializeField] UnitCard cardPrefab;
        [SerializeField] SpriteLibrary sprites;

        /// <summary>The four upgrade parts and the gear stone, in the order the build shows.</summary>
        static readonly int[] Parts = { 101, 102, 103, 104, GearStoneItem };
        const int GearStoneItem = 401;

        GameServices _svc;
        readonly List<UnitCard> _cards = new();
        readonly List<TextMeshProUGUI> _partValues = new();

        public event Action<UnitDef> DetailRequested;
        public event Action<string> Message;
        /// <summary>일러스트 opens the art viewer; 장비 가방 opens the gear bag.</summary>
        public event Action GalleryRequested;
        public event Action GearBagRequested;

        public void Bind(GameServices services)
        {
            _svc = services;
            if (galleryButton != null) galleryButton.SetAction(() => GalleryRequested?.Invoke());
            if (gearBagButton != null) gearBagButton.SetAction(() => GearBagRequested?.Invoke());
            BuildParts();
            BuildCatalogue();
            Refresh();
        }

        void OnEnable() { if (_svc != null) { BuildCatalogue(); Refresh(); } }

        void BuildParts()
        {
            if (partsRoot == null || _svc == null) return;
            foreach (Transform c in partsRoot) Destroy(c.gameObject);
            _partValues.Clear();

            for (int i = 0; i < Parts.Length; i++)
            {
                var go = new GameObject("part_" + Parts[i], typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(partsRoot, false);
                var bg = go.AddComponent<Image>();
                if (partPillSprite != null) { bg.sprite = partPillSprite; bg.type = Image.Type.Sliced; }
                bg.color = UiTheme.Panel;
                bg.raycastTarget = false;
                if (partOutlineSprite != null)
                {
                    var lineGo = new GameObject("Outline", typeof(RectTransform));
                    var lineRt = (RectTransform)lineGo.transform;
                    lineRt.SetParent(rt, false);
                    lineRt.anchorMin = Vector2.zero;
                    lineRt.anchorMax = Vector2.one;
                    lineRt.offsetMin = lineRt.offsetMax = Vector2.zero;
                    var line = lineGo.AddComponent<Image>();
                    line.sprite = partOutlineSprite;
                    line.type = Image.Type.Sliced;
                    line.color = UiTheme.PanelLine;
                    line.raycastTarget = false;
                }

                var icoGo = new GameObject("Icon", typeof(RectTransform));
                var icoRt = (RectTransform)icoGo.transform;
                icoRt.SetParent(rt, false);
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(0f, 0.5f);
                icoRt.sizeDelta = new Vector2(UiTheme.Py(18f), UiTheme.Py(18f));
                icoRt.anchoredPosition = new Vector2(UiTheme.Px(20f), 0f);
                var ico = icoGo.AddComponent<Image>();
                ico.sprite = partIcons != null && i < partIcons.Length ? partIcons[i] : null;
                ico.enabled = ico.sprite != null;
                ico.preserveAspect = true;
                ico.raycastTarget = false;

                // The build writes the part's name in muted 12px and its count in 14px ink,
                // both flowing left from the icon rather than pinned to the pill's edges.
                var nameGo = new GameObject("Name", typeof(RectTransform));
                var nameRt = (RectTransform)nameGo.transform;
                nameRt.SetParent(rt, false);
                nameRt.anchorMin = new Vector2(0f, 0f);
                nameRt.anchorMax = new Vector2(1f, 1f);
                nameRt.offsetMin = new Vector2(UiTheme.Px(32f), 0f);
                nameRt.offsetMax = new Vector2(-UiTheme.Px(8f), 0f);
                var nameTxt = nameGo.AddComponent<TextMeshProUGUI>();
                nameTxt.font = bodyFont;
                nameTxt.fontSize = UiTheme.Py(12f);
                nameTxt.color = UiTheme.Ink2;
                nameTxt.alignment = TextAlignmentOptions.Left;
                nameTxt.raycastTarget = false;
                nameTxt.textWrappingMode = TextWrappingModes.NoWrap;
                nameTxt.text = PartName(Parts[i]);

                var txtGo = new GameObject("Value", typeof(RectTransform));
                var txtRt = (RectTransform)txtGo.transform;
                txtRt.SetParent(rt, false);
                txtRt.anchorMin = new Vector2(0f, 0f);
                txtRt.anchorMax = new Vector2(1f, 1f);
                txtRt.offsetMin = new Vector2(UiTheme.Px(32f), 0f);
                txtRt.offsetMax = new Vector2(-UiTheme.Px(8f), 0f);
                var txt = txtGo.AddComponent<TextMeshProUGUI>();
                txt.font = bodyFont;
                txt.fontSize = UiTheme.Py(14f);
                txt.color = UiTheme.Ink;
                txt.alignment = TextAlignmentOptions.Right;
                txt.raycastTarget = false;
                txt.textWrappingMode = TextWrappingModes.NoWrap;
                _partValues.Add(txt);
            }
        }

        /// <summary>
        /// The part's own table name. The gear stone is the exception: the build labels item
        /// 401 플레인 강화석 in this strip rather than using its longer table name.
        /// </summary>
        string PartName(int itemId)
        {
            if (itemId == GearStoneItem) return "강화석";
            return _svc.Db.Items.TryGetValue(itemId, out var row) ? _svc.Db.Text(row.nameKey) : string.Empty;
        }

        void BuildCatalogue()
        {
            if (rosterRoot == null || cardPrefab == null || _svc == null) return;
            foreach (var c in _cards) if (c != null) Destroy(c.gameObject);
            _cards.Clear();

            foreach (var def in CatalogueOrder())
            {
                var card = Instantiate(cardPrefab, rosterRoot);
                card.Bind(def, sprites);
                card.Tapped += u => DetailRequested?.Invoke(u);
                _cards.Add(card);
            }
        }

        public void Refresh()
        {
            if (_svc == null) return;
            var p = _svc.Profile;

            if (countLabel != null)
                countLabel.text = $"보유 {p.Data.owned.Count}/{_svc.Db.Units.Count}";

            for (int i = 0; i < _partValues.Count && i < Parts.Length; i++)
                _partValues[i].text = UiTheme.Compact(p.Data.Item(Parts[i]));

            foreach (var c in _cards) c.Sync(p);
        }

        /// <summary>
        /// The build sorts owned appliances to the front and then by descending rarity, so the
        /// ones you can actually use lead and the SSRs you are missing sit above the commons.
        /// Table order breaks ties, which keeps the grid stable between refreshes.
        /// </summary>
        List<UnitDef> CatalogueOrder()
        {
            var order = new List<UnitDef>(_svc.Db.Units.Values);
            var data = _svc.Profile.Data;
            order.Sort((a, b) =>
            {
                int owned = (data.Own(b.Id) != null ? 1 : 0) - (data.Own(a.Id) != null ? 1 : 0);
                if (owned != 0) return owned;
                int rarity = ((int)b.Rarity).CompareTo((int)a.Rarity);
                return rarity != 0 ? rarity : a.Order.CompareTo(b.Order);
            });
            return order;
        }

        /// <summary>The lobby guide spotlights one appliance by id.</summary>
        public RectTransform CardFor(string unitId)
        {
            foreach (var c in _cards)
                if (c != null && c.Unit != null && c.Unit.Id == unitId) return (RectTransform)c.transform;
            return null;
        }
    }
}
