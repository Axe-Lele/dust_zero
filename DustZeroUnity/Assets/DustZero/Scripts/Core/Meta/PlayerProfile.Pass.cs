// Season pass: level from XP, two reward tracks, and the premium unlock.
//
// Ported from the 10.15-h build's passSeason/passSt/passLv/passClaim. The season is derived
// from the clock and config rather than stored, so a save that sat through a season rollover
// starts the new one empty instead of carrying stale claims.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public sealed partial class PlayerProfile
    {
        /// <summary>Which season is running now, and when it ends.</summary>
        public readonly struct Season
        {
            public readonly int Id;
            public readonly DateTime End;
            public Season(int id, DateTime end) { Id = id; End = end; }
        }

        public Season CurrentSeason()
        {
            var start = ParseSeasonStart();
            int days = Math.Max(1, _cfg.passSeasonDays);
            var len = TimeSpan.FromDays(days);
            var now = SaveClock.UtcNow();
            int i = Math.Max(0, (int)Math.Floor((now - start).TotalDays / days));
            return new Season(i + 1, start + TimeSpan.FromTicks(len.Ticks * (i + 1)));
        }

        DateTime ParseSeasonStart()
        {
            var text = string.IsNullOrWhiteSpace(_cfg.passSeasonStart) ? "2026-10-12" : _cfg.passSeasonStart;
            return DateTime.TryParse(text, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                                                     : new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>Days left in the season, never negative.</summary>
        public int SeasonDaysLeft()
            => Math.Max(0, (int)Math.Ceiling((CurrentSeason().End - SaveClock.UtcNow()).TotalDays));

        /// <summary>The pass block for the running season, replacing it if the season turned.</summary>
        public PassState Pass
        {
            get
            {
                var season = CurrentSeason();
                if (Data.pass == null || Data.pass.season != season.Id)
                    Data.pass = new PassState { season = season.Id };
                return Data.pass;
            }
        }

        public bool PassPaid => Pass.paid != 0;

        public int PassLevel
        {
            get
            {
                int xp = Pass.xp, level = 1;
                foreach (var r in Db.PassLevels) if (xp >= r.xp) level = r.level;
                return level;
            }
        }

        public int PassMaxLevel
            => Db.PassLevels.Count > 0 ? Db.PassLevels[Db.PassLevels.Count - 1].level : 1;

        /// <summary>XP into the current level, and what the next one costs. (0, 0) at the cap.</summary>
        public (int into, int span) PassLevelProgress()
        {
            int level = PassLevel;
            PassLevelRow cur = null, next = null;
            foreach (var r in Db.PassLevels)
            {
                if (r.level == level) cur = r;
                if (r.level == level + 1) next = r;
            }
            if (cur == null || next == null) return (0, 0);
            return (Pass.xp - cur.xp, next.xp - cur.xp);
        }

        /// <summary>Adds pass XP, clamped at the last level's requirement. Returns levels gained.</summary>
        public int AddPassXp(int amount)
        {
            if (amount <= 0 || Db.PassLevels.Count == 0) return 0;
            int before = PassLevel;
            int cap = Db.PassLevels[Db.PassLevels.Count - 1].xp;
            Pass.xp = Math.Min(cap, Pass.xp + amount);
            MarkDirty();
            return PassLevel - before;
        }

        public bool PassFreeClaimable(PassLevelRow row)
            => row != null && row.level <= PassLevel && !Holds(Pass.gf, row.level);

        public bool PassPaidClaimable(PassLevelRow row)
            => row != null && PassPaid && row.level <= PassLevel && !Holds(Pass.gp, row.level);

        static bool Holds(Dictionary<string, bool> map, int level)
            => map != null && map.TryGetValue(level.ToString(), out var v) && v;

        /// <summary>How many reward cells are waiting, across both tracks.</summary>
        public int PassClaimableCount()
        {
            int n = 0;
            foreach (var r in Db.PassLevels)
            {
                if (PassFreeClaimable(r)) n++;
                if (PassPaidClaimable(r)) n++;
            }
            return n;
        }

        public List<GrantedReward> ClaimPass(PassLevelRow row, bool paid)
        {
            bool allowed = paid ? PassPaidClaimable(row) : PassFreeClaimable(row);
            if (!allowed) return new List<GrantedReward>();

            (paid ? Pass.gp : Pass.gf)[row.level.ToString()] = true;
            var got = Grant(paid ? row.paidGroup : row.freeGroup);
            MarkDirty();
            return got;
        }

        public List<GrantedReward> ClaimAllPass()
        {
            var got = new List<GrantedReward>();
            foreach (var r in Db.PassLevels)
            {
                got.AddRange(ClaimPass(r, false));
                got.AddRange(ClaimPass(r, true));
            }
            return got;
        }
    }
}
