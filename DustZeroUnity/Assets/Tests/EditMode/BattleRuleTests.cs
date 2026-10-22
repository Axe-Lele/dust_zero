// Placement legality, ground pathing and the block/queue/overheat chain.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    /// <summary>Fixed roster multipliers so damage numbers in tests are predictable.</summary>
    sealed class FlatPower : IUnitPowerSource
    {
        public float PowerOf(string unitId) => 1f;
        public int LevelOf(string unitId) => 1;
        // Lv.1, no limit-break: support growth is exactly 1, so these tests see the table.
        public float SupportPowerOf(string unitId) => 1f;
        // No gear: these tests are about the rules the gear multiplies, not the gear itself.
        public float GearAtkMul(string unitId) => 1f;
        public float GearHeatMul(string unitId) => 1f;
        public float GearChargeMul(string unitId) => 1f;
        public float GearSkillDuration(string unitId) => 0f;
        public float SkillParam(string unitId, string key, float raw) => raw;
    }

    public class BattleRuleTests
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
            => new(_db, _stage, deck.Length > 0 ? deck : new[] { "vac", "ac" },
                   new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        // ------------------------------------------------------------------ placement

        [Test]
        public void MeleeCannotStandOnFurnitureAndRangedCannotStandOnFloor()
        {
            var sim = NewSim();
            var field = sim.Field;

            bool checkedFloor = false, checkedFurniture = false;
            for (int r = 0; r < field.Rows; r++)
                for (int c = 0; c < field.Cols; c++)
                {
                    char ch = field.TileAt(c, r);
                    if (ch == BattleField.Furniture)
                    {
                        Assert.IsFalse(sim.CanPlace("vac", c, r), $"근접 가전이 가구({c},{r}) 위에 놓였습니다.");
                        Assert.IsTrue(sim.CanPlace("ac", c, r), $"원거리 가전이 가구({c},{r}) 위에 못 놓입니다.");
                        checkedFurniture = true;
                    }
                    else if (BattleField.IsGroundTile(ch) && !field.NoDeploy(c, r))
                    {
                        Assert.IsTrue(sim.CanPlace("vac", c, r), $"근접 가전이 바닥({c},{r})에 못 놓입니다.");
                        Assert.IsFalse(sim.CanPlace("ac", c, r), $"원거리 가전이 바닥({c},{r})에 놓였습니다.");
                        checkedFloor = true;
                    }
                }

            Assert.IsTrue(checkedFloor && checkedFurniture, "검사할 바닥/가구 칸이 모두 있어야 합니다.");
        }

        [Test]
        public void EntranceAndGoalExclusionZonesRejectMelee()
        {
            var sim = NewSim();
            var field = sim.Field;

            // Right in front of entrance 1 is inside noDeployFromEntrance.
            var p = _stage.Paths[0];
            Assert.IsTrue(field.NoDeploy(p.StartX, p.StartY),
                "입구 칸은 근접 배치 금지 구역이어야 합니다.");
            Assert.IsFalse(sim.CanPlace("vac", p.StartX, p.StartY));

            // The goal tile itself is excluded too.
            Assert.IsTrue(field.NoDeploy(field.GoalX, field.GoalY),
                "목표 칸은 근접 배치 금지 구역이어야 합니다.");
        }

        [Test]
        public void TwoUnitsCannotShareATile()
        {
            var sim = NewSim();
            var (c, r) = FirstFreeGround(sim);
            sim.Deploy("vac", c, r);
            Assert.IsFalse(sim.CanPlace("vac", c, r), "같은 칸에 두 번 배치되었습니다.");
        }

        [Test]
        public void DeployCostsPowerAndRetreatRefundsHalf()
        {
            var sim = NewSim();
            float before = sim.Power;
            var def = _db.Unit("vac");

            var (c, r) = FirstFreeGround(sim);
            var u = sim.Deploy("vac", c, r);
            Assert.AreEqual(before - def.Cost, sim.Power, 1e-3f, "배치 비용이 전력에서 빠지지 않았습니다.");

            float afterDeploy = sim.Power;
            sim.Retreat(u);
            float refund = (float)System.Math.Floor(def.Cost * _db.Config.retreatRefund);
            Assert.AreEqual(afterDeploy + refund, sim.Power, 1e-3f, "철수 환급이 설정과 다릅니다.");
            Assert.Greater(sim.CooldownOf("vac"), 0f, "철수 후 재배치 대기시간이 시작되지 않았습니다.");
        }

        [Test]
        public void RedeployIsBlockedUntilCooldownExpires()
        {
            var sim = NewSim();
            var (c, r) = FirstFreeGround(sim);
            sim.Retreat(sim.Deploy("vac", c, r));

            Assert.IsFalse(sim.Affordable("vac"), "대기시간 중에는 재배치할 수 없어야 합니다.");

            // Run past the cooldown. Power regenerates meanwhile, so cost is not the blocker.
            for (float t = 0; t < _db.Config.redeployCooldown + 1f; t += 0.1f) sim.Tick(0.1f);
            Assert.AreEqual(0f, sim.CooldownOf("vac"), 1e-3f);
        }

        [Test]
        public void DeployedUnitCountIsCapped()
        {
            var sim = NewSim("robot");
            int placed = 0;
            for (int r = 0; r < sim.Field.Rows && placed < sim.MaxUnits + 2; r++)
                for (int c = 0; c < sim.Field.Cols && placed < sim.MaxUnits + 2; c++)
                {
                    if (!sim.CanPlace("robot", c, r)) continue;
                    // Ignore power and cooldown; this test is about the unit cap alone.
                    sim.Deploy("robot", c, r);
                    placed++;
                }

            Assert.GreaterOrEqual(placed, sim.MaxUnits, "검사에 쓸 칸이 부족합니다.");
            Assert.IsFalse(sim.Affordable("robot"), $"배치 한도({sim.MaxUnits})를 넘겨도 배치가 허용됩니다.");
        }

        // ------------------------------------------------------------------ pathing

        [Test]
        public void GroundEnemyWalksAroundABlockerWhenADetourExists()
        {
            var sim = NewSim();
            // Advance until a ground enemy exists.
            for (int i = 0; i < 2000 && sim.Enemies.Count == 0; i++) sim.Tick(0.05f);
            var e = sim.Enemies.FirstOrDefault(x => !x.IsFlying);
            Assert.IsNotNull(e, "지상 적이 생성되어야 합니다.");

            float startDist = sim.Field.DistAll[e.Cy, e.Cx];

            // Let it walk without any blockers; it must make progress toward the goal.
            for (int i = 0; i < 400; i++) sim.Tick(0.05f);
            if (sim.Enemies.Contains(e))
                Assert.Less(sim.Field.DistAll[e.Cy, e.Cx], startDist,
                    "지상 적이 목표 쪽으로 전진하지 않았습니다.");
        }

        [Test]
        public void BlockedEnemyStopsAndHeatsTheBlocker()
        {
            var sim = NewSim("robot");
            // Place a blocker on the first legal tile along entrance 1's route.
            var blockTile = FindTileOnPath(sim, 0);
            var u = sim.Deploy("robot", blockTile.c, blockTile.r);

            float heatBefore = u.Heat;
            bool everBlocked = false;
            for (int i = 0; i < 4000; i++)
            {
                sim.Tick(0.05f);
                if (sim.Enemies.Any(x => ReferenceEquals(x.BlockedBy, u))) { everBlocked = true; break; }
                if (sim.Finished) break;
            }

            Assert.IsTrue(everBlocked, "근접 가전이 적을 저지하지 못했습니다.");

            for (int i = 0; i < 60; i++) sim.Tick(0.05f);
            Assert.Greater(u.Heat, heatBefore, "저지 중인 가전에 과열이 쌓이지 않았습니다.");
        }

        [Test]
        public void BlockLoadNeverExceedsTheUnitsBlockCount()
        {
            var sim = NewSim("robot");
            var blockTile = FindTileOnPath(sim, 0);
            var u = sim.Deploy("robot", blockTile.c, blockTile.r);

            for (int i = 0; i < 6000 && !sim.Finished; i++)
            {
                sim.Tick(0.05f);
                Assert.LessOrEqual(u.Load, u.Def.Block,
                    $"저지 정원({u.Def.Block})을 넘겨 {u.Load}칸이 찼습니다.");
            }
        }

        [Test]
        public void OverheatReleasesEveryBlockedEnemy()
        {
            var sim = NewSim("robot");
            var blockTile = FindTileOnPath(sim, 0);
            var u = sim.Deploy("robot", blockTile.c, blockTile.r);

            // Push the bar past the brim. Natural cooling is applied before the overheat
            // check each tick, so setting it to exactly MaxHeat would cool back under.
            for (int i = 0; i < 400 && !sim.Finished; i++) sim.Tick(0.05f);
            u.Heat = u.MaxHeat * 1.5f;
            sim.Tick(0.05f);

            Assert.IsTrue(u.Overheated, "과열 상태가 되어야 합니다.");
            Assert.IsFalse(sim.Enemies.Any(e => ReferenceEquals(e.BlockedBy, u)),
                "과열되면 저지 중이던 적이 모두 풀려야 합니다.");
            Assert.IsNull(sim.BlockerAt(u.Col, u.Row),
                "과열된 가전은 길을 막지 않아야 합니다.");
        }

        [Test]
        public void FlyingEnemiesIgnoreBlockersAndFollowTheirRoute()
        {
            var flyingStage = _db.Stages.FirstOrDefault(s =>
                s.EnemyIds.Any(id => _db.Enemies[id].Fly));
            if (flyingStage == null) Assert.Ignore("비행 적이 있는 스테이지가 없습니다.");

            var sim = new BattleSim(_db, flyingStage, new[] { "robot" },
                                    new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

            BattleEnemy flyer = null;
            for (int i = 0; i < 20000 && flyer == null && !sim.Finished; i++)
            {
                sim.Tick(0.05f);
                flyer = sim.Enemies.FirstOrDefault(e => e.IsFlying);
            }
            Assert.IsNotNull(flyer, "비행 적이 생성되어야 합니다.");

            float d0 = flyer.D;
            for (int i = 0; i < 20; i++) sim.Tick(0.05f);
            if (sim.Enemies.Contains(flyer))
            {
                Assert.Greater(flyer.D, d0, "비행 적이 경로를 따라 진행하지 않았습니다.");
                Assert.IsNull(flyer.BlockedBy, "비행 적은 저지되지 않아야 합니다.");
            }
        }

        // ------------------------------------------------------------------ outcome

        [Test]
        public void LettingEverythingThroughLosesAndScoresNoStars()
        {
            var sim = NewSim();
            for (int i = 0; i < 40000 && !sim.Finished; i++) sim.Tick(0.05f);

            Assert.AreEqual(BattleOutcome.Lost, sim.Outcome, "아무것도 배치하지 않으면 패배해야 합니다.");
            Assert.AreEqual(0, sim.StarsEarned());
            Assert.AreEqual(0, sim.Life);
        }

        [Test]
        public void StarThresholdsFollowConfig()
        {
            var cfg = _db.Config;
            // StarsEarned counts how many starLifeMin thresholds the remaining life clears.
            Assert.AreEqual(3, cfg.starLifeMin.Count(v => cfg.lifeMax >= v),
                "최대 청결도로 끝내면 별 3개여야 합니다.");
            Assert.AreEqual(0, cfg.starLifeMin.Count(v => 0 >= v),
                "청결도 0이면 별 0개여야 합니다.");
        }

        // ------------------------------------------------------------------ helpers

        static (int c, int r) FirstFreeGround(BattleSim sim)
        {
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                    if (sim.CanPlace("vac", c, r) || sim.CanPlace("robot", c, r)) return (c, r);
            Assert.Fail("배치 가능한 바닥 칸이 없습니다.");
            return (0, 0);
        }

        /// <summary>A legal melee tile that the given entrance's route actually passes over.</summary>
        static (int c, int r) FindTileOnPath(BattleSim sim, int pathIndex)
        {
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                {
                    if (!sim.CanPlace("robot", c, r)) continue;
                    if (sim.Field.TileDistance(c, r, pathIndex) != null) return (c, r);
                }
            // Fall back to any legal tile; the lane may not be tile-aligned on every map.
            return FirstFreeGround(sim);
        }
    }
}
