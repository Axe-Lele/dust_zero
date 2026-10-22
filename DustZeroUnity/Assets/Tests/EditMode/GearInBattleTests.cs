// 장비가 전투 수치를 실제로 바꾸는지.
//
// 장비 시스템은 가방·합성·교환 화면까지 다 만들었지만, 그것이 **전장에서 체감되는가**
// 는 별개 문제다. 배수를 적용하는 줄 하나가 빠져도 화면은 멀쩡하고 테스트도 통과한다.
// 그래서 같은 판을 장비 없이/있이 두 번 돌려 가전의 수치를 직접 비교한다.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    /// <summary>배수를 마음대로 정할 수 있는 장비 공급원.</summary>
    sealed class TunedPower : IUnitPowerSource
    {
        public float Atk = 1f, Heat = 1f, Charge = 1f, SkillDur = 0f;
        public readonly Dictionary<string, float> Params = new();

        public float PowerOf(string unitId) => 1f;
        public int LevelOf(string unitId) => 1;
        public float SupportPowerOf(string unitId) => 1f;
        public float GearAtkMul(string unitId) => Atk;
        public float GearHeatMul(string unitId) => Heat;
        public float GearChargeMul(string unitId) => Charge;
        public float GearSkillDuration(string unitId) => SkillDur;
        public float SkillParam(string unitId, string key, float raw)
            => Params.TryGetValue(key, out var v) ? v : raw;
    }

    public class GearInBattleTests
    {
        GameDatabase _db;
        StageDef _stage;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _stage = _db.StageByOrder(1);
        }

        BattleUnit Deploy(TunedPower power, string unitId)
        {
            var sim = new BattleSim(_db, _stage, new[] { unitId },
                                    power, new FixedRandom(0.5f), new PresentationBus());
            // 비용을 신경 쓰지 않도록 전력을 넉넉히 준 뒤, 놓을 수 있는 첫 칸에 둔다.
            sim.Tick(0.001f);
            for (int r = 0; r < _stage.Rows; r++)
                for (int c = 0; c < _stage.Cols; c++)
                    if (sim.CanPlace(unitId, c, r) && sim.Deploy(unitId, c, r) != null)
                        return sim.Units[0];
            return null;
        }

        /// <summary>공격력 배수가 배치된 가전의 공격력에 실제로 들어가는가.</summary>
        [Test]
        public void AttackGearRaisesTheDeployedUnitsAttack()
        {
            var plain = new TunedPower();
            var geared = new TunedPower { Atk = 1.5f };
            var unitId = _db.UnitOrder.First(u => _db.Unit(u).Atk > 0 && _db.Unit(u).Cost <= 10);

            var a = Deploy(plain, unitId);
            var b = Deploy(geared, unitId);
            Assert.IsNotNull(a, "장비 없이 배치하지 못했습니다.");
            Assert.IsNotNull(b, "장비를 끼고 배치하지 못했습니다.");

            Assert.Greater(b.Atk, a.Atk,
                $"공격력 배수 1.5 를 줬는데 그대로입니다 ({a.Atk} → {b.Atk}).");
            Assert.AreEqual(a.Atk * 1.5f, b.Atk, a.Atk * 0.01f,
                "공격력이 배수만큼 오르지 않았습니다.");
        }

        /// <summary>과열 한도 배수.</summary>
        [Test]
        public void HeatGearRaisesTheHeatCeiling()
        {
            var unitId = _db.UnitOrder.FirstOrDefault(u => _db.Unit(u).Heat > 0);
            if (unitId == null) Assert.Ignore("과열을 쓰는 가전이 없습니다.");

            var a = Deploy(new TunedPower(), unitId);
            var b = Deploy(new TunedPower { Heat = 1.4f }, unitId);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.Greater(b.MaxHeat, a.MaxHeat,
                $"과열 배수 1.4 를 줬는데 그대로입니다 ({a.MaxHeat} → {b.MaxHeat}).");
        }

        /// <summary>스킬 충전 배수는 충전 시간을 **줄여야** 한다.</summary>
        [Test]
        public void ChargeGearShortensTheSkillWindUp()
        {
            var unitId = _db.UnitOrder.FirstOrDefault(u => _db.Unit(u).SkillCharge > 0);
            if (unitId == null) Assert.Ignore("스킬을 쓰는 가전이 없습니다.");

            var a = Deploy(new TunedPower(), unitId);
            var b = Deploy(new TunedPower { Charge = 0.8f }, unitId);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.Less(b.SkillChargeTime, a.SkillChargeTime,
                $"충전 배수 0.8 을 줬는데 그대로입니다 ({a.SkillChargeTime} → {b.SkillChargeTime}).");
        }

        /// <summary>전용장비의 지속 시간 가산.</summary>
        [Test]
        public void ExclusiveGearAddsToTheSkillDuration()
        {
            var unitId = _db.UnitOrder.FirstOrDefault(u => _db.Unit(u).SkillDuration > 0);
            if (unitId == null) Assert.Ignore("지속형 스킬이 있는 가전이 없습니다.");

            var a = Deploy(new TunedPower(), unitId);
            var b = Deploy(new TunedPower { SkillDur = 2f }, unitId);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreEqual(a.SkillDurationAdd + 2f, b.SkillDurationAdd, 0.001f,
                "전용장비 지속 시간이 더해지지 않았습니다.");
        }

        /// <summary>
        /// 전용장비는 스킬 수치 자체(skill_param)도 바꾼다. 가전이 Skp 로 읽는 값이
        /// 공급원을 거치지 않으면, 장비를 껴도 스킬만 그대로인 상태가 조용히 남는다.
        /// </summary>
        [Test]
        public void ExclusiveGearReachesTheSkillParameters()
        {
            var unitId = _db.UnitOrder.FirstOrDefault(
                u => _db.Unit(u).DefenseBehaviour == "vac") ?? "vac";
            var tuned = new TunedPower();
            tuned.Params["pullDps"] = 999f;

            var unit = Deploy(tuned, unitId);
            if (unit == null) Assert.Ignore($"{unitId} 를 배치할 자리가 없습니다.");
            Assert.IsNotNull(unit.Skp, "가전이 스킬 수치를 읽을 통로를 받지 못했습니다.");
            Assert.AreEqual(999f, unit.Skp("pullDps"), 0.001f,
                "전용장비가 바꾼 스킬 수치가 전투에 전달되지 않습니다.");
        }
    }
}
