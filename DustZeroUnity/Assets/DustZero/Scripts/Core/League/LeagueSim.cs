// 가전 대청소 리그 — 혼자 하는 8라운드 오토배틀러.
//
// 원본 index.html 의 `lg*` 함수들을 그대로 옮긴 것이다. 준비(상점·대기석·배치, 시간 제한
// 없음) → 자동 전투 → 결과 → 다음 라운드. 판은 7x6 칸이고 아래 3줄(21칸)이 우리 자리다.
//
// 본편 디펜스와 다른 점:
//  - **길이 없다.** 먼지는 입구에서 걸어오지 않고 판 위쪽에 바로 선다. 서로를 향해 간다.
//  - **전력이 없다.** 코인으로 사고, 같은 가전 세 장이면 ★ 가 오른다.
//  - **계정 성장은 상한이 있다.** config.leagueGrowthMax 까지만 반영된다. 체험 가전은 0 이다.
//
// 이 클래스는 MonoBehaviour 가 아니고 UnityEngine 을 쓰지 않는다. 화면 없이 테스트에서
// 한 판을 끝까지 돌릴 수 있다(원본의 tools/sim/leaguebot.js 와 같은 자리).
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Meta;
using DustZero.Data;

namespace DustZero.Core.League
{
    public sealed class LeagueSim
    {
        /// <summary>판은 7칸 가로, 우리 자리는 아래 3줄이다.</summary>
        public const int Cols = 7, OurRows = 3, Rows = 6;
        public const int BoardCells = Cols * OurRows;

        /// <summary>
        /// 편성이 모자랄 때 상점을 채우는 체험 가전. 원본도 코드에 적어 둔 목록이다
        /// (수치가 아니라 "무엇을 빌려 줄지" 라서 표에 없다). 순서까지 원본과 같다.
        /// </summary>
        static readonly string[] TrialOrder =
            { "vac", "robot", "fan", "gen", "ac", "micro", "iron", "pc" };

        readonly GameDatabase _db;
        readonly ConfigRow _cfg;
        readonly PlayerProfile _profile;
        readonly IRandom _rng;

        // ------------------------------------------------------------------ 판 상태

        public int Tier { get; }
        public LeagueTierRow TierRow { get; }
        public int Round { get; private set; } = 1;
        public int Life { get; private set; }
        public int Gold { get; private set; }
        public int Level { get; private set; }
        public int Xp { get; private set; }
        public int Wins { get; private set; }
        public int Losses { get; private set; }
        /// <summary>연승이면 양수, 연패면 음수.</summary>
        public int Streak { get; private set; }
        public LeaguePhase Phase { get; private set; } = LeaguePhase.Prep;
        /// <summary>다음 라운드에 상점을 그대로 둔다.</summary>
        public bool ShopLocked { get; set; }

        public LeaguePiece[] Bench { get; }
        public LeaguePiece[] Board { get; } = new LeaguePiece[BoardCells];
        public string[] Shop { get; private set; }

        /// <summary>상점에 설 수 있는 가전과 남은 장수.</summary>
        public IReadOnlyList<string> Pool => _pool;
        readonly List<string> _pool = new();
        readonly HashSet<string> _trial = new();
        readonly Dictionary<string, int> _bag = new();

        /// <summary>체험 가전이라 계정 성장이 붙지 않는 가전인지.</summary>
        public bool IsTrial(string unitId) => _trial.Contains(unitId);
        public int Stock(string unitId) => _bag.TryGetValue(unitId, out var n) ? n : 0;

        int _uid;

        // ------------------------------------------------------------------ 전투 상태

        /// <summary>전투 중인 단위들. 준비 단계에서는 비어 있다.</summary>
        public List<LeagueUnit> Units { get; } = new();
        public float BattleTime { get; private set; }
        public bool BattleOver { get; private set; }
        public bool? BattleWin { get; private set; }
        public bool BattleTimeout { get; private set; }
        /// <summary>★ 가 올랐다. 화면이 알림을 띄운다(가전 id, 새 ★).</summary>
        public event Action<string, int> Merged;

        /// <summary>마지막 라운드 결과. 아직 한 라운드도 안 끝났으면 null.</summary>
        public LeagueRoundResult Last { get; private set; }
        /// <summary>가전 종류·★ 별로 넣은 피해 — 결과 화면이 쓴다.</summary>
        public Dictionary<string, float> DamageLog { get; } = new();

        readonly List<Zone> _zones = new();
        Dictionary<string, float> _eff = new();
        float _chill;
        int _battleUid;

        sealed class Zone
        {
            public float X, Y, R, Dps, T, Tick;
            public LeagueUnit Src;
        }

        // ------------------------------------------------------------------ 시작

        public LeagueSim(GameDatabase db, PlayerProfile profile, int tier, IRandom rng)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _cfg = db.Config;
            _rng = rng ?? new SystemRandom();

            Tier = tier;
            TierRow = db.LeagueTiers.FirstOrDefault(t => t.tier == tier) ?? db.LeagueTiers.FirstOrDefault();

