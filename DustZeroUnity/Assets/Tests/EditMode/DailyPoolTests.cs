// 오늘의 청소 일지 (10.18-h) 회귀 검사.
//
// 원본 index.html 의 poolDay / poolBoxes / poolAdd / poolClaim 과 같은 답이 나오는지 본다.
// 하루 경계와 이월은 시계를 밀어서 재므로 실제로 하루를 기다리지 않는다.
using System;
using DustZero.Core;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class DailyPoolTests
    {
        GameDatabase _db;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _now = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
            SaveClock.UtcNow = () => _now;
        }

        [TearDown]
        public void TearDown() => SaveClock.UtcNow = () => DateTime.UtcNow;

        PlayerProfile Fresh() => new(_db, new SaveData(), new FixedRandom(0.5f));

        [Test]
        public void ShippedTableIsThreeBoxesWorthOneFiftyCrystal()
        {
            Assert.AreEqual(3, _db.DailyPool.Count, "daily_pool 이 3행이어야 합니다.");
            int crystal = 0, last = 0;
            foreach (var b in _db.DailyPool)
            {
                Assert.Greater(b.pts, last, "상자 점수가 오름차순이어야 합니다.");
                last = b.pts;
                crystal += b.crystal;
            }
            Assert.AreEqual(100, last, "마지막 상자가 하루 기준 점수입니다.");
            Assert.AreEqual(150, crystal, "하루 크리스탈 총량이 150 이어야 합니다.");
        }

        [Test]
        public void ANewSaveHasNoCarryBox()
        {
            // 기본 생성된 PoolState 는 day 가 비어 있다. 이것을 '어제 기록' 으로 보면
            // 새 계정이 공짜 이월 상자를 들고 시작한다.
            var p = Fresh();
            Assert.AreEqual(0, p.Pool.carry);
            Assert.AreEqual(p.PoolBasePoints, p.PoolMaxPoints);
            Assert.AreEqual(_db.DailyPool.Count, p.PoolBoxes().Count);
        }

        [Test]
        public void PointsStopAtTheDailyMaximum()
        {
            var p = Fresh();
            Assert.AreEqual(10, p.AddPoolPoints(10));
            Assert.AreEqual(90, p.AddPoolPoints(500), "최대치를 넘는 몫은 잘려야 합니다.");
            Assert.AreEqual(0, p.AddPoolPoints(10), "꽉 찬 뒤에는 0 이어야 합니다.");
            Assert.IsTrue(p.PoolFull);
            Assert.AreEqual(100, p.Pool.pts);
        }

        [Test]
        public void EachBoxPaysOnceAndOnlyWhenItsPointsAreReached()
        {
            var p = Fresh();
            p.AddPoolPoints(30);
            Assert.AreEqual(1, p.PoolClaimableCount(), "30점이면 첫 상자만 열립니다.");
            Assert.AreEqual(0, p.ClaimPoolBox("2"), "아직 점수가 모자란 상자는 0 입니다.");

            long before = p.Crystal;
            Assert.AreEqual(40, p.ClaimPoolBox("1"));
            Assert.AreEqual(before + 40, p.Crystal);
            Assert.AreEqual(0, p.ClaimPoolBox("1"), "같은 상자를 두 번 받을 수 없습니다.");
        }

        [Test]
        public void AFullDayPaysExactlyTheTableTotal()
        {
            var p = Fresh();
            p.AddPoolPoints(100);
            int paid = 0;
            foreach (var b in p.PoolBoxes()) paid += p.ClaimPoolBox(b.Id);
            Assert.AreEqual(150, paid);
            Assert.AreEqual(0, p.PoolClaimableCount());
        }

        [Test]
        public void YesterdaysUnfilledPointsBecomeOneExtraBoxToday()
        {
            var p = Fresh();
            p.AddPoolPoints(40);          // 어제 40점만 채웠다

            _now = _now.AddDays(1);        // 하루 넘김
            Assert.AreEqual(60, p.Pool.carry, "못 채운 60점이 따라와야 합니다.");
            Assert.AreEqual(160, p.PoolMaxPoints);
            Assert.AreEqual(_db.DailyPool.Count + 1, p.PoolBoxes().Count);

            var carryBox = p.PoolBoxes()[p.PoolBoxes().Count - 1];
            Assert.IsTrue(carryBox.Carry);
            Assert.AreEqual(PlayerProfile.CarryBoxId, carryBox.Id);
            Assert.AreEqual(160, carryBox.Points, "이월 상자는 기준 + 이월 점수에서 열립니다.");
            // 150 * 60 / 100 = 90 -> 10단위 반올림
            Assert.AreEqual(90, carryBox.Crystal);
        }

        [Test]
        public void TodaysPointsAndClaimsResetWhenTheDayTurns()
        {
            var p = Fresh();
            p.AddPoolPoints(100);
            p.ClaimPoolBox("1");

            _now = _now.AddDays(1);
            Assert.AreEqual(0, p.Pool.pts, "점수는 하루마다 0 에서 시작합니다.");
            Assert.AreEqual(0, p.PoolClaimableCount(), "점수가 0 이면 열린 상자도 없습니다.");

            // 받은 기록도 하루마다 비워지므로, 오늘 다시 30점을 채우면 같은 상자를 또 받는다.
            p.AddPoolPoints(30);
            Assert.AreEqual(40, p.ClaimPoolBox("1"), "어제 받은 상자를 오늘 다시 받을 수 있습니다.");
        }

        [Test]
        public void ComingBackAfterABreakCarriesAWholeDay()
        {
            // 원본의 규칙 그대로: 기록이 어제가 아니면 이월은 하루치 전부다.
            var p = Fresh();
            p.AddPoolPoints(10);

            _now = _now.AddDays(5);
            Assert.AreEqual(p.PoolBasePoints, p.Pool.carry);
        }
    }
}
