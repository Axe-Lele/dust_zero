// 오늘의 청소 일지 — 대청소·협동·리그가 공유하는 하루치 숙제.
//
// 10.18-h 가 대청소의 시간 구간별 크리스탈(surv_reward)을 이걸로 바꿨다. 어느 콘텐츠로
// 채우든 같은 점수통에 들어가고, daily_pool 의 점수마다 크리스탈 상자가 열린다.
// 원본 index.html 의 poolDay / poolBoxes / poolAdd / poolClaim 을 그대로 옮긴 것이다.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    /// <summary>One crystal box on the log: what opens it and what it pays.</summary>
    public readonly struct PoolBox
    {
        /// <summary>The table's box number as text, or "c" for the carry box.</summary>
        public readonly string Id;
        public readonly int Points;
        public readonly int Crystal;
        /// <summary>True for the one box made from yesterday's unfilled points.</summary>
        public readonly bool Carry;

        public PoolBox(string id, int points, int crystal, bool carry)
        {
            Id = id; Points = points; Crystal = crystal; Carry = carry;
        }
    }

    public sealed partial class PlayerProfile
    {
        /// <summary>The carry box's id, kept as one spelling so save and UI cannot drift.</summary>
        public const string CarryBoxId = "c";

        /// <summary>Points that fill the log on an ordinary day — the last box's requirement.</summary>
        public int PoolBasePoints
            => Db.DailyPool.Count == 0 ? 100 : Db.DailyPool[Db.DailyPool.Count - 1].pts;

        int PoolBaseCrystal
        {
            get
            {
                int sum = 0;
                foreach (var b in Db.DailyPool) sum += b.crystal;
                return sum;
            }
        }

        /// <summary>
        /// The log for today, rebuilt when the day turns. Yesterday's unfilled points become
        /// the carry box; a record older than yesterday carries the full day, which is what
        /// the original does for a player coming back after a break.
        /// </summary>
        public PoolState Pool
        {
            get
            {
                var today = SaveClock.TodayKey();
                var p = Data.pool;
                if (p != null && p.day == today) return p;

                // A record with no day is the default-constructed one on a brand-new save.
                // The original has no S.pool at all there, so there is nothing to carry -
                // without this check a new player starts with a free "yesterday" box.
                bool hadPrevious = p != null && !string.IsNullOrEmpty(p.day);

                int carry = 0;
                if (hadPrevious && _cfg.poolCarry != 0)
                {
                    int baseline = PoolBasePoints;
                    carry = p.day == SaveClock.YesterdayKey()
                        ? Math.Max(0, baseline - Math.Min(baseline, p.pts))
                        : baseline;
                }
                Data.pool = new PoolState { day = today, pts = 0, carry = carry };
                return Data.pool;
            }
        }

        /// <summary>Every box on today's log, the carry box last when there is one.</summary>
        public List<PoolBox> PoolBoxes()
        {
            var p = Pool;
            var list = new List<PoolBox>();
            foreach (var b in Db.DailyPool)
                list.Add(new PoolBox(b.box.ToString(), b.pts, b.crystal, false));

            if (p.carry > 0)
            {
                int baseline = PoolBasePoints;
                // The carry box pays in proportion to what was left, rounded to a round ten
                // and never less than 10 - the original's own formula.
                int crystal = Math.Max(10,
                    (int)Math.Round(PoolBaseCrystal * (double)p.carry / baseline / 10d) * 10);
                list.Add(new PoolBox(CarryBoxId, baseline + p.carry, crystal, true));
            }
            return list;
        }

        /// <summary>Points that fill today's log, carry included.</summary>
        public int PoolMaxPoints => PoolBasePoints + Pool.carry;

        public bool PoolFull => Pool.pts >= PoolMaxPoints;

        public bool PoolBoxTaken(string id)
            => Pool.got != null && Pool.got.Contains(id);

        public bool PoolBoxReady(PoolBox box)
            => Pool.pts >= box.Points && !PoolBoxTaken(box.Id);

        /// <summary>How many boxes are sitting there waiting to be opened.</summary>
        public int PoolClaimableCount()
        {
            int n = 0;
            foreach (var b in PoolBoxes()) if (PoolBoxReady(b)) n++;
            return n;
        }

        /// <summary>
        /// Adds points, clamped to today's maximum. Returns what actually went in, so the
        /// caller can say "+7" rather than "+10" when the log was nearly full.
        /// </summary>
        public int AddPoolPoints(int amount)
        {
            if (amount <= 0) return 0;
            var p = Pool;
            int add = Math.Max(0, Math.Min(amount, PoolMaxPoints - p.pts));
            if (add == 0) return 0;
            p.pts += add;
            MarkDirty();
            return add;
        }

        /// <summary>Opens one box. Returns the crystal paid, or 0 if it was not open.</summary>
        public int ClaimPoolBox(string id)
        {
            var p = Pool;
            foreach (var b in PoolBoxes())
            {
                if (b.Id != id) continue;
                if (!PoolBoxReady(b)) return 0;
                (p.got ??= new List<string>()).Add(b.Id);
                Data.AddItem(CrystalItemId, b.Crystal);
                MarkDirty();
                return b.Crystal;
            }
            return 0;
        }
    }
}