            Life = _cfg.leagueLife;
            Gold = _cfg.leagueStartGold;
            Level = _cfg.leagueStartLv;
            Bench = new LeaguePiece[Math.Max(1, _cfg.leagueBench)];
            Shop = new string[Math.Max(1, _cfg.leagueShopSize)];

            BuildPool();
            foreach (var t in _pool) _bag[t] = CopiesFor(t);
            Roll(free: true);
        }

        /// <summary>
        /// 상점에 나오는 가전은 **편성 6종**이다. 보유 가전 전부를 넣으면 많이 모은 계정일수록
        /// 같은 가전이 덜 나와 합체가 어려워진다 — 원본이 편성으로 고정한 이유다.
        /// 편성이 모자라면 다른 보유 가전, 그래도 모자라면 체험 가전으로 채운다.
        /// </summary>
        void BuildPool()
        {
            int want = Math.Max(1, _cfg.leagueTrialMin);
            foreach (var t in _profile.Data.deck)
            {
                if (_pool.Count >= want) break;
                if (string.IsNullOrEmpty(t) || _pool.Contains(t)) continue;
                if (_profile.Data.Own(t) == null || !_db.LeagueUnits.ContainsKey(t)) continue;
                _pool.Add(t);
            }
            foreach (var t in _db.UnitOrder)
            {
                if (_pool.Count >= want) break;
                if (_pool.Contains(t) || _profile.Data.Own(t) == null) continue;
                if (!_db.LeagueUnits.ContainsKey(t)) continue;
                _pool.Add(t);
            }
            foreach (var t in TrialOrder)
            {
                if (_pool.Count >= want) break;
                if (_pool.Contains(t) || !_db.LeagueUnits.ContainsKey(t)) continue;
                _pool.Add(t);
                _trial.Add(t);
            }
        }

        int RankOf(string unitId)
            => _db.Units.TryGetValue(unitId, out var u) ? (int)u.Rarity : 0;

        int CopiesFor(string unitId) => Pick(_cfg.leaguePoolCopies, RankOf(unitId), 1);
        public int CostOf(string unitId) => Pick(_cfg.leagueCost, RankOf(unitId), 1);

        static int Pick(int[] table, int i, int fallback)
            => table != null && i >= 0 && i < table.Length ? table[i] : fallback;

        public int MaxLevel => Math.Max(1, (_cfg.leagueLvXp?.Length ?? 2) - 1);

        // ------------------------------------------------------------------ 상점

        /// <summary>상점을 새로 뽑는다. 첫 뽑기와 라운드가 넘어갈 때는 공짜다.</summary>
        public bool Roll(bool free = false)
        {
            if (!free)
            {
                if (Phase != LeaguePhase.Prep || Gold < _cfg.leagueRerollCost) return false;
                Gold -= _cfg.leagueRerollCost;
            }

            var odds = OddsFor(Level);
            var shop = new string[Shop.Length];
            for (int i = 0; i < shop.Length; i++)
            {
                float x = _rng.Value * 100f;
                int rank = x < odds[0] ? 0 : x < odds[0] + odds[1] ? 1 : 2;

                List<string> choices = null;
                for (int tries = 0; tries < 3 && (choices == null || choices.Count == 0); tries++)
                {
                    int shownRank = rank;
                    choices = _pool.FindAll(t => RankOf(t) == shownRank
                                                 && Stock(t) - Shown(shop, i, t) > 0);
                    if (choices.Count == 0) rank = rank > 0 ? rank - 1 : 2;
                }
                shop[i] = choices.Count > 0 ? choices[(int)(_rng.Value * choices.Count)] : null;
            }
            Shop = shop;
            return true;
        }

        static int Shown(string[] shop, int upTo, string t)
        {
            int n = 0;
            for (int i = 0; i < upTo; i++) if (shop[i] == t) n++;
            return n;
        }

        int[] OddsFor(int level)
        {
            var table = _cfg.leagueOdds;
            if (table == null || table.Length == 0) return new[] { 100, 0, 0 };
            return table[Math.Min(level, table.Length - 1)];
        }

        /// <summary>이 레벨에서의 등급 확률(백분율). 화면이 그대로 보여 준다.</summary>
        public IReadOnlyList<int> Odds => OddsFor(Level);

        public bool Buy(int slot)
        {
            if (Phase != LeaguePhase.Prep || slot < 0 || slot >= Shop.Length) return false;
            var t = Shop[slot];
            if (string.IsNullOrEmpty(t)) return false;

            int cost = CostOf(t);
            if (Gold < cost) return false;

            int bench = Array.IndexOf(Bench, null);
            // 대기석이 꽉 차도 **합쳐질 수 있으면** 산다 — 원본과 같은 예외다.
            if (bench < 0 && CountOf(t, 1) < 2) return false;

            Gold -= cost;
            Shop[slot] = null;
            _bag[t] = Stock(t) - 1;

            var piece = new LeaguePiece { Type = t, Star = 1, Uid = ++_uid };
            if (bench < 0)
            {
                // 합체 재료 하나를 치우고 그 자리에 새 조각을 넣는다.
                //
                // **원본의 결함을 그대로 둔 자리다.** 재료를 덮어쓰므로 장수가 늘지 않아
                // 정작 합체가 일어나지 않는다(★1 두 장 + 구매 = 두 장). 브라우저에서
                // 원본을 돌려 확인했다. 고치면 게임 규칙이 달라지므로 따로 정한다.
                var first = All().First(x => x.Piece.Type == t && x.Piece.Star == 1);
                Put(first.Spot, piece);
            }
            else Bench[bench] = piece;

            Merge(t);
            return true;
        }

