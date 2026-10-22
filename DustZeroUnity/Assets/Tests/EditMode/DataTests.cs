// Loads the shipped tables and checks the rules that would otherwise fail silently at runtime.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DustZero.Data;
using NUnit.Framework;
using UnityEngine;

namespace DustZero.Tests
{
    /// <summary>Reads the real JSON out of StreamingAssets so tests exercise shipped data.</summary>
    public static class TestData
    {
        static GameDatabase _cached;

        public static string DataDir => Path.Combine(Application.streamingAssetsPath, "data");

        public static GameDatabase Db => _cached ??= Load();

        public static GameDatabase Load()
        {
            var json = new Dictionary<string, string>();
            foreach (var name in RawTables.Names)
            {
                var path = Path.Combine(DataDir, name + ".json");
                if (File.Exists(path)) json[name] = File.ReadAllText(path);
            }
            return TableLoader.LoadAndBuild(new DictionaryTableSource(json));
        }
    }

    public class DataTests
    {
        [Test]
        public void EveryTableFilePresent()
        {
            foreach (var name in RawTables.Names)
                Assert.IsTrue(File.Exists(Path.Combine(TestData.DataDir, name + ".json")),
                    $"{name}.json 이 StreamingAssets 에 없습니다. tools/sync_to_unity.py 를 실행하세요.");
        }

        [Test]
        public void ShippedTablesLoadAndValidate()
        {
            // Build() throws TableValidationException listing every problem it found.
            var db = TestData.Load();
            Assert.Greater(db.Stages.Count, 0);
            Assert.Greater(db.UnitOrder.Count, 0);
            Assert.Greater(db.Enemies.Count, 0);
        }

        [Test]
        public void StageScheduleIsExpandedAndSorted()
        {
            foreach (var st in TestData.Db.Stages)
            {
                Assert.Greater(st.Schedule.Length, 0, $"{st.No}: 웨이브가 비었습니다.");
                for (int i = 1; i < st.Schedule.Length; i++)
                    Assert.LessOrEqual(st.Schedule[i - 1].Time, st.Schedule[i].Time,
                        $"{st.No}: 스폰 일정이 시간순이 아닙니다.");
                foreach (var ev in st.Schedule)
                    Assert.IsTrue(ev.PathIndex >= 0 && ev.PathIndex < st.Paths.Length,
                        $"{st.No}: 입구 인덱스 {ev.PathIndex} 가 범위를 벗어납니다.");
            }
        }

        [Test]
        public void EveryGoalIsReachableFromEveryEntrance()
        {
            foreach (var st in TestData.Db.Stages)
            {
                var field = new Core.Defense.BattleField(st);
                for (int pi = 0; pi < st.Paths.Length; pi++)
                {
                    var p = st.Paths[pi];
                    Assert.IsFalse(float.IsPositiveInfinity(field.DistAll[p.StartY, p.StartX]),
                        $"{st.No} 입구 {pi + 1}: 시작 칸 ({p.StartX},{p.StartY}) 에서 목표까지 길이 없습니다.");
                }
            }
        }

        [Test]
        public void SkillDescriptionsHaveNoLeftoverPlaceholders()
        {
            foreach (var id in TestData.Db.UnitOrder)
            {
                var u = TestData.Db.Units[id];
                StringAssert.DoesNotContain("{", u.SkillDesc,
                    $"unit '{id}': 스킬 설명에 채워지지 않은 자리표시자가 남았습니다 -> {u.SkillDesc}");
            }
        }

        [Test]
        public void MissingTableIsReportedByName()
        {
            var json = new Dictionary<string, string>();
            foreach (var name in RawTables.Names)
            {
                if (name == "unit") continue;
                json[name] = File.ReadAllText(Path.Combine(TestData.DataDir, name + ".json"));
            }

            var ex = Assert.Throws<TableValidationException>(
                () => TableLoader.LoadAndBuild(new DictionaryTableSource(json)));
            Assert.IsTrue(ex.Problems.Any(p => p.Contains("unit.json")),
                "누락된 테이블 이름이 오류 메시지에 나와야 합니다.");
        }

        [Test]
        public void DanglingStageMapReferenceIsReported()
        {
            var json = new Dictionary<string, string>();
            foreach (var name in RawTables.Names)
                json[name] = File.ReadAllText(Path.Combine(TestData.DataDir, name + ".json"));

            // Point the first stage at a map that does not exist.
            json["stage"] = json["stage"].Replace("\"mapId\": \"living\"", "\"mapId\": \"nowhere\"");

            var ex = Assert.Throws<TableValidationException>(
                () => TableLoader.LoadAndBuild(new DictionaryTableSource(json)));
            Assert.IsTrue(ex.Problems.Any(p => p.Contains("nowhere")),
                $"잘못된 mapId 가 메시지에 나와야 합니다. 실제: {string.Join(" | ", ex.Problems)}");
        }
    }
}
