// 가전 대청소 리그의 말과 전투 단위.
//
// 원본 index.html 의 `LG.bench` / `LG.board` 원소와 `B.units` 원소를 그대로 옮긴 것이다.
// 전투 단위는 한 판 동안 수백 번 고쳐 쓰이므로 class 다(struct 로 두면 값 복사가 섞인다).
namespace DustZero.Core.League
{
    /// <summary>준비 단계의 말 한 개 — 대기석이나 판 위에 놓인 가전.</summary>
    public sealed class LeaguePiece
    {
        public string Type;
        /// <summary>1~3. 같은 가전 세 장이 모이면 올라간다.</summary>
        public int Star;
        /// <summary>화면이 같은 말을 계속 따라갈 수 있게 주는 일련번호.</summary>
        public int Uid;
        /// <summary>합체로 막 태어났다 — 화면의 반짝임용.</summary>
        public bool Fresh;
    }

    /// <summary>대기석인지 판인지. 옮기기·팔기가 둘을 같이 다룬다.</summary>
    public enum LeagueWhere { Bench, Board }

    /// <summary>판 한 칸을 가리킨다.</summary>
    public readonly struct LeagueSpot
    {
        public readonly LeagueWhere Where;
        public readonly int Index;
        public LeagueSpot(LeagueWhere where, int index) { Where = where; Index = index; }
    }

    /// <summary>한 판의 단계.</summary>
    public enum LeaguePhase { Prep, Battle, Over }

    /// <summary>옮기기 결과. 'Capped' 는 레벨이 허락하는 배치 수를 넘긴 것이다.</summary>
    public enum LeagueMoveResult { No, Yes, Capped }

    /// <summary>전투 중의 한 단위. 아군(가전)과 적(먼지)이 같은 모양을 쓴다.</summary>
    public sealed class LeagueUnit
    {
        public int Id;
        /// <summary>0 = 우리 가전, 1 = 먼지.</summary>
        public int Side;
        public string Type;
        public int Star;

        public float X, Y;
        public float Hp, MaxHp;
        public float Atk;
        /// <summary>공격 간격(초). 시너지 haste 는 여기에 미리 반영된다.</summary>
        public float Interval;
        public float Range;
        public float Speed;

        public float Mana, MaxMana;
        /// <summary>받는 피해를 (1 - Armor) 로 줄인다.</summary>
        public float Armor;
        /// <summary>시너지 dr. 받는 피해를 (1 - Dr) 로 줄인다.</summary>
        public float Dr;
        /// <summary>스킬 피해 배수 — 전용장비와 시너지 skillDmg 를 곱해 둔 값.</summary>
        public float SkillMul = 1f;
        /// <summary>★ 배수. 회복처럼 ★ 에만 비례하는 효과가 쓴다.</summary>
        public float StarMul = 1f;

        public float AtkTimer;
        public bool Alive = true;
        public bool Fly;
        public bool Boss;
        /// <summary>그림 반지름(칸). 사거리 계산에 들어간다.</summary>
        public float Radius = 0.3f;

        public float StunT, FrozenT, SlowT, SlowAmt, HasteT, HasteAmt, BreakT, BreakAmt;
        /// <summary>피격 깜빡임·공격 반동·시전 자세 — 화면만 쓴다.</summary>
        public float HitT, Kick, CastT;
        public int Face = 1;

        public LeagueUnit Target;

        /// <summary>죽을 때 나오는 적과 그 수.</summary>
        public string Split;
        public int SplitN;

        /// <summary>판 위 어느 칸에서 왔는지(아군만).</summary>
        public int Cell = -1;
    }

    /// <summary>시너지 한 줄의 현재 상태 — 화면이 그대로 그린다.</summary>
    public sealed class LeagueSynergyView
    {
        public string Group;
        public string Name;
        /// <summary>지금 판 위에 있는 **가전 종류** 수.</summary>
        public int Count;
        public int[] Needs;
        /// <summary>지금 켜진 단계. 없으면 null.</summary>
        public LeagueSynStep Active;
        /// <summary>다음 단계. 더 없으면 null.</summary>
        public LeagueSynStep Next;
    }

    public sealed class LeagueSynStep
    {
        public int Need;
        public string Key;
        public float Value;
        public string Desc;
    }

    /// <summary>한 라운드가 끝난 결과.</summary>
    public sealed class LeagueRoundResult
    {
        public int Round;
        public bool Win;
        /// <summary>살아남은 먼지 수. 보스는 3마리로 센다 — 깎이는 청결도가 그만큼 늘어난다.</summary>
        public int Left;
        public bool Timeout;
        public int Income;
    }

    /// <summary>판이 끝나고 실제로 받은 것.</summary>
    public sealed class LeagueFinishResult
    {
        /// <summary>8라운드를 청결도를 남기고 끝냈다.</summary>
        public bool Clear;
        /// <summary>오늘의 보상 판 수를 썼다.</summary>
        public bool Counted;
        public System.Collections.Generic.List<string> Got = new();
    }
}