        readonly struct Held
        {
            public readonly LeaguePiece Piece;
            public readonly LeagueSpot Spot;
            public Held(LeaguePiece p, LeagueSpot s) { Piece = p; Spot = s; }
        }

        /// <summary>대기석 먼저, 그다음 판. 원본 `lgAll` 과 순서가 같아야 합체 결과가 같다.</summary>
        IEnumerable<Held> All()
        {
            for (int i = 0; i < Bench.Length; i++)
                if (Bench[i] != null) yield return new Held(Bench[i], new LeagueSpot(LeagueWhere.Bench, i));
            for (int i = 0; i < Board.Length; i++)
                if (Board[i] != null) yield return new Held(Board[i], new LeagueSpot(LeagueWhere.Board, i));
        }

        LeaguePiece At(LeagueSpot s)
            => s.Where == LeagueWhere.Bench ? Bench[s.Index] : Board[s.Index];

        void Put(LeagueSpot s, LeaguePiece p)
        {
            if (s.Where == LeagueWhere.Bench) Bench[s.Index] = p; else Board[s.Index] = p;
        }

        /// <summary>같은 가전·같은 ★ 가 몇 장인지(대기석 + 판).</summary>
        public int CountOf(string unitId, int star)
            => All().Count(x => x.Piece.Type == unitId && x.Piece.Star == star);

        /// <summary>
        /// 같은 가전 셋이 모이면 ★ 를 올린다. 판 위의 말을 먼저 쓰므로 자리가 유지된다.
        /// ★2 셋이 다시 ★3 이 될 수 있어 한 번 더 돈다.
        /// </summary>
        bool Merge(string unitId)
        {
            bool did = false;
            for (int star = 1; star <= 2; star++)
            {
                var xs = All().Where(x => x.Piece.Type == unitId && x.Piece.Star == star).ToList();
                if (xs.Count < 3) continue;

                var use = xs.OrderBy(x => x.Spot.Where == LeagueWhere.Board ? 0 : 1).Take(3).ToList();
                foreach (var x in use) Put(x.Spot, null);
                Put(use[0].Spot, new LeaguePiece
                {
                    Type = unitId, Star = star + 1, Uid = ++_uid, Fresh = true,
                });
                did = true;
                Merged?.Invoke(unitId, star + 1);
            }
            if (did) Merge(unitId);
            return did;
        }

        public int SellValue(LeaguePiece p) => (int)(CostOf(p.Type) * Math.Pow(3, p.Star - 1));

        public bool Sell(LeagueSpot spot)
        {
            if (Phase != LeaguePhase.Prep) return false;
            var p = At(spot);
            if (p == null) return false;

            Gold += SellValue(p);
            // 판 장수는 돌아온다 — ★2 는 세 장이었으므로 세 장으로.
            _bag[p.Type] = Stock(p.Type) + (int)Math.Pow(3, p.Star - 1);
            Put(spot, null);
            return true;
        }

        public int OnBoard => Board.Count(p => p != null);

        /// <summary>
        /// 옮기기. 가려는 자리에 말이 있으면 맞바꾼다. 판으로 **새로** 올릴 때만
        /// 레벨이 허락하는 배치 수를 본다.
        /// </summary>
        public LeagueMoveResult Move(LeagueSpot from, LeagueSpot to)
        {
            if (Phase != LeaguePhase.Prep) return LeagueMoveResult.No;
            var a = At(from);
            if (a == null) return LeagueMoveResult.No;
            var b = At(to);

            if (from.Where == LeagueWhere.Bench && to.Where == LeagueWhere.Board
                && b == null && OnBoard >= Level)
                return LeagueMoveResult.Capped;

            Put(from, b);
            Put(to, a);
            return LeagueMoveResult.Yes;
        }

        public int XpNeeded => Level < MaxLevel ? _cfg.leagueLvXp[Level + 1] : int.MaxValue;

        public void AddXp(int amount)
        {
            Xp += amount;
            while (Level < MaxLevel && Xp >= XpNeeded)
            {
                Xp -= XpNeeded;
                Level++;
            }
            if (Level >= MaxLevel) Xp = 0;
        }

        public bool BuyXp()
        {
            if (Phase != LeaguePhase.Prep || Level >= MaxLevel || Gold < _cfg.leagueXpCost) return false;
            Gold -= _cfg.leagueXpCost;
            AddXp(_cfg.leagueXpBuy);
            return true;
        }

