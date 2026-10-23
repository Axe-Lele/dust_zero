// Reads data/*.json into RawTables.
//
// The game never needs Excel or Python at runtime: tools/build.py writes the JSON, the
// sync step copies it into StreamingAssets, and this loader reads it from there.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DustZero.Data
{
    /// <summary>Supplies raw JSON text per table name. Lets tests feed strings and the game read files.</summary>
    public interface ITableSource
    {
        /// <summary>Returns the JSON text of the named table, or null when absent.</summary>
        string Read(string tableName);
    }

    public sealed class DictionaryTableSource : ITableSource
    {
        readonly IReadOnlyDictionary<string, string> _json;
        public DictionaryTableSource(IReadOnlyDictionary<string, string> json) { _json = json; }
        public string Read(string tableName) => _json.TryGetValue(tableName, out var v) ? v : null;
    }

    public static class TableLoader
    {
        static readonly JsonSerializerSettings Settings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
        };

        /// <summary>
        /// Parses every table from <paramref name="source"/>. A missing file or malformed JSON is
        /// reported with the table name so it is obvious which spreadsheet to look at.
        /// </summary>
        public static RawTables Load(ITableSource source)
        {
            var raw = new RawTables();
            var problems = new List<string>();

            foreach (var name in RawTables.Names)
            {
                var text = source.Read(name);
                if (string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"{name}.json 을 읽지 못했습니다. tools/build.py 를 실행하고 데이터 동기화를 다시 하세요.");
                    continue;
                }
                try { Assign(raw, name, text); }
                catch (Exception e) { problems.Add($"{name}.json 파싱 실패: {e.Message}"); }
            }

            if (problems.Count > 0) throw new TableValidationException(problems);
            return raw;
        }

        public static GameDatabase LoadAndBuild(ITableSource source) => GameDatabase.Build(Load(source));

        static void Assign(RawTables raw, string name, string json)
        {
            switch (name)
            {
                case "enums": raw.enums = De<Dictionary<string, Dictionary<string, int>>>(json); break;
                case "config": raw.config = De<ConfigRow>(json); break;
                case "string_ko": raw.string_ko = De<Dictionary<string, string>>(json); break;
                case "item": raw.item = De<List<ItemRow>>(json); break;
                case "unit": raw.unit = De<List<UnitRow>>(json); break;
                case "unit_levelup": raw.unit_levelup = De<List<UnitLevelupRow>>(json); break;
                case "enemy": raw.enemy = De<List<EnemyRow>>(json); break;
                case "map": raw.map = De<List<MapRow>>(json); break;
                case "stage": raw.stage = De<List<StageRow>>(json); break;
                case "wave": raw.wave = De<List<WaveRow>>(json); break;
                case "reward_group": raw.reward_group = De<List<RewardGroupRow>>(json); break;
                case "condition": raw.condition = De<List<ConditionRow>>(json); break;
                case "content_unlock": raw.content_unlock = De<List<ContentUnlockRow>>(json); break;
                case "star_reward": raw.star_reward = De<List<StarRewardRow>>(json); break;
                case "skill_param": raw.skill_param = De<List<SkillParamRow>>(json); break;
                case "gacha": raw.gacha = De<List<GachaRow>>(json); break;
                case "gacha_pool": raw.gacha_pool = De<List<GachaPoolRow>>(json); break;
                case "tutorial": raw.tutorial = De<List<TutorialRow>>(json); break;
                case "surv_param": raw.surv_param = De<List<SurvParamRow>>(json); break;
                case "surv_combo": raw.surv_combo = De<List<SurvComboRow>>(json); break;
                case "surv_set": raw.surv_set = De<List<SurvSetRow>>(json); break;
                case "surv_wave": raw.surv_wave = De<List<SurvWaveRow>>(json); break;
                case "daily_pool": raw.daily_pool = De<List<DailyPoolRow>>(json); break;
                case "league_unit": raw.league_unit = De<List<LeagueUnitRow>>(json); break;
                case "league_param": raw.league_param = De<List<LeagueParamRow>>(json); break;
                case "league_enemy": raw.league_enemy = De<List<LeagueEnemyRow>>(json); break;
                case "league_tier": raw.league_tier = De<List<LeagueTierRow>>(json); break;
                case "league_round": raw.league_round = De<List<LeagueRoundRow>>(json); break;
                case "league_syn": raw.league_syn = De<List<LeagueSynRow>>(json); break;
                case "tower_floor": raw.tower_floor = De<List<TowerFloorRow>>(json); break;
                case "tower_rule": raw.tower_rule = De<List<TowerRuleRow>>(json); break;
                case "tower_bonus": raw.tower_bonus = De<List<TowerBonusRow>>(json); break;
                case "coop_partner": raw.coop_partner = De<List<CoopPartnerRow>>(json); break;
                case "unit_star": raw.unit_star = De<List<UnitStarRow>>(json); break;
                case "unit_boost": raw.unit_boost = De<List<UnitBoostRow>>(json); break;
                case "attendance": raw.attendance = De<List<AttendanceRow>>(json); break;
                case "guide": raw.guide = De<List<GuideRow>>(json); break;
                case "shop_product": raw.shop_product = De<List<ShopProductRow>>(json); break;
                case "pass_level": raw.pass_level = De<List<PassLevelRow>>(json); break;
                case "area": raw.area = De<List<AreaRow>>(json); break;
                case "power_skill": raw.power_skill = De<List<PowerSkillRow>>(json); break;
                case "gear": raw.gear = De<List<GearRow>>(json); break;
                case "gear_gacha": raw.gear_gacha = De<List<GearGachaRow>>(json); break;
                case "gear_surv": raw.gear_surv = De<List<GearSurvRow>>(json); break;
                case "unit_profile": raw.unit_profile = De<List<UnitProfileRow>>(json); break;
                case "unit_art": raw.unit_art = De<List<UnitArtRow>>(json); break;
                case "unit_behaviour": raw.unit_behaviour = De<List<UnitBehaviourRow>>(json); break;
                // ---- 10.22-g
                case "aff_level": raw.aff_level = De<List<AffLevelRow>>(json); break;
                case "collect_bonus": raw.collect_bonus = De<List<CollectBonusRow>>(json); break;
                case "event": raw.@event = De<List<EventRow>>(json); break;
                case "mail": raw.mail = De<List<MailRow>>(json); break;
                case "mission": raw.mission = De<List<MissionRow>>(json); break;
                case "mission_chest": raw.mission_chest = De<List<MissionChestRow>>(json); break;
                case "quest": raw.quest = De<List<QuestRow>>(json); break;
                case "quest_line": raw.quest_line = De<List<QuestLineRow>>(json); break;
                case "stage_hard": raw.stage_hard = De<List<StageHardRow>>(json); break;
                case "story_ep": raw.story_ep = De<List<StoryEpRow>>(json); break;
                case "story_line": raw.story_line = De<List<StoryLineRow>>(json); break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "알 수 없는 테이블 이름");
            }
        }

        static T De<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
