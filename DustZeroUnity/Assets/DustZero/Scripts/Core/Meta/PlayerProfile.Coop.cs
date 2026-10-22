// 협동 청소의 세이브 쪽 — 해금, 파트너 기억, 최고 기록, 판이 끝났을 때의 정산.
//
// 원본 index.html 의 coopOn / coopSave / coopEnd 보상 부분을 옮긴 것이다.
// 전투 규칙은 Core/Coop/CoopSim.cs 가, 영구적으로 남는 것은 여기가 맡는다.
using System;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    /// <summary>What one finished 협동 run paid out.</summary>
    public readonly struct CoopResult
    {
        public readonly int Waves;
        public readonly bool NewRecord;
        public readonly int Best;
        public readonly int PoolPoints;
        public readonly int Coin;
        public readonly int PassXp;

        public CoopResult(int waves, bool newRecord, int best, int poolPoints, int coin, int passXp)
        {
            Waves = waves;
            NewRecord = newRecord;
            Best = best;
            PoolPoints = poolPoints;
            Coin = coin;
            PassXp = passXp;
        }
    }

    public sealed partial class PlayerProfile
    {
        /// <summary>1-5 를 깨야 열린다. 표에 조건이 없으면 열어 둔다.</summary>
        public bool CoopUnlocked
            => !Db.Unlocks.TryGetValue("coop", out var u) || ConditionMet(u.cond);

        public string CoopLockMessage
            => Db.Unlocks.TryGetValue("coop", out var u) ? Db.Text(u.lockKey) : string.Empty;

        public CoopRecord Coop
        {
            get
            {
                Data.coop ??= new CoopRecord();
                Data.coop.best ??= new System.Collections.Generic.Dictionary<string, int>();
                if (string.IsNullOrEmpty(Data.coop.last)) Data.coop.last = "rush";
                return Data.coop;
            }
        }

        public int CoopBest(string partnerId)
            => Coop.best.TryGetValue(partnerId ?? string.Empty, out int w) ? w : 0;

        /// <summary>Remembers who the player went out with, so the sheet opens on them.</summary>
        public void SetCoopPartner(string partnerId)
        {
            if (string.IsNullOrEmpty(partnerId) || Coop.last == partnerId) return;
            Coop.last = partnerId;
            MarkDirty();
        }

        /// <summary>
        /// 협동 한 판을 정산한다. 일지 점수는 이미 판 안에서 웨이브마다 들어갔으므로
        /// 여기서 다시 주지 않는다 — 코인·패스 경험치·최고 기록만 남긴다.
        ///
        /// 일지 점수를 인자로 받는 것은 결과 화면에 보여 주기 위해서지 적립하기 위해서가 아니다.
        /// </summary>
        public CoopResult FinishCoop(string partnerId, int wavesCleared, int coin, int poolPointsEarned)
        {
            int best = CoopBest(partnerId);
            bool newRecord = wavesCleared > best;
            if (newRecord)
            {
                Coop.best[partnerId] = wavesCleared;
                best = wavesCleared;
            }

            if (coin > 0) Data.AddItem(CoinItemId, coin);

            int passXp = Math.Min(_cfg.coopPassXpMax, wavesCleared * _cfg.coopPassXpPerWave);
            if (passXp > 0) AddPassXp(passXp);

            MarkDirty();
            return new CoopResult(wavesCleared, newRecord, best, poolPointsEarned, coin, passXp);
        }

        /// <summary>
        /// 파트너가 들고 나올 가전의 레벨·★. 내 상위 6대의 평균이라 계정이 자라면
        /// 파트너도 같이 자란다. 보유가 없으면 Lv.1 ★0 이다.
        /// </summary>
        public (int Level, int Star) CoopPartnerStrength()
        {
            // OwnedUnit 은 자기 id 를 들고 있지 않다. 사전 키로 정렬한다.
            var ids = new System.Collections.Generic.List<string>();
            foreach (var key in Data.owned.Keys) if (Db.Units.ContainsKey(key)) ids.Add(key);
            if (ids.Count == 0) return (1, 0);

            ids.Sort((a, b) => PowerOf(b).CompareTo(PowerOf(a)));
            int take = Math.Min(6, ids.Count);

            int lv = 0, star = 0;
            for (int i = 0; i < take; i++)
            {
                var o = Data.Own(ids[i]);
                lv += o.lv;
                star += o.star;
            }
            return (Math.Max(1, (int)Math.Round(lv / (double)take)),
                    (int)Math.Round(star / (double)take));
        }
    }
}
