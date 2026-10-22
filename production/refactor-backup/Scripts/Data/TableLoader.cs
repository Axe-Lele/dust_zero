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
                case "surv_reward": raw.surv_reward = De<List<SurvRewardRow>>(json); break;
                case "unit_star": raw.unit_star = De<List<UnitStarRow>>(json); break;
                case "unit_boost": raw.unit_boost = De<List<UnitBoostRow>>(json); break;
                case "attendance": raw.attendance = De<List<AttendanceRow>>(json); break;
                case "guide": raw.guide = De<List<GuideRow>>(json); break;
                case "shop_product": raw.shop_product = De<List<ShopProductRow>>(json); break;
                case "pass_level": raw.pass_level = De<List<PassLevelRow>>(json); break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "알 수 없는 테이블 이름");
            }
        }

        static T De<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