        // ------------------------------------------------------------------ 시너지

        /// <summary>
        /// 시너지는 **가전 종류** 수로 센다 — 같은 가전을 여러 장 놓아도 하나다.
        /// 계열(fam1~4)과 자리(P 근접 / H 원거리) 두 축이 있다.
        /// </summary>
        public (List<LeagueSynergyView> List, Dictionary<string, float> Eff) Synergies(
            IReadOnlyList<LeaguePiece> board = null)
        {
            var cells = board ?? Board;
            var types = new HashSet<string>(cells.Where(p => p != null).Select(p => p.Type));

            var count = new Dictionary<string, int>();
            foreach (var t in types)
            {
                if (!_db.Units.TryGetValue(t, out var u)) continue;
                Bump(count, "fam" + u.Family);
                Bump(count, u.Tile == TileKind.Ground ? "P" : "H");
            }

            var groups = new Dictionary<string, List<LeagueSynRow>>();
            foreach (var y in _db.LeagueSynergies)
            {
                if (!groups.TryGetValue(y.group, out var list))
                    groups[y.group] = list = new List<LeagueSynRow>();
                list.Add(y);
            }

            var views = new List<LeagueSynergyView>();
            var eff = new Dictionary<string, float>();
            var keys = new Dictionary<string, string>();

            foreach (var kv in groups)
            {
                var rows = kv.Value.OrderBy(r => r.need).ToList();
                int n = count.TryGetValue(kv.Key, out var c) ? c : 0;

                LeagueSynRow active = null;
                foreach (var r in rows) if (n >= r.need) active = r;
                var next = rows.FirstOrDefault(r => r.need > n);

                views.Add(new LeagueSynergyView
                {
                    Group = kv.Key,
                    Name = _db.Text(rows[0].nameKey),
                    Count = n,
                    Needs = rows.Select(r => r.need).ToArray(),
                    Active = active == null ? null : Step(active),
                    Next = next == null ? null : Step(next),
                });

                if (active != null)
                {
                    eff[kv.Key] = active.value;
                    keys[kv.Key] = active.key;
                }
            }

            views.Sort((a, b) =>
            {
                int byActive = (b.Active != null ? 1 : 0) - (a.Active != null ? 1 : 0);
                return byActive != 0 ? byActive : b.Count - a.Count;
            });

            _effKeys = keys;
            return (views, eff);

            LeagueSynStep Step(LeagueSynRow r) => new()
            {
                Need = r.need, Key = r.key, Value = r.value, Desc = FillDesc(r),
            };
        }

        Dictionary<string, string> _effKeys = new();

        string FillDesc(LeagueSynRow r)
            => (_db.Text(r.descKey) ?? string.Empty)
               .Replace("{v_pct}", Math.Round(r.value * 100f).ToString("0"))
               .Replace("{v}", r.value.ToString("0.##"));

        static void Bump(Dictionary<string, int> d, string key)
            => d[key] = (d.TryGetValue(key, out var v) ? v : 0) + 1;

        /// <summary>
        /// 이 가전이 받는 효과 값. 계열/자리 시너지는 **해당 가전에만**, 판 전체에 거는
        /// startMana·chill 은 모두에게 붙는다 — 원본 `lgEff` 와 같다.
        /// </summary>
        float Eff(Dictionary<string, float> eff, string key, string unitId)
        {
            float v = 0f;
            if (!_db.Units.TryGetValue(unitId, out var u)) return 0f;
            foreach (var kv in eff)
            {
                if (!_effKeys.TryGetValue(kv.Key, out var k) || k != key) continue;
                bool mine = kv.Key.StartsWith("fam", StringComparison.Ordinal)
                    ? kv.Key.Substring(3) == u.Family.ToString()
                    : kv.Key == (u.Tile == TileKind.Ground ? "P" : "H");
                if (mine || key == "startMana" || key == "chill") v += kv.Value;
            }
            return v;
        }

        // ------------------------------------------------------------------ 능력치

        /// <summary>
        /// 판 위의 말 하나가 전투에서 쓰는 수치. 계정 성장은 config.leagueGrowthMax 까지만
        /// 반영되고 체험 가전은 0 이다. 전용장비는 스킬 배수에만 붙는다.
        /// </summary>
        public (float Hp, float Atk, float Itv, float Range, float Spd, float Mana,
                float StarMul, float Growth, float ExclMul) UnitStats(LeaguePiece p)
        {
            var b = _db.LeagueUnits[p.Type];
            float sm = p.Star - 1 < (_cfg.leagueStarMul?.Length ?? 0)
                ? _cfg.leagueStarMul[p.Star - 1] : 1f;

            float growth = IsTrial(p.Type)
                ? 0f
                : Math.Min(_cfg.leagueGrowthMax,
                           _cfg.leagueGrowthMax * Math.Max(0f, _profile.PowerOf(p.Type) - 1f)
                           / Math.Max(0.0001f, _cfg.leagueGrowthPow));

            var excl = _profile.ExclusiveState(p.Type);
            float exm = excl != null
                ? 1f + _cfg.leagueExclSkill * (1f + _cfg.exclStarBonus * excl.star)
                : 1f;

            return (b.hp * sm * (1f + growth), b.atk * sm * (1f + growth),
                    b.itv, b.range, b.spd, b.mana, sm, growth, exm);
        }

