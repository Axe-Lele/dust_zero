// Plain DTOs mirroring data/*.json one-for-one.
//
// Field names must stay identical to the JSON keys produced by tools/build.py, so that
// editing tables/*.xlsx is the only place balance numbers ever live. Nothing in this file
// assigns a default value that could silently stand in for a missing table entry -
// TableValidator is what reports gaps, with a message naming the table and row.
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DustZero.Data
{
    /// <summary>
    /// A cell that the spreadsheet may fill with either a number or an identifier string
    /// (tutorial.param / tutorial.untilParam). Kept as text and parsed on demand so a
    /// mistyped cell surfaces at validation time rather than as a silent 0.
    /// </summary>
    public readonly struct Scalar
    {
        public readonly string Raw;

        public Scalar(string raw) { Raw = raw; }

        public bool IsEmpty => string.IsNullOrEmpty(Raw);
        public string AsString() => Raw ?? string.Empty;

        public float AsFloat(float fallback = 0f)
            => float.TryParse(Raw, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public bool TryAsFloat(out float value)
            => float.TryParse(Raw, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out value);

        public override string ToString() => AsString();
    }

    public sealed class ScalarConverter : JsonConverter<Scalar>
    {
        public override Scalar ReadJson(JsonReader reader, System.Type t, Scalar existing, bool hasExisting, JsonSerializer s)
            => reader.Value == null
                ? new Scalar(string.Empty)
                : new Scalar(System.Convert.ToString(reader.Value, System.Globalization.CultureInfo.InvariantCulture));

        public override void WriteJson(JsonWriter writer, Scalar value, JsonSerializer s)
            => writer.WriteValue(value.Raw);
    }

    // ---------------------------------------------------------------- config / strings

    /// <summary>config.json - a single object, not a row list.</summary>
    public sealed class ConfigRow
    {
        /// <summary>
        /// Keys the typed fields below do not name, kept rather than dropped because some
        /// config entries are addressed by a composed name: 10.17-b's support growth reads
        /// <c>&lt;unitId&gt;LvBonus</c> / <c>&lt;unitId&gt;StarBonus</c>, which exist only for
        /// the appliances that have them. Listing them as fields would hardcode in C# the
        /// very thing the table decides.
        /// </summary>
        [Newtonsoft.Json.JsonExtensionData]
        System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> _extra;

        /// <summary>
        /// A config number addressed by name, for keys no field covers.
        ///
        /// <example><code>
        /// if (cfg.TryNum("genLvBonus", out float perLevel)) { ... }
        /// </code></example>
        /// </summary>
        public bool TryNum(string key, out float value)
        {
            value = 0f;
            if (_extra == null || !_extra.TryGetValue(key, out var token) || token == null)
                return false;
            try { value = token.ToObject<float>(); return true; }
            catch { return false; }
        }

        public int deckMax;
        /// <summary>Absolute ceiling across all limit-breaks (45 in 10.15-h).</summary>
        public int lvMax;
        /// <summary>Level cap at zero limit-breaks; unit_star.lvCap raises it from there.</summary>
        public int lvCapBase;
        public int starMax;
        public float lvBonus;
        public float starBonus;
        public float idleCapHours;
        public int maxUnits;
        public float startPower;
        public float regenPerSec;
        public float redeployCooldown;
        public float overheatSec;
        /// <summary>10.17-a: how far ahead an entrance warns, in seconds (was a hard 3).</summary>
        public float spawnWarnSec;
        public int lifeMax;
        public int startRewardGroup;
        public int attendanceRewardGroup;
        public int dupeOverflowRewardGroup;
        public int repRarityAutoSwap;

        /// <summary>Shards granted by a duplicate pull, indexed by rarity R/SR/SSR.</summary>
        public int[] dupeShards;
        /// <summary>Shards needed to recruit an unowned appliance, indexed by rarity.</summary>
        public int[] shardRecruit;
        /// <summary>Power cost of each extra deploy slot: 8 -> 9 -> 10.</summary>
        public float[] multitapCost;

        // --- survivor (infinite since 10.15-h; survDuration and survClearCrystal are gone) ---
        public float survLeaderHp;
        public float survMoveSpeed;
        public float survPickup;
        public float survXpBase;
        public float survXpStep;
        /// <summary>Quadratic term that slows late-run level-ups.</summary>
        public float survXpQuad;
        public int survMaxComp;
        public int survCompMaxLv;
        public int survWeaponMaxLv;
        public float survArena;
        public int survDailyRewards;
        public int survPartPer30s;
        public int survCoinPerKill;
        /// <summary>Seconds before the compounding difficulty ramp begins.</summary>
        public float survScaleStart;
        public float survHpGrowthPerMin;
        public float survDmgGrowthPerMin;
        public float survSpdGrowthPerMin;
        public float survSpdMax;
        public float survBossFirst;
        public float survBossEvery;
        public float survBossHp;
        public float survBossHpStep;
        public int survShardPerMin;
        public int survShardMax;

        // --- shop / season pass ---
        public int adWatchSec;
        public int monthPassDays;
        public int monthPassDailyGroup;
        /// <summary>Season start as "yyyy-MM-dd" local date.</summary>
        public string passSeasonStart;
        public int passSeasonDays;
        public int passLevelBuyCrystal;
        public int passXpStageFirst;
        public int passXpStageRepeat;
        public int passXpSurvPer30s;
        public int passXpSurvMax;
        public int passXpAttend;
        public int passXpAd;

        // ---- 오늘의 청소 일지 (10.18-h). 대청소·협동·리그가 한 점수통을 공유한다.
        /// <summary>Points per full minute survived in 대청소.</summary>
        public int poolSurvPerMin;
        /// <summary>Points per wave held in 협동 청소.</summary>
        public int poolCoopWave;
        /// <summary>Points per round won in 리그.</summary>
        public int poolLeagueWin;
        /// <summary>Points for finishing a 리그 run.</summary>
        public int poolLeagueClear;
        /// <summary>1 = yesterday's unfilled points roll over as one extra box.</summary>
        public int poolCarry;

        // ---- 먼지 타워 (10.18-g)
        /// <summary>Seconds one floor's fight may run before it is called.</summary>
        public int towerTime;
        /// <summary>Account growth the tower passes through; 1.0 = all of it.</summary>
        public float towerGuestPow;
        /// <summary>The purifier's health — the thing the dust is trying to reach.</summary>
        public float towerPurHp;
        /// <summary>A boss floor every N floors.</summary>
        public int towerBossEvery;

        // ---- 협동 청소 (10.18-h ~ 10.19-d). 고리를 도는 먼지를 봇 파트너와 함께 막는다.
        /// <summary>Seconds between waves.</summary>
        public int coopWaveGap;
        /// <summary>Enemy health at wave 1, as a multiple of the reference account's power.</summary>
        public float coopHp0;
        /// <summary>Health multiplier per wave.</summary>
        public float coopHpG;
        public int coopCnt0;
        public float coopCntG;
        public int coopCntMax;
        /// <summary>Boss health relative to the wave's normal health.</summary>
        public float coopBossHp;
        /// <summary>Dust on the ring that ends the run. A boss counts as five.</summary>
        public int coopLoopMax;
        /// <summary>Ring load that starts warning the player.</summary>
        public int coopLoopWarn;
        /// <summary>Speed a held enemy keeps, as a fraction.</summary>
        public float coopHoldSpeed;
        public float coopStartPow;
        /// <summary>First summon's cost; each later one adds coopSumStep.</summary>
        public float coopSum0;
        public float coopSumStep;
        /// <summary>Generator regen per star.</summary>
        public float coopGenMul;
        public float coopKillPow;
        public float coopBossKillPow;
        public float coopWavePow0;
        public float coopWavePowStep;
        /// <summary>How many melee appliances may stand on the ring, per side.</summary>
        public int coopLineMax;
        /// <summary>Seconds before a moved appliance may move again.</summary>
        public float coopMoveCd;
        /// <summary>Reroll cost per star.</summary>
        public float coopRerollCost;
        public int coopStarMax;
        /// <summary>Stat multiplier by star, index = star - 1.</summary>
        public float[] coopStarMul;
        /// <summary>Type-upgrade cost by level already bought.</summary>
        public float[] coopUpCost;
        /// <summary>Attack added per type-upgrade level.</summary>
        public float coopUpStep;
        public int coopCoinPerWave;
        public int coopPassXpPerWave;
        public int coopPassXpMax;

        public int[] starLifeMin;
        public float queueHeatRatio;
        public float powerMax;
        public float retreatRefund;
        public float naturalCoolPerSec;
        public float fireTileHeatPerSec;
        public float wetTileCoolPerSec;
        public float wetTileEnemySpeedMul;
        // 10.16-n additions, read straight from 01_Config.xlsx. Several are per-grade
        // arrays rather than scalars - typing those as single values would make
        // Newtonsoft throw on load rather than quietly misread them.
        public int survStonePerMin;
        /// <summary>Per grade: [5, 10, 15, 10].</summary>
        public int[] gearMaxLv;
        /// <summary>Per grade: [2, 4, 7, 12].</summary>
        public int[] gearStonePerLv;
        /// <summary>Per grade: [300, 600, 1000, 2000].</summary>
        public int[] gearCoinPerLv;
        public float gearStatPerLv;
        /// <summary>Per grade: [3, 10, 30].</summary>
        public int[] gearDismantle;
        public float gearRefund;
        public int gearFuseNeed;
        public int exclMaxStar;
        public float exclStarBonus;
        public int exclDupeEssence;
        public int gearEssencePerPull;
        public int exclExchange;
        public int tutGuestMax;
        public int deployCooldown;
        public float rugTileEnemySpeedMul;
        public float oilTileEnemySpeedMul;
        public int oilTileHeatPerSec;
        public float armorMax;
        public float stunBossMul;

    }

    // ---------------------------------------------------------------- content rows

    /// <summary>
    /// unit_art: which images a appliance uses and how it moves in the lobby room.
    /// Names are file stems, not paths - the editor resolves them under Art/Sprites and
    /// Art/Illust so the table stays free of folder layout.
    /// </summary>
    public sealed class UnitArtRow
    {
        public string id;
        /// <summary>Battle and lobby chibi, from Art/Sprites/&lt;chibi&gt;.png.</summary>
        public string chibi;
        /// <summary>The six illustration slots, from Art/Illust/&lt;name&gt;.png.</summary>
        public string full, turn, keys, face1, face2, face3;
        /// <summary>Lobby walk speed, in screen fractions per second.</summary>
        public float roomSpeed = 0.18f;
    }

    /// <summary>
    /// unit_behaviour: which coded behaviour an appliance drives.
    /// The behaviour itself is logic and stays in C#; which one a unit uses is data, so a new
    /// appliance that reuses an existing pattern needs no code at all.
    /// </summary>
    public sealed class UnitBehaviourRow
    {
        public string id;
        /// <summary>Defense behaviour key, or empty for a plain ranged attacker.</summary>
        public string defense;
        /// <summary>Survivor leader and companion behaviour keys; empty means unavailable.</summary>
        public string survLeader, survComp;
        /// <summary>1 when the appliance supports rather than attacks in 대청소.</summary>
        public int support;
        /// <summary>skill_param keys this behaviour reads, comma separated.</summary>
        public string skp;
    }

    public sealed class ItemRow
    {
        public int id;
        public string nameKey;
        /// <summary>Part family, or 0 for currency and shards.</summary>
        public int family;
        /// <summary>Set on appliance shards (items 301+); names the unit they break through.</summary>
        public string unitId;
    }

    public sealed class UnitRow
    {
        public string id;
        public int order;
        public int rarity;
        public int family;
        public int tile;
        public float cost;
        public float heat;
        public float atk;
        public float itv;
        public float range;
        public int block;
        public float sp;
        public float dur;
        public float cool;
        public int lineCount;
        public int atkTarget;
        public int skillTarget;
        public string nameKey;
        public string shortKey;
        public string roleKey;
        public string skillKey;
        public string descKey;
    }

    public sealed class UnitLevelupRow
    {
        public int id;
        public int lvMin;
        public int lvMax;
        public int coinBase;
        public int coinStep;
        public int partBase;
        public int partStep;
    }

    public sealed class EnemyRow
    {
        public string id;
        public string nameKey;
        public float hp;
        public float spd;
        public float dmg;
        public float itv;
        public float radius;
        public int weight;
        public int fly;
        public string look;
        public int boss;
        /// <summary>Fraction of incoming damage ignored.</summary>
        public float armor;
        /// <summary>Enemy id this one breaks into when killed, and how many.</summary>
        public string split;
        public int splitN;
        /// <summary>Enemy id this one calls in, how often (seconds) and how many.</summary>
        public string summon;
        public float summonEvery;
        public int summonN;
        public int ai;
    }

    public sealed class MapRow
    {
        public string id;
        public string floor;
        public string nameKey;
        public string goalKey;
        public string descKey;
        public string gimKey;
        public int noDeployFromEntrance;
        public int noDeployBeforeGoal;
        public string[] spawnKeys;
        public string[] tiles;
        /// <summary>paths[entrance][waypoint][0]=x, [1]=y. Last waypoint of path 0 is the goal tile.</summary>
        public int[][][] paths;
    }

    public sealed class StageRow
    {
        public int id;
        public int order;
        public int chapter;
        /// <summary>area.id this stage belongs to; the stage tab groups by it.</summary>
        public int area;
        public string no;
        public string mapId;
        public int waveGroup;
        public int boss;
        public float hpMul;
        /// <summary>Enemy damage multiplier, alongside hpMul.</summary>
        public float dmgMul = 1f;
        /// <summary>
        /// The loadout the stage was tuned against. refGear is a fraction of a full gear set,
        /// not a count - it carries values like 0.007, which an int field rejects outright.
        /// </summary>
        public int refLv, refStar;
        public float refGear;
        public int unlockCond;
        public int firstRewardGroup;
        public int idleCoin;
        public int idlePart;
        public int idleCrystal;
        /// <summary>Per-stage starting power; 0 falls back to config.startPower.</summary>
        public float startPower;
    }

    public sealed class WaveRow
    {
        public int groupId;
        public int seq;
        public float t;
        public string enemy;
        public int count;
        public float gap;
        public int entrance;
        /// <summary>
        /// Late-wave pressure, multiplied on top of stage.hpMul. 0 means 1 (no change);
        /// the final waves ramp toward 3 so a stalled defence eventually breaks.
        /// </summary>
        public float hpMul;
    }

    public sealed class RewardGroupRow
    {
        public int groupId;
        public int seq;
        public int kind;
        /// <summary>Item id when kind==Item, unit id (string) when kind==Unit.</summary>
        [JsonConverter(typeof(ScalarConverter))] public Scalar refId;
        public int min;
        public int max;
        public int prob;
    }

    public sealed class ConditionRow
    {
        public int id;
        public int kind;
        [JsonConverter(typeof(ScalarConverter))] public Scalar p1;
        [JsonConverter(typeof(ScalarConverter))] public Scalar p2;
    }

    public sealed class ContentUnlockRow
    {
        public string id;
        public int cond;
        public string lockKey;
    }

    public sealed class StarRewardRow
    {
        public int id;
        public int chapter;
        public int needStars;
        public int rewardGroup;
    }

    public sealed class SkillParamRow
    {
        public string unitId;
        public string key;
        public float value;
        public string note;
    }

    public sealed class GachaRow
    {
        public int id;
        public int poolId;
        public int costItem;
        public int costCount;
        public int pulls;
        public int guaranteeRarity;
        public int unlockCond;
        public string nameKey;
    }

    public sealed class GachaPoolRow
    {
        public int poolId;
        public string unitId;
        public float weight;
    }

    public sealed class TutorialRow
    {
        public int id;
        public int group;
        public int seq;
        public string cond;
        public int trigger;
        [JsonConverter(typeof(ScalarConverter))] public Scalar param;
        public int until;
        [JsonConverter(typeof(ScalarConverter))] public Scalar untilParam;
        public int pause;
        public string focus;
        /// <summary>Forces the deploy onto one tile, written "x,y". Empty leaves it free.</summary>
        public string tile;
        /// <summary>Stage id the forced tile applies to.</summary>
        public int tileStage;
        /// <summary>1 = only test this step's trigger once the previous step is finished.</summary>
        public int afterPrev;
        public string textKey;
    }

    // ---------------------------------------------------------------- survivor rows

    public sealed class SurvParamRow
    {
        public string id;
        public string slot;
        public string key;
        public float value;
        public string note;
    }

    public sealed class SurvComboRow
    {
        public string id;
        public string leader;
        public string comp;
        public string nameKey;
        public string descKey;
    }

    public sealed class SurvSetRow
    {
        public int family;
        public int need;
        public string key;
        public float value;
        public string descKey;
    }

    public sealed class SurvWaveRow
    {
        public int id;
        public float t0;
        public float t1;
        public string enemy;
        public float rate0;
        public float rate1;
        public float hp0;
        public float hp1;
        public float curve;
        public int burst;
        /// <summary>Burst repeat period in seconds. 0 means the burst fires only once.</summary>
        public float every;
    }

    /// <summary>Crystal paid the first time a run passes each survival mark, once per day.</summary>
    // ---------------------------------------------------------------- 리그 (10.18-c)

    /// <summary>One appliance as it fights in the league. Separate stats from the tower
    /// defence unit table: the league is an auto-battler, not a lane defence.</summary>
    public sealed class LeagueUnitRow
    {
        public string id;
        public float hp;
        public float atk;
        /// <summary>Seconds between attacks.</summary>
        public float itv;
        /// <summary>Reach in cells; 1 is melee.</summary>
        public float range;
        /// <summary>Cells per second.</summary>
        public float spd;
        /// <summary>Mana needed before the skill fires.</summary>
        public float mana;
        public string skillKey;
        public string descKey;
    }

    /// <summary>A named coefficient on one league skill, e.g. vac/radius = 1.8.</summary>
    public sealed class LeagueParamRow
    {
        public string unitId;
        public string key;
        public float value;
    }

    public sealed class LeagueEnemyRow
    {
        public string id;
        public float hp;
        public float atk;
        public float itv;
        public float range;
        public float spd;
        /// <summary>Damage taken is scaled by (1 - armor).</summary>
        public float armor;
        /// <summary>1 = flying; melee allies cannot reach it.</summary>
        public int fly;
        /// <summary>Enemy spawned on death, blank for none.</summary>
        public string split;
        public int splitN;
    }

    /// <summary>A league division: how hard the enemies are and what finishing pays.</summary>
    public sealed class LeagueTierRow
    {
        public int tier;
        public string nameKey;
        public float hpMul;
        public float atkMul;
        /// <summary>Crystal for the first ever clear of this tier.</summary>
        public int firstCrystal;
        public int winCoin;
        public int winPart;
        public int finishStone;
    }

    public sealed class LeagueRoundRow
    {
        public int round;
        public string nameKey;
        /// <summary>"enemyId*count, ..." laid out on the dust side.</summary>
        public string squad;
        /// <summary>Extra multiplier for this round alone.</summary>
        public float rMul;
        public int boss;
    }

    /// <summary>A team bonus that switches on once enough of a group is fielded.</summary>
    public sealed class LeagueSynRow
    {
        public string id;
        /// <summary>fam1~4 by appliance family, P melee, H ranged.</summary>
        public string group;
        public int need;
        /// <summary>dr, skillDmg, startMana, chill, hp, haste.</summary>
        public string key;
        public float value;
        public string nameKey;
        public string descKey;
    }

    // ---------------------------------------------------------------- 먼지 타워 (10.18-g)

    /// <summary>One floor of the tower: who shows up, what rule bends the fight, what it pays.</summary>
    public sealed class TowerFloorRow
    {
        public int floor;
        public string nameKey;
        /// <summary>tower_rule id, blank when the floor plays straight.</summary>
        public string rule;
        /// <summary>"enemyId*count, ..." as in league_round.</summary>
        public string squad;
        /// <summary>How many appliances may be fielded.</summary>
        public int slots;
        public float hpMul;
        public float atkMul;
        /// <summary>tower_bonus id for the optional goal.</summary>
        public string bonus;
        /// <summary>The goal's number, when it needs one.</summary>
        public int bonusN;
        public int bonusCrystal;
        public string extra;
        /// <summary>First-clear payout.</summary>
        public int crystal;
        public int coin;
        public int part;
        public int stone;
        /// <summary>Shard item handed out on first clear, blank for none.</summary>
        public string shard;
        public int shardN;
    }

    // ---------------------------------------------------------------- 협동 (10.18-h)

    /// <summary>A bot partner: who it plays as, what it brings, and how it spends power.</summary>
    public sealed class CoopPartnerRow
    {
        public string id;
        /// <summary>The appliance shown as its face.</summary>
        public string unit;
        public string nameKey;
        public string styleKey;
        public string descKey;
        /// <summary>"rush" summons hard; "greedy" merges first.</summary>
        public string style;
        /// <summary>Comma-separated appliance ids the partner fields.</summary>
        public string deck;
        /// <summary>Early-game strength, 1-3, for the pick screen's dots.</summary>
        public int early;
        public int late;
    }

    /// <summary>A floor modifier. kind "a" applies to allies, "e" to the dust.</summary>
    public sealed class TowerRuleRow
    {
        public string id;
        public string kind;
        public string nameKey;
        public string descKey;
        public float p1;
        public float p2;
    }

    /// <summary>An optional per-floor goal that pays extra crystal.</summary>
    public sealed class TowerBonusRow
    {
        public string id;
        public string nameKey;
    }

    /// <summary>One crystal box on the daily log, opened at a point total.</summary>
    public sealed class DailyPoolRow
    {
        public int box;
        public int pts;
        public int crystal;
    }

    public sealed class SurvRewardRow
    {
        public int sec;
        public int crystal;
    }

    // ---------------------------------------------------------------- growth / battle boost

    /// <summary>Cost and level cap of each limit-break step.</summary>
    public sealed class UnitStarRow
    {
        public int star;
        public int shards;
        public int coin;
        public int lvCap;
    }

    /// <summary>In-battle upgrade tier (2 or 3) for one appliance.</summary>
    public sealed class UnitBoostRow
    {
        public string unitId;
        public int tier;
        /// <summary>Power cost = unit.cost * costMul.</summary>
        public float costMul;
        /// <summary>Applied to attack, skill damage, heat capacity, cooling, generation and buffs.</summary>
        public float statMul;
        public int blockAdd;
        public float rangeAdd;
        /// <summary>Skill charge time multiplier; below 1 charges faster.</summary>
        public float spMul;
    }

    // ---------------------------------------------------------------- lobby / shop / pass

    /// <summary>One square of the seven-day attendance wheel.</summary>
    public sealed class AttendanceRow
    {
        public int day;
        public int rewardGroup;
        /// <summary>1 renders the square larger.</summary>
        public int big;
    }

    /// <summary>A forced onboarding step that highlights one UI element.</summary>
    public sealed class GuideRow
    {
        /// <summary>"first" (new account) or "levelup" (after the 1-1 clear).</summary>
        public string flow;
        public int seq;
        /// <summary>UI key to spotlight, e.g. "home.cta", "roster.unit:vac", "detail.levelup".</summary>
        public string target;
        public string textKey;
    }

    public sealed class ShopProductRow
    {
        public int id;
        /// <summary>enums.ShopTab: 1 Crystal, 2 Daily, 3 Package.</summary>
        public int tab;
        public int order;
        /// <summary>enums.PayKind: 1 Iap (test provider), 2 Crystal, 3 Ad.</summary>
        public int payKind;
        /// <summary>Won for Iap, crystal count for Crystal, 0 for Ad.</summary>
        public int price;
        public int rewardGroup;
        /// <summary>enums.LimitType: 0 None, 1 Daily, 2 Weekly, 3 Account.</summary>
        public int limitType;
        public int limitCount;
        /// <summary>Extra reward group paid only on the first purchase. 0 = none.</summary>
        public int firstBonusGroup;
        /// <summary>"idle:&lt;hours&gt;", "pass:month" or "pass:noads".</summary>
        public string special;
        /// <summary>Re-watch delay in seconds for ad products.</summary>
        public int cooldown;
        public int unlockCond;
        public string badgeKey;
        public string nameKey;
        public string descKey;
    }

    public sealed class PassLevelRow
    {
        public int level;
        /// <summary>Cumulative XP needed to reach this level.</summary>
        public int xp;
        public int freeGroup;
        public int paidGroup;
    }

    /// <summary>
    /// The raw payload of every table, exactly as read from disk. Indexing, derivation and
    /// validation all happen later in <see cref="GameDatabase"/> so this type stays a
    /// faithful mirror of the JSON and nothing else.
    /// </summary>
    /// <summary>
    /// A region of the stage tab: 10.16-n groups its 75 stages into five areas, each owning a
    /// span of chapters and its own accent colour.
    /// </summary>
    public sealed class AreaRow
    {
        public int id;
        public string nameKey, shortKey, subKey, descKey;
        public int chFrom, chTo;
        public string color, bg;
    }

    /// <summary>
    /// A power skill - the spare-power abilities the stage tab lists. One row per level, so
    /// a skill is the set of rows sharing an id.
    /// </summary>
    public sealed class PowerSkillRow
    {
        public string id;
        public int lv;
        /// <summary>condition.id gating the unlock, 0 when none.</summary>
        public int unlockCond;
        /// <summary>The stage whose clear opens it.</summary>
        public int unlockStage;
        public double cost, cd, radius, dmg, stun, dur;
        /// <summary>Multipliers the buff skills apply; 1 means "unchanged".</summary>
        public double atkMul = 1d, itvMul = 1d, spMul = 1d;
    }

    /// <summary>
    /// A piece of equipment. Grades 1-3 are generic; grade 4 is an exclusive bound to one
    /// appliance through <see cref="unitId"/>.
    /// </summary>
    public sealed class GearRow
    {
        public int id, grade;
        /// <summary>atk / heat / sp / excl.</summary>
        public string type;
        /// <summary>Set only on exclusives.</summary>
        public string unitId;
        public string nameKey, effKey;
        /// <summary>Flat stat bonuses, as fractions.</summary>
        public double atk, heat, sp;
        /// <summary>Up to two skill-coefficient edits: which key, add or mul, and the value.</summary>
        public string sk1Key, sk1Op, sk2Key, sk2Op;
        public double sk1Val, sk2Val;
    }

    /// <summary>One of the gear banners on the summon tab.</summary>
    public sealed class GearGachaRow
    {
        public int id;
        public string nameKey;
        public int costItem, costCount, pulls;
        /// <summary>Guarantees at least one of this grade or better; 0 means no guarantee.</summary>
        public int guaranteeGrade;
        /// <summary>Per-grade weights in ten-thousandths.</summary>
        public int w1, w2, w3, w4;
    }

    /// <summary>What an exclusive gear changes for its appliance in the survivor mode.</summary>
    public sealed class GearSurvRow
    {
        public int gearId;
        public string unitId;
        /// <summary>leader or comp - which role the change applies to.</summary>
        public string slot;
        public string key, op;
        public double val;
    }

    /// <summary>
    /// The art-viewer entry for one appliance. <see cref="art"/> is a path prefix; when it is
    /// empty the build falls back to the SD drawing, so a missing illustration is not a gap.
    /// </summary>
    public sealed class UnitProfileRow
    {
        public string unitId;
        public string art;
        public string keyColor, bgColor;
        public string taglineKey, profileKey, motifKey;
    }

    public sealed class RawTables
    {
        public Dictionary<string, Dictionary<string, int>> enums;
        public ConfigRow config;
        public Dictionary<string, string> string_ko;
        public List<ItemRow> item;
        public List<UnitRow> unit;
        public List<UnitLevelupRow> unit_levelup;
        public List<EnemyRow> enemy;
        public List<MapRow> map;
        public List<StageRow> stage;
        public List<WaveRow> wave;
        public List<RewardGroupRow> reward_group;
        public List<ConditionRow> condition;
        public List<ContentUnlockRow> content_unlock;
        public List<StarRewardRow> star_reward;
        public List<SkillParamRow> skill_param;
        public List<GachaRow> gacha;
        public List<GachaPoolRow> gacha_pool;
        public List<DailyPoolRow> daily_pool;
        public List<LeagueUnitRow> league_unit;
        public List<LeagueParamRow> league_param;
        public List<LeagueEnemyRow> league_enemy;
        public List<LeagueTierRow> league_tier;
        public List<LeagueRoundRow> league_round;
        public List<LeagueSynRow> league_syn;
        public List<TowerFloorRow> tower_floor;
        public List<TowerRuleRow> tower_rule;
        public List<TowerBonusRow> tower_bonus;
        public List<CoopPartnerRow> coop_partner;
        public List<TutorialRow> tutorial;
        public List<SurvParamRow> surv_param;
        public List<SurvComboRow> surv_combo;
        public List<SurvSetRow> surv_set;
        public List<SurvWaveRow> surv_wave;
        public List<SurvRewardRow> surv_reward;
        public List<UnitStarRow> unit_star;
        public List<UnitBoostRow> unit_boost;
        public List<AttendanceRow> attendance;
        public List<GuideRow> guide;
        public List<ShopProductRow> shop_product;
        public List<PassLevelRow> pass_level;
        public List<AreaRow> area;
        public List<PowerSkillRow> power_skill;
        public List<GearRow> gear;
        public List<GearGachaRow> gear_gacha;
        public List<GearSurvRow> gear_surv;
        public List<UnitProfileRow> unit_profile;
        public List<UnitArtRow> unit_art;
        public List<UnitBehaviourRow> unit_behaviour;

        /// <summary>File stem of every table, in the order the original game loaded them.</summary>
        public static readonly string[] Names =
        {
            "enums", "config", "string_ko", "item", "unit", "unit_boost", "unit_levelup",
            "unit_star", "enemy", "map", "stage", "wave", "reward_group", "attendance",
            "condition", "content_unlock", "star_reward", "skill_param", "gacha", "gacha_pool",
            "daily_pool",
            "tutorial", "guide",
            // surv_reward is gone as of 10.18-h: the time brackets became the shared daily
            // log above (daily_pool + config.pool*).
            "surv_param", "surv_combo", "surv_set", "surv_wave",
            "shop_product", "pass_level",
            // 10.16-n additions: areas and power skills on the stage tab, the gear system,
            // and the art-viewer profiles.
            "area", "power_skill", "gear", "gear_gacha", "gear_surv", "unit_profile",
            "unit_art", "unit_behaviour",
            // 10.18-c 리그 · 10.18-g 먼지 타워. 타워는 리그 전투판을 그대로 쓴다.
            "league_unit", "league_param", "league_enemy", "league_tier", "league_round",
            "league_syn", "tower_floor", "tower_rule", "tower_bonus", "coop_partner",
        };
    }
}
