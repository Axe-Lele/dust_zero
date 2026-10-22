// In-battle boost, multitap and late-wave pressure — the 10.15-h battle additions.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class BoostTests
    {
        GameDatabase _db;
        StageDef _stage;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _stage = _db.Stages[0];
        }

        BattleSim NewSim(params string[] deck)
            => new(_db, _stage, deck.Length > 0 ? deck : new[] { "vac", "ac", "gen", "fan" },
                   new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        static (int c, int r) FirstPlaceable(BattleSim sim, string type)
        {
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                    if (sim.CanPlace(type, c, r)) return (c, r);
            Assert.Fail($"{type} 를 놓을 칸이 없습니다.");
            return (0, 0);
        }


        /// <summary>
        /// A stage built on a real map whose only spawn is far in the future. Power regenerates
        /// without the battle ending, which is what the multitap and boost sinks need: in a
        /// shipped stage the run is over long before 55 power has accumulated.
        /// </summary>
        StageDef LongRunStage(string enemyId = "dust", float waveHpMul = 1f, float spawnAt = 10000f)
        {
            var src = _db.Stages[0];
            return new StageDef
            {
                Id = src.Id, Order = src.Order, Chapter = src.Chapter, No = src.No,
                MapId = src.MapId, Name = src.Name, Floor = src.Floor,
                SpawnNames = src.SpawnNames, GoalName = src.GoalName, Gimmick = src.Gimmick,
                Desc = src.Desc, Tiles = src.Tiles, Paths = src.Paths,
                NoDeployFromEntrance = src.NoDeployFromEntrance,
                NoDeployBeforeGoal = src.NoDeployBeforeGoal,
                HpMul = src.HpMul, Boss = false,
                EnemyIds = new[] { enemyId },
                UnlockCond = src.UnlockCond, FirstRewardGroup = src.FirstRewardGroup,
                IdleCoin = src.IdleCoin, IdlePart = src.IdlePart, IdleCrystal = src.IdleCrystal,
                StartPower = src.StartPower,
                GoalX = src.GoalX, GoalY = src.GoalY,
                Schedule = new[] { new SpawnEvent(spawnAt, enemyId, 0, waveHpMul) },
            };
        }

        BattleSim LongSim(params string[] deck)
            => new(_db, LongRunStage(), deck.Length > 0 ? deck : new[] { "vac", "ac", "gen", "fan" },
                   new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        /// <summary>Gives the sim enough power to act without waiting out the regen.</summary>
        static void Charge(BattleSim sim, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.1f) sim.Tick(0.1f);
        }

        // ------------------------------------------------------------------ boost

        [Test]
        public void Boost_RaisesEveryStatTheTableLists()
        {
            var sim = LongSim();
            var (c, r) = FirstPlaceable(sim, "vac");
            var u = sim.Deploy("vac", c, r);

            float atk0 = u.Atk, heat0 = u.MaxHeat, range0 = u.Range, sp0 = u.SkillChargeTime;
            int block0 = u.Block;

            var row = sim.NextBoost(u);
            Assert.IsNotNull(row, "2단 강화 행이 있어야 합니다.");

            Charge(sim, 60f);
            Assert.IsTrue(sim.CanBoost(u), "전력을 모았는데 강화할 수 없습니다.");
            Assert.IsTrue(sim.Boost(u), "강화가 실패했습니다.");

            Assert.AreEqual(2, u.Tier);
            Assert.AreEqual(atk0 * row.statMul, u.Atk, 1e-3f, "공격력이 statMul 만큼 오르지 않았습니다.");
            Assert.AreEqual(heat0 * row.statMul, u.MaxHeat, 1e-3f, "과열 한도가 오르지 않았습니다.");
            Assert.AreEqual(range0 + row.rangeAdd, u.Range, 1e-3f, "사거리 보너스가 반영되지 않았습니다.");
            Assert.AreEqual(block0 + row.blockAdd, u.Block, "저지 수 보너스가 반영되지 않았습니다.");
            Assert.AreEqual(sp0 * row.spMul, u.SkillChargeTime, 1e-3f, "스킬 충전 시간이 바뀌지 않았습니다.");
        }

        [Test]
        public void Boost_KeepsHeatRatioSoItNeverOverheatsTheUnit()
        {
            var sim = LongSim("robot");
            var (c, r) = FirstPlaceable(sim, "robot");
            var u = sim.Deploy("robot", c, r);

            Charge(sim, 60f);
            // Put the bar at a known fraction, then upgrade.
            u.Heat = u.MaxHeat * 0.8f;
            float ratioBefore = u.HeatRatio;

            Assert.IsTrue(sim.Boost(u));

            Assert.AreEqual(ratioBefore, u.HeatRatio, 1e-3f,
                "강화 후 과열 비율이 유지되지 않았습니다.");
            Assert.IsFalse(u.Overheated, "강화가 과열을 유발했습니다.");
        }

        [Test]
        public void Boost_CostsPowerAndStopsAtMaxTier()
        {
            var sim = LongSim();
            var (c, r) = FirstPlaceable(sim, "vac");
            var u = sim.Deploy("vac", c, r);

            Charge(sim, 120f);
            float before = sim.Power;
            float cost = sim.BoostCost(u).Value;
            Assert.IsTrue(sim.Boost(u));
            Assert.AreEqual(before - cost, sim.Power, 1e-3f, "강화 비용이 전력에서 빠지지 않았습니다.");

            Charge(sim, 120f);
            Assert.IsTrue(sim.Boost(u), "3단 강화가 되어야 합니다.");
            Assert.AreEqual(3, u.Tier);

            Assert.IsNull(sim.NextBoost(u), "3단이 최대여야 합니다.");
            Assert.IsNull(sim.BoostCost(u));
            Assert.IsFalse(sim.Boost(u), "최대 단계에서 더 강화되었습니다.");
        }

        [Test]
        public void Boost_IsRefusedWithoutEnoughPower()
        {
            var sim = LongSim();
            var (c, r) = FirstPlaceable(sim, "vac");
            var u = sim.Deploy("vac", c, r);

            // Straight after deploying, the remaining power is below the boost cost.
            Assert.Less(sim.Power, sim.BoostCost(u).Value, "검사 전제: 전력이 모자라야 합니다.");
            Assert.IsFalse(sim.CanBoost(u));
            Assert.IsFalse(sim.Boost(u), "전력이 없는데 강화되었습니다.");
            Assert.AreEqual(1, u.Tier);
        }

        [Test]
        public void Retreat_RefundsBoostSpendNotJustDeployCost()
        {
            var sim = LongSim();
            var (c, r) = FirstPlaceable(sim, "vac");
            var u = sim.Deploy("vac", c, r);

            Charge(sim, 60f);
            float boostCost = sim.BoostCost(u).Value;
            Assert.IsTrue(sim.Boost(u));

            float spent = _db.Unit("vac").Cost + boostCost;
            Assert.AreEqual(spent, u.Spent, 1e-3f, "누적 소비가 배치+강화 합계와 다릅니다.");

            float before = sim.Power;
            sim.Retreat(u);
            float expected = (float)System.Math.Floor(spent * _db.Config.retreatRefund * 10f) / 10f;
            Assert.AreEqual(before + expected, sim.Power, 1e-3f,
                "철수 환급이 강화 비용을 포함하지 않았습니다.");
        }

        // ------------------------------------------------------------------ multitap

        [Test]
        public void Multitap_AddsASlotPerPurchaseUpToTheTableLength()
        {
            var sim = LongSim();
            int baseMax = _db.Config.maxUnits;
            Assert.AreEqual(baseMax, sim.MaxUnits);

            int taps = _db.Config.multitapCost.Length;
            for (int i = 0; i < taps; i++)
            {
                Charge(sim, 120f);
                float cost = sim.NextTapCost.Value;
                float before = sim.Power;
                Assert.IsTrue(sim.BuyMultitap(), $"{i + 1}번째 멀티탭 구매가 실패했습니다.");
                Assert.AreEqual(before - cost, sim.Power, 1e-3f, "멀티탭 비용이 빠지지 않았습니다.");
                Assert.AreEqual(baseMax + i + 1, sim.MaxUnits);
            }

            Assert.IsNull(sim.NextTapCost, "표에 정의된 수를 넘겨 살 수 있습니다.");
            Assert.IsFalse(sim.BuyMultitap(), "멀티탭이 표보다 많이 팔렸습니다.");
        }

        [Test]
        public void Multitap_RaisesTheDeployCap()
        {
            var sim = LongSim("robot");
            Charge(sim, 120f);   // afford the fills
            // Fill every slot the base cap allows.
            int placed = 0;
            for (int r = 0; r < sim.Field.Rows && placed < _db.Config.maxUnits; r++)
                for (int c = 0; c < sim.Field.Cols && placed < _db.Config.maxUnits; c++)
                    if (sim.CanPlace("robot", c, r)) { sim.Deploy("robot", c, r); placed++; }

            Assert.AreEqual(_db.Config.maxUnits, placed, "검사 전제: 기본 한도까지 채워야 합니다.");
            Assert.IsFalse(sim.Affordable("robot"), "기본 한도에서 배치가 막혀야 합니다.");

            Charge(sim, 120f);
            Assert.IsTrue(sim.BuyMultitap());
            Assert.IsTrue(sim.Affordable("robot"), "멀티탭 구매 후에도 배치가 막혀 있습니다.");
        }

        // ------------------------------------------------------------------ wave pressure

        [Test]
        public void LateWavesCarryTheirOwnHpMultiplier()
        {
            // The table ramps hpMul toward the end of a stage so a stalled defence breaks.
            var withRamp = _db.Stages
                .Where(s => s.Schedule.Any(e => e.WaveHpMul > 1f))
                .ToList();
            Assert.IsNotEmpty(withRamp, "후반 hpMul 이 설정된 스테이지가 있어야 합니다.");

            foreach (var st in withRamp)
            {
                var ordered = st.Schedule.OrderBy(e => e.Time).ToList();
                Assert.AreEqual(1f, ordered.First().WaveHpMul, 1e-3f,
                    $"{st.No}: 첫 웨이브는 배율 1이어야 합니다.");
                Assert.Greater(ordered.Last().WaveHpMul, 1f,
                    $"{st.No}: 마지막 웨이브에 압박 배율이 있어야 합니다.");
            }
        }

        [Test]
        public void SpawnedEnemyHpUsesStageAndWaveMultipliers()
        {
            // Driven off a synthetic schedule: in a shipped stage an undefended run is lost
            // long before the ramped waves arrive, so the real table could never show this.
            const float waveMul = 2.5f;
            var stage = LongRunStage("dust", waveMul, spawnAt: 0.1f);
            var sim = new BattleSim(_db, stage, new[] { "vac" },
                                    new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

            sim.Tick(0.2f);

            var e = sim.Enemies.FirstOrDefault(x => x.Type == "dust");
            Assert.IsNotNull(e, "적이 생성되지 않았습니다.");

            float expected = _db.Enemy("dust").Hp * stage.HpMul * waveMul;
            Assert.AreEqual(expected, e.MaxHp, 0.01f,
                $"적 HP 가 stage({stage.HpMul}) × wave({waveMul}) 로 계산되지 않았습니다.");
        }

        [Test]
        public void StageStartPowerOverridesTheGlobalDefault()
        {
            foreach (var st in _db.Stages)
            {
                var sim = new BattleSim(_db, st, new[] { "vac" },
                                        new FlatPower(), new FixedRandom(0.5f), new PresentationBus());
                Assert.AreEqual(st.StartPower, sim.Power, 1e-3f,
                    $"{st.No}: 시작 전력이 스테이지 설정과 다릅니다.");
            }
        }
    }
}