        // ------------------------------------------------------------------ 전투

        public bool StartBattle()
        {
            if (Phase != LeaguePhase.Prep || OnBoard == 0) return false;

            var (_, eff) = Synergies();
            _eff = eff;
            _chill = Eff(eff, "chill", "vac");

            Units.Clear();
            _zones.Clear();
            DamageLog.Clear();
            BattleTime = 0f;
            BattleOver = false;
            BattleWin = null;
            BattleTimeout = false;
            _battleUid = 0;

            SpawnAllies(eff);
            var round = _db.LeagueRounds.FirstOrDefault(r => r.round == Round)
                        ?? _db.LeagueRounds.LastOrDefault();
            foreach (var q in SquadPositions(round))
                SpawnEnemy(q.Id, q.X + (_rng.Value - 0.5f) * 0.25f, q.Y, round);

            Phase = LeaguePhase.Battle;
            return true;
        }

        void SpawnAllies(Dictionary<string, float> eff)
        {
            for (int i = 0; i < Board.Length; i++)
            {
                var p = Board[i];
                if (p == null) continue;

                var st = UnitStats(p);
                float hpUp = Eff(eff, "hp", p.Type);
                float haste = Eff(eff, "haste", p.Type);

                Units.Add(new LeagueUnit
                {
                    Id = ++_battleUid,
                    Side = 0,
                    Type = p.Type,
                    Star = p.Star,
                    X = i % Cols + 0.5f,
                    Y = OurRows + i / Cols + 0.55f,
                    Hp = st.Hp * (1f + hpUp),
                    MaxHp = st.Hp * (1f + hpUp),
                    Atk = st.Atk,
                    Interval = st.Itv / (1f + haste),
                    Range = st.Range,
                    Speed = st.Spd,
                    Mana = Eff(eff, "startMana", p.Type),
                    MaxMana = st.Mana,
                    Dr = Eff(eff, "dr", p.Type),
                    SkillMul = st.ExclMul * (1f + Eff(eff, "skillDmg", p.Type)),
                    StarMul = st.StarMul,
                    AtkTimer = 0.2f + _rng.Value * 0.3f,
                    Cell = i,
                });
            }
        }

        /// <summary>적 자리: 근접은 앞줄(2), 공중은 가운데(1), 원거리와 보스는 뒷줄(0).</summary>
        IEnumerable<(string Id, float X, float Y)> SquadPositions(LeagueRoundRow round)
        {
            var rows = new List<string>[] { new(), new(), new() };
            foreach (var (id, n) in ParseSquad(round?.squad))
            {
                if (!_db.LeagueEnemies.TryGetValue(id, out var e)) continue;
                int row = id.StartsWith("boss_", StringComparison.Ordinal) ? 0
                        : e.fly != 0 ? 1
                        : e.range > 1.5f ? 0 : 2;
                for (int k = 0; k < n; k++) rows[row].Add(id);
            }

            var outList = new List<(string, float, float)>();
            for (int row = 0; row < 3; row++)
            {
                var ids = rows[row];
                for (int k = 0; k < ids.Count; k++)
                {
                    float x = ids.Count == 1 ? 3.5f : 0.6f + k * (5.8f / Math.Max(1, ids.Count - 1));
                    outList.Add((ids[k], Math.Min(6.5f, x), row + 0.55f));
                }
            }
            return outList;
        }

        /// <summary>"dust*3, mite*2" 를 (id, 수) 로 쪼갠다.</summary>
        public static IEnumerable<(string Id, int Count)> ParseSquad(string squad)
        {
            if (string.IsNullOrWhiteSpace(squad)) yield break;
            foreach (var part in squad.Split(','))
            {
                var token = part.Trim();
                if (token.Length == 0) continue;
                var bits = token.Split('*');
                int n = bits.Length > 1 && int.TryParse(bits[1], out var v) ? v : 1;
                yield return (bits[0].Trim(), Math.Max(1, n));
            }
        }

        /// <summary>
        /// 이번 라운드에 나올 먼지의 자리. 준비 단계에서 흐리게 미리 보여 주는 데 쓴다 —
        /// 원본도 같은 배치 계산을 그대로 다시 부른다.
        /// </summary>
        public IEnumerable<(string Id, float X, float Y)> NextSquad()
        {
            var round = _db.LeagueRounds.FirstOrDefault(r => r.round == Round)
                        ?? _db.LeagueRounds.LastOrDefault();
            return SquadPositions(round);
        }

        /// <summary>이번 라운드 줄. 화면이 이름과 보스 여부를 쓴다.</summary>
        public LeagueRoundRow CurrentRound
            => _db.LeagueRounds.FirstOrDefault(r => r.round == Round)
               ?? _db.LeagueRounds.LastOrDefault();

