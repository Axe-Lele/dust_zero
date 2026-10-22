// 일러스트 정보창 — the art viewer behind 가전 도감's 일러스트 button.
//
// A full-screen overlay measured off the running 10.16-n build: the illustration fills the
// top 56% over the appliance's own pastel background, and a rounded sheet below carries three
// tabs — 프로필 (tagline, profile text, a six-cell fact grid, the chatter lines), 표정 (three
// expression sheets) and 설정화 (turnaround, motif sheet, battle SD).
//
// Every colour and string comes from unit_profile and string_ko; every image comes from
// unit_art through SpriteLibrary. Nothing about a specific appliance is written here.
using System;
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public enum ArtTab { Profile, Face, Sheet }

    public sealed class ArtViewerScreen : MonoBehaviour
    {
        [Header("배경")]
        [SerializeField] Image pageBackground;
        [SerializeField] Image sheetBackground;

        [Header("무대")]
        [SerializeField] Image illustration;
        [SerializeField] GameObject missingArtRoot;
        [SerializeField] Image missingArtChibi;
        [SerializeField] TextMeshProUGUI missingArtLabel;
        [SerializeField] Image rarityPill;
        [SerializeField] TextMeshProUGUI rarityLabel;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI titleTagline;
        [SerializeField] GameObject notOwnedBadge;
        [SerializeField] TextMeshProUGUI indexLabel;
        [SerializeField] UiButton closeButton;
        [SerializeField] UiButton prevButton;
        [SerializeField] UiButton nextButton;

        [Header("탭")]
        [SerializeField] UiButton profileTab;
        [SerializeField] UiButton faceTab;
        [SerializeField] UiButton sheetTab;
        [SerializeField] RectTransform profileUnderline;
        [SerializeField] RectTransform faceUnderline;
        [SerializeField] RectTransform sheetUnderline;
        [SerializeField] TextMeshProUGUI profileTabLabel;
        [SerializeField] TextMeshProUGUI faceTabLabel;
        [SerializeField] TextMeshProUGUI sheetTabLabel;

        [Header("프로필")]
        [SerializeField] GameObject profileBody;
        [SerializeField] TextMeshProUGUI taglineLabel;
        [SerializeField] TextMeshProUGUI profileLabel;
        [SerializeField] Image[] factCells;
        [SerializeField] TextMeshProUGUI[] factKeys;
        [SerializeField] TextMeshProUGUI[] factValues;
        [SerializeField] RectTransform linesRoot;
        [SerializeField] UiButton detailButton;
        [SerializeField] TextMeshProUGUI detailLabel;
        [SerializeField] Sprite lineChipSprite;
        [SerializeField] Sprite lineChipOutline;
        [SerializeField] TMP_FontAsset bodyFont;

        [Header("표정")]
        [SerializeField] GameObject faceBody;
        [SerializeField] Image[] faceFrames;
        [SerializeField] Image[] faceImages;
        [SerializeField] TextMeshProUGUI[] faceCaptions;
        [SerializeField] TextMeshProUGUI faceNote;

        [Header("설정화")]
        [SerializeField] GameObject sheetBody;
        [SerializeField] Image turnImage;
        [SerializeField] GameObject turnMissing;
        [SerializeField] Image keysImage;
        [SerializeField] GameObject keysMissing;
        [SerializeField] TextMeshProUGUI motifLabel;
        [SerializeField] Image[] sdImages;
        [SerializeField] Image[] sdFrames;

        [SerializeField] SpriteLibrary sprites;

        /// <summary>The six facts the build lists, in its order.</summary>
        public const int FactCount = 6;

        GameServices _svc;
        UnitDef _unit;
        IReadOnlyList<UnitDef> _order;
        ArtTab _tab = ArtTab.Profile;
        readonly List<GameObject> _lineChips = new();

        public event Action Closed;
        public event Action<UnitDef> DetailRequested;

        public UnitDef Unit => _unit;
        public ArtTab Tab => _tab;

        void Awake()
        {
            if (closeButton != null) closeButton.SetAction(Close);
            if (prevButton != null) prevButton.SetAction(() => Step(-1));
            if (nextButton != null) nextButton.SetAction(() => Step(1));
            if (profileTab != null) profileTab.SetAction(() => ShowTab(ArtTab.Profile));
            if (faceTab != null) faceTab.SetAction(() => ShowTab(ArtTab.Face));
            if (sheetTab != null) sheetTab.SetAction(() => ShowTab(ArtTab.Sheet));
            if (detailButton != null) detailButton.SetAction(OpenDetail);
            // No SetActive(false) here: the builder already ships this overlay disabled, so
            // Awake first runs *during* Open's SetActive(true) - closing here would shut the
            // screen the same frame it was asked to open.
        }

        /// <summary>Opens the viewer on one appliance. The order drives the ‹ › arrows.</summary>
        public void Open(GameServices services, UnitDef unit, IReadOnlyList<UnitDef> order)
        {
            if (unit == null) return;
            _svc = services;
            _order = order;
            gameObject.SetActive(true);
            Show(unit);
        }

        void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        void OpenDetail()
        {
            var unit = _unit;
            Close();
            DetailRequested?.Invoke(unit);
        }

        void Step(int delta)
        {
            if (_order == null || _order.Count == 0) return;
            int i = IndexOf(_unit);
            Show(_order[((i + delta) % _order.Count + _order.Count) % _order.Count]);
        }

        int IndexOf(UnitDef unit)
        {
            if (_order == null) return 0;
            for (int i = 0; i < _order.Count; i++)
                if (_order[i] == unit) return i;
            return 0;
        }

        void Show(UnitDef unit)
        {
            _unit = unit;
            var profile = Profile(unit);

            // The whole screen takes the appliance's own palette, so each one reads as itself.
            Color key = Hex(profile?.keyColor, UiTheme.Primary);
            Color page = Hex(profile?.bgColor, UiTheme.Panel2);
            if (pageBackground != null) pageBackground.color = page;
            if (sheetBackground != null) sheetBackground.color = UiTheme.Panel;

            var illust = sprites != null ? sprites.Illust(unit.Id) : default;
            bool hasArt = illust.full != null;

            if (illustration != null)
            {
                illustration.sprite = illust.full;
                illustration.enabled = hasArt;
            }
            if (missingArtRoot != null) missingArtRoot.SetActive(!hasArt);
            if (missingArtChibi != null)
            {
                missingArtChibi.sprite = sprites != null ? sprites.Unit(unit.Id) : null;
                missingArtChibi.enabled = missingArtChibi.sprite != null;
            }
            if (missingArtLabel != null) missingArtLabel.text = "일러스트 준비 중";

            int tier = UiTheme.Tier(unit.Rarity);
            if (rarityPill != null) rarityPill.color = UiTheme.Rarity(tier);
            if (rarityLabel != null)
            {
                rarityLabel.text = unit.Rarity.ToString();
                rarityLabel.color = UiTheme.RarityInk(tier);
            }
            if (nameLabel != null) nameLabel.text = unit.Name;

            string tagline = Text(profile?.taglineKey);
            // The build writes the tagline in a darkened key colour so it stays readable on
            // the pale plate; the raw key colour is for the underline and nothing else.
            Color taglineInk = Darken(key, 0.22f);
            if (titleTagline != null) { titleTagline.text = tagline; titleTagline.color = taglineInk; }
            if (taglineLabel != null) { taglineLabel.text = tagline; taglineLabel.color = taglineInk; }

            var owned = _svc != null ? _svc.Profile.Data.Own(unit.Id) : null;
            if (notOwnedBadge != null) notOwnedBadge.SetActive(owned == null);
            if (indexLabel != null && _order != null)
                indexLabel.text = $"{IndexOf(unit) + 1} / {_order.Count}";

            FillProfile(unit, profile, owned, page);
            FillFaces(illust, page, hasArt);
            FillSheet(unit, profile, illust, hasArt);

            PaintTabs(key);
            ShowTab(_tab);
        }

        // ------------------------------------------------------------------ tabs

        void ShowTab(ArtTab tab)
        {
            _tab = tab;
            if (profileBody != null) profileBody.SetActive(tab == ArtTab.Profile);
            if (faceBody != null) faceBody.SetActive(tab == ArtTab.Face);
            if (sheetBody != null) sheetBody.SetActive(tab == ArtTab.Sheet);
            PaintTabs(Hex(Profile(_unit)?.keyColor, UiTheme.Primary));
        }

        /// <summary>The selected tab is inked and underlined in the appliance's key colour.</summary>
        void PaintTabs(Color key)
        {
            Paint(profileTabLabel, profileUnderline, _tab == ArtTab.Profile, key);
            Paint(faceTabLabel, faceUnderline, _tab == ArtTab.Face, key);
            Paint(sheetTabLabel, sheetUnderline, _tab == ArtTab.Sheet, key);
        }

        static void Paint(TextMeshProUGUI label, RectTransform underline, bool on, Color key)
        {
            if (label != null) label.color = on ? UiTheme.Ink : UiTheme.Ink2;
            if (underline == null) return;
            var img = underline.GetComponent<Image>();
            if (img != null) img.color = on ? key : new Color(0f, 0f, 0f, 0f);
        }

        // ------------------------------------------------------------------ 프로필

        void FillProfile(UnitDef unit, UnitProfileRow profile, OwnedUnit owned, Color page)
        {
            if (profileLabel != null) profileLabel.text = Text(profile?.profileKey);

            // The six facts, in the build's order. 계열 and 주요 요소 come from the tables, so
            // a new appliance fills this grid without touching the screen.
            var facts = new (string key, string value)[]
            {
                ("등급", unit.Rarity.ToString()),
                ("역할", unit.Role),
                ("계열", Text($"family.{unit.Family}.name")),
                ("스킬", unit.SkillName),
                ("상태", owned != null
                    ? $"Lv.{owned.lv} · ★{owned.star}/{StarMax}"
                    : "미보유"),
                ("주요 요소", Text(profile?.motifKey)),
            };
            for (int i = 0; i < FactCount; i++)
            {
                if (factCells != null && i < factCells.Length && factCells[i] != null)
                    factCells[i].color = page;
                if (factKeys != null && i < factKeys.Length && factKeys[i] != null)
                    factKeys[i].text = facts[i].key;
                if (factValues != null && i < factValues.Length && factValues[i] != null)
                    factValues[i].text = facts[i].value;
            }

            BuildLines(unit);

            if (detailLabel != null)
                detailLabel.text = owned != null ? "성장·장비 보기" : "영입 방법 보기";
        }

        int StarMax => _svc != null ? _svc.Db.Config.starMax : 5;

        /// <summary>
        /// One speech chip per chatter line. The count is unit.lineCount, so this grows and
        /// shrinks with the table rather than assuming four.
        /// </summary>
        void BuildLines(UnitDef unit)
        {
            if (linesRoot == null) return;
            foreach (var go in _lineChips) if (go != null) Destroy(go);
            _lineChips.Clear();
            if (unit.Lines == null) return;

            foreach (var line in unit.Lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var go = new GameObject("Line", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(linesRoot, false);

                var bg = go.AddComponent<Image>();
                if (lineChipSprite != null) { bg.sprite = lineChipSprite; bg.type = Image.Type.Sliced; }
                bg.color = Color.white;
                bg.raycastTarget = false;

                if (lineChipOutline != null)
                {
                    var lineGo = new GameObject("Outline", typeof(RectTransform));
                    var lrt = (RectTransform)lineGo.transform;
                    lrt.SetParent(rt, false);
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                    var outline = lineGo.AddComponent<Image>();
                    outline.sprite = lineChipOutline;
                    outline.type = Image.Type.Sliced;
                    outline.color = UiTheme.PanelLine;
                    outline.raycastTarget = false;
                }

                var txtGo = new GameObject("Text", typeof(RectTransform));
                var trt = (RectTransform)txtGo.transform;
                trt.SetParent(rt, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(UiTheme.Px(9f), 0f);
                trt.offsetMax = new Vector2(-UiTheme.Px(9f), 0f);
                var txt = txtGo.AddComponent<TextMeshProUGUI>();
                txt.font = bodyFont;
                txt.fontSize = UiTheme.Py(12f);
                txt.color = UiTheme.Ink;
                txt.alignment = TextAlignmentOptions.Left;
                txt.textWrappingMode = TextWrappingModes.NoWrap;
                txt.raycastTarget = false;
                txt.text = $"“{line}”";

                // The build lets each chip hug its own line; uGUI has no flex-wrap, so these
                // sit in a two-column grid instead. The cell fixes the size, which is why
                // there is no fitter here - one would fight the grid and collapse the chip.
                _lineChips.Add(go);
            }
        }

        // ------------------------------------------------------------------ 표정

        void FillFaces(UnitIllust illust, Color page, bool hasArt)
        {
            Sprite[] faces = { illust.face1, illust.face2, illust.face3 };
            for (int i = 0; i < 3; i++)
            {
                if (faceFrames != null && i < faceFrames.Length && faceFrames[i] != null)
                    faceFrames[i].color = page;
                if (faceImages != null && i < faceImages.Length && faceImages[i] != null)
                {
                    faceImages[i].sprite = faces[i];
                    faceImages[i].enabled = faces[i] != null;
                }
                if (faceCaptions != null && i < faceCaptions.Length && faceCaptions[i] != null)
                    faceCaptions[i].text = $"표정 {i + 1}";
            }
            if (faceNote != null)
                faceNote.text = hasArt
                    ? "표정 시트 · 대사·컷신 연출에 쓰는 기본 표정 3종"
                    : "일러스트 표정은 준비 중이에요.";
        }

        // ------------------------------------------------------------------ 설정화

        void FillSheet(UnitDef unit, UnitProfileRow profile,
                       UnitIllust illust, bool hasArt)
        {
            Bind(turnImage, turnMissing, illust.turn);
            Bind(keysImage, keysMissing, illust.keys);
            if (motifLabel != null) motifLabel.text = Text(profile?.motifKey);

            // Battle SD: one chibi per state. The sprite is the same because the game draws
            // its states with effects rather than separate art, so the tint is what differs -
            // this row shows the appliance, not three distinct drawings it does not have.
            var chibi = sprites != null ? sprites.Unit(unit.Id) : null;
            Color[] washes = { UiTheme.Panel2, Mix(UiTheme.Primary, UiTheme.Panel, 0.82f),
                               Mix(UiTheme.Danger, UiTheme.Panel, 0.82f) };
            for (int i = 0; i < 3; i++)
            {
                if (sdImages != null && i < sdImages.Length && sdImages[i] != null)
                {
                    sdImages[i].sprite = chibi;
                    sdImages[i].enabled = chibi != null;
                }
                if (sdFrames != null && i < sdFrames.Length && sdFrames[i] != null)
                    sdFrames[i].color = washes[i];
            }
        }

        static void Bind(Image image, GameObject missing, Sprite sprite)
        {
            if (image != null)
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
            }
            if (missing != null) missing.SetActive(sprite == null);
        }

        // ------------------------------------------------------------------ helpers

        UnitProfileRow Profile(UnitDef unit)
            => unit != null && _svc != null && _svc.Db.UnitProfiles.TryGetValue(unit.Id, out var p)
                ? p : null;

        string Text(string key)
            => string.IsNullOrEmpty(key) || _svc == null ? string.Empty : _svc.Db.Text(key);

        /// <summary>unit_profile stores colours as #RRGGBB text; a bad one falls back loudly.</summary>
        static Color Hex(string hex, Color fallback)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)
                ? c : fallback;

        static Color Darken(Color c, float amount)
            => new(c.r * (1f - amount), c.g * (1f - amount), c.b * (1f - amount), c.a);

        static Color Mix(Color a, Color b, float towardB)
            => Color.Lerp(a, b, towardB);
    }
}
