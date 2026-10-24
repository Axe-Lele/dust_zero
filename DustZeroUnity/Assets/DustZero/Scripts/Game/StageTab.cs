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
        /// <summary>해금 알약 바탕(반경 6 둥근 9-slice).</summary>
        [SerializeField] Sprite condPillSprite;
        /// <summary>챕터 머리의 `.chip` 바탕(알약, 반경이 높이보다 크다).</summary>
        [SerializeField] Sprite chapterChipSprite;
        /// <summary>`.milestones` 판(반경 10) 과 `.msb` 버튼(반경 9) 바탕.</summary>
        [SerializeField] Sprite milestonePanelSprite;
        [SerializeField] Sprite milestoneButtonSprite;
        /// <summary>`.ms-bar` 의 트랙·채움(반경 3).</summary>
        [SerializeField] Sprite milestoneBarSprite;
        /// <summary>보상 칩의 숫자 글꼴(원본은 LED 글꼴을 쓴다).</summary>
        [SerializeField] TMP_FontAsset numberFont;
        [SerializeField] string[] powerSkillIconIds = new string[2];

        [Header("출전 편성")]
        [SerializeField] RectTransform deckMiniRoot;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] UiButton deckChangeButton;

        [Header("구역")]
        [SerializeField] RectTransform areaTabRoot;
        [SerializeField] AreaChip areaChipPrefab;
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
        readonly List<AreaChip> _areaChips = new();

        /// <summary>원본 `.psr em` 알약 색 — 글자 `#B7791F` 가 바탕 `#FFF1C2` 위에 앉는다.</summary>
        static readonly Color CondPillInk = new Color32(0xB7, 0x79, 0x1F, 0xFF);
        static readonly Color CondPillFace = new Color32(0xFF, 0xF1, 0xC2, 0xFF);

        /// <summary>
        /// 전력 스킬 한 줄에서 **탭이 켜진 뒤에** 손봐야 하는 것들.
        ///
        /// 왜 나중인가: 이름 폭을 재야 해금 알약을 그 뒤에 붙일 수 있는데, TMP 는
        /// 꺼져 있는 동안 글자를 짜지 않아 폭을 0 에 가깝게 돌려준다. 실제로 그래서
        /// 알약이 이름 위에 겹쳐 찍혀 둘 다 못 읽었다(이름 ink 53.8 인데 알약이 15.4 에서 시작).
        /// </summary>
        sealed class PowerRow
        {
            public string skillId;
            public int unlockStage;
            public TextMeshProUGUI name, cond;
            public RectTransform pill;
            public CanvasGroup fade;
        }

        readonly List<PowerRow> _powerRows = new();
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
                var chip = Instantiate(areaChipPrefab, areaTabRoot);
                chip.name = "Area" + a.id;
                // 폭은 원본 그리드가 내는 고정 75.6 이다(398 줄에 5칸 · 간격 5).
                // 높이는 칩 자신의 ContentSizeFitter 가 글자 줄 수에서 뽑는다.
                var chipRt = (RectTransform)chip.transform;
                chipRt.sizeDelta = new Vector2(UiTheme.Px(75.6f), chipRt.sizeDelta.y);
                _areaChips.Add(chip);
            }
        }

        /// <summary>
        /// 구역 색. 표의 `color` 가 원본 `--ac` 다 — 구역마다 달라서 테마에 박지 않는다.
        /// 읽지 못하면 선택 칩이 투명해지므로 주황(SSR)으로 떨어뜨린다.
        /// </summary>
        Color AreaAccent(AreaRow a)
            => ColorUtility.TryParseHtmlString(a.color ?? string.Empty, out var c)
               ? c : UiTheme.RaritySSR;

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
            _powerRows.Clear();

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

                // 원본 `.psr em` 은 맨 글자가 아니라 **작은 호박색 알약**이다:
                //   font 11/600 · 글자 #B7791F · 바탕 #FFF1C2 · 반경 6 · 안쪽 0 6 · 왼쪽 간격 4
                var pillGo = new GameObject("CondPill", typeof(RectTransform));
                var pillRt = (RectTransform)pillGo.transform;
                pillRt.SetParent(rt, false);
                pillRt.anchorMin = pillRt.anchorMax = new Vector2(0f, 1f);
                pillRt.pivot = new Vector2(0f, 1f);
                var pill = pillGo.AddComponent<Image>();
                pill.sprite = condPillSprite;
                pill.type = Image.Type.Sliced;
                pill.color = CondPillFace;
                pill.raycastTarget = false;

                var cond = MakeLabel(pillRt, bodyFont, UiTheme.Py(11f), CondPillInk,
                                     TextAlignmentOptions.Center);
                cond.rectTransform.anchorMin = Vector2.zero;
                cond.rectTransform.anchorMax = Vector2.one;
                cond.rectTransform.offsetMin = Vector2.zero;
                cond.rectTransform.offsetMax = Vector2.zero;

                // `.psr.off` 는 줄 전체를 .55 로 흐린다.
                var fade = row.AddComponent<CanvasGroup>();

                _powerRows.Add(new PowerRow
                {
                    skillId = kv.Key, unlockStage = first.unlockStage,
                    name = name, cond = cond, pill = pillRt, fade = fade,
                });

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

        /// <summary>
        /// 해금 알약의 글귀·색·자리를 잡는다. 이름 폭을 재야 하므로 **탭이 켜진 뒤**
        /// <see cref="Refresh"/> 에서 부른다 — 지어질 때 재면 0 이 나온다.
        /// </summary>
        void RefreshPowerSkills()
        {
            foreach (var r in _powerRows)
            {
                if (r.name == null || r.cond == null || r.pill == null) continue;

                var at = _svc.Db.StageById(r.unlockStage);
                bool open = at != null && _svc.Profile.Data.best >= at.Order;
                r.cond.text = at == null ? string.Empty : open ? "해금됨" : $"{at.No} 클리어 시 해금";
                // `.psr.off` — 아직 못 쓰는 스킬은 줄 전체가 흐리다.
                if (r.fade != null) r.fade.alpha = open ? 1f : 0.55f;

                // 이름이 실제로 차지한 폭(advance)을 재고 그 뒤 4 에 알약을 붙인다.
                // 원본 `em` 은 `margin-left:4` · `padding:0 6` 이다.
                r.name.ForceMeshUpdate();
                r.cond.ForceMeshUpdate();
                float nameRun = r.name.preferredWidth;
                float condRun = r.cond.preferredWidth;
                float pad = UiTheme.Px(6f);
                float h = UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(17f));
                r.pill.sizeDelta = new Vector2(condRun + pad * 2f, h);
                r.pill.anchoredPosition = new Vector2(
                    UiTheme.Px(46f) + nameRun + UiTheme.Px(4f), -UiTheme.Py(2f));
                r.pill.gameObject.SetActive(!string.IsNullOrEmpty(r.cond.text));
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

        /// <summary>빈 RectTransform 한 개. 에디터의 UiKit 은 런타임에서 못 쓴다.</summary>
        static RectTransform MakeNode(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

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
            RefreshPowerSkills();
            RefreshList();
        }

        void RefreshAreas()
        {
            var areas = _svc.Db.Areas;
            int here = CurrentArea();
            for (int i = 0; i < _areaChips.Count && i < areas.Count; i++)
            {
                var a = areas[i];
                int id = a.id;
                _areaChips[i].Bind($"{a.id}구역", _svc.Db.Text(a.shortKey), AreaAccent(a),
                                   selected: id == _area, locked: !AreaOpen(a), here: id == here,
                                   onTap: () => { _area = id; Refresh(); });
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

            // **부모에서 먼저 뗀다.** `Destroy` 는 프레임 끝에야 적용되므로 그냥 부르면
            // 옛 머리글이 아직 자식으로 남아 아래의 `SetSiblingIndex` 를 전부 밀어낸다 —
            // 실제로 챕터당 5줄이어야 할 것이 3/3/9 로 갈라져 있었다.
            foreach (var h in _chapterHeads)
                if (h != null) { h.transform.SetParent(null, false); Destroy(h); }
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
                slot++;
                // 원본은 머리글 **바로 다음에** 챕터 별 보상 칸을 둔다.
                var ms = BuildMilestones(want[i].Chapter);
                if (ms != null) { ms.transform.SetSiblingIndex(slot); slot++; }
                slot++;
            }
        }

        readonly List<GameObject> _chapterHeads = new();

        /// <summary>
        /// 한 챕터의 머리글. 원본 `.ch-head` 를 그대로 옮긴다 (index.html 2216~2218):
        /// 왼쪽 70x100 미니맵 + 간격 12 + 폭 290 글자 칸, 칸 안에는
        /// `CH.N` + 챕터 이름 한 줄 · 설명 두 줄 · 방/기믹 칩 한 줄.
        ///
        /// **설명 칸을 290 으로 묶는 것이 핵심이다.** 전체 폭을 주면 설명이 한 줄로
        /// 펴지면서 챕터마다 약 30px 씩 짧아지고, 그 차이가 아래로 쌓여 욕실 구간에서는
        /// 원본과 448px 어긋난다(전수 대조로 확인).
        /// </summary>
        GameObject BuildChapterHead(StageDef first)
        {
            var go = new GameObject($"ch_{first.Chapter}", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(listRoot, false);
            // 원본 `.chapter` 는 패딩 `12px 12px 4px` 이고 목록 간격이 10 이다
            // (우리 목록 간격은 8). 그래서 챕터마다 12+4+2 = **18** 이 비는데,
            // 그 몫을 머리글 위 여백으로 넣는다 — 안 넣으면 챕터가 아래로 갈수록
            // 원본보다 위로 밀린다(CH.3 에서 46px).
            const float TopPad = 18f;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(106f + TopPad);
            _chapterHeads.Add(go);

            // 원본 좌표는 `.app`(좌 16) 안에서 x=29 이므로, 목록 기준으로는 13 이다.
            const float Left = 13f;        // 29 - 16
            const float ThumbW = 70f, Gap = 12f, MetaW = 290f;
            float metaLeft = Left + ThumbW + Gap;
            float top0 = UiTheme.Py(TopPad);

            // ---- 미니맵. 판 그림이 없으면 자리만 비워 둔다(칸을 접지는 않는다).
            var mapRt = MakeNode("Map", rt);
            mapRt.anchorMin = mapRt.anchorMax = new Vector2(0f, 1f);
            mapRt.pivot = new Vector2(0f, 1f);
            mapRt.sizeDelta = new Vector2(UiTheme.Px(ThumbW), UiTheme.Py(100f));
            mapRt.anchoredPosition = new Vector2(UiTheme.Px(Left), -top0);
            var map = mapRt.gameObject.AddComponent<Image>();
            map.sprite = sprites != null ? sprites.Board(first.MapId) : null;
            map.enabled = map.sprite != null;
            map.preserveAspect = true;
            map.raycastTarget = false;

            // ---- `.ttl` — CH.N(13) 과 이름(21/23.1) 이 간격 8 로 나란히.
            float ttlH = UiTheme.Row(UiTheme.Py(13f), UiTheme.Py(23.1f));
            var no = MakeLabel(rt, bodyFont, UiTheme.Py(13f), UiTheme.Ink2, TextAlignmentOptions.Left);
            no.rectTransform.anchorMin = no.rectTransform.anchorMax = new Vector2(0f, 1f);
            no.rectTransform.pivot = new Vector2(0f, 1f);
            no.rectTransform.sizeDelta = new Vector2(UiTheme.Px(34f), ttlH);
            no.rectTransform.anchoredPosition = new Vector2(UiTheme.Px(metaLeft), -top0);
            no.text = $"CH.{first.Chapter}";

            var name = MakeLabel(rt, titleFont != null ? titleFont : bodyFont, UiTheme.Py(21f),
                                 UiTheme.Ink, TextAlignmentOptions.Left);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 1f);
            name.rectTransform.pivot = new Vector2(0f, 1f);
            name.rectTransform.sizeDelta = new Vector2(UiTheme.Px(MetaW - 42f), ttlH);
            name.rectTransform.anchoredPosition = new Vector2(UiTheme.Px(metaLeft + 42f), -top0);
            name.text = _svc.Db.Text($"chapter.{first.Chapter}.name");

            // ---- `p.sc-desc` — 12.5px, 줄 높이 19.375, **두 줄**.
            float descTop = ttlH + UiTheme.Py(5f);
            var desc = MakeLabel(rt, bodyFont, UiTheme.Py(12.5f), UiTheme.Ink2,
                                 TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.overflowMode = TextOverflowModes.Overflow;
            desc.lineSpacing = LineSpacingFor(UiTheme.Py(12.5f), UiTheme.Py(19.375f));
            desc.rectTransform.anchorMin = desc.rectTransform.anchorMax = new Vector2(0f, 1f);
            desc.rectTransform.pivot = new Vector2(0f, 1f);
            desc.rectTransform.sizeDelta = new Vector2(UiTheme.Px(MetaW), UiTheme.Py(38.8f));
            desc.rectTransform.anchoredPosition = new Vector2(UiTheme.Px(metaLeft), -(top0 + descTop));
            desc.text = first.Desc ?? string.Empty;

            // ---- `.chips` — 방 이름, 그리고 기믹이 있으면 주황 경고 칩.
            float chipsTop = descTop + UiTheme.Py(38.8f) + UiTheme.Py(5f);
            float x = UiTheme.Px(metaLeft);
            x = SpawnChapterChip(rt, first.Name, false, x, top0 + chipsTop);
            if (!string.IsNullOrEmpty(first.Gimmick))
                SpawnChapterChip(rt, first.Gimmick, true, x + UiTheme.Px(4f), top0 + chipsTop);

            return go;
        }

        /// <summary>
        /// 챕터 별 보상 칸. 원본 `.milestones` 를 그대로 옮긴다 —
        /// 372x135.7 · 바탕 #E6EAED · 반경 10 · 안쪽 10, 안에 머리줄(22) · 막대(6) ·
        /// 버튼 셋(71.7)이 간격 8 로 쌓인다.
        ///
        /// 이 칸이 없으면 챕터마다 135.7 씩 짧아져 아래 챕터가 전부 위로 밀린다
        /// (전수 대조에서 CH.3 이 359px 어긋나 있었다).
        /// </summary>
        GameObject BuildMilestones(int chapter)
        {
            if (!_svc.Db.StarRewards.TryGetValue(chapter, out var rows) || rows.Count == 0)
                return null;

            const float Left = 13f, W = 372f, Pad = 10f;
            float inner = W - Pad * 2f;          // 352

            var go = new GameObject($"ms_{chapter}", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(listRoot, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(135.7f);
            _chapterHeads.Add(go);

            var panelRt = MakeNode("Panel", rt);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.sizeDelta = new Vector2(UiTheme.Px(W), UiTheme.Py(135.7f));
            panelRt.anchoredPosition = new Vector2(UiTheme.Px(Left), 0f);
            var panel = panelRt.gameObject.AddComponent<Image>();
            panel.sprite = milestonePanelSprite;
            panel.type = Image.Type.Sliced;
            panel.color = new Color32(0xE6, 0xEA, 0xED, 0xFF);
            panel.raycastTarget = false;

            int have = _svc.Profile.ChapterStars(chapter);
            int need = 0;
            foreach (var r in rows) if (r.needStars > need) need = r.needStars;

            // ---- `.ms-head` — 왼쪽 "챕터 별", 오른쪽 "★ have/need".
            float headH = UiTheme.Row(UiTheme.Py(12f), UiTheme.Py(22f));
            var cap = MakeLabel(panelRt, bodyFont, UiTheme.Py(12f), UiTheme.Ink2,
                                TextAlignmentOptions.Left);
            cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0f, 1f);
            cap.rectTransform.pivot = new Vector2(0f, 1f);
            cap.rectTransform.sizeDelta = new Vector2(UiTheme.Px(inner * 0.5f), headH);
            cap.rectTransform.anchoredPosition = new Vector2(UiTheme.Px(Pad), -UiTheme.Py(Pad));
            cap.text = "챕터 별";

            var tally = MakeLabel(panelRt, titleFont != null ? titleFont : bodyFont,
                                  UiTheme.Py(16f), UiTheme.RaritySSR,
                                  TextAlignmentOptions.Right);
            tally.rectTransform.anchorMin = tally.rectTransform.anchorMax = new Vector2(1f, 1f);
            tally.rectTransform.pivot = new Vector2(1f, 1f);
            tally.rectTransform.sizeDelta = new Vector2(UiTheme.Px(inner * 0.5f), headH);
            tally.rectTransform.anchoredPosition = new Vector2(-UiTheme.Px(Pad), -UiTheme.Py(Pad));
            tally.text = $"★ {have}<size=75%>/{need}</size>";

            // ---- `.ms-bar` — 트랙 위에 채움, 그리고 각 문턱 자리에 눈금 2x10.
            float barTop = Pad + 22f + 8f;
            var trackRt = MakeNode("Track", panelRt);
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0f, 1f);
            trackRt.pivot = new Vector2(0f, 1f);
            trackRt.sizeDelta = new Vector2(UiTheme.Px(inner), UiTheme.Py(6f));
            trackRt.anchoredPosition = new Vector2(UiTheme.Px(Pad), -UiTheme.Py(barTop));
            var track = trackRt.gameObject.AddComponent<Image>();
            track.sprite = milestoneBarSprite;
            track.type = Image.Type.Sliced;
            track.color = UiTheme.PanelLine;
            track.raycastTarget = false;

            var fillRt = MakeNode("Fill", trackRt);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(need > 0 ? Mathf.Clamp01(have / (float)need) : 0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = milestoneBarSprite;
            fill.type = Image.Type.Sliced;
            fill.color = UiTheme.Primary;
            fill.raycastTarget = false;

            foreach (var r in rows)
            {
                if (need <= 0) break;
                var tickRt = MakeNode("Tick", trackRt);
                tickRt.anchorMin = tickRt.anchorMax = new Vector2(r.needStars / (float)need, 0.5f);
                tickRt.pivot = new Vector2(1f, 0.5f);
                tickRt.sizeDelta = new Vector2(UiTheme.Px(2f), UiTheme.Py(10f));
                tickRt.anchoredPosition = Vector2.zero;
                var tick = tickRt.gameObject.AddComponent<Image>();
                tick.color = UiTheme.Ink2;
                tick.raycastTarget = false;
            }

            // ---- `.ms-btns` — 폭 113.33 짜리 버튼 셋, 간격 6.
            float btnTop = barTop + 6f + 8f;
            float btnW = (inner - 6f * 2f) / 3f;
            for (int i = 0; i < rows.Count && i < 3; i++)
                BuildMilestoneButton(panelRt, rows[i], have,
                                     Pad + (btnW + 6f) * i, btnTop, btnW);

            return go;
        }

        /// <summary>`.msb` 한 개 — 위에 `★N`, 아래에 보상 아이콘+숫자.</summary>
        void BuildMilestoneButton(RectTransform parent, StarRewardRow row, int have,
                                  float left, float top, float width)
        {
            bool claimed = _svc.Profile.Data.IsStarRewardClaimed(row.id);
            bool ready = !claimed && have >= row.needStars;

            var btnRt = MakeNode($"msb_{row.id}", parent);
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.sizeDelta = new Vector2(UiTheme.Px(width), UiTheme.Py(71.7f));
            btnRt.anchoredPosition = new Vector2(UiTheme.Px(left), -UiTheme.Py(top));
            var face = btnRt.gameObject.AddComponent<Image>();
            face.sprite = milestoneButtonSprite;
            face.type = Image.Type.Sliced;
            // `.msb.ready` 는 주황 #FF9A3C, 평소는 #F7F9FA 에 #C7D0D6 테두리.
            face.color = ready ? UiTheme.Primary : UiTheme.Panel;
            face.raycastTarget = true;

            var btn = btnRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.transition = Selectable.Transition.None;
            btn.interactable = ready;
            if (ready)
            {
                var claim = row;
                btn.onClick.AddListener(() =>
                {
                    _svc.Profile.ClaimStarReward(claim);
                    _svc.Persist();
                    Refresh();
                });
            }

            var star = MakeLabel(btnRt, bodyFont, UiTheme.Py(14f),
                                 ready ? UiTheme.Ink : UiTheme.Ink2, TextAlignmentOptions.Center);
            star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            star.rectTransform.pivot = new Vector2(0.5f, 1f);
            star.rectTransform.sizeDelta = new Vector2(UiTheme.Px(width),
                                                       UiTheme.Row(UiTheme.Py(14f), UiTheme.Py(18.9f)));
            star.rectTransform.anchoredPosition = new Vector2(0f, -UiTheme.Py(6f));
            star.text = $"★{row.needStars}";

            var chipRt = MakeNode("Chips", btnRt);
            chipRt.anchorMin = chipRt.anchorMax = new Vector2(0.5f, 1f);
            chipRt.pivot = new Vector2(0.5f, 1f);
            chipRt.sizeDelta = new Vector2(UiTheme.Px(width - 8f), UiTheme.Py(37.8f));
            chipRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(26.9f));
            var flow = chipRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            flow.spacing = UiTheme.Px(4f);
            flow.childAlignment = TextAnchor.MiddleCenter;
            flow.childControlWidth = flow.childControlHeight = true;
            flow.childForceExpandWidth = flow.childForceExpandHeight = false;
            var chips = chipRt.gameObject.AddComponent<RewardChipRow>();
            chips.Configure(numberFont != null ? numberFont : bodyFont, sprites);
            chips.Ink = ready ? UiTheme.Ink : UiTheme.Ink2;
            chips.Show(_svc.Profile.GroupChips(row.rewardGroup));
        }

        /// <summary>원본 `.chips .chip` 한 개. 돌려주는 값은 그 칩의 오른쪽 끝이다.</summary>
        float SpawnChapterChip(RectTransform parent, string text, bool warn, float x, float top)
        {
            var chipRt = MakeNode("Chip", parent);
            chipRt.anchorMin = chipRt.anchorMax = new Vector2(0f, 1f);
            chipRt.pivot = new Vector2(0f, 1f);
            var img = chipRt.gameObject.AddComponent<Image>();
            img.sprite = chapterChipSprite;
            img.type = Image.Type.Sliced;
            // `.chip` #E6EAED/#5D6871 · `.chip.warn` #FCE3D1/#9A4A12
            img.color = warn ? new Color32(0xFC, 0xE3, 0xD1, 0xFF) : new Color32(0xE6, 0xEA, 0xED, 0xFF);
            img.raycastTarget = false;

            var label = MakeLabel(chipRt, bodyFont, UiTheme.Py(11f),
                                  warn ? (Color)new Color32(0x9A, 0x4A, 0x12, 0xFF)
                                       : (Color)new Color32(0x5D, 0x68, 0x71, 0xFF),
                                  TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;
            label.ForceMeshUpdate();

            // `padding: 2px 8px` — 칩은 글자 폭에 좌우 8 씩 더한 만큼이다.
            float w = label.preferredWidth + UiTheme.Px(16f);
            float h = UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(21f));
            chipRt.sizeDelta = new Vector2(w, h);
            chipRt.anchoredPosition = new Vector2(x, -top);
            return x + w;
        }

        /// <summary>
        /// 원본의 `line-height` 를 TMP 의 `lineSpacing` 으로 옮긴다. TMP 는 글자 크기
        /// 대비 **추가** 간격을 쓰므로 원하는 줄 높이에서 글자 몫을 빼야 한다.
        /// </summary>
        static float LineSpacingFor(float fontSize, float wantedLineHeight)
            => fontSize <= 0f ? 0f : (wantedLineHeight - fontSize * 1.2f) / fontSize * 100f;

        /// <summary>The lobby guide's "stage.first" spotlight target.</summary>
        public RectTransform FirstStageRow()
            => _rows.Count > 0 && _rows[0].gameObject.activeSelf
                ? (RectTransform)_rows[0].transform : null;
    }
}
