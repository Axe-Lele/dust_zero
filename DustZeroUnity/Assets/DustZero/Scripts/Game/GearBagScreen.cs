// 장비 가방 — the bag behind 가전 도감's 장비 가방 button, and the piece detail it opens.
//
// Two panels in one overlay, the way the build swaps one modal for another: the bag lists
// what you own and offers the four bulk actions, and tapping a piece swaps to its detail.
// Every number shown comes from the gear table and config through PlayerProfile.Gear.
using System;
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public enum GearBagPanel { Bag, Detail, Exchange }

    /// <summary>Which piece the detail panel is showing.</summary>
    public readonly struct GearTarget
    {
        public readonly int Uid;
        public readonly string ExclusiveUnit;
        public bool IsExclusive => ExclusiveUnit != null;
        public bool IsNone => Uid == 0 && ExclusiveUnit == null;

        GearTarget(int uid, string unit) { Uid = uid; ExclusiveUnit = unit; }
        public static GearTarget Common(int uid) => new(uid, null);
        public static GearTarget Exclusive(string unitId) => new(0, unitId);
        public static readonly GearTarget None = new(0, null);
    }

    public sealed class GearBagScreen : MonoBehaviour
    {
        [Header("가방")]
        [SerializeField] GameObject bagPanel;
        [SerializeField] TextMeshProUGUI countLabel;
        [SerializeField] TextMeshProUGUI stoneLabel;
        [SerializeField] TextMeshProUGUI essenceLabel;
        [SerializeField] UiButton dismantleButton;
        [SerializeField] TextMeshProUGUI dismantleSub;
        [SerializeField] UiButton fuse1Button;
        [SerializeField] TextMeshProUGUI fuse1Sub;
        [SerializeField] UiButton fuse2Button;
        [SerializeField] TextMeshProUGUI fuse2Sub;
        [SerializeField] UiButton exchangeButton;
        [SerializeField] TextMeshProUGUI exchangeSub;
        [SerializeField] GameObject exclusiveHeading;
        [SerializeField] RectTransform exclusiveGrid;
        [SerializeField] RectTransform commonGrid;
        [SerializeField] TextMeshProUGUI emptyLabel;
        [SerializeField] UiButton closeButton;

        [Header("상세")]
        [SerializeField] GameObject detailPanel;
        [SerializeField] Image detailIcon;
        [SerializeField] Image detailIconPlate;
        [SerializeField] TextMeshProUGUI detailKicker;
        [SerializeField] TextMeshProUGUI detailName;
        [SerializeField] TextMeshProUGUI detailWorn;
        [SerializeField] TextMeshProUGUI statNow;
        [SerializeField] TextMeshProUGUI statNext;
        [SerializeField] GameObject skillBlock;
        [SerializeField] TextMeshProUGUI skillHeading;
        [SerializeField] TextMeshProUGUI skillNow;
        [SerializeField] TextMeshProUGUI skillNext;
        [SerializeField] UiButton enhanceButton;
        [SerializeField] TextMeshProUGUI enhanceLabel;
        [SerializeField] TextMeshProUGUI enhanceSub;
        [SerializeField] UiButton sideButton;
        [SerializeField] TextMeshProUGUI sideLabel;
        [SerializeField] TextMeshProUGUI sideSub;
        [SerializeField] UiButton backButton;

        [Header("전용장비 교환")]
        [SerializeField] GameObject exchangePanel;
        [SerializeField] TextMeshProUGUI exchangeNote;
        [SerializeField] RectTransform exchangeList;
        [SerializeField] GearExchangeRow exchangeRowPrefab;
        [SerializeField] UiButton exchangeBackButton;

        [Header("카드")]
        [SerializeField] RectTransform card;
        [SerializeField] RectTransform bagColumn;
        [SerializeField] RectTransform detailColumn;
        [SerializeField] RectTransform exchangeColumn;

        [Header("칸")]
        [SerializeField] GearCell cellPrefab;
        [SerializeField] SpriteLibrary sprites;

        GameServices _svc;
        GearTarget _target = GearTarget.None;
        GearBagPanel _panel = GearBagPanel.Bag;
        readonly List<GearCell> _cells = new();
        readonly List<GearExchangeRow> _rows = new();

        public event Action Closed;
        public event Action<string> Message;

        public GearBagPanel Panel => _panel;
        public GearTarget Target => _target;

        PlayerProfile P => _svc.Profile;

        void Awake()
        {
            if (closeButton != null) closeButton.SetAction(Close);
            if (backButton != null) backButton.SetAction(() => ShowBag());
            if (dismantleButton != null) dismantleButton.SetAction(DismantleGrade1);
            if (fuse1Button != null) fuse1Button.SetAction(() => Fuse(1));
            if (fuse2Button != null) fuse2Button.SetAction(() => Fuse(2));
            if (exchangeButton != null) exchangeButton.SetAction(ShowExchange);
            if (exchangeBackButton != null) exchangeBackButton.SetAction(() => ShowBag());
            if (enhanceButton != null) enhanceButton.SetAction(Enhance);
            if (sideButton != null) sideButton.SetAction(SideAction);
            // The builder ships this disabled; closing here would shut it as it opens.
        }

        public void Open(GameServices services)
        {
            _svc = services;
            gameObject.SetActive(true);
            ShowBag();
        }

        void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // ------------------------------------------------------------------ 가방

        void ShowBag()
        {
            _panel = GearBagPanel.Bag;
            _target = GearTarget.None;
            if (bagPanel != null) bagPanel.SetActive(true);
            if (detailPanel != null) detailPanel.SetActive(false);
            if (exchangePanel != null) exchangePanel.SetActive(false);
            RefreshBag();
            FitCard(bagColumn);
        }

        /// <summary>
        /// The build's card is height:auto up to 88vh, so a short panel is a short card. A
        /// fixed height leaves a field of empty white under the detail's three rows.
        /// </summary>
        void FitCard(RectTransform column)
        {
            if (card == null || column == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(column);
            float wanted = LayoutUtility.GetPreferredHeight(column);
            float max = UiTheme.Py(820f);
            card.sizeDelta = new Vector2(card.sizeDelta.x, Mathf.Min(wanted, max));
        }

        void RefreshBag()
        {
            if (_svc == null) return;
            var cfg = _svc.Db.Config;

            // The build sorts the bag by grade, then by level, so the best sits first.
            var list = new List<OwnedGear>(P.Data.gear);
            list.Sort((a, b) =>
            {
                int ga = GradeOf(a), gb = GradeOf(b);
                if (ga != gb) return gb.CompareTo(ga);
                if (a.lv != b.lv) return b.lv.CompareTo(a.lv);
                return a.id.CompareTo(b.id);
            });

            var exclusives = new List<string>();
            foreach (var id in _svc.Db.UnitOrder)
                if (P.ExclusiveState(id) != null) exclusives.Add(id);

            if (countLabel != null)
                countLabel.text = $"공용 {list.Count}개 · 전용 {exclusives.Count}/{_svc.Db.UnitOrder.Count}";
            if (stoneLabel != null) stoneLabel.text = UiTheme.Compact(P.ItemCount(PlayerProfile.GearStoneItem));
            if (essenceLabel != null) essenceLabel.text = UiTheme.Compact(P.ItemCount(PlayerProfile.GearEssenceItem));

            // ---- the four bulk actions, each enabled only when it would do something.
            var loose1 = P.LooseGearOfGrade(1);
            SetTool(dismantleButton, dismantleSub, loose1.Count > 0,
                    $"{loose1.Count}개 → 강화석 +{P.DismantleYield(loose1)}");
            SetTool(fuse1Button, fuse1Sub, P.CanFuse(1), $"1등급 {cfg.gearFuseNeed}개");
            SetTool(fuse2Button, fuse2Sub, P.CanFuse(2), $"2등급 {cfg.gearFuseNeed}개");
            SetTool(exchangeButton, exchangeSub,
                    P.ItemCount(PlayerProfile.GearEssenceItem) >= cfg.exclExchange,
                    $"정수 {cfg.exclExchange}");

            // ---- the two grids.
            foreach (var c in _cells) if (c != null) Destroy(c.gameObject);
            _cells.Clear();

            if (exclusiveHeading != null) exclusiveHeading.SetActive(exclusives.Count > 0);
            if (exclusiveGrid != null)
            {
                exclusiveGrid.gameObject.SetActive(exclusives.Count > 0);
                foreach (var unitId in exclusives)
                {
                    var row = P.ExclusiveOf(unitId);
                    var x = P.ExclusiveState(unitId);
                    var cell = Spawn(exclusiveGrid, row.id,
                        x.star > 0 ? $"{new string('★', x.star)} +{x.lv}" : $"☆ +{x.lv}",
                        unitId);
                    var captured = unitId;
                    cell.Tapped += () => ShowDetail(GearTarget.Exclusive(captured));
                }
            }

            if (commonGrid != null)
                foreach (var g in list)
                {
                    string owner = P.GearOwner(g.uid);
                    var cell = Spawn(commonGrid, g.id,
                        owner != null ? $"+{g.lv} · 장착" : $"+{g.lv}", null);
                    int uid = g.uid;
                    cell.Tapped += () => ShowDetail(GearTarget.Common(uid));
                }

            if (emptyLabel != null)
            {
                // Hide the whole row, not just the text: an empty row still reserves height.
                var row = emptyLabel.transform.parent;
                if (row != null) row.gameObject.SetActive(list.Count == 0);
                emptyLabel.gameObject.SetActive(list.Count == 0);
                emptyLabel.text = "아직 없어요. 장비 소환 · 상점 장비 상자 · 합성으로 얻을 수 있어요.";
            }
        }

        GearCell Spawn(RectTransform parent, int gearId, string caption, string exclusiveUnit)
        {
            var cell = Instantiate(cellPrefab, parent);
            var row = _svc.Db.Gear.TryGetValue(gearId, out var r) ? r : null;
            // An exclusive shows its appliance rather than a type icon, as the build does.
            var portrait = exclusiveUnit != null && sprites != null ? sprites.Unit(exclusiveUnit) : null;
            cell.Bind(row, caption, portrait);
            _cells.Add(cell);
            return cell;
        }

        static void SetTool(UiButton button, TextMeshProUGUI sub, bool enabled, string text)
        {
            if (sub != null) sub.text = text;
            if (button != null)
                button.State = enabled ? UiButtonState.Normal : UiButtonState.Disabled;
        }

        int GradeOf(OwnedGear g)
            => _svc.Db.Gear.TryGetValue(g.id, out var r) ? r.grade : 0;

        // ------------------------------------------------------------------ bulk actions

        void DismantleGrade1()
        {
            var loose = P.LooseGearOfGrade(1);
            if (loose.Count == 0) return;
            int total = 0;
            foreach (var g in loose) total += P.DismantleGear(g.uid);
            _svc.Persist();
            Message?.Invoke($"1등급 {loose.Count}개 분해 · 강화석 +{total}");
            RefreshBag();
        }

        void Fuse(int grade)
        {
            var result = P.FuseGear(grade, _svc.Rng);
            if (result == null) return;
            var (gain, refund) = result.Value;
            string name = _svc.Db.Gear.TryGetValue(gain.GearId, out var r)
                ? _svc.Db.Text(r.nameKey) : "장비";
            string grn = _svc.Db.Text($"gear.grade.{grade + 1}");
            _svc.Persist();
            Message?.Invoke(refund > 0
                ? $"합성 성공! {grn} {name} · 강화석 +{refund}"
                : $"합성 성공! {grn} {name}");
            RefreshBag();
        }

        // ------------------------------------------------------------------ 전용장비 교환

        /// <summary>
        /// Every appliance's exclusive, in catalogue order. Rows you cannot take are dimmed
        /// rather than dropped: the point of this screen is to show the full set so you can
        /// decide what to save essence for.
        /// </summary>
        public void ShowExchange()
        {
            _panel = GearBagPanel.Exchange;
            _target = GearTarget.None;
            if (bagPanel != null) bagPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
            if (exchangePanel != null) exchangePanel.SetActive(true);
            RefreshExchange();
            FitCard(exchangeColumn);
        }

        void RefreshExchange()
        {
            if (_svc == null) return;
            var cfg = _svc.Db.Config;
            long essence = P.ItemCount(PlayerProfile.GearEssenceItem);

            if (exchangeNote != null)
                exchangeNote.text =
                    $"전용 정수 {cfg.exclExchange}개로 원하는 전용장비 1개. 이미 있으면 각성 ★+1이에요. " +
                    $"(보유 정수 {UiTheme.Compact(essence)})";

            foreach (var r in _rows) if (r != null) Destroy(r.gameObject);
            _rows.Clear();
            if (exchangeList == null || exchangeRowPrefab == null) return;

            foreach (var unitId in _svc.Db.UnitOrder)
            {
                var row = P.ExclusiveOf(unitId);
                if (row == null) continue;

                var held = P.ExclusiveState(unitId);
                bool maxed = held != null && held.star >= cfg.exclMaxStar;
                bool affordable = !maxed && essence >= cfg.exclExchange;

                string ownership = held == null
                    ? $"{UnitName(unitId)} 전용 · 미보유"
                    : $"{UnitName(unitId)} 전용 · 보유 ★{held.star}{(maxed ? " (최대)" : "")}";
                // The effect shown is the one you would get, not the one you have.
                int shownStar = held == null ? 0 : Math.Min(cfg.exclMaxStar, held.star + 1);

                var item = Instantiate(exchangeRowPrefab, exchangeList);
                item.Bind(unitId, row,
                          sprites != null ? sprites.Unit(unitId) : null,
                          ownership, _svc.Db.Text(row.nameKey),
                          ExclusiveEffect(row, shownStar), affordable);
                var captured = unitId;
                item.Tapped += () => Exchange(captured);
                _rows.Add(item);
            }
        }

        void Exchange(string unitId)
        {
            var gain = P.ExchangeExclusive(unitId);
            if (gain == null) return;
            _svc.Persist();

            var row = P.ExclusiveOf(unitId);
            string name = row != null ? _svc.Db.Text(row.nameKey) : "전용장비";
            Message?.Invoke(gain.Value.Kind switch
            {
                GearGainKind.Awakened => $"{name} · 각성 ★{gain.Value.Amount}",
                GearGainKind.Essence => $"{name} · 정수 +{gain.Value.Amount}",
                _ => $"{name} 획득!",
            });
            ShowBag();
        }

        // ------------------------------------------------------------------ 상세

        public void ShowDetail(GearTarget target)
        {
            _target = target;
            _panel = GearBagPanel.Detail;
            if (bagPanel != null) bagPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(true);
            if (exchangePanel != null) exchangePanel.SetActive(false);
            RefreshDetail();
            FitCard(detailColumn);
        }

        void RefreshDetail()
        {
            if (_svc == null || _target.IsNone) return;
            var cfg = _svc.Db.Config;

            GearRow row;
            int lv, star;
            string owner;
            if (_target.IsExclusive)
            {
                row = P.ExclusiveOf(_target.ExclusiveUnit);
                var x = P.ExclusiveState(_target.ExclusiveUnit);
                if (row == null || x == null) { ShowBag(); return; }
                lv = x.lv; star = x.star; owner = _target.ExclusiveUnit;
            }
            else
            {
                var g = P.GearInst(_target.Uid);
                if (g == null || !_svc.Db.Gear.TryGetValue(g.id, out row)) { ShowBag(); return; }
                lv = g.lv; star = 0; owner = P.GearOwner(_target.Uid);
            }

            int max = cfg.gearMaxLv[Mathf.Clamp(row.grade - 1, 0, 3)];
            string gradeName = _svc.Db.Text($"gear.grade.{row.grade}");

            if (detailIcon != null)
            {
                detailIcon.sprite = _target.IsExclusive && sprites != null
                    ? sprites.Unit(_target.ExclusiveUnit) : GearSpriteFor(row);
                detailIcon.color = _target.IsExclusive ? Color.white : UiTheme.GearGrade(row.grade);
                detailIcon.enabled = detailIcon.sprite != null;
            }
            if (detailIconPlate != null)
                detailIconPlate.color = Tint(UiTheme.GearGrade(row.grade));

            if (detailKicker != null)
                detailKicker.text = _target.IsExclusive
                    ? $"{gradeName} · {UnitName(row.unitId)} 전용 · +{lv}/{max}"
                    : $"{gradeName} · +{lv}/{max}";
            if (detailName != null) detailName.text = _svc.Db.Text(row.nameKey);
            if (detailWorn != null)
            {
                bool show = owner != null && !_target.IsExclusive;
                detailWorn.gameObject.SetActive(show);
                if (show) detailWorn.text = $"{UnitName(owner)} 장착 중";
            }

            var cost = P.GearCost(row.grade, lv);
            if (statNow != null) statNow.text = StatLine(row, lv);
            if (statNext != null)
            {
                statNext.gameObject.SetActive(cost != null);
                if (cost != null) statNext.text = $"다음 +{lv + 1}: {StatLine(row, lv + 1)}";
            }

            // ---- exclusives also list what they do to the appliance's skill.
            if (skillBlock != null) skillBlock.SetActive(_target.IsExclusive);
            if (_target.IsExclusive)
            {
                if (skillHeading != null)
                    skillHeading.text = $"스킬 강화 · {SkillName(row.unitId)}";
                if (skillNow != null) skillNow.text = ExclusiveEffect(row, star);
                if (skillNext != null)
                    skillNext.text = star < cfg.exclMaxStar
                        ? $"각성 ★{star + 1}: {ExclusiveEffect(row, star + 1)} (같은 전용장비를 또 얻으면)"
                        : "최대 각성 · 이후 중복은 전용 정수";
            }

            bool canEnhance = cost != null
                && P.ItemCount(PlayerProfile.GearStoneItem) >= cost.Value.stone
                && P.Coin >= cost.Value.coin;
            if (enhanceLabel != null) enhanceLabel.text = cost != null ? "강화" : "최대 강화";
            if (enhanceSub != null)
                enhanceSub.text = cost != null
                    ? $"강화석 {cost.Value.stone} (보유 {UiTheme.Compact(P.ItemCount(PlayerProfile.GearStoneItem))}) · 코인 {UiTheme.Compact(cost.Value.coin)}"
                    : "—";
            if (enhanceButton != null)
                enhanceButton.State = canEnhance ? UiButtonState.Normal : UiButtonState.Disabled;

            // ---- the side action is 장착 해제 when worn, 분해 when loose, nothing for an exclusive.
            if (sideButton != null)
            {
                bool show = !_target.IsExclusive;
                sideButton.gameObject.SetActive(show);
                if (show)
                {
                    bool worn = owner != null;
                    if (sideLabel != null) sideLabel.text = worn ? "장착 해제" : "분해";
                    if (sideSub != null)
                        sideSub.text = worn
                            ? UnitName(owner)
                            : $"강화석 +{P.DismantleYield(new[] { P.GearInst(_target.Uid) })}";
                    sideButton.State = UiButtonState.Normal;
                }
            }
        }

        void Enhance()
        {
            bool ok = _target.IsExclusive
                ? P.EnhanceExclusiveGear(_target.ExclusiveUnit)
                : P.EnhanceCommonGear(_target.Uid);
            if (!ok) return;
            _svc.Persist();
            RefreshDetail();
        }

        void SideAction()
        {
            if (_target.IsExclusive) return;
            string owner = P.GearOwner(_target.Uid);
            if (owner != null)
            {
                P.UnequipGear(owner);
                _svc.Persist();
                ShowBag();
                return;
            }
            int n = P.DismantleGear(_target.Uid);
            _svc.Persist();
            Message?.Invoke($"분해 · 강화석 +{n}");
            ShowBag();
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// The build's statLine: every non-zero stat as a percentage, with 스킬 충전 written
        /// as a reduction because a shorter charge is the improvement.
        /// </summary>
        string StatLine(GearRow row, int lv)
        {
            var parts = new List<string>();
            Add(row.atk, "공격력", false);
            Add(row.heat, "과열 한도", false);
            Add(row.sp, "스킬 충전", true);
            return parts.Count > 0 ? string.Join(" · ", parts) : "—";

            void Add(double raw, string name, bool negative)
            {
                if (raw == 0d) return;
                float v = (float)raw * (1f + _svc.Db.Config.gearStatPerLv * lv) * 100f;
                parts.Add($"{name} {(negative ? "−" : "+")}{v:0.#}%");
            }
        }

        /// <summary>
        /// The exclusive's effect line, with its {0}/{1} slots filled from the two skill
        /// edits scaled by star. Mirrors the build's exclEffText, including which values it
        /// writes as a percentage.
        /// </summary>
        string ExclusiveEffect(GearRow row, int star)
        {
            string template = _svc.Db.Text(row.effKey);
            if (string.IsNullOrEmpty(template)) return string.Empty;
            float f = 1f + _svc.Db.Config.exclStarBonus * star;

            var vals = new List<string>();
            Val(row.sk1Key, row.sk1Op, row.sk1Val);
            Val(row.sk2Key, row.sk2Op, row.sk2Val);

            for (int i = 0; i < vals.Count; i++)
                template = template.Replace("{" + i + "}", vals[i]);
            return template;

            void Val(string key, string op, double raw)
            {
                if (string.IsNullOrEmpty(key)) return;
                float v = (float)raw;
                bool isPct = op == "mul"
                    || (op == "add" && v < 1f && key != "dur" && !IsDistance(key));
                float shown = op == "mul" ? (v - 1f) * f * 100f
                            : key == "burst" ? v * f * 100f
                            : isPct ? v * f * 100f
                            : v * f;
                vals.Add(op == "mul" || isPct || key == "burst"
                    ? Mathf.RoundToInt(shown).ToString("N0")
                    : shown.ToString("0.##"));
            }

            static bool IsDistance(string key)
                => key.Contains("Radius") || key.Contains("Range") || key.Contains("knockback");
        }

        Sprite GearSpriteFor(GearRow row) => _gearSprites.TryGetValue(row.type ?? "", out var s) ? s : null;

        [Header("장비 아이콘 (atk / heat / sp)")]
        [SerializeField] Sprite gearAtkIcon;
        [SerializeField] Sprite gearHeatIcon;
        [SerializeField] Sprite gearSpIcon;
        Dictionary<string, Sprite> _gearSpritesCache;
        Dictionary<string, Sprite> _gearSprites => _gearSpritesCache ??= new Dictionary<string, Sprite>
        {
            ["atk"] = gearAtkIcon, ["heat"] = gearHeatIcon, ["sp"] = gearSpIcon,
        };

        static Color Tint(Color c) => new(c.r, c.g, c.b, 0.16f);

        string UnitName(string unitId)
            => unitId != null && _svc.Db.Units.TryGetValue(unitId, out var d) ? d.Name : unitId;

        string SkillName(string unitId)
            => unitId != null && _svc.Db.Units.TryGetValue(unitId, out var d) ? d.SkillName : string.Empty;
    }
}
