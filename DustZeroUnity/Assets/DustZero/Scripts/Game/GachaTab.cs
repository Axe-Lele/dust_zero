// 소환: the 가전 banner and the 장비 banner, switched by a segmented control.
//
// Measured off the running 10.16-n build: a dark 16px card holding the banner copy, three
// featured appliances, the three rates, and the 1회/10회 buttons - then a 소환 목록 list that
// spells out every unit in the pool with its exact chance and whether you already own it.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public enum GachaMode { Unit, Gear }

    public sealed class GachaTab : MonoBehaviour
    {
        [Header("모드")]
        [SerializeField] UiButton unitModeButton;
        [SerializeField] TextMeshProUGUI unitModeLabel;
        [SerializeField] Image unitModePlate;
        [SerializeField] UiButton gearModeButton;
        [SerializeField] TextMeshProUGUI gearModeLabel;
        [SerializeField] Image gearModePlate;

        [Header("배너")]
        [SerializeField] TextMeshProUGUI bannerTitleLabel;
        [SerializeField] TextMeshProUGUI bannerDescLabel;
        [SerializeField] RectTransform featuredRoot;
        [SerializeField] RectTransform rateRoot;
        [SerializeField] UiButton singleButton;
        [SerializeField] TextMeshProUGUI singleLabel;
        [SerializeField] TextMeshProUGUI singleCostLabel;
        [SerializeField] UiButton multiButton;
        [SerializeField] TextMeshProUGUI multiLabel;
        [SerializeField] TextMeshProUGUI multiCostLabel;

        [Header("소환 목록")]
        [SerializeField] TextMeshProUGUI poolHeadingLabel;
        [SerializeField] RectTransform poolRoot;

        [Header("칸 만들기")]
        [SerializeField] Sprite cellSprite;
        [SerializeField] Sprite chipSprite;
        [SerializeField] TMP_FontAsset bodyFont;
        [SerializeField] TMP_FontAsset numberFont;
        [SerializeField] SpriteLibrary sprites;

        /// <summary>How many appliances the banner spotlights.</summary>
        const int FeaturedCount = 3;

        GameServices _svc;
        GachaMode _mode = GachaMode.Unit;
        readonly List<GameObject> _spawned = new();

        public event Action<string> Message;
        /// <summary>Raised with everything a pull produced, so the lobby can show the result.</summary>
        public event Action<List<(string unitId, DuplicateResult dup)>> Pulled;

        public GachaMode Mode => _mode;

        public void Bind(GameServices services)
        {
            _svc = services;
            if (unitModeButton != null) unitModeButton.SetAction(() => SetMode(GachaMode.Unit));
            if (gearModeButton != null) gearModeButton.SetAction(() => SetMode(GachaMode.Gear));
            if (singleButton != null) singleButton.SetAction(() => Pull(1));
            if (multiButton != null) multiButton.SetAction(() => Pull(10));
            Refresh();
        }

        void OnEnable() { if (_svc != null) Refresh(); }

        void SetMode(GachaMode mode)
        {
            _mode = mode;
            Refresh();
        }

        // ------------------------------------------------------------------ paint

        public void Refresh()
        {
            if (_svc == null) return;
            PaintModes();
            Clear();

            if (_mode == GachaMode.Unit) RefreshUnitBanner();
            else RefreshGearBanner();
        }

        void PaintModes()
        {
            Paint(unitModePlate, unitModeLabel, _mode == GachaMode.Unit, "가전 소환");
            Paint(gearModePlate, gearModeLabel, _mode == GachaMode.Gear, "장비 소환");

            static void Paint(Image plate, TextMeshProUGUI label, bool on, string text)
            {
                // The chosen half goes dark; the other keeps the sunken ground showing.
                if (plate != null) plate.color = on ? UiTheme.DarkPanel : new Color(0f, 0f, 0f, 0f);
                if (label != null)
                {
                    label.text = text;
                    label.color = on ? UiTheme.Panel : UiTheme.Ink2;
                }
            }
        }

        void Clear()
        {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
        }

        void RefreshUnitBanner()
        {
            var single = BannerFor(1);
            var multi = BannerFor(10);
            var banner = single ?? multi;
            if (banner == null) return;

            if (bannerTitleLabel != null) bannerTitleLabel.text = _svc.Db.Text("banner.1.title");
            if (bannerDescLabel != null) bannerDescLabel.text = _svc.Db.Text("banner.1.desc");
            if (poolHeadingLabel != null) poolHeadingLabel.text = "소환 목록";

            var pool = _svc.Db.Pools.TryGetValue(banner.poolId, out var p0)
                ? (IReadOnlyList<GachaPoolRow>)p0 : System.Array.Empty<GachaPoolRow>();
            double total = pool.Sum(p => (double)p.weight);

            // ---- the three SSRs the banner is selling.
            foreach (var entry in pool.Where(p => RarityOf(p.unitId) == Rarity.SSR)
                                      .OrderBy(p => Order(p.unitId)).Take(FeaturedCount))
                SpawnFeatured(entry.unitId);

            // ---- rates, one span per grade.
            foreach (var rarity in new[] { Rarity.SSR, Rarity.SR, Rarity.R })
            {
                double share = pool.Where(p => RarityOf(p.unitId) == rarity).Sum(p => (double)p.weight);
                if (share <= 0d) continue;
                SpawnRate($"{rarity} {share / total * 100d:0.#}%", UiTheme.Rarity(UiTheme.Tier(rarity)));
            }

            // ---- one row per grade, listing every unit and its exact chance.
            foreach (var rarity in new[] { Rarity.SSR, Rarity.SR, Rarity.R })
            {
                var members = pool.Where(p => RarityOf(p.unitId) == rarity)
                                  .OrderBy(p => Order(p.unitId)).ToList();
                if (members.Count == 0) continue;
                SpawnPoolRow(rarity, members.Select(p =>
                {
                    var def = _svc.Db.Units[p.unitId];
                    bool owned = _svc.Profile.Data.Own(p.unitId) != null;
                    return $"{def.Name} {p.weight / total * 100d:0.00}% · {(owned ? "보유" : "미보유")}";
                }));
            }

            SetPullButton(singleButton, singleLabel, singleCostLabel, single);
            SetPullButton(multiButton, multiLabel, multiCostLabel, multi);
        }

        /// <summary>
        /// The gear banner. Its pool is a grade weighting rather than a unit list, so the
        /// rates row shows the four grades and the list names what each grade can give.
        /// </summary>
        void RefreshGearBanner()
        {
            var banner = _svc.Db.GearBanners.Count > 0 ? _svc.Db.GearBanners[0] : null;
            if (banner == null) return;

            if (bannerTitleLabel != null) bannerTitleLabel.text = _svc.Db.Text(banner.nameKey);
            if (bannerDescLabel != null)
                bannerDescLabel.text = banner.guaranteeGrade > 0
                    ? $"{banner.pulls}회마다 {_svc.Db.Text($"gear.grade.{banner.guaranteeGrade}")} 이상 1개가 확정이에요."
                    : "장비를 뽑아요. 같은 전용장비를 또 얻으면 각성으로 바뀌어요.";
            if (poolHeadingLabel != null) poolHeadingLabel.text = "등급 확률";

            int[] weights = { banner.w1, banner.w2, banner.w3, banner.w4 };
            double total = weights.Sum();

            // Featured: one example piece per grade that actually has a chance.
            foreach (var grade in new[] { 4, 3, 2 })
                if (grade <= weights.Length && weights[grade - 1] > 0)
                {
                    var sample = _svc.Db.Gear.Values.Where(g => g.grade == grade)
                                     .OrderBy(g => g.id).FirstOrDefault();
                    if (sample != null) SpawnFeaturedGear(sample, grade);
                }

            for (int g = 4; g >= 1; g--)
            {
                if (weights[g - 1] <= 0) continue;
                SpawnRate($"{_svc.Db.Text($"gear.grade.{g}")} {weights[g - 1] / total * 100d:0.#}%",
                          UiTheme.GearGrade(g));
            }

            for (int g = 4; g >= 1; g--)
            {
                if (weights[g - 1] <= 0) continue;
                var names = _svc.Db.Gear.Values.Where(x => x.grade == g).OrderBy(x => x.id)
                                .Select(x => _svc.Db.Text(x.nameKey));
                SpawnPoolRowRaw(_svc.Db.Text($"gear.grade.{g}"), UiTheme.GearGrade(g), names);
            }

            string unit = _svc.Db.Text($"item.{banner.costItem}.name");
            SetCost(singleLabel, singleCostLabel, $"{banner.pulls}회 소환", banner.costCount, unit);
            if (multiLabel != null) multiLabel.text = string.Empty;
            if (multiCostLabel != null) multiCostLabel.text = string.Empty;
            if (singleButton != null)
                singleButton.State = _svc.Profile.ItemCount(banner.costItem) >= banner.costCount
                    ? UiButtonState.Normal : UiButtonState.Disabled;
            if (multiButton != null) multiButton.State = UiButtonState.Disabled;
        }

        /// <summary>The gacha table has one row per pull size, so each button gets its own.</summary>
        GachaRow BannerFor(int pulls)
            => _svc.Db.Banners.FirstOrDefault(b => b.pulls == pulls);

        void SetPullButton(UiButton button, TextMeshProUGUI label, TextMeshProUGUI cost,
                           GachaRow banner)
        {
            if (banner == null)
            {
                if (label != null) label.text = string.Empty;
                if (cost != null) cost.text = string.Empty;
                if (button != null) button.State = UiButtonState.Disabled;
                return;
            }
            SetCost(label, cost, $"{banner.pulls}회 소환", banner.costCount,
                    _svc.Db.Text($"item.{banner.costItem}.name"));
            if (button != null)
                button.State = _svc.Profile.CanPull(banner)
                    ? UiButtonState.Normal : UiButtonState.Disabled;
        }

        void SetCost(TextMeshProUGUI label, TextMeshProUGUI cost, string text, int amount,
                     string unit = null)
        {
            if (label != null) label.text = text;
            if (cost != null)
                cost.text = unit == null
                    ? $"크리스탈 {UiTheme.Compact(amount)}"
                    : $"{unit} {UiTheme.Compact(amount)}";
        }

        // ------------------------------------------------------------------ pulling

        void Pull(int count)
        {
            if (_mode == GachaMode.Gear) { PullGear(); return; }

            var banner = BannerFor(count);
            if (banner == null) return;
            if (!_svc.Profile.CanPull(banner))
            {
                Message?.Invoke("크리스탈이 모자라요.");
                return;
            }
            var got = _svc.Profile.Pull(banner);
            _svc.Persist();
            Pulled?.Invoke(got);
            Refresh();
        }

        void PullGear()
        {
            var banner = _svc.Db.GearBanners.Count > 0 ? _svc.Db.GearBanners[0] : null;
            if (banner == null) return;
            if (_svc.Profile.ItemCount(banner.costItem) < banner.costCount)
            {
                Message?.Invoke($"{_svc.Db.Text($"item.{banner.costItem}.name")} 가 모자라요.");
                return;
            }

            _svc.Profile.Data.AddItem(banner.costItem, -banner.costCount);
            int[] weights = { banner.w1, banner.w2, banner.w3, banner.w4 };
            var names = new List<string>();
            for (int i = 0; i < Mathf.Max(1, banner.pulls); i++)
            {
                int grade = PlayerProfile.RollGearGrade(weights, _svc.Rng);
                // The last pull of a run is lifted to the promised grade, which is what
                // "10회마다 상위 등급 1회" means.
                if (banner.guaranteeGrade > 0 && i == banner.pulls - 1 && grade < banner.guaranteeGrade)
                    grade = banner.guaranteeGrade;
                int id = _svc.Profile.RandomGearOf(grade, _svc.Rng);
                var gain = _svc.Profile.AddGear(id);
                if (_svc.Db.Gear.TryGetValue(gain.GearId, out var row))
                    names.Add(_svc.Db.Text(row.nameKey)
                              + (gain.Kind == GearGainKind.Awakened ? $" ★{gain.Amount}"
                               : gain.Kind == GearGainKind.Essence ? $" · 정수 +{gain.Amount}" : ""));
            }
            _svc.Persist();
            Message?.Invoke(string.Join(", ", names));
            Refresh();
        }

        // ------------------------------------------------------------------ cells

        void SpawnFeatured(string unitId)
        {
            var def = _svc.Db.Units[unitId];
            SpawnFeaturedCell(sprites != null ? sprites.Unit(unitId) : null, Color.white,
                              $"{def.Rarity} {def.Name}");
        }

        void SpawnFeaturedGear(GearRow row, int grade)
            => SpawnFeaturedCell(null, UiTheme.GearGrade(grade),
                                 $"{_svc.Db.Text($"gear.grade.{grade}")} {_svc.Db.Text(row.nameKey)}");

        void SpawnFeaturedCell(Sprite art, Color tint, string caption)
        {
            if (featuredRoot == null) return;
            var go = new GameObject("Featured", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(featuredRoot, false);
            rt.sizeDelta = new Vector2(UiTheme.Px(64f), UiTheme.Py(85f));

            var plateGo = new GameObject("Plate", typeof(RectTransform));
            var prt = (RectTransform)plateGo.transform;
            prt.SetParent(rt, false);
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.offsetMin = new Vector2(0f, -UiTheme.Py(64f));
            prt.offsetMax = Vector2.zero;
            var plate = plateGo.AddComponent<Image>();
            if (cellSprite != null) { plate.sprite = cellSprite; plate.type = Image.Type.Sliced; }
            plate.color = UiTheme.DarkPanel2;
            plate.raycastTarget = false;

            if (art != null)
            {
                var icoGo = new GameObject("Art", typeof(RectTransform));
                var irt = (RectTransform)icoGo.transform;
                irt.SetParent(prt, false);
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(UiTheme.Px(3f), UiTheme.Px(3f));
                irt.offsetMax = new Vector2(-UiTheme.Px(3f), -UiTheme.Px(3f));
                var img = icoGo.AddComponent<Image>();
                img.sprite = art;
                img.color = tint;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var capGo = new GameObject("Caption", typeof(RectTransform));
            var crt = (RectTransform)capGo.transform;
            crt.SetParent(rt, false);
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = new Vector2(1f, 0f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = new Vector2(0f, UiTheme.Py(19f));
            var cap = capGo.AddComponent<TextMeshProUGUI>();
            cap.font = bodyFont;
            cap.fontSize = UiTheme.Py(11f);
            cap.color = UiTheme.DarkInk2;
            cap.alignment = TextAlignmentOptions.Center;
            cap.textWrappingMode = TextWrappingModes.NoWrap;
            cap.overflowMode = TextOverflowModes.Ellipsis;
            cap.raycastTarget = false;
            cap.text = caption;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = UiTheme.Px(64f);
            le.preferredHeight = le.minHeight = UiTheme.Py(85f);
            _spawned.Add(go);
        }

        void SpawnRate(string text, Color color)
        {
            if (rateRoot == null) return;
            var go = new GameObject("Rate", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(rateRoot, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = bodyFont;
            t.fontSize = UiTheme.Py(13f);
            t.color = color;
            t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = text;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = UiTheme.Px(58f);
            le.preferredHeight = le.minHeight = UiTheme.Py(16f);
            _spawned.Add(go);
        }

        void SpawnPoolRow(Rarity rarity, IEnumerable<string> chips)
            => SpawnPoolRowRaw(rarity.ToString(), UiTheme.Rarity(UiTheme.Tier(rarity)), chips);

        void SpawnPoolRowRaw(string grade, Color gradeColor, IEnumerable<string> chips)
        {
            if (poolRoot == null) return;
            var go = new GameObject("PoolRow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(poolRoot, false);
            var chipList = chips.ToList();
            int rows = Mathf.Max(1, (chipList.Count + 1) / 2);

            var gradeGo = new GameObject("Grade", typeof(RectTransform));
            var grt = (RectTransform)gradeGo.transform;
            grt.SetParent(rt, false);
            grt.anchorMin = new Vector2(0f, 1f);
            grt.anchorMax = new Vector2(0f, 1f);
            grt.offsetMin = new Vector2(0f, -UiTheme.Py(21f));
            grt.offsetMax = new Vector2(UiTheme.Px(34f), 0f);
            var g = gradeGo.AddComponent<TextMeshProUGUI>();
            g.font = numberFont;
            g.fontSize = UiTheme.Py(12.5f);
            g.color = gradeColor;
            g.alignment = TextAlignmentOptions.TopLeft;
            g.textWrappingMode = TextWrappingModes.NoWrap;
            g.raycastTarget = false;
            g.text = grade;

            var chipHost = new GameObject("Chips", typeof(RectTransform));
            var hrt = (RectTransform)chipHost.transform;
            hrt.SetParent(rt, false);
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = new Vector2(UiTheme.Px(38f), 0f);
            hrt.offsetMax = Vector2.zero;
            var grid = chipHost.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(UiTheme.Px(155f), UiTheme.Py(19f));
            grid.spacing = new Vector2(UiTheme.Px(6f), UiTheme.Py(4f));
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = UiTheme.Py(19f + (rows - 1) * 23f);

            foreach (var text in chipList)
            {
                var chipGo = new GameObject("Chip", typeof(RectTransform));
                chipGo.transform.SetParent(hrt, false);
                var bg = chipGo.AddComponent<Image>();
                if (chipSprite != null) { bg.sprite = chipSprite; bg.type = Image.Type.Sliced; }
                bg.color = UiTheme.Panel2;
                bg.raycastTarget = false;

                var txtGo = new GameObject("Text", typeof(RectTransform));
                var trt = (RectTransform)txtGo.transform;
                trt.SetParent(chipGo.transform, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(UiTheme.Px(7f), 0f);
                trt.offsetMax = new Vector2(-UiTheme.Px(7f), 0f);
                var t = txtGo.AddComponent<TextMeshProUGUI>();
                t.font = bodyFont;
                t.fontSize = UiTheme.Py(11.5f);
                t.color = UiTheme.Ink2;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.raycastTarget = false;
                t.text = text;

                t.overflowMode = TextOverflowModes.Ellipsis;
            }
            _spawned.Add(go);
        }

        // ------------------------------------------------------------------ helpers

        Rarity RarityOf(string unitId) => _svc.Db.Units[unitId].Rarity;
        int Order(string unitId) => _svc.Db.Units[unitId].Order;
    }
}
