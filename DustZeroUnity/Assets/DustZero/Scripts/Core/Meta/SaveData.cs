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
        /// <summary>
        /// uid of the common gear this appliance wears, or 0 for none. Kept on the unit the
        /// way the original does, so removing a unit cannot leave a dangling equip entry.
        /// </summary>
        public int eq;
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

    /// <summary>
    /// 협동 청소 기록. 파트너마다 최고 웨이브를 따로 남기고, 마지막에 고른 파트너를
    /// 기억해 다음에 그 사람이 먼저 떠 있게 한다.
    /// </summary>
    public sealed class CoopRecord
    {
        /// <summary>Best wave survived, keyed by partner id.</summary>
        public Dictionary<string, int> best = new();

        /// <summary>The partner picked last, so the sheet opens on them.</summary>
        public string last = "rush";
    }


    /// <summary>
    /// 가전 대청소 리그 기록. 티어마다 최고 승수와 완주 여부를 남기고, 오늘 쓴 보상 판
    /// 수를 센다. 하루가 바뀌면 판 수만 0 으로 돌아간다.
    /// </summary>
    public sealed class LeagueRecord
    {
        /// <summary>도전할 수 있는 가장 높은 티어. 완주하면 하나 열린다.</summary>
        public int tierMax = 1;
        /// <summary>티어별 최고 승수.</summary>
        public Dictionary<string, int> best = new();
        /// <summary>완주한 티어.</summary>
        public Dictionary<string, bool> cleared = new();
        /// <summary>마지막으로 고른 티어 — 다음에 그 자리에서 열린다.</summary>
        public int lastTier = 1;

        /// <summary>Local calendar day this record belongs to, "yyyy-M-d".</summary>
        public string day;
        /// <summary>보상 판을 오늘 몇 번 썼는지.</summary>
        public int runs;
    }

    /// <summary>
    /// Season pass progress. Reset wholesale when the season rolls over, exactly as the
    /// original does: a new season is a new object, not a cleared one.
    /// </summary>
    public sealed class PassState
    {
        public int season;
        public int xp;
        /// <summary>1 once the premium track is bought for this season.</summary>
        public int paid;
        /// <summary>level -> claimed, free track.</summary>
        public Dictionary<string, bool> gf = new();
        /// <summary>level -> claimed, premium track.</summary>
        public Dictionary<string, bool> gp = new();
    }

    /// <summary>Shop purchase counters. Daily and weekly buckets clear on their own keys.</summary>
    public sealed class ShopState
    {
        public string day = "";
        public string week = "";
        /// <summary>product id -> count bought today.</summary>
        public Dictionary<string, int> dc = new();
        /// <summary>product id -> count bought this week.</summary>
        public Dictionary<string, int> wc = new();
        /// <summary>product id -> count bought ever.</summary>
        public Dictionary<string, int> ac = new();
        /// <summary>product id -> the first-purchase bonus has been paid.</summary>
        public Dictionary<string, bool> first = new();
        /// <summary>product id -> unix ms when an ad product may be watched again.</summary>
        public Dictionary<string, long> cd = new();
        public int noAds;
    }

    /// <summary>The seven-day attendance wheel: which square is next, and when it last turned.</summary>
    public sealed class AttendanceState
    {
        /// <summary>Squares already taken, so the next one is day n + 1.</summary>
        public int n;
        public string last = "";
    }

    /// <summary>
    /// Sound settings, matching the original's `opt` blob. No migration entry is needed: the
    /// field initializer below means a file written before this existed deserializes to these
    /// defaults rather than null.
    /// </summary>
    public sealed class AudioOptions
    {
        /// <summary>BGM volume 0-100, as the original's slider stored it.</summary>
        public int bgm = 60;
        /// <summary>1 silences the music without losing the volume setting.</summary>
        public int mute;
    }

    /// <summary>
    /// One common gear piece the player owns. Common gear is a *copy*, not a type: two
    /// 1101s level independently, so each needs its own identity - hence uid, which is also
    /// what the equip map points at.
    /// </summary>
    public sealed class OwnedGear
    {
        /// <summary>Stable per-copy id, assigned on acquisition and never reused.</summary>
        public int uid;
        /// <summary>gear.id of the type.</summary>
        public int id;
        public int lv;
        /// <summary>Enhancement stones already sunk in, so dismantling can refund a share.</summary>
        public int sp;
    }

    /// <summary>
    /// An appliance's exclusive gear. There is at most one per appliance and it is never a
    /// copy, so it is keyed by unit id rather than carrying a uid.
    /// </summary>
    public sealed class ExclusiveGear
    {
        public int lv;
        /// <summary>Duplicate pulls raise this, capped at config.exclMaxStar.</summary>
        public int star;
        public int sp;
    }

    /// <summary>
    /// 오늘의 청소 일지. 대청소·협동·리그가 같은 점수통에 넣고, 점수마다 크리스탈 상자가
    /// 열린다. 하루가 바뀌면 통째로 새로 만들고, 어제 못 채운 몫은 하루만 따라온다.
    /// </summary>
    public sealed class PoolState
    {
        /// <summary>Local calendar day this record belongs to, "yyyy-M-d".</summary>
        public string day = "";
        public int pts;
        /// <summary>Boxes already taken: the table's box number, or "c" for the carry box.</summary>
        public List<string> got = new();
        /// <summary>Extra points carried in from yesterday; 0 when there is no carry box.</summary>
        public int carry;
    }

    public sealed class SaveData
    {
        /// <summary>Current on-disk schema version. Raise it in lockstep with SaveMigrator.</summary>
        public const int CurrentVersion = 7;

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

        /// <summary>
        /// Lobby onboarding progress: flow ("first" or "levelup") -> steps already completed.
        /// A missing key means the flow has not started; reaching the table's row count for
        /// that flow means it is done. Independent of tutOff/tut, which are the battle guide.
        /// </summary>
        public Dictionary<string, int> guide = new();

        /// <summary>Common gear the player owns, newest last.</summary>
        public List<OwnedGear> gear = new();
        /// <summary>Next uid to hand out. Monotonic so a dismantled uid is never reissued.</summary>
        public int gearUid = 1;
        /// <summary>unit id -> that appliance's exclusive gear, once obtained.</summary>
        public Dictionary<string, ExclusiveGear> excl = new();

        public SurvivorRecord surv = new();
        public CoopRecord coop = new();
        public LeagueRecord league = new();

        public PassState pass = new();
        public ShopState shop = new();
        public AttendanceState att = new();
        public PoolState pool = new();
        public AudioOptions opt = new();

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

        public int GuideStep(string flow) => guide.TryGetValue(flow, out var v) ? v : 0;

        public void AdvanceGuide(string flow) => guide[flow] = GuideStep(flow) + 1;

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
                guide = new Dictionary<string, int>(guide),
                idleBank = new IdleBank { coin = idleBank.coin, part = idleBank.part, crystal = idleBank.crystal },
                surv = new SurvivorRecord
                {
                    day = surv.day, runs = surv.runs, bestT = surv.bestT,
                    bestKills = surv.bestKills, bestEver = surv.bestEver, leader = surv.leader,
                    marks = new List<int>(surv.marks ?? new List<int>()),
                },
            };
            foreach (var kv in owned) c.owned[kv.Key] = new OwnedUnit { lv = kv.Value.lv, star = kv.Value.star };

            c.pass = new PassState
            {
                season = pass.season, xp = pass.xp, paid = pass.paid,
                gf = new Dictionary<string, bool>(pass.gf),
                gp = new Dictionary<string, bool>(pass.gp),
            };
            c.shop = new ShopState
            {
                day = shop.day, week = shop.week, noAds = shop.noAds,
                dc = new Dictionary<string, int>(shop.dc),
                wc = new Dictionary<string, int>(shop.wc),
                ac = new Dictionary<string, int>(shop.ac),
                first = new Dictionary<string, bool>(shop.first),
                cd = new Dictionary<string, long>(shop.cd),
            };
            c.league = new LeagueRecord
            {
                tierMax = league.tierMax, lastTier = league.lastTier,
                day = league.day, runs = league.runs,
                best = new Dictionary<string, int>(league.best ?? new Dictionary<string, int>()),
                cleared = new Dictionary<string, bool>(league.cleared ?? new Dictionary<string, bool>()),
            };
            c.att = new AttendanceState { n = att.n, last = att.last };
            c.pool = new PoolState
            {
                day = pool.day, pts = pool.pts, carry = pool.carry,
                got = new List<string>(pool.got ?? new List<string>()),
            };
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

        /// <summary>Yesterday's key, for the daily log's carry-over.</summary>
        public static string YesterdayKey()
        {
            var d = UtcNow().ToLocalTime().AddDays(-1);
            return $"{d.Year}-{d.Month}-{d.Day}";
        }

        /// <summary>
        /// The Monday of the current local week, used for the shop's weekly limits. Matches
        /// the original, which shifts Sunday back six days rather than forward one.
        /// </summary>
        public static string WeekKey()
        {
            var d = UtcNow().ToLocalTime().Date;
            d = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
            return $"{d.Year}-{d.Month}-{d.Day}";
        }
    }
}
