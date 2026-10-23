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
    /// 먼지 타워 기록. 층마다 첫 클리어와 보너스 목표를 따로 센다 — 둘 다 한 번씩만 준다.
    /// 지난 편성은 칸과 가전을 같은 자리끼리 짝지은 두 줄로 남긴다(Dictionary 보다
    /// JSON 으로 오갈 때 순서가 안 흔들린다).
    /// </summary>
    public sealed class TowerRecord
    {
        /// <summary>깬 가장 높은 층.</summary>
        public int best;
        public Dictionary<string, bool> cleared = new();
        public Dictionary<string, bool> bonus = new();
        /// <summary>층마다 누구로 깼는지(최대 셋).</summary>
        public Dictionary<string, List<string>> who = new();
        /// <summary>지난 편성의 칸 번호.</summary>
        public List<int> form = new();
        /// <summary>같은 자리의 가전 id.</summary>
        public List<string> formTypes = new();
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

        // ---- 10.22-b 설정. 원본 `S.opt.{vib,shk,dmg,fx}` 이고 1 이 켬이다.
        /// <summary>진동.</summary>
        public int vib = 1;
        /// <summary>화면 흔들림.</summary>
        public int shk = 1;
        /// <summary>피해 숫자 띄우기.</summary>
        public int dmg = 1;
        /// <summary>
        /// 효과 줄이기. 원본은 **문자열**로 `'lo'` 일 때만 줄인다(`S.opt.fx==='lo'`).
        /// 다른 셋과 달리 0/1 이 아니므로 bool 로 바꾸지 않는다 — 나중에 'mid' 같은
        /// 단계가 생겨도 그대로 읽힌다.
        /// </summary>
        public string fx = "";
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

    /// <summary>
    /// 가전 호감도. 원본은 `S.aff[가전id] = {xp, lv, day, talk, gift}` 로 들고 있다.
    /// day 가 오늘이 아니면 talk·gift 는 0 으로 되돌아간다 — 날짜를 따로 세는 칸을
    /// 두지 않고 "언제 센 것인지"를 같이 적는 방식이라, 자정에 돌릴 코드가 필요 없다.
    /// </summary>
    public sealed class Affinity
    {
        public int xp;
        public int lv = 1;
        /// <summary>talk·gift 를 센 날("yyyy-M-d").</summary>
        public string day = "";
        /// <summary>오늘 말 걸었으면 1(하루 한 번).</summary>
        public int talk;
        /// <summary>오늘 준 간식 수. 상한은 config.affGiftDaily.</summary>
        public int gift;
    }

    /// <summary>먼지 도감. 원본 `S.codex = {seen, kill, got}`.</summary>
    public sealed class CodexState
    {
        /// <summary>적 id -> 한 번이라도 봤나.</summary>
        public Dictionary<string, bool> seen = new();
        /// <summary>적 id -> 누적 처치 수.</summary>
        public Dictionary<string, double> kill = new();
        /// <summary>적 id -> 발견 보상(크리스탈)을 받았나.</summary>
        public Dictionary<string, bool> got = new();
    }

    /// <summary>
    /// 미션 한 묶음. 일일·주간은 `k` 가 날/주 열쇠이고 그것이 바뀌면 통째로 새로
    /// 시작한다(원본 `msSt()` 가 하는 일). 이벤트 묶음은 열쇠 없이 회차마다 하나다.
    /// </summary>
    public sealed class MissionBucket
    {
        /// <summary>이 묶음이 속한 날/주 열쇠. 이벤트 묶음에서는 빈 문자열.</summary>
        public string k = "";
        /// <summary>센 값(미션 key -> 누적).</summary>
        public Dictionary<string, double> c = new();
        /// <summary>받은 미션 id.</summary>
        public Dictionary<string, bool> got = new();
        /// <summary>받은 상자 id.</summary>
        public Dictionary<string, bool> ch = new();
    }

    /// <summary>업적은 날이 바뀌어도 이어지므로 받은 것만 들고 있으면 된다.</summary>
    public sealed class AchieveState
    {
        public Dictionary<string, bool> got = new();
    }

    /// <summary>미션·업적·이벤트. 원본 `S.ms = {st, d, w, a:{got}, ev}`.</summary>
    public sealed class MissionState
    {
        /// <summary>상태형 값(최고 기록처럼 누적이 아니라 '지금 얼마인지'를 보는 것).</summary>
        public Dictionary<string, double> st = new();
        public MissionBucket d = new();
        public MissionBucket w = new();
        public AchieveState a = new();
        /// <summary>이벤트 id(회차 포함) -> 그 회차의 진행.</summary>
        public Dictionary<string, MissionBucket> ev = new();
    }

    /// <summary>퀘스트. 원본 `S.quest = {got}` — 받은 퀘스트만 적는다(진행은 미션 쪽 카운터를 본다).</summary>
    public sealed class QuestState
    {
        public Dictionary<string, bool> got = new();
    }

    /// <summary>가전 스토리 미니게임 기록.</summary>
    public sealed class StoryGame
    {
        public double best;
        /// <summary>한 번이라도 성공했나(첫 성공에만 호감도를 준다).</summary>
        public int ok;
    }

    /// <summary>가전 스토리. 원본 `S.story = {tab, done, game}`.</summary>
    public sealed class StoryState
    {
        /// <summary>가전 id -> 스토리 탭 첫 방문 크리스탈을 받았나.</summary>
        public Dictionary<string, bool> tab = new();
        /// <summary>에피소드 id -> 봤나.</summary>
        public Dictionary<string, bool> done = new();
        /// <summary>미니게임 id -> 기록.</summary>
        public Dictionary<string, StoryGame> game = new();
    }

    /// <summary>우편 한 통. id 는 원본이 'm1', 'm2' 처럼 붙이므로 문자열이다.</summary>
    public sealed class MailItem
    {
        public string id = "";
        public string from = "";
        public string title = "";
        public string body = "";
        /// <summary>첨부 보상 그룹. 빈 목록이면 읽기만 하는 안내문이다.</summary>
        public List<int> groups = new();
        /// <summary>받은 시각(Unix ms).</summary>
        public long at;
        /// <summary>사라질 시각(Unix ms). 0 이면 안 사라진다.</summary>
        public long exp;
        public int read;
        /// <summary>보상을 받아 처리가 끝났나.</summary>
        public int done;
    }

    /// <summary>우편함. 원본 `S.mail = {sent, list, seq}`.</summary>
    public sealed class MailState
    {
        /// <summary>이미 보낸 우편의 열쇠(표 id 또는 'ev&lt;이벤트id&gt;'). 두 번 보내지 않으려고 둔다.</summary>
        public Dictionary<string, bool> sent = new();
        public List<MailItem> list = new();
        /// <summary>다음 우편 번호. 지운 우편의 번호를 다시 쓰지 않는다.</summary>
        public int seq;
    }

    public sealed class SaveData
    {
        /// <summary>Current on-disk schema version. Raise it in lockstep with SaveMigrator.</summary>
        public const int CurrentVersion = 8;

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
        public TowerRecord tower = new();

        public PassState pass = new();
        public ShopState shop = new();
        public AttendanceState att = new();
        public PoolState pool = new();
        public AudioOptions opt = new();

        // ---- 10.22-g 에서 들어온 것들. 이름과 모양은 원본 `S.<키>` 를 그대로 따른다.

        /// <summary>가전 id -> 호감도.</summary>
        public Dictionary<string, Affinity> aff = new();
        /// <summary>출석 팝업을 자동으로 띄운 날("yyyy-M-d"). 하루 한 번만 띄운다.</summary>
        public string attAuto = "";
        public CodexState codex = new();
        /// <summary>SSR 없이 뽑은 횟수. config.gachaPity 에 닿으면 다음 1회가 확정이다.</summary>
        public int gpity;
        /// <summary>심화 모드에서 깬 가장 높은 order.</summary>
        public int hbest;
        /// <summary>심화 스테이지 id -> 별 수. 보통 스테이지의 stars 와 따로 센다.</summary>
        public Dictionary<string, int> hstars = new();
        /// <summary>리그 판 배속(1·2). 화면 설정이라 판이 끝나도 남는다.</summary>
        public int lgSpeed = 1;
        public MailState mail = new();
        public MissionState ms = new();
        public QuestState quest = new();
        public StoryState story = new();
        /// <summary>무료 첫 소환 안내를 썼나.</summary>
        public int tutPull;
        /// <summary>마지막 전투 안내가 뜬 전투 시각. 안내가 몰려 뜨는 것을 막는 데 쓴다.</summary>
        public float tutLastT;
        /// <summary>'새 콘텐츠 열림' 안내를 이미 본 콘텐츠 id.</summary>
        public Dictionary<string, bool> unlSeen = new();

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

        static MissionBucket CloneBucket(MissionBucket b) => new()
        {
            k = b?.k ?? "",
            c = new Dictionary<string, double>(b?.c ?? new Dictionary<string, double>()),
            got = new Dictionary<string, bool>(b?.got ?? new Dictionary<string, bool>()),
            ch = new Dictionary<string, bool>(b?.ch ?? new Dictionary<string, bool>()),
        };

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
            c.tower = new TowerRecord
            {
                best = tower.best,
                cleared = new Dictionary<string, bool>(tower.cleared ?? new Dictionary<string, bool>()),
                bonus = new Dictionary<string, bool>(tower.bonus ?? new Dictionary<string, bool>()),
                form = new List<int>(tower.form ?? new List<int>()),
                formTypes = new List<string>(tower.formTypes ?? new List<string>()),
            };
            foreach (var kv in tower.who ?? new Dictionary<string, List<string>>())
                c.tower.who[kv.Key] = new List<string>(kv.Value ?? new List<string>());
            c.att = new AttendanceState { n = att.n, last = att.last };
            c.pool = new PoolState
            {
                day = pool.day, pts = pool.pts, carry = pool.carry,
                got = new List<string>(pool.got ?? new List<string>()),
            };
            c.opt = new AudioOptions
            {
                bgm = opt.bgm, mute = opt.mute,
                vib = opt.vib, shk = opt.shk, dmg = opt.dmg, fx = opt.fx,
            };

            // ---- 10.22-g. 얕게 베끼면 사본을 고칠 때 원본이 같이 바뀐다.
            c.attAuto = attAuto; c.gpity = gpity; c.hbest = hbest;
            c.lgSpeed = lgSpeed; c.tutPull = tutPull; c.tutLastT = tutLastT;
            c.hstars = new Dictionary<string, int>(hstars ?? new Dictionary<string, int>());
            c.unlSeen = new Dictionary<string, bool>(unlSeen ?? new Dictionary<string, bool>());
            foreach (var kv in aff ?? new Dictionary<string, Affinity>())
                c.aff[kv.Key] = new Affinity
                {
                    xp = kv.Value.xp, lv = kv.Value.lv, day = kv.Value.day,
                    talk = kv.Value.talk, gift = kv.Value.gift,
                };
            c.codex = new CodexState
            {
                seen = new Dictionary<string, bool>(codex.seen ?? new Dictionary<string, bool>()),
                kill = new Dictionary<string, double>(codex.kill ?? new Dictionary<string, double>()),
                got = new Dictionary<string, bool>(codex.got ?? new Dictionary<string, bool>()),
            };
            c.quest = new QuestState
            {
                got = new Dictionary<string, bool>(quest.got ?? new Dictionary<string, bool>()),
            };
            c.story = new StoryState
            {
                tab = new Dictionary<string, bool>(story.tab ?? new Dictionary<string, bool>()),
                done = new Dictionary<string, bool>(story.done ?? new Dictionary<string, bool>()),
            };
            foreach (var kv in story.game ?? new Dictionary<string, StoryGame>())
                c.story.game[kv.Key] = new StoryGame { best = kv.Value.best, ok = kv.Value.ok };
            c.mail = new MailState
            {
                seq = mail.seq,
                sent = new Dictionary<string, bool>(mail.sent ?? new Dictionary<string, bool>()),
            };
            foreach (var m in mail.list ?? new List<MailItem>())
                c.mail.list.Add(new MailItem
                {
                    id = m.id, from = m.from, title = m.title, body = m.body,
                    at = m.at, exp = m.exp, read = m.read, done = m.done,
                    groups = new List<int>(m.groups ?? new List<int>()),
                });
            c.ms = new MissionState
            {
                st = new Dictionary<string, double>(ms.st ?? new Dictionary<string, double>()),
                d = CloneBucket(ms.d), w = CloneBucket(ms.w),
                a = new AchieveState
                {
                    got = new Dictionary<string, bool>(ms.a?.got ?? new Dictionary<string, bool>()),
                },
            };
            foreach (var kv in ms.ev ?? new Dictionary<string, MissionBucket>())
                c.ms.ev[kv.Key] = CloneBucket(kv.Value);
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