        void SpawnEnemy(string id, float x, float y, LeagueRoundRow round)
        {
            if (!_db.LeagueEnemies.TryGetValue(id, out var e)) return;
            var def = _db.Enemy(id);

            float hpMul = (TierRow?.hpMul ?? 1f) * (round?.rMul ?? 1f);
            float atkMul = (TierRow?.atkMul ?? 1f) * (round?.rMul ?? 1f);

            Units.Add(new LeagueUnit
            {
                Id = ++_battleUid,
                Side = 1,
                Type = id,
                Hp = e.hp * hpMul,
                MaxHp = e.hp * hpMul,
                Atk = e.atk * atkMul,
                // 냉기 시너지는 적의 공격 간격을 늘린다.
                Interval = e.itv / Math.Max(0.05f, 1f - _chill),
                Range = e.range,
                Speed = e.spd,
                Armor = e.armor,
                X = x,
                Y = y,
                AtkTimer = 0.4f + _rng.Value * 0.4f,
                Fly = e.fly != 0,
                Boss = def?.Boss ?? false,
                Radius = def != null && def.Radius > 0f ? def.Radius : 0.3f,
                Split = e.split,
                SplitN = e.splitN,
            });
        }

        static float Dist(LeagueUnit a, LeagueUnit b) => Mathf2.Hypot(a.X - b.X, a.Y - b.Y);

        /// <summary>근접 가전은 공중을 때릴 수 없다.</summary>
        static bool CanHit(LeagueUnit a, LeagueUnit b)
            => b.Alive && b.Side != a.Side && !(a.Side == 0 && b.Fly && a.Range < 1.5f);

