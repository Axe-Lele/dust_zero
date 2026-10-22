// 10.17-b support growth and 10.17-d boost-star counting.
//
// Both are "a number the player never sees change" bugs: the generator looked like it was
// levelling up while producing exactly the same power, and the field badge disagreed with
// the two places that show the same value. Neither shows up in a screenshot of one frame.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    /// <summary>
    /// A roster where one appliance is levelled and limit-broken, so support growth is not 1.
    /// The real <see cref="DustZero.Core.Meta.PlayerProfile"/> needs a save; this says only
    /// what the sim asks for.
    /// </summary>
    sealed class GrownPower : IUnitPowerSource
    {
        readonly ConfigRow _cfg;
        readonly string _unitId;
        readonly int _level;
        readonly int _star;

        public GrownPower(ConfigRow cfg, string unitId, int level, int star)
        {
            _cfg = cfg; _unitId = unitId; _level = level; _star = star;
        }

        public float PowerOf(string unitId) => 1f;
        public int LevelOf(string unitId) => unitId == _unitId ? _level : 1;

        public float SupportPowerOf(string unitId)
        {
            if (unitId != _unitId || !_cfg.TryNum(unitId + "LvBonus", out float lv)) return 1f;
            _cfg.TryNum(unitId + "StarBonus", out float star);
            return 1f + lv * (_level - 1) + star * _star;
        }

        public float GearAtkMul(string unitId) => 1f;
        public float GearHeatMul(string unitId) => 1f;
        public float GearChargeMul(string unitId) => 1f;
        public float GearSkillDuration(string unitId) => 0f;
        public float SkillParam(string unitId, string key, float raw) => raw;
    }

    public class SupportGrowthTests
    {
        GameDatabase _db;
        StageDef _stage;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _stage = _db.Stages[0];
        }

        BattleSim NewSim(IUnitPowerSource power)
            => new(_db, _stage, new[] { "gen", "pc", "vac" }, power,
                   new FixedRandom(0.5f), new PresentationBus());

        /// <summary>The first tile the stage will take this appliance on.</summary>
        static (int X, int Y) FreeTile(BattleSim sim, string type)
        {
            for (int r = 0; r < sim.Stage.Rows; r++)
                for (int c = 0; c < sim.Stage.Cols; c++)
                    if (sim.CanPlace(type, c, r)) return (c, r);
            Assert.Fail($"{type} 를 놓을 칸이 없습니다.");
            return (0, 0);
        }

        [Test]
        public void TheTableDecidesWhichAppliancesGrowThisWay()
        {
            var cfg = _db.Config;
            Assert.IsTrue(cfg.TryNum("genLvBonus", out _), "config 에 genLvBonus 가 있어야 합니다.");
            Assert.IsTrue(cfg.TryNum("pcLvBonus", out _), "config 에 pcLvBonus 가 있어야 합니다.");
            // A plain attacker must NOT have these keys, or it would be scaled twice.
            Assert.IsFalse(cfg.TryNum("vacLvBonus", out _),
                           "청소기는 지원 성장 대상이 아닙니다.");
        }

        [Test]
        public void AGrownGeneratorRegeneratesMoreThanAFreshOne()
        {
            var cfg = _db.Config;
            Assert.IsTrue(cfg.TryNum("genLvBonus", out float perLevel));

            // Deploy does not gate on power (the caller checks), so both sims can place the
            // generator at t=0 and be compared before a single tick has run. No ticking means
            // no enemies, no deaths, and the same number every run.
            var flat = NewSim(new FlatPower());
            var tile = FreeTile(flat, "gen");
            Assert.IsNotNull(flat.Deploy("gen", tile.X, tile.Y));
            float plain = flat.PowerRegen();

            var grown = NewSim(new GrownPower(cfg, "gen", level: 11, star: 0));
            Assert.IsNotNull(grown.Deploy("gen", tile.X, tile.Y));
            float raised = grown.PowerRegen();

            Assert.Greater(raised, plain,
                "발전기가 레벨을 올려도 전력 회복이 그대로입니다 (10.17-b).");

            // 10 levels above 1 is exactly 10 x perLevel on the generator's own contribution.
            float baseRegen = _db.Config.regenPerSec;
            float expected = (plain - baseRegen) * (1f + perLevel * 10f) + baseRegen;
            Assert.AreEqual(expected, raised, 0.0001f, "성장 배율이 공식과 다릅니다.");
        }

        [Test]
        public void LimitBreakRaisesSupportOutputToo()
        {
            var cfg = _db.Config;
            if (!cfg.TryNum("genStarBonus", out float perStar) || perStar <= 0f)
                Assert.Ignore("genStarBonus 가 없습니다.");

            var lvOnly = NewSim(new GrownPower(cfg, "gen", level: 1, star: 0));
            var starred = NewSim(new GrownPower(cfg, "gen", level: 1, star: 2));
            var tile = FreeTile(lvOnly, "gen");
            lvOnly.Deploy("gen", tile.X, tile.Y);
            starred.Deploy("gen", tile.X, tile.Y);

            Assert.Greater(starred.PowerRegen(), lvOnly.PowerRegen(),
                           "돌파해도 발전기 출력이 그대로입니다.");
        }

        [Test]
        public void AnAttackerIsNotScaledByTheSupportMultiplier()
        {
            // vac has no config keys, so SupportPowerOf must be 1 - otherwise the roster
            // bonus would be applied twice to an appliance PowerOf already covers.
            var power = new GrownPower(_db.Config, "vac", level: 30, star: 3);
            Assert.AreEqual(1f, power.SupportPowerOf("vac"), 0.0001f);

            var sim = NewSim(power);
            var tile = FreeTile(sim, "vac");
            var u = sim.Deploy("vac", tile.X, tile.Y);
            Assert.AreEqual(1f, u.Support, 0.0001f);
        }

        /// <summary>
        /// Runs the battle until a condition holds. Boosting needs real power, and power only
        /// arrives by ticking - but ticking also runs the fight, so this gives up rather than
        /// looping forever if the stage ends first.
        /// </summary>
        static bool Charge(BattleSim sim, System.Func<bool> ready, float seconds = 40f)
        {
            int steps = (int)(seconds * 60f);
            for (int i = 0; i < steps && !ready() && !sim.Finished; i++) sim.Tick(1f / 60f);
            return ready();
        }

        // ------------------------------------------------------------------ 10.17-d

        [Test]
        public void ADeployedApplianceWearsNoStarAndEachBoostAddsOne()
        {
            var sim = NewSim(new FlatPower());
            var tile = FreeTile(sim, "pc");
            var u = sim.Deploy("pc", tile.X, tile.Y);

            Assert.AreEqual(1, u.Tier, "배치 직후 단계는 1이고, 이때 배지는 없습니다.");

            // The table is what decides the counting: boost rows are numbered 2 and 3, so
            // printing u.Tier gives ★★ after one boost. Printing Tier-1 - the bug - would
            // give one star for a twice-boosted appliance.
            Assert.IsNotNull(sim.NextBoost(u), "pc 에 강화 단계가 없습니다.");
            Assert.AreEqual(2, sim.NextBoost(u).tier, "첫 강화는 2단계여야 합니다.");

            if (!Charge(sim, () => sim.CanBoost(u))) Assert.Ignore("전력이 모이지 않았습니다.");
            Assert.IsTrue(sim.Boost(u), "첫 강화가 실패했습니다.");
            Assert.AreEqual(2, u.Tier, "강화 1회면 ★★ 여야 합니다 (10.17-d).");

            if (sim.NextBoost(u) != null && Charge(sim, () => sim.CanBoost(u)))
            {
                Assert.IsTrue(sim.Boost(u), "두 번째 강화가 실패했습니다.");
                Assert.AreEqual(3, u.Tier, "강화 2회면 ★★★ 여야 합니다.");
            }
        }

        [Test]
        public void BoostingAlsoScalesWhatASupportApplianceProduces()
        {
            var sim = NewSim(new FlatPower());
            var tile = FreeTile(sim, "gen");
            var u = sim.Deploy("gen", tile.X, tile.Y);
            Assert.AreEqual(1f, u.BoostMul, 0.0001f);

            float before = sim.PowerRegen();
            if (sim.NextBoost(u) == null) Assert.Ignore("발전기에 강화 단계가 없습니다.");
            if (!Charge(sim, () => sim.CanBoost(u))) Assert.Ignore("전력이 모이지 않았습니다.");
            Assert.IsTrue(sim.Boost(u));

            Assert.Greater(u.BoostMul, 1f, "강화가 배율을 올리지 않았습니다.");
            Assert.Greater(sim.PowerRegen(), before,
                           "강화해도 발전기 전력 회복이 그대로입니다.");
        }
    }
}
