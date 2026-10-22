// Appliance detail, redesign 2b.
//
// The top half is the character stage with arrows to page through the roster; the bottom
// sheet carries 능력치 / 스킬 / 이야기 and the two actions. 강화 is a level-up: the mockup
// draws its two cost chips, and a chip the player cannot afford turns red.
using System;
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class UnitDetailScreen : MonoBehaviour
    {
        [Header("무대")]
        [SerializeField] Image portrait;
        [SerializeField] Image gradeBadge;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI roleChip;
        [SerializeField] TextMeshProUGUI tileChip;
        [SerializeField] TextMeshProUGUI starLabel;
        [SerializeField] UiButton backButton;
        [SerializeField] UiButton prevButton;
        [SerializeField] UiButton nextButton;
        [SerializeField] SpriteLibrary sprites;

        [Header("서브 탭")]
        [SerializeField] UiToggleChip statSubTab;
        [SerializeField] UiToggleChip skillSubTab;
        [SerializeField] UiToggleChip storySubTab;
        [SerializeField] GameObject statBody;
        [SerializeField] GameObject skillBody;
        [SerializeField] GameObject storyBody;

        [Header("레벨")]
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] TextMeshProUGUI levelCapLabel;
        [SerializeField] RectTransform levelFill;
        [SerializeField] TextMeshProUGUI levelCostLabel;

        [Header("능력치")]
        [SerializeField] TextMeshProUGUI powerValue;
        [SerializeField] TextMeshProUGUI rangeValue;
        [SerializeField] TextMeshProUGUI costValue;
        [SerializeField] TextMeshProUGUI coolValue;

        [Header("지원 가전 성장 (10.17-b)")]
        /// <summary>One line under the stat grid, for appliances with config &lt;id&gt;LvBonus.</summary>
        [SerializeField] TextMeshProUGUI supportRow;

        [Header("스킬")]
        [SerializeField] TextMeshProUGUI skillNameLabel;
        [SerializeField] TextMeshProUGUI skillDescLabel;
        [SerializeField] Image skillIcon;

        [Header("이야기")]
        [SerializeField] TextMeshProUGUI storyLabel;

        [Header("돌파")]
        [SerializeField] GameObject starRoot;
        [SerializeField] TextMeshProUGUI starNeedLabel;
        [SerializeField] UiButton starButton;

        [Header("행동")]
        [SerializeField] UiToggleChip deckToggle;
        [SerializeField] UiButton upgradeButton;
        [SerializeField] TextMeshProUGUI partCostLabel;
        [SerializeField] TextMeshProUGUI coinCostLabel;
        [SerializeField] Image partCostFace;
        [SerializeField] Image coinCostFace;

        GameServices _svc;
        readonly List<UnitDef> _order = new();
        int _index;

        public event Action Closed;
        public event Action<string> Message;

        public UnitDef Current => _index >= 0 && _index < _order.Count ? _order[_index] : null;

        /// <summary>The lobby guide's "detail.levelup"/"detail.close" spotlight targets.</summary>
        public RectTransform ResolveGuideTarget(string key) => key switch
        {
            "detail.levelup" => upgradeButton != null ? (RectTransform)upgradeButton.transform : null,
            "detail.close" => backButton != null ? (RectTransform)backButton.transform : null,
            _ => null,
        };

        void Awake()
        {
            if (backButton != null) backButton.SetAction(Close);
            if (prevButton != null) prevButton.SetAction(() => Step(-1));
            if (nextButton != null) nextButton.SetAction(() => Step(1));
            if (upgradeButton != null) upgradeButton.SetAction(Upgrade);
            if (starButton != null) starButton.SetAction(StarUp);
            if (statSubTab != null) statSubTab.Bind("능력치", () => ShowSub(0));
            if (skillSubTab != null) skillSubTab.Bind("스킬", () => ShowSub(1));
            if (storySubTab != null) storySubTab.Bind("이야기", () => ShowSub(2));
            if (deckToggle != null) deckToggle.Bind("편성", ToggleDeck);
        }

        /// <summary>Opens on one appliance, with the whole roster paged behind the arrows.</summary>
        public void Open(GameServices services, UnitDef unit, IReadOnlyList<UnitDef> order)
        {
            _svc = services;
            _order.Clear();
            if (order != null) _order.AddRange(order);
            if (_order.Count == 0) _order.AddRange(services.Db.Units.Values);

            _index = Mathf.Max(0, _order.FindIndex(u => u == unit));
            gameObject.SetActive(true);
            ShowSub(0);
            Refresh();
        }

        void Close()
        {
            _svc?.Guide?.ReportReached("detail.close");
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void Step(int delta)
        {
            if (_order.Count == 0) return;
            _index = (_index + delta + _order.Count) % _order.Count;
            Refresh();
        }

        void ShowSub(int which)
        {
            if (statBody != null) statBody.SetActive(which == 0);
            if (skillBody != null) skillBody.SetActive(which == 1);
            if (storyBody != null) storyBody.SetActive(which == 2);
            if (statSubTab != null) statSubTab.State = which == 0 ? ChipState.Selected : ChipState.Normal;
            if (skillSubTab != null) skillSubTab.State = which == 1 ? ChipState.Selected : ChipState.Normal;
            if (storySubTab != null) storySubTab.State = which == 2 ? ChipState.Selected : ChipState.Normal;
        }

        // ------------------------------------------------------------------ actions

        void ToggleDeck()
        {
            var unit = Current;
            if (unit == null || !_svc.Profile.Data.Owns(unit.Id)) return;

            var deck = _svc.Profile.Data.deck;
            if (!deck.Contains(unit.Id) && deck.Count >= _svc.Db.Config.deckMax)
            {
                Message?.Invoke($"편성은 {_svc.Db.Config.deckMax}칸까지예요.");
                return;
            }
            _svc.Profile.ToggleDeck(unit.Id);
            _svc.Persist();
            Refresh();
        }

        /// <summary>강화 means a level-up when owned, and a shard recruit when not.</summary>
        void Upgrade()
        {
            var unit = Current;
            if (unit == null) return;

            if (!_svc.Profile.Data.Owns(unit.Id))
            {
                if (!_svc.Profile.Recruit(unit.Id))
                {
                    Message?.Invoke($"조각이 {_svc.Profile.RecruitCost(unit.Id)}개 필요해요.");
                    return;
                }
                _svc.Persist();
                Refresh();
                Message?.Invoke($"{unit.Name} 영입!");
                return;
            }

            if (!_svc.Profile.LevelUp(unit.Id))
            {
                Message?.Invoke(AtCap(unit) ? "돌파해야 더 올릴 수 있어요." : "재화가 모자라요.");
                return;
            }
            _svc.Guide?.ReportReached("detail.levelup");
            _svc.Persist();
            Refresh();
        }

        void StarUp()
        {
            var unit = Current;
            if (unit == null) return;
            if (!_svc.Profile.StarUp(unit.Id)) { Message?.Invoke("조각이나 코인이 모자라요."); return; }
            _svc.Persist();
            Refresh();
            Message?.Invoke($"{unit.Name} 돌파!");
        }

        bool AtCap(UnitDef unit)
        {
            var owned = _svc.Profile.Data.Own(unit.Id);
            return owned != null && owned.lv >= _svc.Profile.LevelCapOf(unit.Id);
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            var unit = Current;
            if (_svc == null || unit == null) return;

            var profile = _svc.Profile;
            var owned = profile.Data.Own(unit.Id);
            bool has = owned != null;

            if (nameLabel != null) nameLabel.text = unit.Name;
            if (roleChip != null) roleChip.text = string.IsNullOrEmpty(unit.Role) ? "가전" : unit.Role;
            if (tileChip != null) tileChip.text = unit.Tile == TileKind.Ground ? "지상" : "가구";
            if (gradeBadge != null && sprites != null) gradeBadge.sprite = sprites.Grade(unit.Rarity);
            if (portrait != null && sprites != null)
            {
                portrait.sprite = sprites.Unit(unit.Id);
                portrait.enabled = portrait.sprite != null;
                portrait.color = has ? Color.white : new Color(0.17f, 0.14f, 0.13f, 0.55f);
            }

            int starMax = _svc.Db.Config.starMax;
            int star = owned?.star ?? 0;
            if (starLabel != null)
                starLabel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.RaritySSR)}>"
                               + new string('★', star) + "</color>"
                               + $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.StarOff)}>"
                               + new string('★', Mathf.Max(0, starMax - star)) + "</color>";

            RefreshLevel(unit, owned);
            RefreshStats(unit);
            RefreshSkillAndStory(unit);
            RefreshStarBox(unit, has);
            RefreshActions(unit, has);
        }

        void RefreshLevel(UnitDef unit, OwnedUnit owned)
        {
            int cap = _svc.Profile.LevelCapOf(unit.Id);
            int lv = owned?.lv ?? 0;
            if (levelLabel != null) levelLabel.text = owned != null ? $"Lv.{lv}" : "미보유";
            if (levelCapLabel != null) levelCapLabel.text = $"/ {cap}";
            if (levelFill != null)
                levelFill.anchorMax = new Vector2(cap <= 1 ? 0f : Mathf.Clamp01((lv - 1) / (float)(cap - 1)),
                                                  levelFill.anchorMax.y);

            var cost = owned != null && lv < cap ? _svc.Profile.LevelUpCost(lv) : null;
            if (levelCostLabel != null)
                levelCostLabel.text = owned == null ? ""
                                    : cost == null ? "레벨 최대"
                                    : $"다음 Lv.{lv + 1}";
        }

        void RefreshStats(UnitDef unit)
        {
            // Shown at the appliance's current level, which is what the player will deploy.
            float mul = _svc.Profile.PowerOf(unit.Id);
            if (powerValue != null)
                powerValue.text = unit.Atk > 0f ? $"{unit.Atk * mul:N0}" : "–";
            if (rangeValue != null)
                rangeValue.text = unit.Range > 0f ? $"{unit.Range:0.#}칸" : "근접";
            if (costValue != null) costValue.text = UiTheme.Watts(unit.Cost);
            if (coolValue != null) coolValue.text = $"{unit.Cool:0.#}초";
            RefreshSupportGrowth(unit);
        }

        /// <summary>
        /// 10.17-b. The generator and the computer do nothing that 청소력 measures, so without
        /// this the sheet showed identical numbers at Lv.1 and Lv.45 for an appliance that had
        /// in fact been growing. Which appliances these are comes from the config keys, not
        /// from a list here - a third support appliance needs only its two table rows.
        /// </summary>
        void RefreshSupportGrowth(UnitDef unit)
        {
            if (supportRow == null) return;
            var cfg = _svc.Db.Config;
            bool grows = cfg.TryNum(unit.Id + "LvBonus", out float perLevel);
            supportRow.gameObject.SetActive(grows);
            if (!grows) return;

            float sup = _svc.Profile.SupportPowerOf(unit.Id);
            string text;
            if (unit.Id == "gen")
            {
                text = $"전력 회복 +{_svc.Db.Skp(unit.Id, "regenPerSec") * sup * 100f:0.#}W/초" +
                       $" · 풀가동 {_svc.Db.Skp(unit.Id, "burst") * sup * 100f:N0}W";
            }
            else
            {
                text = $"버프 공격력 +{_svc.Db.Skp(unit.Id, "buffAtk") * sup * 100f:0.#}%";
            }

            // What one more level is worth, so the growth is visible before paying for it.
            if (_svc.Profile.Data.Owns(unit.Id) && _svc.Profile.LevelOf(unit.Id) < cfg.lvMax)
            {
                text += unit.Id == "gen"
                    ? $" · 레벨업 +{_svc.Db.Skp(unit.Id, "regenPerSec") * perLevel * 100f:0.#}W/초"
                    : $" · 레벨업 +{_svc.Db.Skp(unit.Id, "buffAtk") * perLevel * 100f:0.#}%p";
            }
            supportRow.text = text;
        }

        void RefreshSkillAndStory(UnitDef unit)
        {
            if (skillNameLabel != null)
                skillNameLabel.text = string.IsNullOrEmpty(unit.SkillName) ? "고유 능력 없음" : unit.SkillName;
            if (skillDescLabel != null)
                skillDescLabel.text = string.IsNullOrEmpty(unit.SkillDesc)
                    ? "이 가전은 따로 발동하는 기술이 없어요."
                    : unit.SkillDesc;
            if (skillIcon != null) skillIcon.enabled = skillIcon.sprite != null;

            // The table ships the appliance's lines; there is no separate story column, so the
            // tab shows what actually exists rather than inventing a profile blurb.
            if (storyLabel != null)
                storyLabel.text = unit.Lines != null && unit.Lines.Length > 0
                    ? string.Join("\n\n", unit.Lines)
                    : "아직 들려줄 이야기가 없어요.";
        }

        void RefreshStarBox(UnitDef unit, bool has)
        {
            var next = has ? _svc.Profile.NextStarStep(unit.Id) : null;
            bool show = has && next != null;
            if (starRoot != null) starRoot.SetActive(show);
            if (!show) return;

            long shards = _svc.Profile.ShardCount(unit.Id);
            if (starNeedLabel != null)
                starNeedLabel.text = $"조각 {shards}/{next.shards} · 코인 {next.coin:N0} → 레벨 상한 {next.lvCap}";
            if (starButton != null)
                starButton.State = _svc.Profile.CanStarUp(unit.Id)
                    ? UiButtonState.Normal : UiButtonState.Disabled;
        }

        void RefreshActions(UnitDef unit, bool has)
        {
            if (deckToggle != null)
            {
                bool inDeck = has && _svc.Profile.Data.deck.Contains(unit.Id);
                deckToggle.Text = inDeck ? "편성 중" : "편성";
                deckToggle.State = !has ? ChipState.Locked
                                 : inDeck ? ChipState.Selected : ChipState.Normal;
            }

            if (!has)
            {
                // Unowned: the button recruits with shards instead of levelling.
                int need = _svc.Profile.RecruitCost(unit.Id);
                long have = _svc.Profile.ShardCount(unit.Id);
                if (upgradeButton != null)
                {
                    upgradeButton.Text = "영입";
                    upgradeButton.State = _svc.Profile.CanRecruit(unit.Id)
                        ? UiButtonState.Normal : UiButtonState.Disabled;
                }
                SetCost(partCostLabel, partCostFace, have >= need, need == int.MaxValue ? "–" : $"{have}/{need}");
                SetCost(coinCostLabel, coinCostFace, true, "조각");
                return;
            }

            if (upgradeButton != null) upgradeButton.Text = "강화";

            var owned = _svc.Profile.Data.Own(unit.Id);
            var cost = owned.lv < _svc.Profile.LevelCapOf(unit.Id) ? _svc.Profile.LevelUpCost(owned.lv) : null;
            if (cost == null)
            {
                if (upgradeButton != null) upgradeButton.State = UiButtonState.Disabled;
                SetCost(partCostLabel, partCostFace, true, "–");
                SetCost(coinCostLabel, coinCostFace, true, AtCap(unit) ? "돌파 필요" : "최대");
                return;
            }

            int partId = _svc.Profile.PartItemOf(unit.Family);
            bool partOk = _svc.Profile.ItemCount(partId) >= cost.Value.Part;
            bool coinOk = _svc.Profile.Coin >= cost.Value.Coin;

            if (upgradeButton != null)
                upgradeButton.State = partOk && coinOk ? UiButtonState.Normal : UiButtonState.Disabled;
            SetCost(partCostLabel, partCostFace, partOk, $"{cost.Value.Part:N0}");
            SetCost(coinCostLabel, coinCostFace, coinOk, $"{cost.Value.Coin:N0}");
        }

        /// <summary>A cost chip the player cannot pay turns red, as the mockup specifies.</summary>
        static void SetCost(TextMeshProUGUI label, Image face, bool affordable, string text)
        {
            if (label != null)
            {
                label.text = text;
                label.color = affordable ? UiTheme.Ink : UiTheme.Danger;
            }
            if (face != null)
                face.color = affordable ? UiTheme.Panel : new Color(1f, 0.90f, 0.88f);
        }
    }
}
