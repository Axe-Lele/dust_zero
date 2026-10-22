// 가전 대청소 리그 화면 — 상점·대기석·판·전투.
//
// 원본 `lgSync` / `lgFrame` 을 옮긴 것이다. 규칙은 전부 LeagueSim 이 들고 있고 이 클래스는
// 누르는 것과 보여 주는 것만 한다. 10.19-e 를 430x932 에서 재서 자리를 맞췄다:
// HUD 12/106 · 시너지 136/29 · 판 175/336 · 안내 529/18 · 대기석 561/48 · 버튼 627/36 ·
// 상점 679/85 · 출발 782/46 · 힌트 838/19.2.
using System.Collections.Generic;
using DustZero.Core;
using DustZero.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class LeagueScreen : MonoBehaviour
    {
        [Header("판")]
        [SerializeField] LeagueView view;
        [SerializeField] Camera boardCamera;
        /// <summary>우리 자리 21칸에 겹쳐 둔 투명 단추들.</summary>
        [SerializeField] RectTransform cellRoot;
        [SerializeField] UiButton cellPrefab;

        [Header("HUD")]
        [SerializeField] TextMeshProUGUI chipLabel;
        [SerializeField] TextMeshProUGUI lifeLabel;
        [SerializeField] TextMeshProUGUI goldLabel;
        [SerializeField] TextMeshProUGUI levelLabel;
        [SerializeField] TextMeshProUGUI xpLabel;
        [SerializeField] RectTransform xpFill;
        [SerializeField] UiButton speedButton;
        [SerializeField] UiButton exitButton;

        [Header("시너지")]
        [SerializeField] RectTransform synergyRoot;
        [SerializeField] TMP_FontAsset bodyFont;
        /// <summary>시너지 칩의 둥근 바탕.</summary>
        [SerializeField] Sprite chipSprite;

        [Header("줄")]
        [SerializeField] TextMeshProUGUI infoLeftLabel;
        [SerializeField] TextMeshProUGUI infoRightLabel;
        [SerializeField] TextMeshProUGUI hintLabel;

        [Header("대기석")]
        [SerializeField] RectTransform benchRoot;
        [SerializeField] LeagueBenchCell benchPrefab;

        [Header("버튼")]
        [SerializeField] UiButton rerollButton;
        [SerializeField] TextMeshProUGUI rerollLabel;
        [SerializeField] UiButton xpButton;
        [SerializeField] TextMeshProUGUI xpButtonLabel;
        [SerializeField] UiButton sellButton;
        [SerializeField] TextMeshProUGUI sellLabel;

        [Header("상점")]
        [SerializeField] RectTransform shopRoot;
        [SerializeField] LeagueShopCard shopPrefab;

        [Header("출발")]
        [SerializeField] UiButton goButton;
        [SerializeField] TextMeshProUGUI goLabel;

        [Header("알림과 결과")]
        [SerializeField] GameObject bannerRoot;
        [SerializeField] TextMeshProUGUI bannerTitle;
        [SerializeField] TextMeshProUGUI bannerNote;
        [SerializeField] GameObject resultRoot;
        [SerializeField] TextMeshProUGUI resultTitle;
        [SerializeField] TextMeshProUGUI resultStats;
        [SerializeField] TextMeshProUGUI resultRewards;
        [SerializeField] TextMeshProUGUI resultNote;
        [SerializeField] UiButton resultHomeButton;
        [SerializeField] UiButton resultAgainButton;

        [SerializeField] SpriteLibrary sprites;

        GameServices _svc;
        LeagueSim _sim;
        int _tier = 1;

        readonly List<LeagueBenchCell> _bench = new();
        readonly List<LeagueShopCard> _shop = new();
        readonly List<UiButton> _cells = new();
        readonly List<GameObject> _synergyChips = new();

        /// <summary>고른 말. 없으면 Where 가 Bench 이고 Index 가 −1 이다.</summary>
        LeagueSpot? _selected;

        float _speed = 2f;
        /// <summary>전투가 끝나고 결과를 넘기기까지 남은 시간 — 원본의 0.9초.</summary>
        float _endDelay;
        float _bannerT;
        float _syncT;

        /// <summary>로비로 돌아간다.</summary>
        public event System.Action Exited;

        void Awake()
        {
            if (rerollButton != null) rerollButton.SetAction(() => { if (_sim != null && _sim.Roll()) Sync(); });
            if (xpButton != null) xpButton.SetAction(() => { if (_sim != null && _sim.BuyXp()) Sync(); });
            if (sellButton != null) sellButton.SetAction(SellOrLock);
            if (goButton != null) goButton.SetAction(Go);
            if (speedButton != null) speedButton.SetAction(CycleSpeed);
            if (exitButton != null) exitButton.SetAction(Quit);
            if (resultHomeButton != null) resultHomeButton.SetAction(() => { Hide(); Exited?.Invoke(); });
            if (resultAgainButton != null) resultAgainButton.SetAction(() => Begin(_svc, _tier));
        }

        void Hide()
        {
            if (resultRoot != null) resultRoot.SetActive(false);
            if (boardCamera != null) boardCamera.enabled = false;
            view?.ClearAll();
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ 시작

        public void Begin(GameServices services, int tier)
        {
            _svc = services;
            _tier = tier;
            gameObject.SetActive(true);

            _sim = new LeagueSim(_svc.Db, _svc.Profile, tier,
                                 new LeagueRandom(System.Environment.TickCount));
            _selected = null;
            _endDelay = 0f;
            _bannerT = 0f;

            if (resultRoot != null) resultRoot.SetActive(false);
            if (bannerRoot != null) bannerRoot.SetActive(false);

            BuildBench();
            BuildShop();
            BuildCells();

            view.Build();
            if (boardCamera != null) boardCamera.enabled = true;
            FrameBoard();
            Sync();
        }

        void OnDisable()
        {
            if (boardCamera != null) boardCamera.enabled = false;
        }

        /// <summary>판 카메라를 원본 `#lgCv` 자리(16,175 398x336)에 맞춘다.</summary>
        void FrameBoard()
        {
            if (boardCamera == null) return;
            boardCamera.rect = UiTheme.PageViewport(16f, 175f, 398f, 336f);
            // 칸 7개가 가로를 꽉 채우고 세로 6칸이 그대로 들어가야 한다.
            boardCamera.orthographicSize = LeagueSim.Rows / 2f;
            boardCamera.transform.position =
                new Vector3(LeagueSim.Cols / 2f, -LeagueSim.Rows / 2f, -10f);
        }

        void BuildBench()
        {
            foreach (var c in _bench) if (c != null) Destroy(c.gameObject);
            _bench.Clear();
            if (benchRoot == null || benchPrefab == null) return;

            for (int i = 0; i < _sim.Bench.Length; i++)
            {
                int index = i;
                var cell = Instantiate(benchPrefab, benchRoot);
                cell.Tapped += () => TapSpot(new LeagueSpot(LeagueWhere.Bench, index));
                _bench.Add(cell);
            }
        }

        void BuildShop()
        {
            foreach (var c in _shop) if (c != null) Destroy(c.gameObject);
            _shop.Clear();
            if (shopRoot == null || shopPrefab == null) return;

            for (int i = 0; i < _sim.Shop.Length; i++)
            {
                int index = i;
                var card = Instantiate(shopPrefab, shopRoot);
                card.Tapped += () => BuyFrom(index);
                _shop.Add(card);
            }
        }

        /// <summary>판 위 21칸에 투명 단추를 겹쳐 둔다 — 카메라 그림은 입력을 받지 않는다.</summary>
        void BuildCells()
        {
            if (_cells.Count > 0 || cellRoot == null || cellPrefab == null) return;
            for (int i = 0; i < LeagueSim.BoardCells; i++)
            {
                int index = i;
                var b = Instantiate(cellPrefab, cellRoot);
                b.SetAction(() => TapSpot(new LeagueSpot(LeagueWhere.Board, index)));
                _cells.Add(b);
            }
        }

        // ------------------------------------------------------------------ 조작

        void BuyFrom(int slot)
        {
            if (_sim == null) return;
            if (_sim.Buy(slot)) { _selected = null; Sync(); return; }

            var t = _sim.Shop[slot];
            if (string.IsNullOrEmpty(t)) return;
            SetHint(_sim.Gold < _sim.CostOf(t)
                ? "코인이 모자라요."
                : "대기석이 꽉 찼어요. 팔거나 판에 놓아 주세요.");
        }

        /// <summary>고르기와 옮기기. 같은 자리를 다시 누르면 고르기를 푼다.</summary>
        void TapSpot(LeagueSpot spot)
        {
            if (_sim == null || _sim.Phase != LeaguePhase.Prep) return;

            if (_selected == null)
            {
                if (PieceAt(spot) == null) return;
                _selected = spot;
                Sync();
                return;
            }

            var from = _selected.Value;
            if (from.Where == spot.Where && from.Index == spot.Index)
            {
                _selected = null;
                Sync();
                return;
            }

            var move = _sim.Move(from, spot);
            if (move == LeagueMoveResult.Capped)
            {
                SetHint($"판에는 레벨만큼만 놓을 수 있어요 ({_sim.OnBoard}/{_sim.Level}).");
                return;
            }
            if (move == LeagueMoveResult.Yes) _selected = null;
            Sync();
        }

        LeaguePiece PieceAt(LeagueSpot s)
            => s.Where == LeagueWhere.Bench ? _sim.Bench[s.Index] : _sim.Board[s.Index];

        void SellOrLock()
        {
            if (_sim == null || _sim.Phase != LeaguePhase.Prep) return;
            if (_selected != null)
            {
                if (_sim.Sell(_selected.Value)) _selected = null;
            }
            else _sim.ShopLocked = !_sim.ShopLocked;
            Sync();
        }

        void CycleSpeed()
        {
            _speed = _speed >= 3f ? 1f : _speed + 1f;
            Sync();
        }

        void Go()
        {
            if (_sim == null) return;
            if (_sim.Phase == LeaguePhase.Prep)
            {
                if (_sim.StartBattle()) { _selected = null; Sync(); }
                return;
            }
            // 전투 중이면 결과까지 바로 돌린다.
            if (_sim.Phase == LeaguePhase.Battle) _sim.RunBattleToEnd();
        }

        void Quit()
        {
            if (_sim == null) return;
            if (_sim.Phase == LeaguePhase.Over) { Hide(); Exited?.Invoke(); return; }
            ShowResult(quit: true);
        }

        // ------------------------------------------------------------------ 프레임

        void Update()
        {
            if (_sim == null) return;

            if (_bannerT > 0f)
            {
                _bannerT -= Time.unscaledDeltaTime;
                if (_bannerT <= 0f && bannerRoot != null) bannerRoot.SetActive(false);
            }

            if (_sim.Phase != LeaguePhase.Battle) return;

            if (!_sim.BattleOver)
            {
                float acc = Time.unscaledDeltaTime * _speed;
                while (acc > 0f && !_sim.BattleOver)
                {
                    float h = Mathf.Min(acc, 1f / 60f);
                    _sim.Step(h);
                    acc -= h;
                }
                if (_sim.BattleOver) ShowRoundBanner();
            }
            else if (_endDelay > 0f)
            {
                _endDelay -= Time.unscaledDeltaTime;
                if (_endDelay <= 0f) FinishRound();
            }

            view.Sync(_sim);
            if ((_syncT -= Time.unscaledDeltaTime) <= 0f) { _syncT = 0.2f; Sync(); }
        }

        void ShowRoundBanner()
        {
            _endDelay = 0.9f;
            bool win = _sim.BattleWin == true;
            int left = _sim.Last?.Left ?? 0;
            Banner(win ? "승리!" : "패배",
                   win ? string.Empty
                       : $"청결도 −{_svc.Db.Config.leagueLoseBase + CountFoes()}"
                         + (_sim.BattleTimeout ? " (시간 초과)" : string.Empty));
        }

        int CountFoes()
        {
            int n = 0;
            foreach (var u in _sim.Units) if (u.Side == 1 && u.Alive) n += u.Boss ? 3 : 1;
            return n;
        }

        void FinishRound()
        {
            var last = _sim.EndRound();
            if (_sim.Phase == LeaguePhase.Over) { ShowResult(quit: false); return; }

            var round = Round(_sim.Round);
            Banner($"{_sim.Round}라운드",
                   $"{(round != null ? _svc.Db.Text(round.nameKey) : string.Empty)}"
                   + (round != null && round.boss != 0 ? " · 보스" : string.Empty)
                   + $" · 코인 +{last.Income}", 1.3f);
            view.ShowBoard(_sim);
            Sync();
        }

        Data.LeagueRoundRow Round(int n)
        {
            foreach (var r in _svc.Db.LeagueRounds) if (r.round == n) return r;
            return null;
        }

        void Banner(string title, string note, float seconds = 1f)
        {
            if (bannerRoot == null) return;
            bannerRoot.SetActive(true);
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerNote != null) bannerNote.text = note ?? string.Empty;
            _bannerT = seconds;
        }

        void ShowResult(bool quit)
        {
            var got = _sim.Finish(quit);
            if (bannerRoot != null) bannerRoot.SetActive(false);
            if (resultRoot == null) return;

            resultRoot.SetActive(true);
            var tierName = _sim.TierRow != null ? _svc.Db.Text(_sim.TierRow.nameKey) : string.Empty;
            if (resultTitle != null)
                resultTitle.text = quit ? "중간에 나왔어요"
                                 : got.Clear ? $"{tierName} 완주!" : "탈락했어요";
            if (resultStats != null)
                resultStats.text = $"{_sim.Wins}승 · {_sim.Losses}패 · 남은 청결도 {Mathf.Max(0, _sim.Life)}";
            if (resultRewards != null)
                resultRewards.text = got.Got.Count > 0
                    ? "보상: " + string.Join(", ", got.Got)
                    : got.Counted ? "오늘 보상 판을 다 써서 기록만 남았어요."
                                  : "2라운드 전에 끝나 보상 없이 끝났어요(오늘 판 수는 그대로).";
            if (resultNote != null)
                resultNote.text = $"오늘 보상 판 {_svc.Profile.LeagueRunsLeft()}/{_svc.Db.Config.leagueDailyRewards} 남음";
            _svc.Persist();
        }

        // ------------------------------------------------------------------ 그리기

        void SetHint(string text)
        {
            if (hintLabel != null) hintLabel.text = text;
        }

        public void Sync()
        {
            if (_sim == null) return;
            var cfg = _svc.Db.Config;
            bool prep = _sim.Phase == LeaguePhase.Prep;

            if (chipLabel != null)
            {
                var name = _sim.TierRow != null ? _svc.Db.Text(_sim.TierRow.nameKey) : string.Empty;
                chipLabel.text = $"{name} · {_sim.Round}/{cfg.leagueRounds}";
            }
            if (lifeLabel != null)
            {
                lifeLabel.text = Mathf.Max(0, _sim.Life).ToString();
                lifeLabel.color = _sim.Life <= 6 ? UiTheme.LedLife : UiTheme.Led;
            }
            if (goldLabel != null) goldLabel.text = _sim.Gold.ToString();
            if (levelLabel != null) levelLabel.text = $"레벨 {_sim.Level} · 배치 {_sim.OnBoard}/{_sim.Level}";
            if (xpLabel != null)
                xpLabel.text = _sim.Level >= _sim.MaxLevel ? "최고" : $"{_sim.Xp}/{_sim.XpNeeded}";
            if (xpFill != null)
            {
                float pct = _sim.Level >= _sim.MaxLevel ? 1f
                          : Mathf.Clamp01(_sim.Xp / (float)Mathf.Max(1, _sim.XpNeeded));
                xpFill.anchorMax = new Vector2(pct, 1f);
                xpFill.offsetMax = new Vector2(0f, xpFill.offsetMax.y);
            }
            if (speedButton != null) speedButton.Text = $"{_speed:0}×";

            SyncSynergies();
            SyncBench();
            SyncShop();
            SyncCells(prep);

            if (rerollLabel != null) rerollLabel.text = $"새로고침 · {cfg.leagueRerollCost}";
            if (rerollButton != null)
                rerollButton.State = prep && _sim.Gold >= cfg.leagueRerollCost
                    ? UiButtonState.Normal : UiButtonState.Disabled;

            if (xpButtonLabel != null)
                xpButtonLabel.text = _sim.Level >= _sim.MaxLevel
                    ? "최고 레벨" : $"경험치 +{cfg.leagueXpBuy} · {cfg.leagueXpCost}";
            if (xpButton != null)
                xpButton.State = prep && _sim.Level < _sim.MaxLevel && _sim.Gold >= cfg.leagueXpCost
                    ? UiButtonState.Normal : UiButtonState.Disabled;

            var sel = _selected != null ? PieceAt(_selected.Value) : null;
            if (sellLabel != null)
                sellLabel.text = sel != null ? $"판매 +{_sim.SellValue(sel)}"
                               : _sim.ShopLocked ? "잠금 해제" : "상점 잠금";
            if (sellButton != null)
                sellButton.State = prep ? UiButtonState.Normal : UiButtonState.Disabled;

            var roundRow = Round(_sim.Round);
            string roundName = roundRow != null ? _svc.Db.Text(roundRow.nameKey) : string.Empty;
            if (goLabel != null)
                goLabel.text = prep
                    ? _sim.OnBoard > 0 ? $"전투 시작 · {roundName}" : "가전을 판에 놓아 주세요"
                    : "결과 바로 보기";
            if (goButton != null)
                goButton.State = _sim.Phase == LeaguePhase.Over || (prep && _sim.OnBoard == 0)
                    ? UiButtonState.Disabled : UiButtonState.Normal;

            int benchUsed = 0;
            foreach (var p in _sim.Bench) if (p != null) benchUsed++;
            if (infoLeftLabel != null)
                infoLeftLabel.text = prep
                    ? $"{_sim.Round}라운드 · {roundName}" + (roundRow != null && roundRow.boss != 0 ? " (보스)" : string.Empty)
                    : _sim.Phase == LeaguePhase.Over ? $"판이 끝났어요 · {_sim.Wins}승 {_sim.Losses}패"
                                                     : $"{_sim.Round}라운드 전투 중";
            if (infoRightLabel != null)
                infoRightLabel.text = prep ? $"대기석 {benchUsed}/{_sim.Bench.Length}"
                                   : _sim.Phase == LeaguePhase.Over ? string.Empty
                                   : $"남은 먼지 {CountAliveFoes()}";

            if (hintLabel != null)
            {
                if (sel != null)
                {
                    var st = _sim.UnitStats(sel);
                    var unit = _svc.Db.Units[sel.Type];
                    var skill = _svc.Db.LeagueUnits[sel.Type];
                    hintLabel.text = $"{unit.Name} {new string('★', sel.Star)} · "
                                   + $"체력 {Mathf.Round(st.Hp)} · 공격 {Mathf.Round(st.Atk)}"
                                   + (st.Growth > 0f ? $" (성장 +{Mathf.Round(st.Growth * 100f)}%)" : string.Empty)
                                   + $" · {_svc.Db.Text(skill.skillKey)}";
                }
                else hintLabel.text = prep
                    ? "카드를 눌러 사고, 대기석·판의 가전을 눌러 고른 뒤 놓을 칸을 눌러요."
                    : "전투는 자동이에요. 배속을 바꾸거나 결과를 바로 볼 수 있어요.";
            }

            if (prep) view.ShowBoard(_sim);
            view.ShowSelection(_selected is { Where: LeagueWhere.Board } s2 ? s2.Index : -1);
        }

        int CountAliveFoes()
        {
            int n = 0;
            foreach (var u in _sim.Units) if (u.Side == 1 && u.Alive) n++;
            return n;
        }

        /// <summary>원본은 켜진 것 먼저, 최대 네 개만 보여 준다.</summary>
        void SyncSynergies()
        {
            foreach (var go in _synergyChips) if (go != null) Destroy(go);
            _synergyChips.Clear();
            if (synergyRoot == null) return;

            var (list, _) = _sim.Synergies();
            int shown = 0;
            foreach (var v in list)
            {
                if (shown >= 4) break;
                int need = v.Next?.Need ?? v.Needs[^1];
                _synergyChips.Add(Chip($"{v.Name.Replace("가전", string.Empty)} {v.Count}/{need}",
                                       v.Active != null));
                shown++;
            }
        }

        /// <summary>시너지 칩 하나. 켜지면 어두운 판에 밝은 글씨가 된다(원본 `.lg-chip.on`).</summary>
        GameObject Chip(string text, bool on)
        {
            var go = new GameObject("Chip", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(synergyRoot, false);

            var bg = go.AddComponent<Image>();
            bg.sprite = chipSprite;
            bg.type = Image.Type.Sliced;
            bg.color = on ? UiTheme.Ink : Color.white;
            bg.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UiTheme.Py(29f);

            var labelGo = new GameObject("Text", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(UiTheme.Px(8f), 0f);
            lrt.offsetMax = new Vector2(-UiTheme.Px(8f), 0f);

            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.font = bodyFont;
            tmp.fontSize = UiTheme.Py(11.5f);
            tmp.color = on ? UiTheme.DarkInk : UiTheme.Ink2;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = text;
            tmp.raycastTarget = false;
            le.preferredWidth = tmp.GetPreferredValues(text).x + UiTheme.Px(16f);

            return go;
        }

        void SyncBench()
        {
            for (int i = 0; i < _bench.Count && i < _sim.Bench.Length; i++)
            {
                var p = _sim.Bench[i];
                bool selected = _selected is { Where: LeagueWhere.Bench } s && s.Index == i;
                _bench[i].Show(p, sprites, selected, p != null && _sim.IsTrial(p.Type));
            }
        }

        void SyncShop()
        {
            for (int i = 0; i < _shop.Count && i < _sim.Shop.Length; i++)
            {
                var t = _sim.Shop[i];
                bool hot = t != null && _sim.CountOf(t, 1) >= 2;
                bool poor = t != null && _sim.Gold < _sim.CostOf(t);
                _shop[i].Show(t, t != null ? _svc.Db.Units[t] : null, sprites,
                              t != null ? _sim.CostOf(t) : 0,
                              hot, poor, t != null && _sim.IsTrial(t),
                              t != null ? _svc.Db.Text($"family.{_svc.Db.Units[t].Family}.name") : null);
            }
        }

        void SyncCells(bool prep)
        {
            for (int i = 0; i < _cells.Count; i++)
                _cells[i].State = prep ? UiButtonState.Normal : UiButtonState.Disabled;
        }
    }
}
