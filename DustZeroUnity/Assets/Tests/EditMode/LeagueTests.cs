// 가전 대청소 리그(오토배틀러) 회귀 검사.
//
// 원본 index.html 의 lg* 함수와 같은 답이 나오는지 본다. 디펜스·생존과 달리 리그는
// **상점 경제와 합체**가 규칙의 절반이라, 전투만큼이나 그쪽을 본다.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core;
using DustZero.Core.League;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class LeagueTests
    {
        GameDatabase _db;
        ConfigRow _cfg;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _cfg = _db.Config;
        }

        /// <summary>편성을 꽉 채운 계정으로 한 판을 연다. 난수는 씨앗으로 고정한다.</summary>
        LeagueSim Run(int seed = 12345, int tier = 1)
        {
            var rng = new LeagueRandom(seed);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);

            var deck = new List<string>(data.owned.Keys);
            foreach (var id in _db.UnitOrder)
            {
                if (deck.Count >= _cfg.deckMax) break;
                if (!deck.Contains(id)) deck.Add(id);
            }
            data.deck = deck;

            return new LeagueSim(_db, profile, tier, rng);
        }

        /// <summary>대기석에 말을 직접 놓는다. 상점 운에 기대지 않으려는 검사용 손잡이.</summary>
        static void Place(LeagueSim lg, LeagueWhere where, int index, string type, int star = 1)
        {
            var piece = new LeaguePiece { Type = type, Star = star, Uid = -(index + 1) };
            if (where == LeagueWhere.Bench) lg.Bench[index] = piece;
            else lg.Board[index] = piece;
        }

        // ------------------------------------------------------------------ 상점과 경제

        /// <summary>
        /// 상점 뽑기가 원본과 같은 수열을 쓰는지. 기준값은 브라우저에서 원본 `lgRng` 를
        /// 그대로 돌려 받아 적은 것이다 — 이게 맞아야 같은 씨앗으로 원본과 대조할 수 있다.
        /// </summary>
        [Test]
        public void TheShopRandomMatchesTheOriginalSequence()
        {
            var expected = new Dictionary<int, float[]>
            {
                [1] = new[] { 0.627073941f, 0.002735721f, 0.52744704f,
                              0.981050967f, 0.968377898f, 0.281103503f },
                [12345] = new[] { 0.979728268f, 0.306752264f, 0.484205422f,
                                  0.817934413f, 0.509428369f, 0.34747186f },
            };

            foreach (var kv in expected)
            {
                var rng = new LeagueRandom(kv.Key);
                for (int i = 0; i < kv.Value.Length; i++)
                    Assert.AreEqual(kv.Value[i], rng.Value, 1e-6f,
                                    $"씨앗 {kv.Key} 의 {i + 1}번째 난수가 원본과 다릅니다.");
            }
        }

        [Test]
        public void NewRunStartsWithTheTablesNumbers()
        {
            var lg = Run();
            Assert.AreEqual(_cfg.leagueLife, lg.Life, "청결도가 config 와 다릅니다.");
            Assert.AreEqual(_cfg.leagueStartGold, lg.Gold, "시작 코인이 config 와 다릅니다.");
            Assert.AreEqual(_cfg.leagueStartLv, lg.Level, "시작 레벨이 config 와 다릅니다.");
            Assert.AreEqual(1, lg.Round);
            Assert.AreEqual(LeaguePhase.Prep, lg.Phase);
            Assert.AreEqual(_cfg.leagueShopSize, lg.Shop.Length, "상점 칸 수가 config 와 다릅니다.");
            Assert.AreEqual(_cfg.leagueBench, lg.Bench.Length, "대기석 칸 수가 config 와 다릅니다.");
        }

        [Test]
        public void TheShopOnlyOffersApplianceTheRunHasStockOf()
        {
            var lg = Run();
            foreach (var t in lg.Shop)
            {
                if (t == null) continue;
                Assert.Contains(t, lg.Pool.ToList(), $"{t} 은 이 판의 상점 목록에 없습니다.");
            }
        }

        [Test]
        public void BuyingSpendsGoldAndTakesOneCopyOutOfStock()
        {
            var lg = Run();
            int slot = System.Array.FindIndex(lg.Shop, t => t != null);
            Assume.That(slot, Is.GreaterThanOrEqualTo(0), "상점이 비어 검사할 수 없습니다.");

            var type = lg.Shop[slot];
            int gold = lg.Gold, stock = lg.Stock(type), cost = lg.CostOf(type);

            Assert.IsTrue(lg.Buy(slot), "살 수 있어야 합니다.");
            Assert.AreEqual(gold - cost, lg.Gold, "코인이 값만큼 줄지 않았습니다.");
            Assert.AreEqual(stock - 1, lg.Stock(type), "상점 재고가 줄지 않았습니다.");
            Assert.IsNull(lg.Shop[slot], "산 칸이 비지 않았습니다.");
        }

        [Test]
        public void SellingReturnsTheCopiesTheStarWasMadeOf()
        {
            var lg = Run();
            var type = lg.Pool[0];
            int stock = lg.Stock(type);

            // ★2 는 세 장으로 만든 것이므로 팔면 세 장이 돌아온다.
            Place(lg, LeagueWhere.Bench, 0, type, star: 2);
            Assert.IsTrue(lg.Sell(new LeagueSpot(LeagueWhere.Bench, 0)));
            Assert.AreEqual(stock + 3, lg.Stock(type), "★2 를 팔았는데 세 장이 돌아오지 않았습니다.");
        }

        [Test]
        public void SellValueFollowsTheStar()
        {
            var lg = Run();
            var type = lg.Pool[0];
            int cost = lg.CostOf(type);
            Assert.AreEqual(cost, lg.SellValue(new LeaguePiece { Type = type, Star = 1 }));
            Assert.AreEqual(cost * 3, lg.SellValue(new LeaguePiece { Type = type, Star = 2 }));
            Assert.AreEqual(cost * 9, lg.SellValue(new LeaguePiece { Type = type, Star = 3 }));
        }

        // ------------------------------------------------------------------ 합체

        [Test]
        public void ThreeOfAKindBecomeOneStarHigher()
        {
            var lg = Run();
            var type = lg.Pool[0];

            Place(lg, LeagueWhere.Bench, 0, type);
            Place(lg, LeagueWhere.Bench, 1, type);
            Place(lg, LeagueWhere.Bench, 2, type);
            // 합체는 사거나 놓을 때 일어난다. 직접 놓았으므로 한 번 더 사서 깨운다.
            lg.Bench[2] = null;
            Place(lg, LeagueWhere.Bench, 2, type);
            InvokeMerge(lg, type);

            Assert.AreEqual(1, lg.CountOf(type, 2), "★2 가 한 장 나와야 합니다.");
            Assert.AreEqual(0, lg.CountOf(type, 1), "재료 ★1 이 남아 있습니다.");
        }

        [Test]
        public void MergingKeepsThePieceOnTheBoardWhereItStood()
        {
            var lg = Run();
            var type = lg.Pool[0];

            Place(lg, LeagueWhere.Board, 5, type);
            Place(lg, LeagueWhere.Bench, 0, type);
            Place(lg, LeagueWhere.Bench, 1, type);
            InvokeMerge(lg, type);

            Assert.IsNotNull(lg.Board[5], "판 위의 자리가 비었습니다 — 합체가 자리를 옮겼습니다.");
            Assert.AreEqual(2, lg.Board[5].Star, "판 위 말이 ★2 가 되지 않았습니다.");
            Assert.IsNull(lg.Bench[0]);
            Assert.IsNull(lg.Bench[1]);
        }

        [Test]
        public void NineOfAKindClimbAllTheWayToThreeStars()
        {
            var lg = Run();
            var type = lg.Pool[0];
            // 대기석(8칸)만으로는 아홉 장이 안 들어간다. 나머지는 판에 올린다.
            int placed = 0;
            for (int i = 0; i < lg.Bench.Length && placed < 9; i++, placed++)
                Place(lg, LeagueWhere.Bench, i, type);
            for (int i = 0; placed < 9; i++, placed++) Place(lg, LeagueWhere.Board, i, type);
            InvokeMerge(lg, type);

            Assert.AreEqual(1, lg.CountOf(type, 3), "★3 이 나오지 않았습니다.");
            Assert.AreEqual(0, lg.CountOf(type, 2), "재료 ★2 가 남아 있습니다.");
        }

        /// <summary>
        /// 대기석이 꽉 차도 **합쳐질 수 있으면** 산다. 다만 원본은 이때 재료 한 장을
        /// 새 조각으로 덮어써서 장수가 늘지 않는다 — 코인과 재고만 쓰고 합체는 일어나지
        /// 않는다. 브라우저에서 원본을 직접 돌려 확인한 동작이고(★1 두 장 그대로, ★2 없음),
        /// 이식본도 같게 맞춰 두었다. 고칠지는 게임 규칙 변경이라 따로 정한다.
        /// </summary>
        [Test]
        public void AFullBenchBuysButTheOriginalLosesTheCopyItOverwrites()
        {
            var lg = Run();
            int slot = System.Array.FindIndex(lg.Shop, t => t != null);
            Assume.That(slot, Is.GreaterThanOrEqualTo(0));
            var type = lg.Shop[slot];
            var filler = lg.Pool.First(t => t != type);

            for (int i = 0; i < lg.Bench.Length; i++) Place(lg, LeagueWhere.Bench, i, filler);
            Place(lg, LeagueWhere.Bench, 0, type);
            Place(lg, LeagueWhere.Bench, 1, type);

            int gold = lg.Gold, stock = lg.Stock(type);
            Assert.IsTrue(lg.Buy(slot), "합쳐질 수 있으면 대기석이 꽉 차도 살 수 있어야 합니다.");
            Assert.AreEqual(gold - lg.CostOf(type), lg.Gold, "값은 치러야 합니다.");
            Assert.AreEqual(stock - 1, lg.Stock(type), "재고는 줄어야 합니다.");

            Assert.AreEqual(2, lg.CountOf(type, 1), "원본과 달리 장수가 늘었습니다.");
            Assert.AreEqual(0, lg.CountOf(type, 2), "원본은 이 경우 합체하지 않습니다.");
        }

        [Test]
        public void AFullBenchRefusesAPurchaseThatCannotMerge()
        {
            var lg = Run();
            int slot = System.Array.FindIndex(lg.Shop, t => t != null);
            Assume.That(slot, Is.GreaterThanOrEqualTo(0));
            var type = lg.Shop[slot];
            var filler = lg.Pool.First(t => t != type);

            for (int i = 0; i < lg.Bench.Length; i++) Place(lg, LeagueWhere.Bench, i, filler);
            int gold = lg.Gold;

            Assert.IsFalse(lg.Buy(slot), "대기석이 꽉 찼는데 샀습니다.");
            Assert.AreEqual(gold, lg.Gold, "실패한 구매가 코인을 썼습니다.");
        }

        // ------------------------------------------------------------------ 배치

        [Test]
        public void TheBoardTakesNoMorePiecesThanTheLevelAllows()
        {
            var lg = Run();
            var type = lg.Pool[0];

            for (int i = 0; i < lg.Level; i++) Place(lg, LeagueWhere.Board, i, type);
            Place(lg, LeagueWhere.Bench, 0, lg.Pool[^1]);

            var move = lg.Move(new LeagueSpot(LeagueWhere.Bench, 0),
                               new LeagueSpot(LeagueWhere.Board, lg.Level));
            Assert.AreEqual(LeagueMoveResult.Capped, move, "레벨 한도를 넘겨 올렸습니다.");
            Assert.AreEqual(lg.Level, lg.OnBoard);
        }

        [Test]
        public void SwappingWithAPieceAlreadyOnTheBoardIsAlwaysAllowed()
        {
            var lg = Run();
            var a = lg.Pool[0];
            var b = lg.Pool[^1];

            for (int i = 0; i < lg.Level; i++) Place(lg, LeagueWhere.Board, i, a);
            Place(lg, LeagueWhere.Bench, 0, b);

            var move = lg.Move(new LeagueSpot(LeagueWhere.Bench, 0), new LeagueSpot(LeagueWhere.Board, 0));
            Assert.AreEqual(LeagueMoveResult.Yes, move, "맞바꿈은 한도와 무관해야 합니다.");
            Assert.AreEqual(b, lg.Board[0].Type);
            Assert.AreEqual(a, lg.Bench[0].Type);
        }

        [Test]
        public void BuyingExperienceRaisesTheLevelOnTheTablesCurve()
        {
            var lg = Run();
            int level = lg.Level;
            while (lg.Gold >= _cfg.leagueXpCost && lg.Level == level) lg.BuyXp();
            Assert.Greater(lg.Level, level, "경험치를 샀는데 레벨이 오르지 않았습니다.");
        }

        // ------------------------------------------------------------------ 시너지

        [Test]
        public void SynergiesCountKindsNotCopies()
        {
            var lg = Run();
            var type = lg.Pool[0];
            var unit = _db.Units[type];

            Place(lg, LeagueWhere.Board, 0, type);
            Place(lg, LeagueWhere.Board, 1, type);
            Place(lg, LeagueWhere.Board, 2, type);

            var (list, _) = lg.Synergies();
            var fam = list.First(v => v.Group == "fam" + unit.Family);
            Assert.AreEqual(1, fam.Count, "같은 가전 세 장이 세 종류로 세어졌습니다.");
        }

        [Test]
        public void ASynergyTurnsOnOnceEnoughKindsStand()
        {
            var lg = Run();
            // 같은 계열 두 종류를 세운다 — 원본의 첫 단계가 2 다.
            var pair = _db.Units.Values
                .Where(u => _db.LeagueUnits.ContainsKey(u.Id))
                .GroupBy(u => u.Family)
                .First(g => g.Count() >= 2)
                .Take(2).Select(u => u.Id).ToList();

            Place(lg, LeagueWhere.Board, 0, pair[0]);
            Place(lg, LeagueWhere.Board, 1, pair[1]);

            var (list, eff) = lg.Synergies();
            string group = "fam" + _db.Units[pair[0]].Family;
            var view = list.First(v => v.Group == group);
            Assert.AreEqual(2, view.Count);
            Assert.IsNotNull(view.Active, $"{group} 이 두 종류에서 켜지지 않았습니다.");
            Assert.IsTrue(eff.ContainsKey(group), "효과 목록에 들어가지 않았습니다.");
        }

        // ------------------------------------------------------------------ 전투

        [Test]
        public void ABattleNeedsSomethingOnTheBoard()
        {
            var lg = Run();
            Assert.IsFalse(lg.StartBattle(), "판이 비었는데 전투가 시작됐습니다.");
            Assert.AreEqual(LeaguePhase.Prep, lg.Phase);
        }

        [Test]
        public void BothSidesLineUpWhenTheBattleStarts()
        {
            var lg = Run();
            Place(lg, LeagueWhere.Board, 10, lg.Pool[0]);
            Assert.IsTrue(lg.StartBattle());

            Assert.AreEqual(LeaguePhase.Battle, lg.Phase);
            Assert.AreEqual(1, lg.Units.Count(u => u.Side == 0), "우리 가전이 서지 않았습니다.");
            Assert.Greater(lg.Units.Count(u => u.Side == 1), 0, "먼지가 서지 않았습니다.");

            // 우리 자리는 아래 3줄, 먼지는 위 3줄이다.
            foreach (var u in lg.Units)
                if (u.Side == 0) Assert.GreaterOrEqual(u.Y, LeagueSim.OurRows, "가전이 먼지 쪽에 섰습니다.");
                else Assert.Less(u.Y, LeagueSim.OurRows, "먼지가 우리 쪽에 섰습니다.");
        }

        [Test]
        public void MeleeCannotReachFliers()
        {
            var lg = Run();
            // 1 사거리 가전 하나만 세우고, 공중 적이 나오는 라운드까지 이기며 올라간다.
            var melee = _db.LeagueUnits.Values.First(u => u.range < 1.5f).id;
            Place(lg, LeagueWhere.Board, 10, melee);

            LeagueUnit flier = null;
            for (int guard = 0; guard < _cfg.leagueRounds && flier == null; guard++)
            {
                Assert.IsTrue(lg.StartBattle(), "전투를 시작하지 못했습니다.");
                foreach (var u in lg.Units.Where(x => x.Side == 1).ToList())
                    if (!u.Fly) u.Alive = false;
                flier = lg.Units.FirstOrDefault(u => u.Side == 1 && u.Fly && u.Alive);
                if (flier != null) break;

                lg.Step(1f / 30f);      // 적이 모두 죽었으므로 이 틱에서 이긴다
                lg.EndRound();
            }
            Assume.That(flier, Is.Not.Null, "어느 라운드에도 공중 적이 없습니다.");

            float hp = flier.Hp;
            for (int i = 0; i < 120; i++) lg.Step(1f / 30f);
            Assert.AreEqual(hp, flier.Hp, 0.001f, "근접 가전이 공중 적을 때렸습니다.");
        }

        [Test]
        public void RunningOutOfTimeCountsAsALoss()
        {
            var lg = Run();
            Place(lg, LeagueWhere.Board, 10, lg.Pool[0]);
            lg.StartBattle();

            // 양쪽 다 못 죽이도록 체력을 크게 올려 두면 제한 시간까지 간다.
            foreach (var u in lg.Units) { u.MaxHp = u.Hp = 1e9f; u.Atk = 0f; }
            lg.RunBattleToEnd();

            Assert.IsTrue(lg.BattleOver, "제한 시간이 지나도 끝나지 않았습니다.");
            Assert.IsTrue(lg.BattleTimeout, "시간 초과로 기록되지 않았습니다.");
            Assert.IsFalse(lg.BattleWin == true, "시간 초과인데 이긴 것으로 처리됐습니다.");
        }

        [Test]
        public void LosingCostsTheBaseCleanlinessPlusTheDustLeftStanding()
        {
            var lg = Run();
            Place(lg, LeagueWhere.Board, 10, lg.Pool[0]);
            lg.StartBattle();

            // 우리 쪽을 지운다 — 남은 먼지가 전부 살아 있는 채로 진다.
            foreach (var u in lg.Units.Where(x => x.Side == 0)) u.Alive = false;
            int foes = lg.Units.Count(u => u.Side == 1 && u.Alive);
            Assume.That(foes, Is.GreaterThan(0));

            int life = lg.Life;
            lg.Step(1f / 30f);
            var result = lg.EndRound();

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Win);
            Assert.AreEqual(foes, result.Left, "살아남은 먼지 수가 틀립니다.");
            Assert.AreEqual(life - (_cfg.leagueLoseBase + foes), lg.Life,
                            "깎인 청결도가 기본값 + 남은 먼지와 다릅니다.");
        }

        [Test]
        public void WinningPaysIncomeInterestAndAStreakBonus()
        {
            var lg = Run();
            Place(lg, LeagueWhere.Board, 10, lg.Pool[0]);
            lg.StartBattle();
            foreach (var u in lg.Units.Where(x => x.Side == 1)) u.Alive = false;
            lg.Step(1f / 30f);

            int gold = lg.Gold;
            var result = lg.EndRound();

            Assert.IsTrue(result.Win);
            Assert.AreEqual(1, lg.Wins);
            Assert.AreEqual(1, lg.Streak);
            Assert.AreEqual(2, lg.Round, "다음 라운드로 넘어가지 않았습니다.");
            Assert.AreEqual(LeaguePhase.Prep, lg.Phase);

            int interest = System.Math.Min(_cfg.leagueInterestMax, gold / _cfg.leagueInterestPer);
            int expected = _cfg.leagueRoundGold + _cfg.leagueWinGold + interest;
            Assert.AreEqual(expected, result.Income, "수입이 기본 + 승리 + 이자와 다릅니다.");
            Assert.AreEqual(gold + expected, lg.Gold);
        }

        [Test]
        public void TrialAppliancesGetNoAccountGrowth()
        {
            var lg = Run();
            var trial = lg.Pool.FirstOrDefault(t => lg.IsTrial(t));
            Assume.That(trial, Is.Not.Null, "이 계정에는 체험 가전이 없습니다.");

            var st = lg.UnitStats(new LeaguePiece { Type = trial, Star = 1 });
            Assert.AreEqual(0f, st.Growth, 0.0001f, "체험 가전에 계정 성장이 붙었습니다.");
        }

        [Test]
        public void AccountGrowthIsCappedByTheTable()
        {
            var lg = Run();
            var owned = lg.Pool.FirstOrDefault(t => !lg.IsTrial(t));
            Assume.That(owned, Is.Not.Null);

            var st = lg.UnitStats(new LeaguePiece { Type = owned, Star = 1 });
            Assert.LessOrEqual(st.Growth, _cfg.leagueGrowthMax + 0.0001f,
                               "계정 성장이 상한을 넘었습니다.");
        }

        [Test]
        public void StarsMultiplyTheTablesStats()
        {
            var lg = Run();
            var type = lg.Pool[0];
            var one = lg.UnitStats(new LeaguePiece { Type = type, Star = 1 });
            var two = lg.UnitStats(new LeaguePiece { Type = type, Star = 2 });

            float ratio = _cfg.leagueStarMul[1] / _cfg.leagueStarMul[0];
            Assert.AreEqual(one.Hp * ratio, two.Hp, one.Hp * 0.001f, "★2 체력 배수가 표와 다릅니다.");
            Assert.AreEqual(one.Atk * ratio, two.Atk, one.Atk * 0.001f, "★2 공격력 배수가 표와 다릅니다.");
        }

        // ------------------------------------------------------------------ 한 판 전체

        [Test]
        public void AWholeRunEndsAtTheLastRoundOrWhenCleanlinessRunsOut()
        {
            var lg = Run();
            int guard = 0;
            while (lg.Phase != LeaguePhase.Over && guard++ < 50)
            {
                // 매 라운드 상점에서 살 수 있는 것을 사서 판에 올린다.
                for (int i = 0; i < lg.Shop.Length; i++) lg.Buy(i);
                for (int b = 0; b < lg.Bench.Length && lg.OnBoard < lg.Level; b++)
                {
                    if (lg.Bench[b] == null) continue;
                    int cell = System.Array.FindIndex(lg.Board, p => p == null);
                    if (cell < 0) break;
                    lg.Move(new LeagueSpot(LeagueWhere.Bench, b), new LeagueSpot(LeagueWhere.Board, cell));
                }

                if (!lg.StartBattle()) break;
                lg.RunBattleToEnd();
                lg.EndRound();
            }

            Assert.AreEqual(LeaguePhase.Over, lg.Phase, "판이 끝나지 않았습니다.");
            Assert.IsTrue(lg.Life <= 0 || lg.Round >= _cfg.leagueRounds,
                          $"끝났는데 청결도 {lg.Life}, 라운드 {lg.Round} 입니다.");
            Assert.AreEqual(lg.Round, lg.Wins + lg.Losses, "승패 합이 치른 라운드와 다릅니다.");
        }

        [Test]
        public void QuittingInTheFirstRoundDoesNotSpendARewardRun()
        {
            var rng = new SystemRandom(7);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);
            data.deck = new List<string>(data.owned.Keys);

            var lg = new LeagueSim(_db, profile, 1, rng);
            int left = profile.LeagueRunsLeft();

            var finish = lg.Finish(quit: true);
            Assert.IsFalse(finish.Counted, "1라운드에 그만뒀는데 보상 판을 썼습니다.");
            Assert.AreEqual(left, profile.LeagueRunsLeft());
        }

        [Test]
        public void ClearingATierOpensTheNextOneOnceOnly()
        {
            var rng = new SystemRandom(11);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);
            data.deck = new List<string>(data.owned.Keys);

            var row = _db.LeagueTiers.First();
            long crystal = profile.Crystal;

            var first = profile.FinishLeagueRun(row.tier, row, wins: 5, clear: true, counted: false, quit: false);
            Assert.IsTrue(profile.LeagueCleared(row.tier), "완주가 기록되지 않았습니다.");
            Assert.AreEqual(2, profile.LeagueTierMax, "다음 티어가 열리지 않았습니다.");
            Assert.AreEqual(crystal + row.firstCrystal, profile.Crystal, "첫 완주 크리스탈이 다릅니다.");

            long after = profile.Crystal;
            var second = profile.FinishLeagueRun(row.tier, row, wins: 5, clear: true, counted: false, quit: false);
            Assert.AreEqual(after, profile.Crystal, "첫 완주 크리스탈을 두 번 줬습니다.");
            Assert.IsFalse(second.Got.Any(g => g.Contains("첫 완주")));
        }

        [Test]
        public void TheDailyRewardRunsRunOut()
        {
            var rng = new SystemRandom(13);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);
            var row = _db.LeagueTiers.First();

            for (int i = 0; i < _cfg.leagueDailyRewards; i++)
            {
                var got = profile.FinishLeagueRun(row.tier, row, wins: 1, clear: false, counted: true, quit: false);
                Assert.IsTrue(got.Counted, $"{i + 1}번째 판이 보상 판을 쓰지 않았습니다.");
            }
            Assert.AreEqual(0, profile.LeagueRunsLeft());

            long coin = profile.Coin;
            var extra = profile.FinishLeagueRun(row.tier, row, wins: 1, clear: false, counted: true, quit: false);
            Assert.IsFalse(extra.Counted, "판 수를 다 썼는데 또 셌습니다.");
            Assert.AreEqual(coin, profile.Coin, "판 수를 다 쓰고도 코인을 줬습니다.");
        }

        /// <summary>
        /// 합체는 사거나 놓을 때만 일어난다. 검사에서 말을 직접 꽂았을 때 그 자리를
        /// 대신해 줄 손잡이 — 비공개 메서드를 부르는 대신 한 장을 뺐다 다시 꽂는다.
        /// </summary>
        static void InvokeMerge(LeagueSim lg, string type)
        {
            var method = typeof(LeagueSim).GetMethod("Merge",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "LeagueSim.Merge 를 찾지 못했습니다.");
            method.Invoke(lg, new object[] { type });
        }
    }
}
