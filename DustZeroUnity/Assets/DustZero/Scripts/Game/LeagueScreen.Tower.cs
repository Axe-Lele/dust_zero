// 먼지 타워 화면 — 원본 `twEnter` / `twFloorInfo` / `twSync` / `twShowOver`.
//
// 원본이 `#league` 에 `tw` 클래스를 붙여 같은 화면을 쓰듯, 여기서도 LeagueScreen 을
// 그대로 쓰고 타워 쪽만 이 partial 에 둔다. 다른 것은 셋이다:
//   · HUD 의 청결도·코인·레벨 줄 대신 층 · 남은 시간 · 출전 줄(`.tw-ro`)
//   · 시너지 아래 층 규칙 줄(`.tw-rule`)
//   · 대기석·버튼·상점 대신 보유 가전 목록(`.tw-own`)
// 그래서 판·안내·출발·힌트의 세로 자리도 리그와 다르다. 전부 430x932 에서 실측했다 —
// production/qa/audit/original/TOWER-MEASUREMENTS.md.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.League;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed partial class LeagueScreen
    {
        [Header("타워 · HUD")]
        [SerializeField] TextMeshProUGUI kickerLabel;
        [SerializeField] GameObject leagueReadoutRoot;
        [SerializeField] GameObject towerReadoutRoot;
        [SerializeField] TextMeshProUGUI towerFloorValue;
        [SerializeField] TextMeshProUGUI towerGoalCaption;
        [SerializeField] TextMeshProUGUI towerGoalValue;
        [SerializeField] TextMeshProUGUI towerUseValue;

        [Header("타워 · 층 규칙 줄")]
        [SerializeField] GameObject ruleRoot;
        [SerializeField] Image ruleFace;
        [SerializeField] Image ruleLine;
        [SerializeField] TextMeshProUGUI ruleNameLabel;
        [SerializeField] TextMeshProUGUI ruleDescLabel;
        [SerializeField] TextMeshProUGUI ruleBonusLabel;
        [SerializeField] RectTransform ruleTopRow;
        [SerializeField] LayoutElement ruleNameLayout;

        [Header("타워 · 출전 목록")]
        [SerializeField] RectTransform ownRoot;
        [SerializeField] TowerOwnCell ownPrefab;
        [SerializeField] UiButton autoButton;

        [Header("타워 · 층 목록")]
        [SerializeField] GameObject floorSheetRoot;
        [SerializeField] RectTransform floorListRoot;
        [SerializeField] TowerFloorCard floorPrefab;
        [SerializeField] ScrollRect floorScroll;
        [SerializeField] TextMeshProUGUI floorHeadLabel;
        [SerializeField] TextMeshProUGUI floorCountLabel;
        [SerializeField] TextMeshProUGUI floorNoteLabel;
        [SerializeField] TextMeshProUGUI floorRoofLabel;
        [SerializeField] UiButton floorCloseButton;
        [SerializeField] UiButton floorGoButton;
        [SerializeField] TextMeshProUGUI floorGoLabel;

        [Header("타워 · 층 상세")]
        [SerializeField] GameObject infoSheetRoot;
        [SerializeField] TextMeshProUGUI infoTitleLabel;
        [SerializeField] TextMeshProUGUI infoStateLabel;
        [SerializeField] Image infoRuleFace;
        [SerializeField] Image infoRuleLine;
        [SerializeField] TextMeshProUGUI infoRuleNameLabel;
        [SerializeField] TextMeshProUGUI infoRuleDescLabel;
        [SerializeField] TextMeshProUGUI infoGoalLabel;
        [SerializeField] TextMeshProUGUI infoGoalSubLabel;
        [SerializeField] Image infoBonusFace;
        [SerializeField] Image infoBonusLine;
        [SerializeField] TextMeshProUGUI infoBonusLabel;
        [SerializeField] TextMeshProUGUI infoBonusSubLabel;
        [SerializeField] RectTransform infoFoesRoot;
        [SerializeField] TextMeshProUGUI infoRewardLabel;
        [SerializeField] UiButton infoBackButton;
        [SerializeField] UiButton infoStartButton;

        /// <summary>리그에서 쓰는 줄들 — 타워에서는 숨기고 자리를 다시 잡는다.</summary>
        [Header("타워 · 자리 바꾸기")]
        [SerializeField] RectTransform hudRoot;
        [SerializeField] RectTransform barRoot;
        [SerializeField] RectTransform infoRoot;
        [SerializeField] RectTransform goRoot;
        [SerializeField] RectTransform hintRoot;

        readonly List<TowerOwnCell> _ownCells = new();
        readonly List<TowerFloorCard> _floorRows = new();
        readonly List<GameObject> _foeCards = new();

        /// <summary>먼지 칸의 둥근 바탕(원본 `.tw-foes>div`, 모서리 10).</summary>
        [SerializeField] Sprite foeCardSprite;

        /// <summary>층 목록 창에 세우는 공기청정기 — 판에 쓰는 것과 같은 그림이다.</summary>
        [SerializeField] Sprite purifierSprite;

        /// <summary>층 상세에서 고른 층. 0이면 아직 안 골랐다.</summary>
        int _pickFloor;

        /// <summary>리그 자리로 되돌리기 위해 Awake 에서 적어 둔 값.</summary>
        readonly Dictionary<RectTransform, (Vector2 Min, Vector2 Max)> _leagueRects = new();

        public bool IsTower => _sim != null && _sim.IsTower;

        /// <summary>타워에서 로비로 나간다.</summary>
        public event System.Action TowerExited;

        void AwakeTower()
        {
            Remember(infoRoot);
            Remember(goRoot);
            Remember(hintRoot);

            if (autoButton != null) autoButton.SetAction(TowerAutoFill);
            if (floorCloseButton != null)
                floorCloseButton.SetAction(() => { HideTower(); TowerExited?.Invoke(); });
            if (floorGoButton != null)
                floorGoButton.SetAction(() => { if (_pickFloor > 0) ShowFloorInfo(_pickFloor); });
            if (infoBackButton != null) infoBackButton.SetAction(() => OpenFloorPick(_svc));
            if (infoStartButton != null)
                infoStartButton.SetAction(() => { if (_pickFloor > 0) BeginTower(_svc, _pickFloor); });
        }

        void Remember(RectTransform rt)
        {
            if (rt != null) _leagueRects[rt] = (rt.offsetMin, rt.offsetMax);
        }

        // ------------------------------------------------------------------ 층 목록

        /// <summary>
        /// 원본 `twEnter` — 탑 한 채를 통째로 보여 준다. 위층이 먼저고, 갈 수 있는 가장
        /// 높은 층에 '도전' 띠가 붙는다. 잠긴 층은 눌러도 열리지 않는다.
        /// </summary>
        public void OpenFloorPick(GameServices services)
        {
            _svc = services;
            gameObject.SetActive(true);

            if (tierRoot != null) tierRoot.SetActive(false);
            if (resultRoot != null) resultRoot.SetActive(false);
            if (bannerRoot != null) bannerRoot.SetActive(false);
            if (infoSheetRoot != null) infoSheetRoot.SetActive(false);
            if (boardCamera != null) boardCamera.enabled = false;
            if (floorSheetRoot != null) floorSheetRoot.SetActive(true);
            // 원본은 층 목록을 로비 위에 띄운다. 우리는 리그 화면 안이라 판이 열리기 전까지
            // 리그 쪽 줄을 덮어 둔다 — 빈 HUD 가 시트 뒤에 비치지 않게.
            ShowBattleChrome(false);

            var db = _svc.Db;
            var profile = _svc.Profile;
            int top = profile.TowerTopFloor();
            _pickFloor = top;

            BuildFloorRows();

            int cleared = 0, bonusLeft = 0, bonusCrystal = 0;
            foreach (var f in db.TowerFloors)
            {
                if (profile.TowerCleared(f.floor)) cleared++;
                if (profile.TowerCleared(f.floor) && !profile.TowerBonusDone(f.floor))
                {
                    bonusLeft++;
                    bonusCrystal += f.bonusCrystal;
                }
            }

            if (floorHeadLabel != null) floorHeadLabel.text = "먼지 타워";
            if (floorCountLabel != null)
                floorCountLabel.text = $"{cleared}/{db.TowerFloors.Count}층 청소";
            if (floorNoteLabel != null)
                floorNoteLabel.text = "층마다 첫 클리어 보상과 보너스 목표 보상을 한 번씩 받아요. "
                                    + "재도전은 무료·무제한, 키운 가전이 그대로 강해요."
                                    + (bonusLeft > 0
                                        ? $"\n보너스 남은 층 {bonusLeft}곳 · 크리스탈 {bonusCrystal}"
                                        : string.Empty);
            if (floorRoofLabel != null)
                floorRoofLabel.text = $"{db.TowerFloors.Count + 1}층부터는 준비 중이에요";
            if (floorGoLabel != null)
                floorGoLabel.text = profile.TowerCleared(top) ? $"{top}층 다시 보기" : $"{top}층 도전";

            ScrollToFloor(top);
        }

        void BuildFloorRows()
        {
            foreach (var r in _floorRows) if (r != null) Destroy(r.gameObject);
            _floorRows.Clear();
            if (floorListRoot == null || floorPrefab == null) return;

            var db = _svc.Db;
            var profile = _svc.Profile;
            int top = profile.TowerTopFloor();

            // 원본은 위층부터 쌓는다 — 탑이니까.
            foreach (var f in db.TowerFloors.OrderByDescending(x => x.floor))
            {
                int number = f.floor;
                bool done = profile.TowerCleared(number);
                var state = done ? TowerFloorState.Cleared
                          : number == top ? TowerFloorState.Current
                          : number > top ? TowerFloorState.Locked
                          : TowerFloorState.Open;
                bool boss = f.rule == "boss";
                string kind = TowerText.RuleKind(db, f);

                string reward = done
                    ? profile.TowerBonusDone(number)
                        ? "청소 완료 · 보너스 받음"
                        : $"청소 완료 · 보너스 크리스탈 {f.bonusCrystal}"
                    : TowerText.RewardLine(db, f);

                var row = Instantiate(floorPrefab, floorListRoot);
                row.Show(number, state, boss,
                         string.IsNullOrEmpty(f.rule) ? null : TowerText.RuleName(db, f),
                         UiTheme.TowerTag(kind, boss), db.Text(f.nameKey), reward);
                if (row.Window != null)
                {
                    var (guest, guestTint) = WindowGuest(f);
                    row.Window.Show(done, state == TowerFloorState.Locked, boss, f.rule,
                                    FoeSprites(f), WhoSprites(number), guest, guestTint);
                }
                row.Tapped += () => OnFloorTapped(number);
                _floorRows.Add(row);
            }
        }

        /// <summary>창에 세울 먼지 그림 최대 셋. 보스 층은 보스 하나만 세운다.</summary>
        List<Sprite> FoeSprites(TowerFloorRow floor)
        {
            var list = new List<Sprite>();
            if (sprites == null) return list;

            // 보스 층은 원본도 보스만 크게 그린다.
            bool bossFloor = floor.rule == "boss";
            foreach (var (id, _) in LeagueSim.ParseSquad(floor.squad))
            {
                bool isBoss = id.StartsWith("boss_");
                if (bossFloor && !isBoss) continue;
                if (!_svc.Db.Enemies.TryGetValue(id, out var def)) continue;
                var art = sprites.Enemy(id, def.Look, isBoss);
                if (art != null && !list.Contains(art)) list.Add(art);
                if (list.Count >= (bossFloor ? 1 : 3)) break;
            }
            return list;
        }

        /// <summary>깬 층에 세울 가전 — 그때 데리고 간 셋이다(원본 `sv.who`).</summary>
        List<Sprite> WhoSprites(int floor)
        {
            var list = new List<Sprite>();
            if (sprites == null) return list;
            foreach (var id in _svc.Profile.TowerWho(floor))
            {
                var art = sprites.Unit(id);
                if (art != null) list.Add(art);
                if (list.Count >= 3) break;
            }
            return list;
        }

        /// <summary>
        /// 창 오른쪽에 서는 손님. 지키기는 공기청정기, 손님 층은 빌려 주는 가전,
        /// 황금 층은 금빛 먼지, 거울 층은 회색 가전이다(원본 `twWindow`).
        /// </summary>
        (Sprite Art, Color Tint) WindowGuest(TowerFloorRow floor)
        {
            if (sprites == null) return (null, Color.white);
            switch (floor.rule)
            {
                case "guard":
                    return (purifierSprite, Color.white);
                case "guest":
                    return (sprites.Unit(floor.extra), Color.white);
                case "golden":
                    return (_svc.Db.Enemies.TryGetValue("dust", out var dust)
                                ? sprites.Enemy("dust", dust.Look, false) : null,
                            new Color(1f, 0.82f, 0.32f));
                case "mirror":
                    // 원본도 청소기 한 대를 회색으로 세워 '가짜 가전' 임을 보여 준다.
                    return (sprites.Unit("vac"), new Color(0.62f, 0.58f, 0.66f));
                default:
                    return (null, Color.white);
            }
        }

        void OnFloorTapped(int floor)
        {
            if (floor > _svc.Profile.TowerTopFloor())
            {
                SetHint("아래층부터 청소해 주세요.");
                return;
            }
            ShowFloorInfo(floor);
        }

        void ScrollToFloor(int floor)
        {
            if (floorScroll == null || _floorRows.Count == 0) return;
            int index = _floorRows.FindIndex(r => r != null && r.Floor == floor);
            if (index < 0) return;
            // 목록은 위층부터라 0이 꼭대기다. 고른 층이 가운데에 오게 맞춘다.
            float t = _floorRows.Count <= 1 ? 0f : index / (float)(_floorRows.Count - 1);
            floorScroll.verticalNormalizedPosition = Mathf.Clamp01(1f - t);
        }

        // ------------------------------------------------------------------ 층 상세

        /// <summary>원본 `twFloorInfo` — 규칙·목표·보너스·먼지·첫 클리어 보상을 보여 준다.</summary>
        public void ShowFloorInfo(int floor)
        {
            var db = _svc.Db;
            var profile = _svc.Profile;
            var row = db.TowerFloor(floor);
            if (row == null) return;

            _pickFloor = floor;
            if (floorSheetRoot != null) floorSheetRoot.SetActive(false);
            if (infoSheetRoot != null) infoSheetRoot.SetActive(true);

            bool cleared = profile.TowerCleared(floor);
            bool bonusDone = profile.TowerBonusDone(floor);
            string kind = TowerText.RuleKind(db, row);
            var (fill, line, ink) = UiTheme.TowerRuleKind(kind);

            if (infoTitleLabel != null) infoTitleLabel.text = $"{floor}층 · {db.Text(row.nameKey)}";
            if (infoStateLabel != null) infoStateLabel.text = cleared ? "청소 완료" : "첫 도전";
            if (infoRuleFace != null) infoRuleFace.color = fill;
            if (infoRuleLine != null) infoRuleLine.color = line;
            if (infoRuleNameLabel != null)
            {
                infoRuleNameLabel.text = string.IsNullOrEmpty(row.rule)
                    ? "기본 층" : $"층 규칙 · {TowerText.RuleName(db, row)}";
                infoRuleNameLabel.color = ink;
            }
            if (infoRuleDescLabel != null) infoRuleDescLabel.text = TowerText.RuleDesc(db, row);

            if (infoGoalLabel != null) infoGoalLabel.text = TowerText.GoalText(db, row);
            if (infoGoalSubLabel != null)
                infoGoalSubLabel.text = $"제한 {TowerText.TimeLimit(db, row):0}초 · "
                                      + $"출전 {TowerText.Slots(db, row)}대";
            if (infoBonusFace != null)
                infoBonusFace.color = bonusDone ? UiTheme.TowerBonusOkBg : UiTheme.TowerBonusBg;
            if (infoBonusLine != null)
                infoBonusLine.color = bonusDone ? UiTheme.TowerBonusOkLine : UiTheme.TowerBonusLine;
            if (infoBonusLabel != null)
                infoBonusLabel.text = (bonusDone ? "보너스 받음\n" : "보너스\n")
                                    + TowerText.BonusText(db, row);
            if (infoBonusSubLabel != null)
                infoBonusSubLabel.text = $"크리스탈 {row.bonusCrystal} · 한 번";

            BuildFoeCards(row);
            if (infoRewardLabel != null)
                infoRewardLabel.text = cleared
                    ? "첫 클리어 · 받았어요"
                    : $"첫 클리어 · {TowerText.RewardLine(db, row)}";
        }

        /// <summary>
        /// 이 층에 나오는 먼지를 칸으로 보여 준다(원본 `.tw-foes`, 실측 72x81).
        /// 같은 종류는 합쳐 세고, 그림이 없는 먼지는 이름만 남는다.
        /// </summary>
        void BuildFoeCards(TowerFloorRow floor)
        {
            if (infoFoesRoot == null) return;
            foreach (var go in _foeCards) if (go != null) Destroy(go);
            _foeCards.Clear();

            var db = _svc.Db;
            var counted = new Dictionary<string, int>();
            foreach (var (id, n) in LeagueSim.ParseSquad(floor.squad))
                counted[id] = counted.TryGetValue(id, out var had) ? had + n : n;

            foreach (var pair in counted)
            {
                if (!db.Enemies.TryGetValue(pair.Key, out var def)) continue;
                _foeCards.Add(FoeCard(def, pair.Value, pair.Key));
            }
        }

        GameObject FoeCard(Data.EnemyDef def, int count, string id)
        {
            var go = new GameObject("Foe_" + id, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(infoFoesRoot, false);
            rt.sizeDelta = new Vector2(UiTheme.Px(72f), UiTheme.Py(81f));

            var face = go.AddComponent<Image>();
            face.sprite = foeCardSprite;
            face.type = Image.Type.Sliced;
            face.color = Color.white;
            face.raycastTarget = false;

            var art = new GameObject("Art", typeof(RectTransform));
            var artRt = (RectTransform)art.transform;
            artRt.SetParent(rt, false);
            artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 1f);
            artRt.pivot = new Vector2(0.5f, 1f);
            artRt.anchoredPosition = new Vector2(0f, -UiTheme.Py(2f));
            artRt.sizeDelta = new Vector2(UiTheme.Sq(44f), UiTheme.Sq(44f));
            var artImg = art.AddComponent<Image>();
            artImg.sprite = sprites != null
                ? sprites.Enemy(id, def.Look, id.StartsWith("boss_")) : null;
            artImg.enabled = artImg.sprite != null;
            artImg.preserveAspect = true;
            artImg.raycastTarget = false;

            Label("Name", def.Name, UiTheme.Py(10.5f), UiTheme.Ink2, 48f, rt);
            Label("Count", $"×{count}", UiTheme.Py(12f), UiTheme.Ink, 64f, rt);
            return go;

            void Label(string id2, string text, float size, Color ink, float top, RectTransform parent)
            {
                var n = new GameObject(id2, typeof(RectTransform));
                var nrt = (RectTransform)n.transform;
                nrt.SetParent(parent, false);
                nrt.anchorMin = new Vector2(0f, 1f);
                nrt.anchorMax = new Vector2(1f, 1f);
                nrt.offsetMin = new Vector2(0f, -UiTheme.Py(top) - UiTheme.Line(size));
                nrt.offsetMax = new Vector2(0f, -UiTheme.Py(top));
                var tmp = n.AddComponent<TextMeshProUGUI>();
                tmp.font = bodyFont;
                tmp.fontSize = size;
                tmp.color = ink;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                tmp.text = text;
                tmp.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------ 한 층 시작

        /// <summary>원본 `startTower` — 층 하나를 열고 편성 단계로 들어간다.</summary>
        public void BeginTower(GameServices services, int floor)
        {
            _svc = services;
            var row = _svc.Db.TowerFloor(floor);
            if (row == null) return;

            gameObject.SetActive(true);
            if (tierRoot != null) tierRoot.SetActive(false);
            if (floorSheetRoot != null) floorSheetRoot.SetActive(false);
            if (infoSheetRoot != null) infoSheetRoot.SetActive(false);
            if (resultRoot != null) resultRoot.SetActive(false);
            if (bannerRoot != null) bannerRoot.SetActive(false);

            _pickFloor = floor;
            _sim = new LeagueSim(_svc.Db, _svc.Profile, row,
                                 new LeagueRandom(System.Environment.TickCount));
            _selected = null;
            _endDelay = 0f;
            _bannerT = 0f;
            _rounded = false;

            ShowBattleChrome(true);
            ShowTowerChrome(true);
            BuildOwnCells();
            BuildCells();

            view.Build();
            if (boardCamera != null) boardCamera.enabled = true;
            FrameBoard();
            TowerSync();

            Banner($"{floor}층", $"{_svc.Db.Text(row.nameKey)} · {TowerText.RuleName(_svc.Db, row)}", 1.3f);
        }

        /// <summary>리그 전용 줄을 숨기고 타워 자리로 옮긴다(또는 되돌린다).</summary>
        void ShowTowerChrome(bool on)
        {
            if (kickerLabel != null) kickerLabel.text = on ? "TOWER" : "LEAGUE";
            if (!on)
            {
                if (resultHomeButton != null) resultHomeButton.Text = "로비로";
                if (resultAgainButton != null) resultAgainButton.Text = "다시 하기";
            }
            if (leagueReadoutRoot != null) leagueReadoutRoot.SetActive(!on);
            if (towerReadoutRoot != null) towerReadoutRoot.SetActive(on);
            if (ruleRoot != null) ruleRoot.SetActive(on);
            if (ownRoot != null) ownRoot.gameObject.SetActive(on);
            if (autoButton != null) autoButton.gameObject.SetActive(on);
            if (benchRoot != null) benchRoot.gameObject.SetActive(!on);
            if (barRoot != null) barRoot.gameObject.SetActive(!on);
            if (shopRoot != null) shopRoot.gameObject.SetActive(!on);

            // 타워 힌트는 두 줄이다(실측 38.4 = 19.2 x 2). 리그는 한 줄로 되돌린다.
            if (hintLabel != null)
                hintLabel.textWrappingMode = on ? TMPro.TextWrappingModes.Normal
                                                : TMPro.TextWrappingModes.NoWrap;

            if (on) LayoutTower();
            else RestoreLeagueRects();
        }

        /// <summary>판이 열려 있을 때만 보이는 줄들. 시트만 떠 있을 때는 전부 숨긴다.</summary>
        void ShowBattleChrome(bool on)
        {
            if (hudRoot != null) hudRoot.gameObject.SetActive(on);
            if (synergyRoot != null) synergyRoot.gameObject.SetActive(on);
            if (infoRoot != null) infoRoot.gameObject.SetActive(on);
            if (goRoot != null) goRoot.gameObject.SetActive(on);
            if (hintRoot != null) hintRoot.gameObject.SetActive(on);
            if (cellRoot != null) cellRoot.gameObject.SetActive(on);
            if (!on)
            {
                if (ruleRoot != null) ruleRoot.SetActive(false);
                if (ownRoot != null) ownRoot.gameObject.SetActive(false);
                if (benchRoot != null) benchRoot.gameObject.SetActive(false);
                if (barRoot != null) barRoot.gameObject.SetActive(false);
                if (shopRoot != null) shopRoot.gameObject.SetActive(false);
            }
        }

        void RestoreLeagueRects()
        {
            foreach (var pair in _leagueRects)
                if (pair.Key != null)
                {
                    pair.Key.offsetMin = pair.Value.Min;
                    pair.Key.offsetMax = pair.Value.Max;
                }
        }

        /// <summary>
        /// 타워의 세로 자리. 보유 가전이 6대를 넘으면 목록이 줄 단위로 늘고 아래 셋이
        /// 그만큼 내려간다 — 원본도 같은 흐름이다(실측: 1줄 54 · 2줄 113).
        /// </summary>
        void LayoutTower()
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(_ownCells.Count / 6f));
            float ownH = rows * OwnCellH + (rows - 1) * OwnGap;
            float drop = TowerRuleDrop;

            Place(ruleRoot != null ? (RectTransform)ruleRoot.transform : null,
                  RuleTop, RuleH + drop);
            // 글자가 들어가는 두 줄은 줄 높이를 지켜야 한다 — 모자라면 TMP 가 안 그린다.
            PlacePx(ruleTopRow, 8f, RuleTopHeight);
            PlacePx(ruleBonusLabel != null ? (RectTransform)ruleBonusLabel.transform : null,
                    31f + drop, UiTheme.Row(UiTheme.Py(11f), UiTheme.Py(15.4f)));

            Place(infoRoot, InfoTop + drop, InfoH);
            Place(ownRoot, OwnTop + drop, ownH);
            Place(goRoot, OwnTop + drop + ownH + 18f, GoH);
            Place(hintRoot, OwnTop + drop + ownH + 18f + GoH + 10f, HintH);
            FrameBoard();
        }

        /// <summary>
        /// 규칙 설명이 몇 줄인지. 원본은 흐름 배치라 저절로 늘지만 우리는 자리를 직접
        /// 잡으므로 글자 폭을 재서 센다(최대 두 줄 — 실측 54.4 / 70 두 가지뿐이다).
        /// </summary>
        void MeasureRuleLines()
        {
            _ruleLines = 1;
            if (ruleDescLabel == null || ruleNameLabel == null) return;

            // 이름은 줄지 않는다(원본 `auto 1fr` 격자) — 글자 폭을 그대로 잡아 둔다.
            float nameW = ruleNameLabel.GetPreferredValues(ruleNameLabel.text).x;
            if (ruleNameLayout != null)
                ruleNameLayout.minWidth = ruleNameLayout.preferredWidth = nameW;

            // 남는 폭(페이지 단위): 398 − 양쪽 패딩 10 − 이름 − 사이 8.
            float left = 398f - 20f - 8f - nameW / Mathf.Max(0.0001f, UiTheme.Px(1f));
            float need = ruleDescLabel.GetPreferredValues(ruleDescLabel.text).x
                         / Mathf.Max(0.0001f, UiTheme.Px(1f));
            if (need > left) _ruleLines = 2;
        }

        int _ruleLines = 1;

        /// <summary>
        /// 규칙 줄 첫 칸(이름 + 설명)의 높이. 설명이 두 줄이면 우리 글꼴의 줄 높이 두 개가
        /// 들어가야 한다 — 원본의 16.8 두 줄보다 조금 높다(글꼴 금속이 달라서다).
        /// </summary>
        float RuleTopHeight =>
            UiTheme.Row(UiTheme.Py(15f), _ruleLines * UiTheme.Line(UiTheme.Py(12f)));

        /// <summary>설명이 두 줄인 층에서 판과 그 아래가 내려가는 만큼(페이지 단위).</summary>
        float TowerRuleDrop =>
            Mathf.Max(0f, RuleTopHeight / Mathf.Max(0.0001f, UiTheme.Py(1f)) - 21f);

        /// <summary>자리는 페이지 좌표로, 높이는 이미 잰 캔버스 값으로 놓는다.</summary>
        static void PlacePx(RectTransform rt, float top, float heightPx)
        {
            if (rt == null) return;
            rt.offsetMin = new Vector2(rt.offsetMin.x, -UiTheme.Py(top) - heightPx);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -UiTheme.Py(top));
        }

        /// <summary>한 줄을 원본 페이지 좌표(430x932)로 올려놓는다.</summary>
        static void Place(RectTransform rt, float top, float height)
        {
            if (rt == null) return;
            rt.offsetMin = new Vector2(rt.offsetMin.x, -UiTheme.Py(top + height));
            rt.offsetMax = new Vector2(rt.offsetMax.x, -UiTheme.Py(top));
        }

        // 430x932 실측값. TOWER-MEASUREMENTS.md 1절.
        const float RuleTop = 182f, RuleH = 54.4f;
        const float BoardTop = 246.4f, BoardH = 336f;
        const float InfoTop = 600.4f, InfoH = 26f;
        const float OwnTop = 640.4f, OwnCellH = 54f, OwnGap = 5f, OwnCellW = 62.16f;
        const float GoH = 46f, HintH = 38.4f;

        void BuildOwnCells()
        {
            foreach (var c in _ownCells) if (c != null) Destroy(c.gameObject);
            _ownCells.Clear();
            if (ownRoot == null || ownPrefab == null) return;

            foreach (var id in _sim.TowerRoster())
            {
                string unitId = id;
                var cell = Instantiate(ownPrefab, ownRoot);
                cell.Tapped += () => ToggleOwn(unitId);
                _ownCells.Add(cell);
            }
            LayoutTower();
        }

        void ToggleOwn(string unitId)
        {
            if (_sim == null) return;
            string result = _sim.TowerToggle(unitId);
            if (result == "cap")
                SetHint($"이 층은 {_sim.Level}대까지 출전해요. 먼저 하나를 빼 주세요.");
            else if (result == "full") SetHint("놓을 칸이 없어요.");
            _selected = null;
            TowerSync();
        }

        void TowerAutoFill()
        {
            if (_sim == null || _sim.Phase != LeaguePhase.Prep) return;
            for (int i = 0; i < _sim.Board.Length; i++) _sim.Board[i] = null;
            _sim.TowerAutoFill();
            _selected = null;
            TowerSync();
        }

        // ------------------------------------------------------------------ 진행

        void TowerGo()
        {
            if (_sim.Phase == LeaguePhase.Prep)
            {
                if (_sim.StartBattle()) { _selected = null; _rounded = false; TowerSync(); }
                return;
            }
            if (_sim.Phase == LeaguePhase.Battle) _sim.RunBattleToEnd();
        }

        void TowerQuit()
        {
            HideBoard();
            OpenFloorPick(_svc);
        }

        void HideBoard()
        {
            ShowBattleChrome(true);
            _sim = null;
            if (boardCamera != null) boardCamera.enabled = false;
            view?.ClearAll();
            ShowTowerChrome(false);
        }

        void HideTower()
        {
            if (floorSheetRoot != null) floorSheetRoot.SetActive(false);
            if (infoSheetRoot != null) infoSheetRoot.SetActive(false);
            HideBoard();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 결과 창의 오른쪽 단추. 이겼고 위층이 있으면 그리로, 아니면 같은 층을 다시 돈다.
        /// </summary>
        void TowerAgain()
        {
            int floor = _sim.Tower.Floor;
            int next = _sim.BattleWin == true && _svc.Db.TowerFloor(floor + 1) != null
                ? floor + 1 : floor;
            BeginTower(_svc, next);
        }

        /// <summary>원본 `twShowOver` — 보상은 첫 클리어와 보너스에 한 번씩만 붙는다.</summary>
        void TowerFinish()
        {
            var result = _sim.TowerReward();
            bool win = _sim.BattleWin == true;
            int floor = _sim.Tower.Floor;
            var db = _svc.Db;
            var row = _sim.Tower.Row;

            if (bannerRoot != null) bannerRoot.SetActive(false);
            if (resultRoot == null) return;

            resultRoot.SetActive(true);
            bool hasNext = win && db.TowerFloor(floor + 1) != null;
            if (resultHomeButton != null) resultHomeButton.Text = "층 목록";
            if (resultAgainButton != null)
                resultAgainButton.Text = hasNext ? $"{floor + 1}층으로"
                                       : win ? "보너스 다시 도전" : "다시 도전";
            if (resultTitle != null)
                resultTitle.text = win ? $"{floor}층 청소 완료!" : "실패했어요";
            if (resultStats != null)
                resultStats.text = win
                    ? $"{_sim.BattleTime:0.0}초 · 쓰러진 가전 {_sim.Tower.Down}"
                    : _sim.TowerWhy();
            if (resultRewards != null)
                resultRewards.text = result.Got.Count > 0
                    ? "보상: " + string.Join(", ", result.Got)
                    : win ? "첫 클리어 보상은 이미 받았어요." : string.Empty;
            if (resultNote != null)
            {
                string bonus = TowerText.BonusText(db, row);
                bool done = _svc.Profile.TowerBonusDone(floor);
                resultNote.text = $"보너스 · {bonus} — "
                                + (done ? result.Bonus ? "달성!" : "받음"
                                        : win ? "못 했어요" : "—")
                                + "\n편성을 바꿔 바로 다시 도전할 수 있어요(무료).";
            }
            _svc.Persist();
        }

        // ------------------------------------------------------------------ 그리기

        /// <summary>원본 `twSync`. 읽는 칸·규칙 줄·출전 목록·안내를 한 번에 맞춘다.</summary>
        void TowerSync()
        {
            var db = _svc.Db;
            var tower = _sim.Tower;
            var row = tower.Row;
            bool prep = _sim.Phase == LeaguePhase.Prep;

            if (chipLabel != null)
                chipLabel.text = $"{tower.Floor}층 · {db.Text(row.nameKey)}";
            if (speedButton != null) speedButton.Text = $"{_speed:0}×";

            if (towerFloorValue != null) towerFloorValue.text = $"{tower.Floor}층";
            if (towerGoalCaption != null)
                towerGoalCaption.text = tower.RuleId == "guard" ? "버티기" : "남은 시간";
            if (towerGoalValue != null)
            {
                // 지키기 층의 버티는 시간은 규칙이 들고 있다 — 판이 열리기 전에도 보여 준다.
                float limit = TowerText.TimeLimit(db, row);
                int left = Mathf.Max(0, Mathf.CeilToInt(limit - (prep ? 0f : _sim.BattleTime)));
                towerGoalValue.text = $"{left / 60}:{left % 60:00}";
            }
            if (towerUseValue != null) towerUseValue.text = $"{_sim.OnBoard}/{_sim.Level}";

            string kind = TowerText.RuleKind(db, row);
            var (fill, line, ink) = UiTheme.TowerRuleKind(kind);
            if (ruleFace != null) ruleFace.color = fill;
            if (ruleLine != null) ruleLine.color = line;
            if (ruleNameLabel != null)
            {
                ruleNameLabel.text = TowerText.RuleName(db, row);
                ruleNameLabel.color = ink;
            }
            if (ruleDescLabel != null) ruleDescLabel.text = TowerText.RuleDesc(db, row);
            MeasureRuleLines();
            LayoutTower();
            if (ruleBonusLabel != null)
                ruleBonusLabel.text = $"보너스 · {TowerText.BonusText(db, row)}"
                                    + (_svc.Profile.TowerBonusDone(tower.Floor) ? " (받음)" : string.Empty);

            SyncSynergies();
            SyncOwnCells(prep);
            SyncCells(prep);

            if (goLabel != null)
                goLabel.text = prep
                    ? _sim.OnBoard > 0 ? $"출동 · {tower.Floor}층" : "가전을 판에 놓아 주세요"
                    : "결과 바로 보기";
            if (goButton != null)
                goButton.State = _sim.Phase == LeaguePhase.Over || (prep && _sim.OnBoard == 0)
                    ? UiButtonState.Disabled : UiButtonState.Normal;

            if (infoLeftLabel != null)
                infoLeftLabel.text = prep
                    ? $"목표 · {TowerText.GoalText(db, row)}"
                    : $"{tower.Floor}층 전투 중";
            if (infoRightLabel != null)
                infoRightLabel.text = prep ? string.Empty : $"남은 먼지 {CountTowerFoes()}";
            // 원본은 전투에 들어가면 안내 줄을 통째로 바꿔 단다 — 자동 편성 단추는 사라지고
            // 그 자리에 남은 먼지 수가 온다.
            if (autoButton != null) autoButton.gameObject.SetActive(prep);

            var sel = _selected != null ? PieceAt(_selected.Value) : null;
            if (hintLabel != null)
            {
                if (sel != null)
                {
                    var st = _sim.UnitStats(sel);
                    var unit = db.Units[sel.Type];
                    var skill = db.LeagueUnits[sel.Type];
                    hintLabel.text = $"{unit.Name} · 체력 {Mathf.Round(st.Hp)} · 공격 {Mathf.Round(st.Atk)}"
                                   + $" (성장 ×{st.StarMul:0.00})"
                                   + $" · {db.Text(skill.skillKey)} · 다른 칸을 누르면 옮겨요";
                }
                else hintLabel.text = prep
                    ? "아래 가전을 누르면 판에 놓이고, 다시 누르면 빠져요. "
                      + "판의 가전을 눌러 고른 뒤 다른 칸을 누르면 옮겨요."
                    : "전투는 자동이에요. 배속을 바꾸거나 결과를 바로 볼 수 있어요.";
            }

            if (prep)
                view.ShowBoard(_sim,
                               _selected is { Where: LeagueWhere.Board } s ? s.Index : -1,
                               _selected != null);
        }

        /// <summary>황금 먼지는 잡는 대상이 아니라 쫓는 대상이라 남은 수에서 뺀다.</summary>
        int CountTowerFoes()
        {
            int n = 0;
            foreach (var u in _sim.Units) if (u.Side == 1 && u.Alive && !u.Flees) n++;
            return n;
        }

        void SyncOwnCells(bool prep)
        {
            var db = _svc.Db;
            var roster = _sim.TowerRoster();
            for (int i = 0; i < _ownCells.Count && i < roster.Count; i++)
            {
                string id = roster[i];
                var owned = _svc.Profile.Data.Own(id);
                bool placed = _sim.Board.Any(p => p != null && p.Type == id);
                _ownCells[i].Show(id, sprites, db.Units[id].Rarity,
                                  owned != null ? $"Lv{owned.lv}" : "손님",
                                  owned?.star ?? 0, placed,
                                  TowerText.Recommended(db, _sim.Tower.Row, id, _sim.Tower.Guest),
                                  prep);
            }
        }
    }
}
