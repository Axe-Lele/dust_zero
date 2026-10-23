// 가전 한 대의 동작 상태. 그림만 고르고 규칙은 건드리지 않는다.
//
// 원본 art/261022-motion/motion.js 의 cue/step/pick 을 그대로 옮겼다. 거기 적힌 대로
// **연출 전용**이다 — 난수도, 피해도, 쿨다운도 없다. 그래서 Unity 를 모르는 순수
// 클래스로 두었고, 프레임 번호가 맞는지는 EditMode 에서 바로 잴 수 있다.
//
// 아틀라스는 320 칸 아홉이고 칸 번호는 이렇게 나뉜다.
//   0~2  기본 동작 — 준비 · 발동 · 마무리
//   3~8  스킬 — 준비(3·4·5) · 유지(6·7 되풀이) · 마무리(8)
namespace DustZero.Core.Defense
{
    /// <summary>지금 무엇을 그리는 중인가.</summary>
    public enum MotionKind { Idle, Attack, Skill }

    public sealed class MotionState
    {
        /// <summary>기본 동작 한 번의 길이(초). 원본 `attackSeconds`.</summary>
        public const float AttackSeconds = 0.36f;
        /// <summary>스킬 동작이 적어도 이만큼은 보인다. 원본 `skillSeconds`.</summary>
        public const float SkillSeconds = 0.84f;
        /// <summary>지원 가전(컴퓨터·선풍기·발전기)이 스스로 움직이는 주기.</summary>
        public const float PulseSeconds = 2.4f;

        float _attack, _attackAge;
        float _skill, _skillAge;
        float _lastKick, _pulse;
        bool _wasSkill;

        public MotionKind Kind { get; private set; } = MotionKind.Idle;
        /// <summary>마지막으로 고른 칸. 그릴 것이 없으면 −1.</summary>
        public int Frame { get; private set; } = -1;

        public void CueAttack() { _attack = AttackSeconds; _attackAge = 0f; }
        public void CueSkill() { _skill = SkillSeconds; _skillAge = 0f; }

        /// <summary>
        /// 한 프레임 흘린다.
        /// <paramref name="attackClock"/> 은 공격 때 **올라가는** 값이다 — 그 순간을 공격으로
        /// 읽는다. 원본은 `kick`(때릴 때 튀는 연출값)을 봤지만 우리 쪽은 근접 공격만 그걸
        /// 세운다. 그래서 공격 간격 타이머(AtkT)를 본다: 쏘는 순간 간격으로 되돌아가므로
        /// 근접·원거리·연쇄 가릴 것 없이 같은 신호가 된다.
        /// 스킬은 켜진 순간만 한 번 집는다.
        /// <paramref name="pulse"/> 는 때리지 않는 지원 가전용이다.
        /// </summary>
        public void Step(float dt, float attackClock, bool skillActive, bool pulse = false)
        {
            _attack = Mathf2.Max(0f, _attack - dt);
            _skill = Mathf2.Max(0f, _skill - dt);
            _attackAge += dt;
            _skillAge += dt;

            if (attackClock > _lastKick + 0.025f) CueAttack();
            _lastKick = attackClock;

            if (skillActive && !_wasSkill) CueSkill();
            _wasSkill = skillActive;

            if (pulse)
            {
                _pulse += dt;
                if (_pulse >= PulseSeconds) { _pulse = 0f; CueAttack(); }
            }
            else _pulse = 0f;
        }

        /// <summary>
        /// 지금 그릴 칸. 과열 중이면 아무것도 그리지 않는다(원본 `pick` 의 `u.over`).
        /// <paramref name="skillT"/> 는 남은 스킬 시간이다 — 스킬이 길면 유지 자세를
        /// 되풀이하다가 끝 0.18초에 마무리로 넘어간다.
        /// </summary>
        public int Pick(bool overheated, float skillT)
        {
            if (overheated) { Kind = MotionKind.Idle; Frame = -1; return -1; }

            if (_skill > 0f || skillT > 0f)
            {
                Kind = MotionKind.Skill;
                float age = _skillAge;
                float remaining = Mathf2.Max(_skill, skillT);
                Frame = age < 0.42f
                    ? 3 + System.Math.Min(2, (int)(age / 0.14f))
                    : remaining <= 0.18f
                        ? 8
                        : 6 + ((int)((age - 0.42f) / 0.16f) % 2);
                return Frame;
            }

            if (_attack > 0f)
            {
                Kind = MotionKind.Attack;
                float progress = Mathf2.Min(0.999f, _attackAge / AttackSeconds);
                Frame = System.Math.Min(2, (int)(progress * 3f));
                return Frame;
            }

            Kind = MotionKind.Idle;
            Frame = -1;
            return -1;
        }
    }
}
