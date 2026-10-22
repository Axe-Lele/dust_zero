// 콘텐츠: the modes you play outside the stage list.
//
// One dark mode card for 대청소, measured off the running 10.16-n build: the kicker and
// title, the three leaders it can run, the rules, three stat cells, the time-bracket reward
// strip, and the CTA - which reads as a lock message until 1-1 is cleared. A pale "준비 중"
// card follows it, as the build's placeholder for whatever comes next.
using System;
using System.Collections.Generic;
using DustZero.Core.Meta;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class ContentTab : MonoBehaviour
    {
        [Header("카드")]
        [SerializeField] TextMeshProUGUI kickerLabel;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] RectTransform leaderRow;
        [SerializeField] TextMeshProUGUI leadLabel;
        [SerializeField] TextMeshProUGUI[] ruleLabels;

        [Header("기록")]
        [SerializeField] TextMeshProUGUI[] statKeys;
        [SerializeField] TextMeshProUGUI[] statValues;

        [Header("보충 줄")]
        [SerializeField] TextMeshProUGUI rewardLabel;

        /// <summary>원본 `#modePool` — 콘텐츠 탭 맨 위의 일지 카드.</summary>
        public const int PoolBoxCount = 3;
        [SerializeField] TextMeshProUGUI poolPtsLabel;
        [SerializeField] Image poolFill;
        [SerializeField] TextMeshProUGUI poolNoteLabel;
        [SerializeField] UiButton[] poolBoxButtons = new UiButton[PoolBoxCount];
        [SerializeField] TextMeshProUGUI[] poolBoxPtsLabels = new TextMeshProUGUI[PoolBoxCount];
        [SerializeField] TextMeshProUGUI[] poolBoxCrystalLabels = new TextMeshProUGUI[PoolBoxCount];
        [SerializeField] TextMeshProUGUI[] poolBoxStateLabels = new TextMeshProUGUI[PoolBoxCount];
        [SerializeField] TMP_FontAsset bodyFont;

        [Header("출격")]
        [SerializeField] TextMeshProUGUI runsLabel;
        [SerializeField] UiButton goButton;

        [Header("\ud611\ub3d9 \uccad\uc18c")]
        [SerializeField] ModeCard coopCard;
        [SerializeField] TextMeshProUGUI goLabel;

        [Header("먼지 타워 · 리그")]
        [SerializeField] ModeCard towerCard;
        [SerializeField] ModeCard leagueCard;
        [SerializeField] SpriteLibrary sprites;

        /// <summary>The three stat cells the build shows, in its order.</summary>
        public const int StatCount = 3;

        /// <summary>Raised with a toast line when a log box is opened.</summary>
        public event Action<string> Claimed;

        GameServices _svc;
        readonly List<GameObject> _spawned = new();

        public event Action SurvivorChosen;

        /// <summary>협동 청소로 출동. 파트너 고르기를 먼저 띄운다.</summary>
        public event Action CoopChosen;

        /// <summary>가전 대청소 리그로 출동.</summary>
        public event Action LeagueChosen;

        public void Bind(GameServices services)
        {
            _svc = services;
            if (goButton != null) goButton.SetAction(() => SurvivorChosen?.Invoke());
            if (coopCard != null && coopCard.Go != null)
                coopCard.Go.SetAction(() => CoopChosen?.Invoke());
            // 먼지 타워·리그는 아직 모드 자체가 이식되지 않았다. 카드는 원본대로 보여 주되
            // 누르면 그렇다고 말한다 — 열린 척하고 빈 화면으로 보내지 않는다.
            if (towerCard != null && towerCard.Go != null)
                towerCard.Go.SetAction(() => Claimed?.Invoke(NotPortedYet));
            if (leagueCard != null && leagueCard.Go != null)
                leagueCard.Go.SetAction(() => LeagueChosen?.Invoke());
            BuildLeaders();
            RefreshPoolCard();
            Refresh();
        }

        void OnEnable() { if (_svc != null) Refresh(); }

        /// <summary>
        /// The appliances that can lead a run. unit_behaviour says which, so a new leader
        /// appears here without touching this screen.
        /// </summary>
        void BuildLeaders()
        {
            if (leaderRow == null || _svc == null) return;
            foreach (Transform c in leaderRow) Destroy(c.gameObject);

            foreach (var id in _svc.Db.UnitOrder)
            {
                var def = _svc.Db.Units[id];
                if (string.IsNullOrEmpty(def.SurvivorLeader)) continue;

                var go = new GameObject("Leader_" + id, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(leaderRow, false);
                rt.sizeDelta = new Vector2(UiTheme.Py(40f), UiTheme.Py(40f));
                var img = go.AddComponent<Image>();
                img.sprite = sprites != null ? sprites.Unit(id) : null;
                img.enabled = img.sprite != null;
                img.preserveAspect = true;
                img.raycastTarget = false;
                _spawned.Add(go);
            }
        }

        /// <summary>
        /// 오늘의 청소 일지 상자들. 10.18-h 가 시간 구간별 크리스탈을 이걸로 바꿨다 —
        /// 점수가 차면 눌러서 받는다. 어제 못 채운 몫이 있으면 상자가 하나 더 붙는다.
        /// </summary>
        /// <summary>
        /// 오늘의 청소 일지 카드. 원본은 이걸 콘텐츠 탭 **맨 위**에 둔다 —
        /// 일지는 대청소·협동·리그가 같이 채우는 것이라 한 모드에 딸려 있으면 안 된다.
        /// </summary>
        void RefreshPoolCard()
        {
            if (_svc == null) return;
            var profile = _svc.Profile;
            var cfg = _svc.Db.Config;
            int pts = profile.Pool.pts;
            int max = profile.PoolMaxPoints;

            if (poolPtsLabel != null) poolPtsLabel.text = $"{pts}/{max}점";
            if (poolFill != null)
            {
                var rt = poolFill.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(max > 0 ? Mathf.Clamp01(pts / (float)max) : 0f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            if (poolNoteLabel != null)
                poolNoteLabel.text =
                    $"어디서든 모아요 · 대청소 버틴 1분 +{cfg.poolSurvPerMin} · "
                    + $"협동 막은 웨이브 +{cfg.poolCoopWave} · "
                    + $"리그 이긴 라운드 +{cfg.poolLeagueWin}(완주 +{cfg.poolLeagueClear})";

            var boxes = profile.PoolBoxes();
            for (int i = 0; i < PoolBoxCount; i++)
            {
                bool used = i < boxes.Count;
                if (poolBoxButtons[i] != null) poolBoxButtons[i].gameObject.SetActive(used);
                if (!used) continue;

                var box = boxes[i];
                var id = box.Id;
                bool taken = profile.PoolBoxTaken(id);
                bool ready = profile.PoolBoxReady(box);

                if (poolBoxPtsLabels[i] != null)
                    poolBoxPtsLabels[i].text = box.Carry ? "어제 몫" : $"{box.Points}점";
                if (poolBoxCrystalLabels[i] != null)
                    poolBoxCrystalLabels[i].text = box.Crystal.ToString();
                if (poolBoxStateLabels[i] != null)
                    poolBoxStateLabels[i].text = taken ? "받음" : ready ? "받기" : "—";

                if (poolBoxButtons[i] == null) continue;
                poolBoxButtons[i].State = ready ? UiButtonState.Claimable
                                        : taken ? UiButtonState.Disabled : UiButtonState.Normal;
                poolBoxButtons[i].SetAction(ready ? () => Claim(id) : (System.Action)null);
            }
        }

        /// <summary>Opens one log box and repaints, as the original's pc-box click does.</summary>
        void Claim(string id)
        {
            int crystal = _svc.Profile.ClaimPoolBox(id);
            if (crystal <= 0) return;
            _svc.Persist();
            Claimed?.Invoke($"크리스탈 {crystal} 받았어요.");
            Refresh();
        }

        public void Refresh()
        {
            // 상자 상태가 점수에 따라 바뀌므로 갱신 때마다 다시 짓는다.
            // 일지 상자는 맨 위 '오늘의 청소 일지' 카드가 맡는다.
            // 대청소 카드 안에 또 그리면 같은 것이 두 번 보인다.
            RefreshPoolCard();
            if (_svc == null) return;
            var p = _svc.Profile;
            var rec = p.Data.surv;
            RefreshCoop(p);
            RefreshTower(p);
            RefreshLeague(p);

            bool open = p.SurvivorUnlocked;
            var cfg = _svc.Db.Config;

            if (kickerLabel != null) kickerLabel.text = "무한 버티기";
            if (titleLabel != null) titleLabel.text = "대청소 모드";
            if (leadLabel != null)
                leadLabel.text = "리더 하나를 움직여 쓰러질 때까지 버텨요. 공격은 자동이에요.";

            if (ruleLabels != null)
            {
                string[] rules =
                {
                    "성장 레벨업마다 편성 가전이 동료로 합류 · 짝이 맞으면 협동기",
                    // Integer minutes and seconds: these config values are floats, so a
                    // plain divide prints "2.5분 30초".
                    $"위험 {(int)cfg.survBossEvery / 60}분마다 보스 · " +
                    $"{Minutes(cfg.survScaleStart)}부터 적이 계속 강해짐",
                };
                for (int i = 0; i < ruleLabels.Length && i < rules.Length; i++)
                    if (ruleLabels[i] != null) ruleLabels[i].text = "· " + rules[i];
            }

            // ---- the three stat cells.
            var (pts, max) = _svc.Profile.SurvivorPoolToday();

            string[] keys = { "청소 일지", "오늘 최고", "역대 최고" };
            string[] values =
            {
                $"{pts}/{max}",
                Clock(rec != null ? rec.bestT : 0),
                Clock(rec != null ? rec.bestEver : 0),
            };
            for (int i = 0; i < StatCount; i++)
            {
                if (statKeys != null && i < statKeys.Length && statKeys[i] != null)
                    statKeys[i].text = keys[i];
                if (statValues != null && i < statValues.Length && statValues[i] != null)
                    statValues[i].text = values[i];
            }

            if (rewardLabel != null)
            {
                int carry = _svc.Profile.Pool.carry;
                rewardLabel.text = $"버틴 1분마다 청소 일지 +{_svc.Db.Config.poolSurvPerMin}점"
                    + (carry > 0 ? $" · 어제 못 채운 {carry}점은 오늘까지"
                                 : " · 여러 판에 나눠 모아도 돼요");
            }
            if (runsLabel != null)
                runsLabel.text = $"부품·코인·조각 보상 판 {p.SurvivorRunsLeft()}/{cfg.survDailyRewards} 남음 " +
                                 "· 30초 미만은 판 수를 쓰지 않아요";

            if (goLabel != null)
                goLabel.text = open ? "대청소 시작" : "1-1을 클리어하면 대청소 모드가 열려요.";
            if (goButton != null)
                goButton.State = open ? UiButtonState.Normal : UiButtonState.Locked;
        }

        /// <summary>"2분 30초" - whole minutes, and seconds only when there are any.</summary>
        static string Minutes(float seconds)
        {
            int t = Mathf.RoundToInt(seconds);
            return t % 60 == 0 ? $"{t / 60}분" : $"{t / 60}분 {t % 60}초";
        }

        /// <summary>
        /// 협동 카드(원본 `renderCoopCard`). 파트너 두 명의 이름·성향, 웨이브 기록,
        /// 오늘의 청소 일지를 원본과 같은 모드 카드 틀로 보여 준다.
        /// </summary>
        void RefreshCoop(PlayerProfile p)
        {
            if (coopCard == null) return;
            var db = _svc.Db;
            var cfg = db.Config;
            bool unlocked = p.CoopUnlocked;

            var partners = db.CoopPartners;
            string Name(CoopPartnerRow r) => db.Text(r.nameKey);
            string Style(CoopPartnerRow r) => db.Text(r.styleKey);

            var who = new List<string>();
            foreach (var r in partners) who.Add($"{Name(r)}({Style(r)})");

            var cells = new (string, string)[ModeCard.StatCount];
            for (int i = 0; i < ModeCard.StatCount - 1; i++)
                cells[i] = i < partners.Count
                    ? ($"{Style(partners[i])} 최고", $"{p.CoopBest(partners[i].id)}웨이브")
                    : (string.Empty, string.Empty);
            var (pts, max) = p.SurvivorPoolToday();
            cells[ModeCard.StatCount - 1] = ("청소 일지", $"{pts}/{max}");

            coopCard.Set(
                "봇 파트너 · 랜덤 소환", "협동 청소",
                "칸을 골라 가전을 무작위로 소환하고, 같은 ★ 두 대를 합쳐 키워요. " +
                $"위는 파트너, 아래는 나. 고리 위 먼지가 {cfg.coopLoopMax}마리가 되면 끝.",
                new[]
                {
                    "파트너 " + string.Join(" · ", who),
                    $"보상 버틴 웨이브마다 청소 일지 +{cfg.poolCoopWave} · 코인",
                },
                cells, null,
                unlocked ? "파트너 고르기" : p.CoopLockMessage, unlocked);
        }

        /// <summary>
        /// 먼지 타워는 표와 카드만 이식돼 있고 전투가 아직 없다. 카드를 눌렀을 때 이 말을
        /// 띄운다 — 원본 문구가 아니라 이식 상태를 알리는 우리 쪽 안내다.
        /// (리그는 2026-10-22 에 들어갔으므로 더는 이 안내를 쓰지 않는다.)
        /// </summary>
        const string NotPortedYet = "이 모드는 아직 이식 중이에요. 카드의 정보만 먼저 보여 줘요.";

        /// <summary>
        /// 먼지 타워 카드(원본 `renderTowerCard`). 층 수·다음 큰 보상·보너스 남은 층은
        /// tower_floor 표에서 세고, 진행도는 아직 저장 기록이 없어 0 이다.
        /// </summary>
        void RefreshTower(PlayerProfile p)
        {
            if (towerCard == null) return;
            var db = _svc.Db;
            int floors = db.TowerFloors.Count;
            bool unlocked = p.TowerUnlocked;

            // 원본 `nb`: 아직 깨지 않은 층 중 5의 배수인 첫 층.
            int next = 0;
            foreach (var f in db.TowerFloors)
                if (f.floor % 5 == 0) { next = f.floor; break; }

            var first = db.TowerFloor(1);
            string sub = string.Empty;
            if (unlocked && first != null)
            {
                string rule = !string.IsNullOrEmpty(first.rule)
                              && db.TowerRules.TryGetValue(first.rule, out var r)
                    ? db.Text(r.nameKey) : "규칙 없음";
                sub = $"다음: 1층 {db.Text(first.nameKey)} · {rule}";
            }

            towerCard.Set(
                $"1회성 보상 던전 · {floors}층", "먼지 타워",
                "맨션을 한 층씩 청소해요. 층마다 규칙이 달라서 편성을 바꿔 가며 올라가요.",
                new[]
                {
                    "보상 층마다 첫 클리어 한 번 + 보너스 목표 한 번 · 재도전 무료",
                    "성장 키운 가전 레벨·돌파·장비가 그대로 반영돼요",
                },
                new[]
                {
                    ("청소한 층", $"0/{floors}"),
                    ("다음 큰 보상", next > 0 ? $"{next}층" : "—"),
                    ("보너스 남음", "0층"),
                },
                sub, unlocked ? "입장" : p.TowerLockMessage, unlocked);
        }

        /// <summary>
        /// 가전 대청소 리그 카드(원본 `renderLeagueCard`). 티어 칸은 league_tier 를 그대로
        /// 따라가고, 아직 기록이 없어 첫 티어만 0승 · 나머지는 잠김이다.
        /// </summary>
        void RefreshLeague(PlayerProfile p)
        {
            if (leagueCard == null) return;
            var db = _svc.Db;
            var cfg = db.Config;
            bool unlocked = p.LeagueUnlocked;

            cfg.TryNum("leagueRounds", out float rounds);
            cfg.TryNum("leagueGrowthMax", out float growth);
            cfg.TryNum("leagueDailyRewards", out float daily);

            var cells = new (string, string)[ModeCard.StatCount];
            for (int i = 0; i < ModeCard.StatCount; i++)
            {
                if (i < db.LeagueTiers.Count)
                {
                    var t = db.LeagueTiers[i];
                    // 기록이 없으니 첫 티어만 열려 있고(0승) 나머지는 잠김 — 원본의 tierMax=1 과 같다.
                    cells[i] = (db.Text(t.nameKey), i == 0 ? "0승" : "잠김");
                }
                else cells[i] = (string.Empty, string.Empty);
            }

            leagueCard.Set(
                $"오토배틀러 · {Mathf.RoundToInt(rounds)}라운드", "가전 대청소 리그",
                "상점에서 가전을 사서 판에 놓으면 전투는 자동이에요. 한 판 4~5분.",
                new[]
                {
                    "합체 같은 가전 3장 → ★2, ★2 3장 → ★3(모습이 바뀜)",
                    $"시너지 같은 계열·역할이 모이면 효과 · 키운 가전은 최대 +{Mathf.RoundToInt(growth * 100f)}%",
                },
                cells,
                $"코인·부품 보상 판 0/{Mathf.RoundToInt(daily)} 남음 · 이긴 라운드마다 청소 일지 +{cfg.poolLeagueWin}",
                unlocked ? "입장" : p.LeagueLockMessage, unlocked);
        }

        /// <summary>Seconds as m:ss, the way the build writes every survival time.</summary>
        static string Clock(int seconds)
            => $"{seconds / 60}:{seconds % 60:00}";
    }
}
