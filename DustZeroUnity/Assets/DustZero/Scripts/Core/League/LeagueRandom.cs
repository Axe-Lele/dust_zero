// 원본 `lgRng` 와 **같은 수열**을 내는 난수.
//
// 리그는 상점 뽑기가 판의 절반이라, 같은 씨앗이면 같은 상점이 나와야 원본과 대조할 수
// 있다. 원본은 mulberry32 를 쓰고 32비트 연산을 그대로 따라야 하므로 unchecked uint 다.
// (브라우저에서 같은 씨앗으로 뽑은 값과 대조해 확인했다 — LeagueTests 참조.)
using System;

namespace DustZero.Core.League
{
    public sealed class LeagueRandom : IRandom
    {
        uint _a;

        public LeagueRandom(int seed)
        {
            unchecked { _a = (uint)seed; }
            if (_a == 0) _a = 1;
        }

        public float Value
        {
            get
            {
                unchecked
                {
                    _a += 0x6D2B79F5u;
                    uint t = _a;
                    t = Mul(t ^ (t >> 15), t | 1u);
                    t ^= t + Mul(t ^ (t >> 7), t | 61u);
                    return ((t ^ (t >> 14)) & 0xFFFFFFFFu) / 4294967296f;
                }
            }
        }

        /// <summary>JavaScript 의 Math.imul — 32비트로 자르는 곱셈이다.</summary>
        static uint Mul(uint a, uint b)
        {
            unchecked { return (uint)((int)a * (int)b); }
        }
    }
}
