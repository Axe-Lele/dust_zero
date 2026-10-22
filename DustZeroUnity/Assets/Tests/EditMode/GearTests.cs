// 장비 rules: copies, exclusives, enhancing, dismantling, fusing and what they do to stats.
//
// Gear is where a save can quietly go wrong - a uid reused, a piece dismantled while worn, a
// fusion that eats the wrong copies. These are pure rules over the save, so they are cheap to
// test properly, and every number comes from config rather than being asserted literally.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Meta;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class GearTests
    {
        DustZero.Data.GameDatabase _db;
        PlayerProfile _p;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            // In-memory store: these are rules over a fresh save, never the player's file.
            var svc = new SaveService("mem", _db, new FixedRandom(0.5f), new MemoryFileStore());
            _p = new PlayerProfile(_db, svc.NewProfile(), new FixedRandom(0.5f));
        }

        /// <summary>A grade's first table id, so tests never hardcode a piece.</summary>
        int AnyGearOfGrade(int grade)
            => _db.Gear.Values.Where(g => g.grade == grade).OrderBy(g => g.id).First().id;

        string AnyUnitWithExclusive()
            => _db.ExclusiveOfUnit.Keys.OrderBy(k => k).First();

        // ------------------------------------------------------------------ acquiring

        [Test]
        public void CommonGearAddsACopyWithItsOwnIdentity()
        {
            int id = AnyGearOfGrade(1);

            _p.AddGear(id);
            _p.AddGear(id);

            Assert.AreEqual(2, _p.Data.gear.Count, "같은 장비를 두 번 얻으면 두 개가 되어야 합니다.");
            var uids = _p.Data.gear.Select(g => g.uid).ToList();
            CollectionAssert.AllItemsAreUnique(uids, "장비 uid 가 겹치면 장착이 엉킵니다.");
            CollectionAssert.DoesNotContain(uids, 0, "uid 0 은 '장착 안 함'을 뜻하므로 쓸 수 없습니다.");
        }

        [Test]
        public void ExclusiveGearAwakensInsteadOfStacking()
        {
            string unit = AnyUnitWithExclusive();
            int id = _db.ExclusiveOfUnit[unit].id;
            int cap = _db.Config.exclMaxStar;

            var first = _p.AddGear(id);
            Assert.AreEqual(GearGainKind.New, first.Kind);
            Assert.AreEqual(0, _p.ExclusiveState(unit).star);
            Assert.IsEmpty(_p.Data.gear, "전용장비는 가방의 공용 목록에 들어가면 안 됩니다.");

            for (int i = 1; i <= cap; i++)
            {
                var again = _p.AddGear(id);
                Assert.AreEqual(GearGainKind.Awakened, again.Kind);
                Assert.AreEqual(i, again.Amount);
                Assert.AreEqual(i, _p.ExclusiveState(unit).star);
            }

            long before = _p.ItemCount(PlayerProfile.GearEssenceItem);
            var overflow = _p.AddGear(id);
            Assert.AreEqual(GearGainKind.Essence, overflow.Kind,
                "최대 각성 뒤의 중복은 정수로 바뀌어야 합니다.");
            Assert.AreEqual(cap, _p.ExclusiveState(unit).star, "각성이 상한을 넘었습니다.");
            Assert.AreEqual(before + _db.Config.exclDupeEssence,
                _p.ItemCount(PlayerProfile.GearEssenceItem));
        }

        // ------------------------------------------------------------------ enhancing

        [Test]
        public void EnhancingSpendsStoneAndCoinAndRemembersWhatWasSpent()
        {
            int uid = NewGear(1);
            var cost = _p.CommonGearCost(uid).Value;
            _p.Data.AddItem(PlayerProfile.GearStoneItem, cost.stone);
            _p.Data.AddItem(PlayerProfile.CoinItemId, cost.coin);

            Assert.IsTrue(_p.EnhanceCommonGear(uid));

            var g = _p.GearInst(uid);
            Assert.AreEqual(1, g.lv);
            Assert.AreEqual(cost.stone, g.sp, "분해 환급은 쓴 강화석을 기억해야 계산됩니다.");
            Assert.AreEqual(0, _p.ItemCount(PlayerProfile.GearStoneItem));
        }

        [Test]
        public void EnhancingIsRefusedWithoutTheMaterials()
        {
            int uid = NewGear(1);
            Assert.IsFalse(_p.EnhanceCommonGear(uid), "재료 없이 강화되면 안 됩니다.");
            Assert.AreEqual(0, _p.GearInst(uid).lv);
        }

        [Test]
        public void EnhancingStopsAtTheGradesLevelCap()
        {
            int uid = NewGear(1);
            int cap = _db.Config.gearMaxLv[0];
            _p.Data.AddItem(PlayerProfile.GearStoneItem, 100000);
            _p.Data.AddItem(PlayerProfile.CoinItemId, 10000000);

            for (int i = 0; i < cap; i++)
                Assert.IsTrue(_p.EnhanceCommonGear(uid), $"{i + 1}강에서 막혔습니다.");

            Assert.AreEqual(cap, _p.GearInst(uid).lv);
            Assert.IsNull(_p.CommonGearCost(uid), "상한에서는 비용이 없어야 합니다.");
            Assert.IsFalse(_p.EnhanceCommonGear(uid), "상한을 넘어 강화되었습니다.");
        }

        // ------------------------------------------------------------------ dismantle

        [Test]
        public void DismantlingAWornPieceIsRefused()
        {
            int uid = NewGear(1);
            string unit = _db.UnitOrder[0];
            _p.AddUnit(unit);
            _p.EquipGear(unit, uid);

            Assert.AreEqual(0, _p.DismantleGear(uid),
                "장착 중인 장비가 분해되면 가전이 조용히 벗겨집니다.");
            Assert.IsNotNull(_p.GearInst(uid), "거절했는데 장비가 사라졌습니다.");
        }

        [Test]
        public void DismantlingRefundsAShareOfWhatWasInvested()
        {
            int uid = NewGear(1);
            var cost = _p.CommonGearCost(uid).Value;
            _p.Data.AddItem(PlayerProfile.GearStoneItem, cost.stone);
            _p.Data.AddItem(PlayerProfile.CoinItemId, cost.coin);
            _p.EnhanceCommonGear(uid);

            long before = _p.ItemCount(PlayerProfile.GearStoneItem);
            int expected = _db.Config.gearDismantle[0]
                         + (int)(cost.stone * _db.Config.gearRefund);

            Assert.AreEqual(expected, _p.DismantleGear(uid));
            Assert.AreEqual(before + expected, _p.ItemCount(PlayerProfile.GearStoneItem));
            Assert.IsNull(_p.GearInst(uid), "분해했는데 가방에 남아 있습니다.");
        }

        // ------------------------------------------------------------------ fusing

        [Test]
        public void FusionEatsTheLeastInvestedLooseCopiesFirst()
        {
            int need = _db.Config.gearFuseNeed;
            // need + 2: one gets enhanced, one gets worn, and `need` plain copies remain -
            // so the pick has a real choice to get wrong.
            var uids = Enumerable.Range(0, need + 2).Select(_ => NewGear(1)).ToList();

            // Raise one so it is clearly the most invested, and wear another.
            var cost = _p.CommonGearCost(uids[0]).Value;
            _p.Data.AddItem(PlayerProfile.GearStoneItem, cost.stone);
            _p.Data.AddItem(PlayerProfile.CoinItemId, cost.coin);
            _p.EnhanceCommonGear(uids[0]);

            string unit = _db.UnitOrder[0];
            _p.AddUnit(unit);
            _p.EquipGear(unit, uids[1]);

            var pick = _p.GearFusePick(1);
            Assert.AreEqual(need, pick.Count);
            CollectionAssert.DoesNotContain(pick.Select(g => g.uid).ToList(), uids[1],
                "장착 중인 장비가 합성 재료로 뽑혔습니다.");
            CollectionAssert.DoesNotContain(pick.Select(g => g.uid).ToList(), uids[0],
                "더 키운 장비를 두고 낮은 것부터 써야 합니다.");
        }

        [Test]
        public void FusionConsumesItsCostAndProducesTheNextGrade()
        {
            int need = _db.Config.gearFuseNeed;
            for (int i = 0; i < need; i++) NewGear(1);
            Assert.IsTrue(_p.CanFuse(1));

            int before = _p.Data.gear.Count;
            var result = _p.FuseGear(1, new FixedRandom(0.5f));

            Assert.IsNotNull(result);
            Assert.AreEqual(before - need + 1, _p.Data.gear.Count,
                $"{need}개를 먹고 1개가 나와야 합니다.");
            var made = _p.Data.gear.Last();
            Assert.AreEqual(2, _db.Gear[made.id].grade, "합성 결과가 한 등급 위가 아닙니다.");
        }

        [Test]
        public void FusionIsRefusedBelowTheRequiredCountAndAtTheTopGrade()
        {
            for (int i = 0; i < _db.Config.gearFuseNeed - 1; i++) NewGear(1);
            Assert.IsFalse(_p.CanFuse(1), "재료가 모자란데 합성이 열렸습니다.");
            Assert.IsNull(_p.FuseGear(1, new FixedRandom(0.5f)));

            for (int i = 0; i < _db.Config.gearFuseNeed; i++) NewGear(3);
            Assert.IsFalse(_p.CanFuse(3), "3등급은 더 올라갈 곳이 없습니다.");
        }

        // ------------------------------------------------------------------ equipping

        [Test]
        public void EquippingMovesAPieceRatherThanCopyingIt()
        {
            int uid = NewGear(1);
            string a = _db.UnitOrder[0], b = _db.UnitOrder[1];
            _p.AddUnit(a);
            _p.AddUnit(b);

            _p.EquipGear(a, uid);
            Assert.AreEqual(a, _p.GearOwner(uid));

            _p.EquipGear(b, uid);
            Assert.AreEqual(b, _p.GearOwner(uid), "옮겨가지 않았습니다.");
            Assert.AreEqual(0, _p.Data.Own(a).eq, "이전 가전이 같은 장비를 계속 들고 있습니다.");
        }

        // ------------------------------------------------------------------ stats

        [Test]
        public void WornGearRaisesTheStatsItDeclares()
        {
            string unit = _db.UnitOrder[0];
            _p.AddUnit(unit);
            Assert.AreEqual(1f, _p.GearAtkMul(unit), 0.0001f, "맨몸인데 배율이 1이 아닙니다.");

            // An attack piece: the table says what it gives, the test does not.
            var row = _db.Gear.Values.Where(g => g.grade == 1 && g.atk > 0).OrderBy(g => g.id).First();
            _p.AddGear(row.id);
            int uid = _p.Data.gear.Last().uid;
            _p.EquipGear(unit, uid);

            Assert.AreEqual(1f + (float)row.atk, _p.GearAtkMul(unit), 0.0001f);
        }

        [Test]
        public void ChargeReductionIsCappedSoItCanNeverReachZero()
        {
            string unit = AnyUnitWithExclusive();
            _p.AddUnit(unit);

            var sp = _db.Gear.Values.Where(g => g.grade <= 3 && g.sp > 0).OrderByDescending(g => g.sp).First();
            _p.AddGear(sp.id);
            int uid = _p.Data.gear.Last().uid;
            _p.EquipGear(unit, uid);
            _p.AddGear(_db.ExclusiveOfUnit[unit].id);

            // Enhance both as far as the wallet allows, to push the stack as high as it goes.
            _p.Data.AddItem(PlayerProfile.GearStoneItem, 1000000);
            _p.Data.AddItem(PlayerProfile.CoinItemId, 100000000);
            while (_p.EnhanceCommonGear(uid)) { }
            while (_p.EnhanceExclusiveGear(unit)) { }

            float mul = _p.GearChargeMul(unit);
            Assert.GreaterOrEqual(mul, 0.4f - 0.0001f,
                "스킬 충전 감소가 상한(60%)을 넘어 0초에 가까워졌습니다.");
            Assert.LessOrEqual(mul, 1f);
        }

        [Test]
        public void ExclusiveSkillEditsScaleWithItsStar()
        {
            // Whichever appliance has an additive edit; the table decides, not this test.
            var pair = _db.ExclusiveOfUnit.OrderBy(kv => kv.Key)
                .FirstOrDefault(kv => !string.IsNullOrEmpty(kv.Value.sk1Key)
                                      && kv.Value.sk1Op == "add" && kv.Value.sk1Key != "dur");
            Assert.IsNotNull(pair.Key, "add 형 효과를 가진 전용장비가 하나는 있어야 합니다.");
            string unit = pair.Key;
            var row = pair.Value;

            _p.AddUnit(unit);
            _p.AddGear(row.id);

            float atZero = _p.SkillParam(unit, row.sk1Key, 0f);
            Assert.AreEqual((float)row.sk1Val, atZero, 0.0001f, "★0 에서 표 값 그대로여야 합니다.");

            _p.AddGear(row.id);          // ★1
            float atOne = _p.SkillParam(unit, row.sk1Key, 0f);
            Assert.Greater(atOne, atZero, "각성해도 효과가 커지지 않았습니다.");
            Assert.AreEqual((float)row.sk1Val * (1f + _db.Config.exclStarBonus), atOne, 0.0001f);
        }

        [Test]
        public void AnApplianceWithoutAnExclusiveGetsItsRawSkillParams()
        {
            string unit = _db.UnitOrder.First(u => _db.ExclusiveOfUnit.ContainsKey(u));
            _p.AddUnit(unit);
            Assert.AreEqual(12.5f, _p.SkillParam(unit, "nosuchkey", 12.5f), 0.0001f,
                "전용장비가 없으면 원래 값이 그대로 와야 합니다.");
            Assert.AreEqual(0f, _p.GearSkillDuration(unit), 0.0001f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Adds one loose common piece of a grade and returns its uid.</summary>
        int NewGear(int grade)
        {
            _p.AddGear(AnyGearOfGrade(grade));
            return _p.Data.gear.Last().uid;
        }
    }
}
