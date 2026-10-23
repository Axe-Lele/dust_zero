// 스테이지: the house progress plate, the power skills, the sortie strip, the five regions
// and the chapter list for whichever region is open.
//
// Laid out from the running 10.16-n build. The 스테이지/콘텐츠 segmented control the five-tab
// shell needed is gone: 콘텐츠 is its own bottom tab, so keeping it here listed it twice.
using System;
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class StageTab : MonoBehaviour
    {
        [Header("머리말")]
        [SerializeField] TextMeshProUGUI headCountLabel;

        [Header("진행")]
        [SerializeField] TextMeshProUGUI bestStageLabel;
        [SerializeField] TextMeshProUGUI nextRewardLabel;

        [Header("전력 스킬")]
        [SerializeField] RectTransform powerSkillRoot;
        /// <summary>전력 스킬 아이콘. 표의 id(storm·boost) 순서로 넣는다.</summary>
        [SerializeField] Sprite[] powerSkillIcons = new Sprite[2];
        [SerializeField] string[] powerSkillIconIds = new string[2];

        [Header("출전 편성")]
        [SerializeField] RectTransform deckMiniRoot;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] UiButton deckChangeButton;

        [Header("구역")]
        [SerializeField] RectTransform areaTabRoot;
        [SerializeField] UiToggleChip areaChipPrefab;
        [SerializeField] TextMeshProUGUI areaKickerLabel;
        [SerializeField] TextMeshProUGUI areaTitleLabel;
        [SerializeField] TextMeshProUGUI areaSubLabel;
        [SerializeField] TextMeshProUGUI areaDescLabel;
        [SerializeField] TextMeshProUGUI areaStatLabel;
        [SerializeField] Image areaArtImage;
        /// <summary>구역별 집 단면도. 표 순서(1~5)대로 넣는다.</summary>
        [SerializeField] Sprite[] houseArt = new Sprite[5];

        [Header("목록")]
        [SerializeField] RectTransform listRoot;
        [SerializeField] StageRow rowPrefab;

        [Header("그림")]
        [SerializeField] Sprite miniSprite;

        [Header("글꼴")]
        [SerializeField] TMP_FontAsset bodyFont;
        [SerializeField] TMP_FontAsset titleFont;

        GameServices _svc;
        readonly List<StageRow> _rows = new();
        readonly List<UiToggleChip> _areaChips = new();
        readonly List<GameObject> _spawned = new();
        int _area = 1;

        public event Action<StageDef> StageChosen;
        /// <summary>Raised by 변경, which the shell routes to the 편성 tab.</summary>
        public event Action DeckRequested;

        public void Bind(GameServices services)
        {
            _svc = services;
            if (deckChangeButton != null) deckChangeButton.SetAction(() => DeckRequested?.Invoke());
            BuildAreaTabs();
            BuildPowerSkills();
            Refresh();
        }

        void OnEnable() { if (_svc != null) Refresh(); }

        // ------------------------------------------------------------------ regions

        void BuildAreaTabs()
        {
            foreach (var c in _areaChips) if (c != null) Destroy(c.gameObject);
            _areaChips.Clear();
            if (areaTabRoot == null || areaChipPrefab == null || _svc == null) return;

            foreach (var a in _svc.Db.Areas)
            {
                int id = a.id;
                var chip = Instantiate(areaChipPrefab, areaTabRoot);
                chip.name = "Area" + id;
                // 원본 칩은 세 줄이다: 작은 '1구역' · 굵은 '1층' · 진행 중이면 '진행 중'.
                // 가운데 줄만 쓰면 어느 구역인지, 지금 어디를 하고 있는지가 사라진다.
                // 원본 칩은 76x47 세 줄이다. 기본 칩은 한 줄이라 먼저 풀어 준다.
                chip.AllowMultiLine(UiTheme.Py(47f));
                chip.Bind(AreaChipText(a), () => { _area = id; Refresh(); });
                // 글자를 넣은 뒤 실제 높이로 맞춘다. 줄 수는 글꼴이 정한다.
                chip.FitToText(UiTheme.Py(47f));
                // 폭은 원본과 같이 **고정 75.6** 이다. 칩 프리팹에는 가로를 내용에 맞추는
                // ContentSizeFitter 가 붙어 있어 먼저 꺼야 한다 — 켜 둔 채로는 칩이 넓어져
                // 다섯 개가 398 줄에 안 들어가고 마지막 구역이 화면 밖으로 밀린다.
                var fit = chip.GetComponent<ContentSizeFitter>();
                if (fit != null) fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                var chipRt = (RectTransform)chip.transform;
                chipRt.sizeDelta = new Vector2(UiTheme.Px(75.6f), chipRt.sizeDelta.y);
                var chipLe = chip.GetComponent<LayoutElement>();
                if (chipLe != null) { chipLe.preferredWidth = UiTheme.Px(75.6f); chipLe.minWidth = UiTheme.Px(75.6f); }
                _areaChips.Add(chip);
            }
        }

        /// <summary>
        /// 구역 칩의 세 줄. 원본 `.atab` 은 small(구역 번호) · b(이름) · i.here(진행 중)
        /// 를 쌓는다. 우리 칩은 라벨이 하나라 서식으로 줄을 나눈다.
        /// </summary>
        string AreaChipText(AreaRow a)
        {
            string name = _svc.Db.Text(a.shortKey);
            string head = $"<size=72%>{a.id}구역</size>";
            bool here = CurrentArea() == a.id;
            string foot = here ? "<size=68%>진행 중</size>" : string.Empty;
            return here ? $"{head}\n{name}\n{foot}" : $"{head}\n{name}";
        }

        /// <summary>구역 중 지금 진행 중인 곳 — 최고 스테이지가 속한 구역이다.</summary>
        int CurrentArea()
        {
            int best = _svc.Profile.Data.best;
            foreach (var s in _svc.Db.Stages)
                if (s.Order == best + 1) return s.Area;
            foreach (var s in _svc.Db.Stages)
                if (s.Order == best) return s.Area;
            return 1;
        }

        /// <summary>그 구역의 집 단면도. 없으면 null 이라 바탕색으로 떨어진다.</summary>
        Sprite AreaHouse(int areaId)
            => houseArt != null && areaId >= 1 && areaId <= houseArt.Length
               ? houseArt[areaId - 1] : null;

        /// <summary>The first chapter of a region opens once its first stage is unlocked.</summary>
        bool AreaOpen(AreaRow a)
        {
            foreach (var s in _svc.Db.Stages)
                if (s.Area == a.id) return _svc.Profile.Data.best >= s.Order - 1;
            return false;
        }

        // ------------------------------------------------------------------ power skills

        void BuildPowerSkills()
        {
            if (powerSkillRoot == null || _svc == null || bodyFont == null) return;
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();

            foreach (var kv in _svc.Db.PowerSkills)
            {
                var rows = kv.Value;
                if (rows.Count == 0) continue;
                var first = rows[0];

                var row = new GameObject("ps_" + kv.Key, typeof(RectTransform));
                var rt = (RectTransform)row.transform;
                rt.SetParent(powerSkillRoot, false);
                var le = row.AddComponent<LayoutElement>();
                le.preferredHeight = UiTheme.Py(54f);
                le.minHeight = le.preferredHeight;
                _spawned.Add(row);

                // 왼쪽 46px 은 아이콘 자리다. 원본은 여기에 36x36 스킬 그림을 둔다 —
                // 비워 두면 글자만 뜬 줄이 되어 어느 스킬인지 한눈에 안 들어온다.
                var icoGo = new GameObject("Icon", typeof(RectTransform));
                var icoRt = (RectTransform)icoGo.transform;
                icoRt.SetParent(rt, false);
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(0f, 1f);
                icoRt.pivot = new Vector2(0f, 1f);
                icoRt.sizeDelta = new Vector2(UiTheme.Sq(36f), UiTheme.Sq(36f));
                icoRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(4f));
                var ico = icoGo.AddComponent<Image>();
                ico.sprite = PowerSkillIcon(kv.Key);
                ico.enabled = ico.sprite != null;
                ico.preserveAspect = true;
                ico.raycastTarget = false;

                // Name and the unlock note share the first line, as the build has them;
                // stacking them left the description two lines short of its own box.
                var name = MakeLabel(rt, titleFont != null ? titleFont : bodyFont, UiTheme.Py(13f),
                                     UiTheme.Ink, TextAlignmentOptions.Left);
                name.rectTransform.anchorMin = new Vector2(0f, 1f);
                name.rectTransform.anchorMax = new Vector2(0f, 1f);
                name.rectTransform.offsetMin = new Vector2(UiTheme.Px(46f), -UiTheme.Py(19f));
                name.rectTransform.offsetMax = new Vector2(UiTheme.Px(150f), 0f);
                name.text = _svc.Db.Text($"ps.{kv.Key}.name");

                var cond = MakeLabel(rt, bodyFont, UiTheme.Py(11f), UiTheme.Primary,
                                     TextAlignmentOptions.Left);
                cond.rectTransform.anchorMin = new Vector2(0f, 1f);
                cond.rectTransform.anchorMax = new Vector2(1f, 1f);
                // 원본 `.psr` 은 이름과 안내를 한 줄에 **흘린다** — 안내 글자는 이름이
                // 끝난 자리에서 간격 4 와 안쪽 여백 6 을 더한 곳에서 시작한다. 154 처럼
                // 고정 자리를 적으면 이름이 짧은 스킬에서 33 이나 벌어진다.
                // 막 만든 TMP 는 글자를 아직 한 번도 짜 보지 않아 폭을 0 으로 돌려준다.
                name.ForceMeshUpdate();
                float nameRun = name.GetPreferredValues(name.text).x;
                cond.rectTransform.offsetMin =
                    new Vector2(UiTheme.Px(46f) + nameRun + UiTheme.Px(10f), -UiTheme.Py(18f));
                cond.rectTransform.offsetMax = new Vector2(0f, -UiTheme.Py(1f));
                var at = _svc.Db.StageById(first.unlockStage);
                bool open = at != null && _svc.Profile.Data.best >= at.Order;
                cond.text = at == null ? "" : open ? "해금됨" : $"{at.No} 클리어 시 해금";
                cond.color = open ? UiTheme.Ink2 : UiTheme.Primary;

                var desc = MakeLabel(rt, bodyFont, UiTheme.Py(11.5f), UiTheme.Ink2,
                                     TextAlignmentOptions.TopLeft);
                desc.textWrappingMode = TextWrappingModes.Normal;
                desc.overflowMode = TextOverflowModes.Overflow;
                desc.rectTransform.anchorMin = Vector2.zero;
                desc.rectTransform.anchorMax = Vector2.one;
                desc.rectTransform.offsetMin = new Vector2(UiTheme.Px(46f), 0f);
                desc.rectTransform.offsetMax = new Vector2(0f, -UiTheme.Py(21f));
                desc.text = FillSkillText(_svc.Db.Text($"ps.{kv.Key}.desc"), first);
            }
        }

        /// <summary>표의 스킬 id 로 아이콘을 고른다. 못 찾으면 그리지 않는다.</summary>
        Sprite PowerSkillIcon(string id)
        {
            if (powerSkillIcons == null || powerSkillIconIds == null) return null;
            for (int i = 0; i < powerSkillIconIds.Length && i < powerSkillIcons.Length; i++)
                if (powerSkillIconIds[i] == id) return powerSkillIcons[i];
            return null;
        }

        /// <summary>
        /// The description strings are templates - "반경 {radius}칸", "+{atk_pct}%" - and the
        /// build fills them from the skill's own row. Printing them raw shows the braces.
        /// Speed reads off the attack-interval multiplier, so 0.8 interval is +25% speed.
        /// </summary>
        static string FillSkillText(string template, PowerSkillRow r)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            string Pct(double mul) => Mathf.RoundToInt((float)(mul - 1d) * 100f).ToString();
            return template
                .Replace("{radius}", Trim(r.radius))
                .Replace("{stun}", Trim(r.stun))
                .Replace("{dur}", Trim(r.dur))
                .Replace("{dmg}", Trim(r.dmg))
                .Replace("{cost}", Trim(r.cost))
                .Replace("{cd}", Trim(r.cd))
                .Replace("{atk_pct}", Pct(r.atkMul))
                .Replace("{spd_pct}", Pct(r.itvMul > 0d ? 1d / r.itvMul : 1d))
                .Replace("{sp}", Trim(r.spMul));
        }

        /// <summary>Numbers read as "1.5" and "8", never "8.0".</summary>
        static string Trim(double v) => v == System.Math.Floor(v)
            ? ((long)v).ToString()
            : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        TextMeshProUGUI MakeLabel(RectTransform parent, TMP_FontAsset font, float size,
                                  Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            if (_svc == null) return;
            var p = _svc.Profile;

            if (headCountLabel != null)
                headCountLabel.text = $"{p.Data.best}/{_svc.Db.Stages.Count} 클리어";

            var best = p.Data.best > 0 ? _svc.Db.Stages[Mathf.Min(p.Data.best, _svc.Db.Stages.Count) - 1] : null;
            if (bestStageLabel != null) bestStageLabel.text = best != null ? best.No : "—";

            var next = p.Data.best < _svc.Db.Stages.Count ? _svc.Db.Stages[p.Data.best] : null;
            if (nextRewardLabel != null)
                nextRewardLabel.text = next == null
                    ? "모든 스테이지를 클리어했어요."
                    : $"다음 {next.No} 클리어 시 시급 코인 {next.IdleCoin:N0}, " +
                      $"부품 {next.IdlePart:N0}, 크리스탈 {next.IdleCrystal:N0}";

            RefreshAreas();
            RefreshDeckStrip();
            RefreshList();
        }

        void RefreshAreas()
        {
            var areas = _svc.Db.Areas;
            for (int i = 0; i < _areaChips.Count && i < areas.Count; i++)
            {
                bool open = AreaOpen(areas[i]);
                _areaChips[i].State = areas[i].id == _area ? ChipState.Selected
                                    : open ? ChipState.Normal : ChipState.Locked;
            }

            AreaRow area = null;
            foreach (var a in areas) if (a.id == _area) { area = a; break; }
            if (area == null) return;

            if (areaKickerLabel != null)
                areaKickerLabel.text = $"{_svc.Db.Text(area.shortKey)} · CH.{area.chFrom}~{area.chTo}";
            if (areaTitleLabel != null) areaTitleLabel.text = _svc.Db.Text(area.nameKey);
            if (areaSubLabel != null) areaSubLabel.text = _svc.Db.Text(area.subKey);
            if (areaDescLabel != null) areaDescLabel.text = _svc.Db.Text(area.descKey);
            // 원본은 여기에 집 단면도를 그린다(#houseCv 158x190). 구역마다 한 장씩
            // 구워 두고 바꿔 끼운다 — 바탕색만 칠하면 빈 패널로 보인다.
            if (areaArtImage != null)
            {
                var house = AreaHouse(area.id);
                areaArtImage.sprite = house;
                areaArtImage.color = house != null ? Color.white
                                   : (ColorUtility.TryParseHtmlString(area.bg, out var bg) ? bg : Color.white);
                areaArtImage.preserveAspect = house != null;
            }

            int total = 0, cleared = 0, stars = 0;
            foreach (var s in _svc.Db.Stages)
            {
                if (s.Area != _area) continue;
                total++;
                if (_svc.Profile.Data.best >= s.Order) cleared++;
                stars += _svc.Profile.Data.StarsOf(s.Id);
            }
            if (areaStatLabel != null)
                areaStatLabel.text = $"클리어 {cleared}/{total} · 별 {stars}/{total * 3}";
        }

        void RefreshDeckStrip()
        {
            if (deckMiniRoot == null) return;
            foreach (Transform c in deckMiniRoot) Destroy(c.gameObject);

            foreach (var id in _svc.Profile.Data.deck)
            {
                if (!_svc.Db.Units.TryGetValue(id, out var def)) continue;
                var go = new GameObject("mini_" + id, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(deckMiniRoot, false);
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = UiTheme.Px(34f);
                le.preferredHeight = le.minHeight = UiTheme.Py(34f);
                var img = go.AddComponent<Image>();
                if (miniSprite != null) { img.sprite = miniSprite; img.type = Image.Type.Sliced; }
                img.color = UiTheme.RarityBg(UiTheme.Tier(def.Rarity));
                img.raycastTarget = false;

                // 등급 바탕만 칠하고 끝내면 회색 네모 넉 장이 남는다. 원본은 그 위에
                // 가전 얼굴을 얹는다 — 어떤 편성으로 나가는지가 이 줄의 전부다.
                var faceGo = new GameObject("face", typeof(RectTransform));
                var faceRt = (RectTransform)faceGo.transform;
                faceRt.SetParent(rt, false);
                faceRt.anchorMin = Vector2.zero;
                faceRt.anchorMax = Vector2.one;
                faceRt.offsetMin = new Vector2(UiTheme.Px(3f), UiTheme.Py(3f));
                faceRt.offsetMax = new Vector2(-UiTheme.Px(3f), -UiTheme.Py(3f));
                var face = faceGo.AddComponent<Image>();
                face.sprite = sprites != null ? sprites.Unit(id) : null;
                face.enabled = face.sprite != null;
                face.preserveAspect = true;
                face.raycastTarget = false;
            }
        }

        void RefreshList()
        {
            if (listRoot == null || rowPrefab == null) return;
            var want = new List<StageDef>();
            foreach (var s in _svc.Db.Stages) if (s.Area == _area) want.Add(s);

            foreach (var h in _chapterHeads) if (h != null) Destroy(h);
            _chapterHeads.Clear();

            while (_rows.Count < want.Count) _rows.Add(Instantiate(rowPrefab, listRoot));
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i < want.Count;
                _rows[i].gameObject.SetActive(on);
                if (!on) continue;
                var stage = want[i];
                _rows[i].Current = _svc.Profile.Data.best == stage.Order - 1;
                _rows[i].Bind(stage, _svc, () => StageChosen?.Invoke(stage));
            }

            // 원본은 챕터가 바뀔 때마다 머리글을 넣는다 — 'CH.1 거실 사수' 와
            // 그 챕터의 맵 설명. 없으면 스테이지가 그냥 줄로 주루 늘어졌다.
            int seen = -1, slot = 0;
            for (int i = 0; i < want.Count; i++)
            {
                if (want[i].Chapter == seen) { slot++; continue; }
                seen = want[i].Chapter;
                var head = BuildChapterHead(want[i]);
                head.transform.SetSiblingIndex(slot);
                slot += 2;
            }
        }

        readonly List<GameObject> _chapterHeads = new();

        /// <summary>
        /// 한 챕터의 머리글. 원본 `article.chapter` 의 글 부분을 옮긴 것이다 —
        /// 'CH.N' · 챕터 이름 · 맵 설명. 썸네일은 아직 없다.
        /// </summary>
        GameObject BuildChapterHead(StageDef first)
        {
            var go = new GameObject($"ch_{first.Chapter}", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(listRoot, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(64f);
            _chapterHeads.Add(go);

            var no = MakeLabel(rt, bodyFont, UiTheme.Py(11f), UiTheme.Ink2, TextAlignmentOptions.Left);
            no.rectTransform.anchorMin = new Vector2(0f, 1f);
            no.rectTransform.anchorMax = new Vector2(0f, 1f);
            no.rectTransform.offsetMin = new Vector2(UiTheme.Px(2f), -UiTheme.Py(18f));
            no.rectTransform.offsetMax = new Vector2(UiTheme.Px(60f), 0f);
            no.text = $"CH.{first.Chapter}";

            var name = MakeLabel(rt, titleFont != null ? titleFont : bodyFont, UiTheme.Py(17f),
                                 UiTheme.Ink, TextAlignmentOptions.Left);
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.offsetMin = new Vector2(UiTheme.Px(62f), -UiTheme.Py(25f));
            name.rectTransform.offsetMax = new Vector2(0f, -UiTheme.Py(1f));
            name.text = _svc.Db.Text($"chapter.{first.Chapter}.name");

            var desc = MakeLabel(rt, bodyFont, UiTheme.Py(11.5f), UiTheme.Ink2,
                                 TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.rectTransform.anchorMin = new Vector2(0f, 0f);
            desc.rectTransform.anchorMax = new Vector2(1f, 1f);
            desc.rectTransform.offsetMin = new Vector2(UiTheme.Px(2f), UiTheme.Py(2f));
            desc.rectTransform.offsetMax = new Vector2(0f, -UiTheme.Py(28f));
            desc.text = first.Desc ?? string.Empty;

            return go;
        }

        /// <summary>The lobby guide's "stage.first" spotlight target.</summary>
        public RectTransform FirstStageRow()
            => _rows.Count > 0 && _rows[0].gameObject.activeSelf
                ? (RectTransform)_rows[0].transform : null;
    }
}
