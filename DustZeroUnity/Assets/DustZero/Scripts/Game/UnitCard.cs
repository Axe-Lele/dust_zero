// One appliance tile - the 10.16-n `.ut` button, used by the 가전 catalogue and the 편성 pool.
//
// Measured off the running build: a 94x137 card on --card with a 1.5px --line outline and a
// 12px radius, the top 92x92 filled with the rarity's pale tint behind the portrait. The
// rarity initials sit as bare coloured text in the top-left, and up to two corner tags ride
// the top-right: 편성 for deck membership and an orange "영입 가능"/"돌파 가능" prompt, which
// drops below 편성 when both are showing.
using System;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class UnitCard : MonoBehaviour
    {
        [SerializeField] Image cardFill;
        [SerializeField] Image cardOutline;
        [SerializeField] Image picBackground;
        [SerializeField] Image portrait;
        [SerializeField] TextMeshProUGUI rarityLabel;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI subLabel;
        [SerializeField] GameObject deckTag;
        [SerializeField] TextMeshProUGUI deckLabel;
        [SerializeField] GameObject hotTag;
        [SerializeField] TextMeshProUGUI hotLabel;
        [SerializeField] RectTransform hotRect;
        [SerializeField] Button button;
        [SerializeField] SpriteLibrary sprites;

        /// <summary>Where the hot tag sits alone, and how far it drops when 편성 is also up.</summary>
        const float HotTopPage = 4f, HotTopLowPage = 22f;

        public UnitDef Unit { get; private set; }

        /// <summary>
        /// Replaces the 편성 corner tag with another word. The 대청소 leader sheet marks the
        /// picked appliance with 리더 on the same corner, exactly as `unitTile(t,{tag:'리더'})`.
        /// Null restores the deck tag.
        /// </summary>
        public string TagOverride { get; set; }

        /// <summary>Raised on a tap, with the appliance this tile shows.</summary>
        public event Action<UnitDef> Tapped;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => Tapped?.Invoke(Unit));
        }

        public void Bind(UnitDef unit, SpriteLibrary library)
        {
            Unit = unit;
            if (library != null) sprites = library;

            if (nameLabel != null) nameLabel.text = unit != null ? unit.Name : string.Empty;
            if (rarityLabel != null)
            {
                rarityLabel.text = unit != null ? unit.Rarity.ToString() : string.Empty;
                if (unit != null) rarityLabel.color = UiTheme.Rarity(UiTheme.Tier(unit.Rarity));
            }
            if (picBackground != null)
                picBackground.color = unit != null
                    ? UiTheme.RarityBg(UiTheme.Tier(unit.Rarity)) : UiTheme.Panel2;
            if (portrait != null)
            {
                portrait.sprite = unit != null && sprites != null ? sprites.Unit(unit.Id) : null;
                portrait.enabled = portrait.sprite != null;
            }
        }

        /// <summary>Repaints from the profile: ownership, level, deck membership, shards.</summary>
        public void Sync(PlayerProfile profile)
        {
            if (profile == null) return;
            if (Unit == null)
            {
                if (portrait != null) portrait.enabled = false;
                if (subLabel != null) subLabel.text = "비어 있음";
                if (deckTag != null) deckTag.SetActive(false);
                if (hotTag != null) hotTag.SetActive(false);
                return;
            }

            var owned = profile.Data.Own(Unit.Id);
            bool has = owned != null;

            // `.ut.none` greys and fades the portrait rather than swapping to another sprite.
            if (portrait != null)
                portrait.color = has ? Color.white : new Color(0.25f, 0.25f, 0.25f, 0.35f);
            if (nameLabel != null) nameLabel.color = has ? UiTheme.Ink : UiTheme.Ink2;

            if (subLabel != null) subLabel.text = SubtitleFor(profile, owned);

            bool inDeck = TagOverride != null
                ? TagOverride.Length > 0
                : has && profile.Data.deck.Contains(Unit.Id);
            if (deckLabel != null) deckLabel.text = TagOverride ?? "편성";
            if (deckTag != null) deckTag.SetActive(inDeck);

            string hot = profile.CanRecruit(Unit.Id) ? "영입 가능"
                       : has && profile.CanStarUp(Unit.Id) ? "돌파 가능" : null;
            if (hotTag != null) hotTag.SetActive(hot != null);
            if (hot != null)
            {
                if (hotLabel != null) hotLabel.text = hot;
                // `.tag.hot.lo` - step down so the two tags do not overlap.
                if (hotRect != null)
                {
                    float top = UiTheme.Py(inDeck ? HotTopLowPage : HotTopPage);
                    hotRect.anchoredPosition = new Vector2(hotRect.anchoredPosition.x, -top);
                }
            }
        }

        /// <summary>
        /// The build writes one of three things under the name: the level (with limit-break
        /// stars once earned), shard progress toward recruiting, or plain 미보유.
        /// </summary>
        string SubtitleFor(PlayerProfile profile, OwnedUnit owned)
        {
            if (owned != null)
                return owned.star > 0
                    ? $"Lv.{owned.lv} · {new string('★', owned.star)}"
                    : $"Lv.{owned.lv}";

            long shards = profile.ShardCount(Unit.Id);
            if (shards <= 0) return "미보유";
            int need = profile.RecruitCost(Unit.Id);
            return need == int.MaxValue ? "미보유" : $"조각 {shards}/{need}";
        }
    }
}
