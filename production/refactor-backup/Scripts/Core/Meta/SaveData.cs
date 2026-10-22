// The persisted player profile.
//
// Deliberately a flat, explicit DTO: it is the file format, so every field here is a
// compatibility commitment. Version is bumped by SaveMigrator whenever the shape changes.
using System;
using System.Collections.Generic;

namespace DustZero.Core.Meta
{
    public sealed class OwnedUnit
    {
        public int lv = 1;
        /// <summary>Limit-break count from duplicate pulls, capped at config.starMax.</summary>
        public int star;
    }

    public sealed class IdleBank
    {
        /// <summary>Fractional carry-over kept when a claim rounds down, so nothing is lost.</summary>
        public double coin;
        public double part;
        public double crystal;
    }

    public sealed class SurvivorRecord
    {
        /// <summary>Local calendar day this record belongs to, "yyyy-M-d".</summary>
        public string day;
        /// <summary>Part/coin runs used today.</summary>
        public int runs;
        /// <summary>Best survival time today, in whole seconds.</summary>
        public int bestT;
        public int bestKills;
        /// <summary>Lifetime best survival time, in whole seconds.</summary>
        public int bestEver;
        public string leader;
        /// <summary>
        /// surv_reward marks already paid today. Crystal is capped by paying each mark once
        /// per day, so this list is what stops a replay from farming the same milestone.
        /// </summary>
        public List<int> marks = new();
    }

    public sealed class SaveData
    {
        /// <summary>Current on-disk schema version. Raise it in lockstep with SaveMigrator.</summary>
        public const int CurrentVersion = 3;

        public int version = CurrentVersion;

        /// <summary>item id -> count. 1 is coin, 2 is crystal, 101-104 are family parts.</summary>
        public Dictionary<string, double> items = new();

        /// <summary>unit id -> level and limit-break.</summary>
        public Dictionary<string, OwnedUnit> owned = new();

        public List<string> deck = new();

        /// <summary>Highest stage order cleared. 0 means nothing cleared yet.</summary>
        public int best;

        /// <summary>Unix milliseconds UTC when idle accrual last started. 0 means not started.</summary>
        public long idleSince;
        public IdleBank idleBank = new();

        /// <summary>stage id -> best star count.</summary>
        public Dictionary<string, int> stars = new();
        /// <summary>star_reward id -> claimed. Presence is what blocks a second payout.</summary>
        public Dictionary<string, bool> starClaimed = new();

        public int speed = 1;
        public string rep;
        /// <summary>Calendar day of the last attendance claim, "yyyy-M-d".</summary>
        public string daily;

        /// <summary>tutorial id -> seen.</summary>
        public Dictionary<string, bool> tut = new();
        /// <summary>1 when the player switched tutorials off.</summary>
        public int tutOff;
        /// <summary>Highest unconditional tutorial group opened so far.</summary>
        public int tutOpen;

        public SurvivorRecord surv = new();

        public double Item(int id) => items.TryGetValue(id.ToString(), out var v) ? v : 0d;

        public void AddItem(int id, double n)
        {
            var key = id.ToString();
            items[key] = (items.TryGetValue(key, out var v) ? v : 0d) + n;
        }

        public bool Owns(string unitId) => owned.ContainsKey(unitId);

        public OwnedUnit Own(string unitId) => owned.TryGetValue(unitId, out var o) ? o : null;

        public int StarsOf(int stageId) => stars.TryGetValue(stageId.ToString(), out var v) ? v : 0;

        public void SetStars(int stageId, int value) => stars[stageId.ToString()] = value;

        public bool IsStarRewardClaimed(int rewardId)
            => starClaimed.TryGetValue(rewardId.ToString(), out var v) && v;

        public void MarkStarRewardClaimed(int rewardId) => starClaimed[rewardId.ToString()] = true;

        public bool SawTutorial(int id) => tut.TryGetValue(id.ToString(), out var v) && v;

        public void MarkTutorial(int id) => tut[id.ToString()] = true;

        public SaveData Clone()
        {
            var c = new SaveData
            {
                version = version,
                best = best,
                idleSince = idleSince,
                speed = speed,
                rep = rep,
                daily = daily,
                tutOff = tutOff,
                tutOpen = tutOpen,
                deck = new List<string>(deck),
                items = new Dictionary<string, double>(items),
                stars = new Dictionary<string, int>(stars),
                starClaimed = new Dictionary<string, bool>(starClaimed),
                tut = new Dictionary<string, bool>(tut),
                idleBank = new IdleBank { coin = idleBank.coin, part = idleBank.part, crystal = idleBank.crystal },
                surv = new SurvivorRecord
                {
                    day = surv.day, runs = surv.runs, bestT = surv.bestT,
                    bestKills = surv.bestKills, bestEver = surv.bestEver, leader = surv.leader,
                    marks = new List<int>(surv.marks ?? new List<int>()),
                },
            };
            foreach (var kv in owned) c.owned[kv.Key] = new OwnedUnit { lv = kv.Value.lv, star = kv.Value.star };
            return c;
        }
    }

    public static class SaveClock
    {
        /// <summary>Overridable so idle accrual and daily resets can be tested without waiting.</summary>
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;

        public static long NowUnixMs()
            => (long)(UtcNow() - DateTime.UnixEpoch).TotalMilliseconds;

        /// <summary>Local calendar key used for attendance and daily survivor limits.</summary>
        public static string TodayKey()
        {
            var d = UtcNow().ToLocalTime();
            return $"{d.Year}-{d.Month}-{d.Day}";
        }
    }
}
