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

        public int[] starLifeMin;
        public float queueHeatRatio;
        public float powerMax;
        public float retreatRefund;
        public float naturalCoolPerSec;
        public float fireTileHeatPerSec;
        public float wetTileCoolPerSec;
        public float wetTileEnemySpeedMul;
    }

    // ---------------------------------------------------------------- content rows

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
        public string no;
        public string mapId;
        public int waveGroup;
        public int boss;
        public float hpMul;
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

        /// <summary>File stem of every table, in the order the original game loaded them.</summary>
        public static readonly string[] Names =
        {
            "enums", "config", "string_ko", "item", "unit", "unit_boost", "unit_levelup",
            "unit_star", "enemy", "map", "stage", "wave", "reward_group", "attendance",
            "condition", "content_unlock", "star_reward", "skill_param", "gacha", "gacha_pool",
            "tutorial", "guide",
            "surv_param", "surv_combo", "surv_set", "surv_wave", "surv_reward",
            "shop_product", "pass_level"
        };
    }
}