        LeagueUnit PickTarget(LeagueUnit u)
        {
            LeagueUnit best = null;
            float bd = float.MaxValue;
            foreach (var o in Units)
            {
                if (!CanHit(u, o)) continue;
                float d = Dist(u, o);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        float Hit(LeagueUnit src, LeagueUnit tgt, float amount, bool pierce = false)
        {
            if (!tgt.Alive) return 0f;

            float d = amount
                      * (pierce ? 1f : 1f - tgt.Armor)
                      * (1f - tgt.Dr)
                      * (1f + (tgt.BreakT > 0f ? tgt.BreakAmt : 0f));
            tgt.Hp -= d;
            tgt.HitT = 0.12f;

            if (tgt.Side == 0) tgt.Mana = Math.Min(tgt.MaxMana, tgt.Mana + _cfg.leagueManaPerHit);
            if (src != null && src.Side == 0)
            {
                var key = src.Type + src.Star;
                DamageLog[key] = (DamageLog.TryGetValue(key, out var v) ? v : 0f) + d;
            }

            if (tgt.Hp <= 0f)
            {
                tgt.Alive = false;
                tgt.Hp = 0f;
                if (!string.IsNullOrEmpty(tgt.Split) && tgt.SplitN > 0)
                {
                    var round = _db.LeagueRounds.FirstOrDefault(r => r.round == Round);
                    for (int i = 0; i < tgt.SplitN; i++)
                        SpawnEnemy(tgt.Split,
                                   Mathf2.Clamp(tgt.X + (i - (tgt.SplitN - 1) / 2f) * 0.45f, 0.3f, 6.7f),
                                   tgt.Y, round);
                }
            }
            return d;
        }

        IEnumerable<LeagueUnit> Near(float x, float y, float r, int side)
            => Units.Where(o => o.Alive && o.Side == side
                                && Mathf2.Hypot(o.X - x, o.Y - y) <= r + o.Radius * 0.5f);

        /// <summary>가전마다 다른 자동 스킬. 계수는 전부 league_param 에서 온다.</summary>
        void Cast(LeagueUnit u)
        {
            float P(string key) => _db.LeagueParam(u.Type, key);
            float M = u.SkillMul;
            var tg = u.Target != null && u.Target.Alive ? u.Target : PickTarget(u);

            u.Mana = 0f;
            u.CastT = 0.5f;

            switch (u.Type)
            {
                case "vac":   // 빨아들이고 기절
                    foreach (var o in Near(u.X, u.Y, P("radius"), 1).ToList())
                    {
                        float d = Dist(u, o);
                        if (d <= 0f) d = 1f;
                        float pull = Math.Min(0.6f, d - 0.5f);
                        o.X += (u.X - o.X) / d * pull;
                        o.Y += (u.Y - o.Y) / d * pull;
                        Hit(u, o, u.Atk * P("dmg") * M);
                        o.StunT = Math.Max(o.StunT, P("stun"));
                    }
                    break;

                case "robot":
                    foreach (var o in Near(u.X, u.Y, P("radius"), 1).ToList())
                        Hit(u, o, u.Atk * P("dmg") * M);
                    break;

                case "fridge":
                    foreach (var o in Near(u.X, u.Y, P("radius"), 1).ToList())
                    {
                        Hit(u, o, u.Atk * P("dmg") * M);
                        o.FrozenT = Math.Max(o.FrozenT, P("freeze"));
                    }
                    break;

                case "iron":
                    if (tg != null)
                    {
                        Hit(u, tg, u.Atk * P("dmg") * M, pierce: true);
                        tg.StunT = Math.Max(tg.StunT, P("stun"));
                    }
                    break;

                case "ac":
                    if (tg != null)
                        foreach (var o in Near(tg.X, tg.Y, P("radius"), 1).ToList())
                        {
                            Hit(u, o, u.Atk * P("dmg") * M);
                            o.SlowT = P("slowSec");
                            o.SlowAmt = P("slow");
                        }
                    break;

                case "micro":
                    if (tg != null)
                        foreach (var o in Near(tg.X, tg.Y, P("radius"), 1).ToList())
                            Hit(u, o, u.Atk * P("dmg") * M);
                    break;

                case "washer":
                    if (tg != null)
                        foreach (var o in Near(tg.X, tg.Y, P("radius"), 1).ToList())
                        {
                            Hit(u, o, u.Atk * P("dmg") * M);
                            float d = Mathf2.Hypot(o.X - tg.X, o.Y - tg.Y);
                            if (d <= 0f) d = 1f;
                            o.Y = Math.Max(0.3f, o.Y - P("push") * 0.6f);
                            o.X += (o.X - tg.X) / d * P("push") * 0.4f;
                        }
                    break;

                case "pc":    // 아군 공격 속도
                {
                    float byStar = u.Star == 1 ? 1f : u.Star == 2 ? 1.3f : 1.6f;
                    foreach (var o in Near(u.X, u.Y, P("radius"), 0).ToList())
                    {
                        o.HasteT = P("dur");
                        o.HasteAmt = P("haste") * byStar * M;
                    }
                    break;
                }

                case "fan":   // 아군 회복
                    foreach (var o in Near(u.X, u.Y, P("radius"), 0).ToList())
                        o.Hp = Math.Min(o.MaxHp, o.Hp + P("heal") * u.StarMul * M);
                    break;

                case "gen":   // 아군 마나
                    foreach (var o in Near(u.X, u.Y, P("radius"), 0).ToList())
                        if (o != u) o.Mana = Math.Min(o.MaxMana, o.Mana + P("mana") * M);
                    break;

                case "stove": // 불바다
                    if (tg != null)
                        _zones.Add(new Zone
                        {
                            X = tg.X, Y = tg.Y, R = P("radius"),
                            Dps = u.Atk * P("dps") * M, T = P("dur"), Src = u,
                        });
                    break;

                case "boiler":  // 방어 깎기
                    foreach (var o in Near(u.X, u.Y, u.Range + 0.5f, 1).ToList())
                    {
                        Hit(u, o, u.Atk * P("dmg") * M);
                        o.BreakT = P("dur");
                        o.BreakAmt = P("brk");
                    }
                    break;
            }
        }

        /// <summary>한 틱. 원본은 1/30 초로 돌린다.</summary>
        public void Step(float dt)
        {
            if (Phase != LeaguePhase.Battle || BattleOver) return;
            BattleTime += dt;

            foreach (var z in _zones)
            {
                z.T -= dt;
                z.Tick -= dt;
                if (z.Tick > 0f) continue;
                z.Tick = 0.25f;
                foreach (var o in Near(z.X, z.Y, z.R, 1).ToList())
                    if (!o.Fly) Hit(z.Src, o, z.Dps * 0.25f);
            }
            _zones.RemoveAll(z => z.T <= 0f);

            // 번호로 돈다 — 분열하는 적이 전투 중에 목록을 늘린다. 원본의 for..of 도
            // 새로 들어온 적을 그 자리에서 함께 돌린다(foreach 로는 예외가 난다).
            for (int ui = 0; ui < Units.Count; ui++)
            {
                var u = Units[ui];
                if (!u.Alive) continue;

                u.Kick = Math.Max(0f, u.Kick - dt);
                u.HitT = Math.Max(0f, u.HitT - dt);
                u.CastT = Math.Max(0f, u.CastT - dt);
                u.StunT = Math.Max(0f, u.StunT - dt);
                u.FrozenT = Math.Max(0f, u.FrozenT - dt);
                u.SlowT = Math.Max(0f, u.SlowT - dt);
                u.HasteT = Math.Max(0f, u.HasteT - dt);
                u.BreakT = Math.Max(0f, u.BreakT - dt);

                if (u.StunT > 0f || u.FrozenT > 0f) continue;

                if (u.Target == null || !CanHit(u, u.Target)) u.Target = PickTarget(u);
                var tg = u.Target;
                if (tg == null) continue;

                float d = Dist(u, tg);
                float reach = u.Range + tg.Radius * 0.6f + 0.25f;
                u.AtkTimer -= dt * (1f + (u.HasteT > 0f ? u.HasteAmt : 0f));

                if (d > reach)
                {
                    float sp = u.Speed * (u.SlowT > 0f ? 1f - u.SlowAmt : 1f) * dt;
                    float move = Math.Min(sp, d - reach + 0.01f);
                    u.X += (tg.X - u.X) / d * move;
                    u.Y += (tg.Y - u.Y) / d * move;
                    u.Face = tg.X < u.X ? -1 : 1;
                }
                else if (u.AtkTimer <= 0f)
                {
                    u.AtkTimer = u.Interval;
                    Hit(u, tg, u.Atk);
                    u.Kick = 0.25f;
                    if (u.Side == 0)
                    {
                        u.Mana = Math.Min(u.MaxMana, u.Mana + _cfg.leagueManaPerAtk);
                        if (u.Mana >= u.MaxMana) Cast(u);
                    }
                }
            }

            Separate();
            Settle();
        }

        /// <summary>겹침 풀기. 공중과 지상은 서로 밀지 않는다.</summary>
        void Separate()
        {
            var alive = Units.Where(u => u.Alive).ToList();
            const float min = 0.55f;
            for (int i = 0; i < alive.Count; i++)
                for (int j = i + 1; j < alive.Count; j++)
                {
                    var a = alive[i];
                    var b = alive[j];
                    if (a.Fly != b.Fly) continue;
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    float d = Mathf2.Hypot(dx, dy);
                    if (d <= 0f) d = 0.01f;
                    if (d >= min) continue;
                    float push = (min - d) / 2f;
                    a.X -= dx / d * push; a.Y -= dy / d * push;
                    b.X += dx / d * push; b.Y += dy / d * push;
                }
            foreach (var u in alive)
            {
                u.X = Mathf2.Clamp(u.X, 0.3f, Cols - 0.3f);
                u.Y = Mathf2.Clamp(u.Y, 0.3f, Rows - 0.3f);
            }
        }

        void Settle()
        {
            bool ours = Units.Any(u => u.Side == 0 && u.Alive);
            var foes = Units.Where(u => u.Side == 1 && u.Alive).ToList();

            if (foes.Count == 0) { BattleOver = true; BattleWin = true; }
            else if (!ours) { BattleOver = true; BattleWin = false; }
            else if (BattleTime >= _cfg.leagueRoundTime)
            {
                BattleOver = true;
                BattleWin = false;
                BattleTimeout = true;
            }

            // 보스는 셋으로 센다 — 졌을 때 깎이는 청결도가 그만큼 크다.
            if (BattleOver) _left = foes.Sum(e => e.Boss ? 3 : 1);
        }

        int _left;

        /// <summary>화면 없이 전투를 끝까지 돌린다. 테스트와 봇이 쓴다.</summary>
        public void RunBattleToEnd()
        {
            int guard = (int)(30f * _cfg.leagueRoundTime) + 60;
            while (!BattleOver && guard-- > 0) Step(1f / 30f);
        }

        // ------------------------------------------------------------------ 라운드 마무리

        /// <summary>전투가 끝난 뒤 청결도·연승·수입을 정산하고 다음 라운드를 연다.</summary>
        public LeagueRoundResult EndRound()
        {
            if (Phase != LeaguePhase.Battle || !BattleOver) return null;
            bool win = BattleWin == true;

            if (win)
            {
                Wins++;
                Streak = Streak > 0 ? Streak + 1 : 1;
                _profile.AddPoolPoints(_cfg.poolLeagueWin);
            }
            else
            {
                Losses++;
                Streak = Streak < 0 ? Streak - 1 : -1;
                Life -= _cfg.leagueLoseBase + _left;
            }

            Last = new LeagueRoundResult
            {
                Round = Round, Win = win, Left = _left, Timeout = BattleTimeout,
            };
            Units.Clear();
            _zones.Clear();

            if (Life <= 0 || Round >= _cfg.leagueRounds)
            {
                Phase = LeaguePhase.Over;
                return Last;
            }

            Round++;
            Phase = LeaguePhase.Prep;

            int interest = Math.Min(_cfg.leagueInterestMax, Gold / Math.Max(1, _cfg.leagueInterestPer));
            int income = _cfg.leagueRoundGold
                         + (win ? _cfg.leagueWinGold : 0)
                         + interest
                         + (Math.Abs(Streak) >= 2 ? 1 : 0);
            Gold += income;
            Last.Income = income;
            AddXp(_cfg.leagueXpPerRound);

            if (!ShopLocked) Roll(free: true);
            ShopLocked = false;
            return Last;
        }

        /// <summary>
        /// 판을 끝내고 보상을 준다. 2라운드 이상 했을 때만 오늘의 보상 판 수를 쓴다.
        /// 받은 것은 PlayerProfile 이 실제로 넣어 준다 — 여기서 수치를 만들지 않는다.
        /// </summary>
        public LeagueFinishResult Finish(bool quit = false)
        {
            bool clear = !quit && Life > 0 && Round >= _cfg.leagueRounds;
            bool counted = Round >= 2 && !quit;
            return _profile.FinishLeagueRun(Tier, TierRow, Wins, clear, counted, quit);
        }
    }
}
