// 협동 청소 (10.18-h ~ 10.19-d) 회귀 검사.
//
// 원본 index.html 의 coop* 함수와 같은 답이 나오는지 본다. 고리판은 출구가 없고
// 웨이브가 끝없이 이어지므로, 본편 디펜스 검사로는 잡히지 않는 규칙들만 모았다.
using System.Collections.Generic;
using DustZero.Core;
using DustZero.Core.Coop;
using DustZero.Core.Defense;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class CoopTests
    {
        GameDatabase _db;
        ConfigRow _cfg;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _cfg = _db.Config;
        }

        /// <summary>A run with a full six-appliance deck, so every tile kind is summonable.</summary>
        CoopSim Run(string partner = "rush", float roll = 0.5f)
        {
            var rng = new FixedRandom(roll);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);

            // 시작 보상만으로는 4종뿐이라 가구 칸 가전이 모자랄 수 있다. 표에서 채운다.
            var deck = new List<string>(data.owned.Keys);
            foreach (var id in _db.Units.Keys)
            {
                if (deck.Count >= _cfg.deckMax) break;
                if (!deck.Contains(id)) deck.Add(id);
            }
            data.deck = deck;

            return new CoopSim(_db, _db.CoopPartner(partner), deck, profile, rng,
                               new PresentationBus(), 5, 10, 1);
        }

        /// <summary>Tops a side up so a test can act without waiting for the ring to pay out.</summary>
        static void Fund(CoopSim sim, CoopSide side = CoopSide.Player) => sim.GrantPower(side, 60f);

        static (int Col, int Row) FirstFree(CoopSim sim, TileKind tile)
        {
            var cells = sim.FreeCells(CoopSide.Player, tile);
            return cells.Count > 0 ? cells[0] : (-1, -1);
        }

        static void Advance(CoopSim sim, float seconds)
        {
            const float Dt = 1f / 60f;
            for (int i = 0; i < seconds * 60 && !sim.Finished; i++) sim.Tick(Dt);
        }

        // ------------------------------------------------------------------ 판

        [Test]
        public void TheRingHasNoExitSoNothingEverLeaks()
        {
            var sim = Run();
            Advance(sim, 300f);

            Assert.Greater(sim.Battle.Killed, 0, "5분 동안 한 마리도 못 잡았습니다.");
            Assert.AreEqual(0, sim.Battle.Leaked,
                            "고리판에는 골이 없습니다. 적이 샜다면 이동이 길을 벗어난 것입니다.");
        }

        [Test]
        public void TheWindowCellIsNotAPlacementSpot()
        {
            var sim = Run();
            int c = GameDatabase.CoopEntryCell[0], r = GameDatabase.CoopEntryCell[1];
            Assert.IsNull(sim.TileAt(c, r), "창문 칸은 적이 들어오는 입구라 비워 둬야 합니다.");

            foreach (var cell in sim.FreeCells(CoopSide.Partner, TileKind.Ground))
                Assert.IsFalse(cell.Col == c && cell.Row == r, "빈칸 목록에 창문이 들어갔습니다.");
        }

        [Test]
        public void EachSideOwnsItsOwnHalfOfTheBoard()
        {
            // 위 5줄이 파트너(0~4), 아래가 나(5~9).
            Assert.IsTrue(CoopSim.OwnsRow(CoopSide.Partner, 0));
            Assert.IsTrue(CoopSim.OwnsRow(CoopSide.Partner, GameDatabase.CoopSplit));
            Assert.IsFalse(CoopSim.OwnsRow(CoopSide.Partner, GameDatabase.CoopSplit + 1));
            Assert.IsTrue(CoopSim.OwnsRow(CoopSide.Player, GameDatabase.CoopSplit + 1));
            Assert.IsFalse(CoopSim.OwnsRow(CoopSide.Player, GameDatabase.CoopSplit));

            var sim = Run();
            foreach (var cell in sim.FreeCells(CoopSide.Player, TileKind.Ground))
                Assert.Greater(cell.Row, GameDatabase.CoopSplit, "내 빈칸에 파트너 줄이 섞였습니다.");
        }

        // ------------------------------------------------------------------ 소환

        [Test]
        public void SummonCostClimbsWithEachOneSummoned()
        {
            var sim = Run();
            Assert.AreEqual(_cfg.coopSum0, sim.SummonCost(CoopSide.Player), 0.001f);

            var cell = sim.FreeCells(CoopSide.Player, TileKind.Ground)[0];
            Assert.IsNotNull(sim.Summon(CoopSide.Player, cell), "첫 소환이 실패했습니다.");

            Assert.AreEqual(_cfg.coopSum0 + _cfg.coopSumStep,
                            sim.SummonCost(CoopSide.Player), 0.001f,
                            "소환값이 coopSumStep 만큼 올라야 합니다.");
        }

        [Test]
        public void ASummonedApplianceStartsAtOneStar()
        {
            var sim = Run();
            var u = sim.Summon(CoopSide.Player, sim.FreeCells(CoopSide.Player, TileKind.Ground)[0]);
            Assert.AreEqual(1, sim.StarOf(u));
            Assert.AreEqual(CoopSide.Player, sim.SideOf(u));
        }

        [Test]
        public void TheRingTakesOnlyCoopLineMaxMeleePerSide()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            Assert.GreaterOrEqual(cells.Count, _cfg.coopLineMax + 1,
                                  "이 검사를 하려면 고리 칸이 한도보다 많아야 합니다.");

            for (int i = 0; i < _cfg.coopLineMax; i++)
            {
                Fund(sim);
                Assert.IsNotNull(sim.Summon(CoopSide.Player, cells[i]), $"{i + 1}번째 근접 소환 실패");
            }

            Assert.IsFalse(sim.RingHasRoom(CoopSide.Player));
            Fund(sim);
            Assert.IsNull(sim.Summon(CoopSide.Player, cells[_cfg.coopLineMax]),
                          $"고리 위에는 {_cfg.coopLineMax}대까지만 설 수 있어야 합니다.");
        }

        [Test]
        public void ASummonYouCannotAffordDoesNothing()
        {
            var sim = Run();
            // 시작 전력으로 살 수 있는 만큼 사 버리면 다음 소환은 거절돼야 한다.
            while (sim.Summon(CoopSide.Player, FirstFree(sim, TileKind.Furniture)) != null) { }

            Assert.Less(sim.PowerOf(CoopSide.Player), sim.SummonCost(CoopSide.Player),
                        "전력이 소환값보다 적어야 이 검사가 뜻이 있습니다.");
            int before = sim.Battle.Units.Count;
            Assert.IsNull(sim.Summon(CoopSide.Player, FirstFree(sim, TileKind.Furniture)));
            Assert.AreEqual(before, sim.Battle.Units.Count, "전력이 없는데 가전이 늘었습니다.");
        }

        // ------------------------------------------------------------------ 합치기

        [Test]
        public void MergingTwoOfTheSameStarRaisesItByOne()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            Fund(sim);
            var a = sim.Summon(CoopSide.Player, cells[0]);
            Fund(sim);
            var b = sim.Summon(CoopSide.Player, cells[1]);

            Assert.IsTrue(sim.CanMerge(a, b), "★1 두 대는 합칠 수 있어야 합니다.");
            int units = sim.Battle.Units.Count;

            var merged = sim.Merge(a, b);
            Assert.IsNotNull(merged);
            Assert.AreEqual(2, sim.StarOf(merged), "합치면 ★이 하나 올라야 합니다.");
            Assert.AreEqual(units - 1, sim.Battle.Units.Count, "합쳤는데 가전 수가 안 줄었습니다.");
        }

        [Test]
        public void MergingRaisesAttackByTheStarTable()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            Fund(sim);
            var a = sim.Summon(CoopSide.Player, cells[0]);
            Fund(sim);
            var b = sim.Summon(CoopSide.Player, cells[1]);
            if (a.Type != b.Type) Assert.Ignore("두 소환이 다른 가전이라 배율을 직접 비교할 수 없습니다.");

            float before = b.Atk;
            var merged = sim.Merge(a, b);

            float expected = before / _cfg.coopStarMul[0] * _cfg.coopStarMul[1];
            Assert.AreEqual(expected, merged.Atk, expected * 0.001f,
                            "★2 공격력이 coopStarMul[1] 배여야 합니다.");
        }

        [Test]
        public void SidesNeverMergeIntoEachOther()
        {
            var sim = Run();
            Fund(sim);
            var mine = sim.Summon(CoopSide.Player, sim.FreeCells(CoopSide.Player, TileKind.Ground)[0]);
            var theirs = sim.Summon(CoopSide.Partner, sim.FreeCells(CoopSide.Partner, TileKind.Ground)[0]);

            Assert.IsNotNull(theirs, "파트너 소환이 실패했습니다.");
            Assert.IsFalse(sim.CanMerge(mine, theirs), "내 가전과 파트너 가전은 합쳐지면 안 됩니다.");
        }

        /// <summary>
        /// ★N 한 대를 만든다. ★1 두 대로 ★2, ★2 두 대로 ★3 … 식이라 한 번에 필요한
        /// 빈칸은 두어 칸뿐이다 — 합칠 때마다 자리가 하나 비기 때문이다.
        /// </summary>
        static BattleUnit BuildToStar(CoopSim sim, int target)
        {
            Fund(sim);
            var acc = sim.Summon(CoopSide.Player, FirstFree(sim, TileKind.Furniture));
            if (acc == null) return null;

            for (int star = 1; star < target; star++)
            {
                var mate = BuildToStar(sim, star);
                if (mate == null) return null;
                acc = sim.Merge(mate, acc);
                if (acc == null) return null;
            }
            return acc;
        }

        [Test]
        public void MergingStopsAtTheStarCeiling()
        {
            var sim = Run();
            var top = BuildToStar(sim, _cfg.coopStarMax);
            if (top == null) Assert.Ignore("★ 천장까지 올릴 빈칸이 모자랍니다.");

            Assert.AreEqual(_cfg.coopStarMax, sim.StarOf(top),
                            $"★{_cfg.coopStarMax} 까지 올라가야 합니다.");

            Fund(sim);
            var mate = sim.Summon(CoopSide.Player, FirstFree(sim, TileKind.Furniture));
            if (mate == null) Assert.Ignore("짝을 세울 빈칸이 없습니다.");

            Assert.IsFalse(sim.CanMerge(top, mate), "★ 천장을 넘겨 합쳐지면 안 됩니다.");
            Assert.IsNull(sim.Merge(top, mate));
        }

        // ------------------------------------------------------------------ 옮기기 · 강화

        [Test]
        public void AMovedApplianceCannotMoveAgainUntilItsCooldownPasses()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            Fund(sim);
            var u = sim.Summon(CoopSide.Player, cells[0]);

            var dest = sim.FreeCells(CoopSide.Player, TileKind.Ground)[0];
            Assert.IsTrue(sim.Move(u, dest.Col, dest.Row), "첫 이동이 실패했습니다.");
            Assert.AreEqual(_cfg.coopMoveCd, sim.MoveCooldownOf(u), 0.001f);

            var again = sim.FreeCells(CoopSide.Player, TileKind.Ground)[0];
            Assert.IsFalse(sim.Move(u, again.Col, again.Row),
                           $"옮긴 가전은 {_cfg.coopMoveCd}초 동안 다시 못 옮겨야 합니다.");

            Advance(sim, _cfg.coopMoveCd + 1f);
            Assert.AreEqual(0f, sim.MoveCooldownOf(u), 0.001f, "쿨다운이 안 풀렸습니다.");
        }

        [Test]
        public void MeleeCannotBeMovedOntoFurnitureAndViceVersa()
        {
            var sim = Run();
            Fund(sim);
            var melee = sim.Summon(CoopSide.Player, sim.FreeCells(CoopSide.Player, TileKind.Ground)[0]);
            var furniture = sim.FreeCells(CoopSide.Player, TileKind.Furniture)[0];

            Assert.IsFalse(sim.Move(melee, furniture.Col, furniture.Row),
                           "근접 가전은 고리 칸으로만 옮길 수 있어야 합니다.");
        }

        [Test]
        public void MovingOntoThePartnersHalfIsRefused()
        {
            var sim = Run();
            Fund(sim);
            var u = sim.Summon(CoopSide.Player, sim.FreeCells(CoopSide.Player, TileKind.Ground)[0]);
            var theirs = sim.FreeCells(CoopSide.Partner, TileKind.Ground)[0];

            Assert.IsFalse(sim.Move(u, theirs.Col, theirs.Row), "파트너 자리로 넘어가면 안 됩니다.");
        }

        [Test]
        public void UpgradingRaisesTheWholeTypeAndRunsOutAtTheLastLevel()
        {
            var sim = Run();
            var type = sim.Battle.Deck[0];

            Assert.AreEqual(1f, sim.UpgradeMultiplier(CoopSide.Player, type), 0.001f);

            for (int lv = 0; lv < _cfg.coopUpCost.Length; lv++)
            {
                Assert.AreEqual(_cfg.coopUpCost[lv], sim.UpgradeCost(CoopSide.Player, type).Value, 0.001f,
                                $"{lv}단계 강화 비용이 표와 다릅니다.");
                Fund(sim);
                Assert.IsTrue(sim.Upgrade(CoopSide.Player, type), $"{lv}단계 강화가 실패했습니다.");
            }

            Assert.IsNull(sim.UpgradeCost(CoopSide.Player, type), "마지막 단계 뒤에는 더 올릴 수 없어야 합니다.");
            Assert.IsFalse(sim.Upgrade(CoopSide.Player, type));
            Assert.AreEqual(1f + _cfg.coopUpStep * _cfg.coopUpCost.Length,
                            sim.UpgradeMultiplier(CoopSide.Player, type), 0.001f);
        }

        [Test]
        public void EachSideUpgradesSeparately()
        {
            var sim = Run();
            var type = sim.Battle.Deck[0];
            Fund(sim);
            sim.Upgrade(CoopSide.Player, type);

            Assert.AreEqual(1, sim.UpgradeLevel(CoopSide.Player, type));
            Assert.AreEqual(0, sim.UpgradeLevel(CoopSide.Partner, type),
                            "내 강화가 파트너에게 넘어갔습니다.");
        }

        // ------------------------------------------------------------------ 전력과 보상

        [Test]
        public void TheTwoSidesKeepSeparatePowerPools()
        {
            var sim = Run();
            Fund(sim);
            float mine = sim.PowerOf(CoopSide.Player), theirs = sim.PartnerPower;
            float cost = sim.SummonCost(CoopSide.Player);

            sim.Summon(CoopSide.Player, FirstFree(sim, TileKind.Ground));

            Assert.AreEqual(theirs, sim.PartnerPower, 0.001f,
                            "내가 소환했는데 파트너 전력이 움직였습니다.");
            Assert.AreEqual(mine - cost, sim.PowerOf(CoopSide.Player), 0.001f,
                            "내 전력에서 소환값만큼만 빠져야 합니다.");
        }

        [Test]
        public void SurvivingAWavePaysTheDailyLogAndCoins()
        {
            var sim = Run();
            Advance(sim, 300f);

            Assert.Greater(sim.WavesCleared, 0, "5분을 버텼는데 넘긴 웨이브가 없습니다.");
            Assert.AreEqual(sim.WavesCleared * _cfg.poolCoopWave, sim.PoolPoints,
                            "일지 점수가 웨이브당 poolCoopWave 여야 합니다.");
            Assert.AreEqual(sim.WavesCleared * _cfg.coopCoinPerWave, sim.Coin,
                            "코인이 웨이브당 coopCoinPerWave 여야 합니다.");
        }

        [Test]
        public void TheRunEndsWhenTheRingFillsUp()
        {
            // 내가 한 대도 안 놓으면 파트너 혼자로는 못 버틴다. 체력이 웨이브마다 1.15배씩
            // 오르고 마릿수도 늘기 때문이다. 넉넉히 돌려 끝나는 것을 본다.
            var sim = Run();
            Advance(sim, 60f * 30f);

            Assert.IsTrue(sim.Finished, "30분이 지나도 안 끝났습니다. 고리가 차지 않습니다.");
            Assert.IsTrue(sim.Lost, "고리가 가득 차면 지는 것이어야 합니다.");
            Assert.GreaterOrEqual(sim.PeakRingLoad, _cfg.coopLoopMax);
        }

        [Test]
        public void ABossCrowdsTheRingAsMuchAsFiveDust()
        {
            var sim = Run();
            Advance(sim, 300f);

            int plain = 0, bosses = 0;
            foreach (var e in sim.Battle.Enemies)
            {
                if (e.Dead) continue;
                if (e.Def.Boss) bosses++; else plain++;
            }
            Assert.AreEqual(plain + bosses * CoopSim.BossRingWeight, sim.RingLoad,
                            "보스는 먼지 5마리로 세어야 합니다.");
        }

        // ------------------------------------------------------------------ 파트너

        [Test]
        public void ThePartnerPlaysOnItsOwnHalfOnly()
        {
            var sim = Run();
            Advance(sim, 180f);

            int theirs = 0;
            foreach (var u in sim.Battle.Units)
            {
                if (sim.SideOf(u) != CoopSide.Partner) continue;
                theirs++;
                Assert.LessOrEqual(u.Row, GameDatabase.CoopSplit,
                                   "파트너가 내 자리에 가전을 놓았습니다.");
            }
            Assert.Greater(theirs, 0, "3분 동안 파트너가 한 대도 안 놓았습니다.");
        }

        [Test]
        public void TheGreedyPartnerBuildsFewerButStrongerThanRush()
        {
            var rush = Run("rush");
            var greedy = Run("greedy");
            Advance(rush, 240f);
            Advance(greedy, 240f);

            int rushStars = 0, greedyStars = 0, rushN = 0, greedyN = 0;
            foreach (var u in rush.Battle.Units)
                if (rush.SideOf(u) == CoopSide.Partner) { rushN++; rushStars += rush.StarOf(u); }
            foreach (var u in greedy.Battle.Units)
                if (greedy.SideOf(u) == CoopSide.Partner) { greedyN++; greedyStars += greedy.StarOf(u); }

            Assert.Greater(rushN, 0, "rush 파트너가 아무것도 안 놓았습니다.");
            Assert.Greater(greedyN, 0, "greedy 파트너가 아무것도 안 놓았습니다.");

            float rushAvg = rushStars / (float)rushN, greedyAvg = greedyStars / (float)greedyN;
            Assert.Greater(greedyAvg, rushAvg,
                           $"greedy 가 합치기형이므로 ★ 평균이 높아야 합니다 (greedy {greedyAvg:0.00} vs rush {rushAvg:0.00}).");
        }

        [Test]
        public void ThePartnerNeverSpendsMyPower()
        {
            var sim = Run();
            Fund(sim);
            float mine = sim.Battle.Power;

            // 봇이 여러 번 생각할 만큼만 돌린다. 웨이브 보너스가 끼지 않게 짧게.
            const float Dt = 1f / 60f;
            for (int i = 0; i < 120; i++) sim.Tick(Dt);

            Assert.GreaterOrEqual(sim.Battle.Power, mine,
                                  "파트너가 움직이는 동안 내 전력이 줄었습니다.");
        }

        // ------------------------------------------------------------------ 입력

        [Test]
        public void TappingAnEmptyCellPicksASummonSpotAndTappingItAgainClearsIt()
        {
            var sim = Run();
            var cell = sim.FreeCells(CoopSide.Player, TileKind.Ground)[0];

            Assert.AreEqual(CoopTapResult.SpotPicked, sim.Tap(cell.Col, cell.Row));
            Assert.IsTrue(sim.Spot.HasValue);
            Assert.AreEqual(CoopTapResult.SpotCleared, sim.Tap(cell.Col, cell.Row));
            Assert.IsFalse(sim.Spot.HasValue);
        }

        [Test]
        public void TappingThePartnersApplianceSaysSoInsteadOfSelectingIt()
        {
            var sim = Run();
            var u = sim.Summon(CoopSide.Partner, sim.FreeCells(CoopSide.Partner, TileKind.Ground)[0]);
            Assert.IsNotNull(u);

            Assert.AreEqual(CoopTapResult.PartnersUnit, sim.Tap(u.Col, u.Row));
            Assert.IsNull(sim.Selected, "파트너 가전이 선택되면 안 됩니다.");
        }

        [Test]
        public void TappingTwoOfMyOwnMergesThem()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            Fund(sim);
            var a = sim.Summon(CoopSide.Player, cells[0]);
            Fund(sim);
            var b = sim.Summon(CoopSide.Player, cells[1]);

            Assert.AreEqual(CoopTapResult.Selected, sim.Tap(a.Col, a.Row));
            Assert.AreEqual(CoopTapResult.Merged, sim.Tap(b.Col, b.Row));
            Assert.IsNull(sim.Selected);
        }

        [Test]
        public void TheRingFullTapTellsYouWhyInsteadOfPickingASpot()
        {
            var sim = Run();
            var cells = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            for (int i = 0; i < _cfg.coopLineMax; i++)
            {
                Fund(sim);
                sim.Summon(CoopSide.Player, cells[i]);
            }

            var left = sim.FreeCells(CoopSide.Player, TileKind.Ground);
            if (left.Count == 0) Assert.Ignore("고리 칸이 전부 차서 눌러 볼 빈칸이 없습니다.");
            Assert.AreEqual(CoopTapResult.RingFull, sim.Tap(left[0].Col, left[0].Row));
        }

        // ------------------------------------------------------------------ 세이브

        PlayerProfile Account(int clearedStages)
        {
            var data = new SaveData { best = clearedStages };
            var p = new PlayerProfile(_db, data, new FixedRandom(0.5f));
            p.Grant(_cfg.startRewardGroup);
            return p;
        }

        /// <summary>
        /// 문턱은 content_unlock 이 정한다. 예전에는 "1-5" 가 테스트에 박혀 있었는데
        /// 10.21-g 가 협동을 1-5 → 3-5 로 옮기자 그대로 깨졌다. 표가 바뀌면 같이
        /// 따라가도록 **표에서 읽어** 그 앞뒤를 본다 — 수치는 표에서만 온다.
        /// </summary>
        [Test]
        public void CoopIsLockedUntilItsUnlockStageIsCleared()
        {
            int gate = UnlockOrder("coop");
            Assert.Greater(gate, 1, "협동 해금 조건을 표에서 읽지 못했습니다.");

            Assert.IsFalse(Account(gate - 1).CoopUnlocked, "문턱 직전에는 잠겨 있어야 합니다.");
            Assert.IsTrue(Account(gate).CoopUnlocked, "문턱을 깨면 열려야 합니다.");
            Assert.IsNotEmpty(Account(gate - 1).CoopLockMessage, "잠금 안내문이 있어야 합니다.");
        }

        /// <summary>content_unlock 의 조건이 가리키는 스테이지의 order.</summary>
        int UnlockOrder(string content)
        {
            var u = _db.Unlocks[content];
            var c = _db.Conditions[u.cond];
            return _db.StageById(int.Parse(c.p1.AsString())).Order;
        }

        [Test]
        public void AnOlderSaveGainsAnEmptyCoopRecord()
        {
            var old = new SaveData { version = 6, coop = null };
            SaveMigrator.Migrate(old);

            Assert.AreEqual(SaveData.CurrentVersion, old.version);
            Assert.IsNotNull(old.coop, "v7 로 올리면 협동 기록이 생겨야 합니다.");
            Assert.AreEqual(0, old.coop.best.Count, "예전 파일에 최고 기록이 있을 리 없습니다.");
            Assert.AreEqual("rush", old.coop.last, "기본 파트너가 비어 있으면 안 됩니다.");
        }

        [Test]
        public void FinishingARunRecordsTheBestWavePerPartner()
        {
            var p = Account(5);

            var first = p.FinishCoop("rush", 14, 0, 0);
            Assert.IsTrue(first.NewRecord);
            Assert.AreEqual(14, p.CoopBest("rush"));
            Assert.AreEqual(0, p.CoopBest("greedy"), "최고 기록은 파트너마다 따로여야 합니다.");

            var worse = p.FinishCoop("rush", 9, 0, 0);
            Assert.IsFalse(worse.NewRecord, "기록보다 못한 판은 신기록이 아닙니다.");
            Assert.AreEqual(14, p.CoopBest("rush"), "기록이 낮은 값으로 덮였습니다.");
        }

        [Test]
        public void FinishingARunPaysCoinsAndCappedPassExperience()
        {
            var p = Account(5);
            double coin = p.Data.Item(PlayerProfile.CoinItemId);
            int xp = p.Data.pass.xp;

            var r = p.FinishCoop("rush", 14, 1050, 42);

            Assert.AreEqual(coin + 1050, p.Data.Item(PlayerProfile.CoinItemId), 0.001,
                            "코인이 들어오지 않았습니다.");
            Assert.AreEqual(14 * _cfg.coopPassXpPerWave, r.PassXp,
                            "패스 경험치가 웨이브당 coopPassXpPerWave 여야 합니다.");
            Assert.AreEqual(xp + r.PassXp, p.Data.pass.xp);

            var big = Account(5).FinishCoop("rush", 999, 0, 0);
            Assert.AreEqual(_cfg.coopPassXpMax, big.PassXp,
                            $"패스 경험치는 {_cfg.coopPassXpMax} 에서 잘려야 합니다.");
        }

        [Test]
        public void TheDailyLogIsNotPaidTwiceAtSettlement()
        {
            // 일지 점수는 판 안에서 웨이브마다 이미 들어간다. 정산이 또 주면 두 배가 된다.
            var p = Account(5);
            int before = p.Pool.pts;

            p.FinishCoop("rush", 14, 0, 42);

            Assert.AreEqual(before, p.Pool.pts,
                            "정산에서 일지 점수를 또 주면 안 됩니다. 판 안에서 이미 적립됩니다.");
        }

        [Test]
        public void ThePartnerIsRememberedForNextTime()
        {
            var p = Account(5);
            Assert.AreEqual("rush", p.Coop.last, "기본값이 있어야 합니다.");

            p.SetCoopPartner("greedy");
            Assert.AreEqual("greedy", p.Coop.last);
        }

        [Test]
        public void ThePartnerBorrowsTheAverageOfMyBestSix()
        {
            var p = Account(5);
            foreach (var id in new List<string>(p.Data.owned.Keys))
            {
                p.Data.owned[id].lv = 11;
                p.Data.owned[id].star = 2;
            }

            var (level, star) = p.CoopPartnerStrength();
            Assert.AreEqual(11, level, "모두 Lv.11 이면 평균도 11 이어야 합니다.");
            Assert.AreEqual(2, star);
        }

    }
}
