// 모션 프레임 고르기. 그림이 아니라 **번호**를 재므로 Unity 없이 바로 확인된다.
//
// 이 표의 숫자는 원본 art/261022-motion/motion.js 에서 온 것이다. 눈으로 "움직이네"
// 라고 보는 것으로는 3·4·5 가 뒤집혀 있어도 모른다.
using DustZero.Core.Defense;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class MotionStateTests
    {
        [Test]
        public void IdleDrawsNothing()
        {
            var m = new MotionState();
            m.Step(0.1f, attackClock: 0f, skillActive: false);
            Assert.AreEqual(-1, m.Pick(false, 0f), "가만히 있을 때는 SD 를 그려야 합니다.");
            Assert.AreEqual(MotionKind.Idle, m.Kind);
        }

        /// <summary>공격 신호가 **올라간 순간**이 공격이다(우리는 공격 간격 타이머를 본다).</summary>
        [Test]
        public void ARisingAttackClockStartsTheAttack()
        {
            var m = new MotionState();
            m.Step(0.016f, attackClock: 0f, skillActive: false);
            Assert.AreEqual(-1, m.Pick(false, 0f));

            m.Step(0.016f, attackClock: 0.15f, skillActive: false);
            Assert.AreEqual(0, m.Pick(false, 0f), "공격 첫 칸은 0 입니다.");
            Assert.AreEqual(MotionKind.Attack, m.Kind);
        }

        /// <summary>그 값은 공격 뒤 줄어든다(쿨다운). 줄어드는 것을 공격으로 읽으면 계속 떨린다.</summary>
        [Test]
        public void AFallingAttackClockDoesNotRestartTheAttack()
        {
            var m = new MotionState();
            m.Step(0.016f, 0.15f, false);
            for (int i = 0; i < 40; i++) m.Step(0.016f, 0.15f - i * 0.003f, false);
            Assert.AreEqual(-1, m.Pick(false, 0f), "신호가 줄어드는 동안 공격이 다시 시작됐습니다.");
        }

        /// <summary>기본 동작은 0.36초에 걸쳐 0 → 1 → 2 로 간다.</summary>
        [Test]
        public void AttackWalksFrames0To2AndStops()
        {
            var m = new MotionState();
            m.Step(0.016f, 0.15f, false);

            Assert.AreEqual(0, m.Pick(false, 0f));
            Step(m, 0.13f);
            Assert.AreEqual(1, m.Pick(false, 0f));
            Step(m, 0.13f);
            Assert.AreEqual(2, m.Pick(false, 0f));
            Step(m, 0.20f);
            Assert.AreEqual(-1, m.Pick(false, 0f), "0.36초가 지나면 SD 로 돌아가야 합니다.");
        }

        /// <summary>
        /// 스킬은 준비(3·4·5) → 유지(6·7 되풀이) → 마무리(8) 다. 긴 스킬이 유지에서
        /// 멈춰 있지 않고 두 칸을 오가는지가 핵심이다.
        /// </summary>
        [Test]
        public void SkillOpensSustainsAndRecovers()
        {
            var m = new MotionState();
            m.Step(0.016f, 0f, skillActive: true);

            Assert.AreEqual(3, m.Pick(false, 3f), "스킬 첫 칸은 3 입니다.");
            Step(m, 0.14f); Assert.AreEqual(4, m.Pick(false, 3f));
            Step(m, 0.14f); Assert.AreEqual(5, m.Pick(false, 3f));

            Step(m, 0.14f);
            int a = m.Pick(false, 3f);
            Step(m, 0.16f);
            int b = m.Pick(false, 3f);
            CollectionAssert.AreEquivalent(new[] { 6, 7 }, new[] { a, b },
                "유지 자세가 6·7 을 오가지 않습니다.");

            // 마무리 칸은 **둘 다** 0.18 아래일 때 나온다 — 원본이
            // remaining = max(제 타이머, 남은 스킬 시간) 으로 본다. 스킬 시간만 짧게 줘도
            // 제 타이머가 남아 있으면 유지 자세가 계속된다(여기서 7 이 나와 알았다).
            // skillActive 를 다시 true 로 주면 **올라간 순간**으로 읽혀 스킬이 새로
            // 시작된다(여기서 5 가 나와 알았다). 남은 시간은 Pick 의 skillT 로만 준다.
            Step(m, 0.40f);
            Assert.AreEqual(8, m.Pick(false, 0.1f), "끝날 때 마무리 칸(8)이 나오지 않습니다.");
        }

        /// <summary>스킬이 켜진 **순간**만 집는다. 켜져 있는 동안 계속 다시 시작하면 안 된다.</summary>
        [Test]
        public void SkillIsCuedOnceOnTheRisingEdge()
        {
            var m = new MotionState();
            m.Step(0.016f, 0f, true);
            Step(m, 0.50f, skillActive: true);
            // 다시 시작했다면 나이가 0 으로 돌아가 준비 칸(3~5)이 나온다.
            Assert.GreaterOrEqual(m.Pick(false, 3f), 6, "스킬이 매 프레임 다시 시작되고 있습니다.");
        }

        /// <summary>때리지 않는 지원 가전은 2.4초마다 스스로 한 번 움직인다.</summary>
        [Test]
        public void SupportUnitsPulseOnTheirOwn()
        {
            var m = new MotionState();
            Step(m, 2.0f, pulse: true);
            Assert.AreEqual(-1, m.Pick(false, 0f), "2.4초 전에 움직였습니다.");
            Step(m, 0.5f, pulse: true);
            Assert.AreEqual(0, m.Pick(false, 0f), "2.4초가 지나도 움직이지 않았습니다.");
        }

        /// <summary>과열 중에는 멈춘 모습을 보여야 한다 — 원본도 모션을 덮어쓴다.</summary>
        [Test]
        public void OverheatOverridesEverything()
        {
            var m = new MotionState();
            m.Step(0.016f, 0f, skillActive: true);
            Assert.AreEqual(-1, m.Pick(overheated: true, skillT: 3f));
        }

        static void Step(MotionState m, float seconds, bool skillActive = false, bool pulse = false)
        {
            for (float t = 0f; t < seconds; t += 0.016f) m.Step(0.016f, 0f, skillActive, pulse);
        }
    }
}
