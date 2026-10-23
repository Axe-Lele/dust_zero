// 10.22-g 가 들여온 표 11개가 **정말로 읽히는지** 본다.
//
// 왜 따로 두나: RawTables.Names 는 손으로 적는 목록이다. 표를 만들고 StreamingAssets 에
// 넣어도 거기 적지 않으면 게임은 그 표를 모르고, 아무 오류도 나지 않는다. 반대로 적어
// 두고 파일이 없으면 목록만 길어진다. 둘 다 "조용히 틀린" 쪽이라 단언으로 묶는다.
using System.IO;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class NewTableTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp() => _db = TestData.Db;

        static readonly string[] NewTables =
        {
            "mission", "mission_chest", "event", "mail", "aff_level", "collect_bonus",
            "quest_line", "quest", "story_ep", "story_line", "stage_hard",
        };

        /// <summary>목록에 적은 표는 StreamingAssets 에 실제로 있어야 한다.</summary>
        [Test]
        public void EveryNewTableIsOnDisk()
        {
            foreach (var name in NewTables)
                Assert.IsTrue(File.Exists(Path.Combine(TestData.DataDir, name + ".json")),
                    $"{name}.json 이 StreamingAssets 에 없습니다 — 목록에만 적혀 있습니다.");
        }

        /// <summary>목록에 적혀 있어야 TableLoader 가 읽는다. 빠지면 Raw 가 null 로 남는다.</summary>
        [Test]
        public void EveryNewTableIsRegisteredInNames()
        {
            foreach (var name in NewTables)
                CollectionAssert.Contains(RawTables.Names, name,
                    $"{name} 이 RawTables.Names 에 없습니다 — 게임이 이 표를 모릅니다.");
        }

        /// <summary>
        /// 행이 실제로 들어왔는지. 0 행이면 읽기는 했지만 모양이 어긋나 비어 돌아온 것이다.
        /// 수는 10.22-g 원본 data/*.json 에서 센 값이다.
        /// </summary>
        [Test]
        public void EveryNewTableLoadsItsRows()
        {
            Assert.AreEqual(94, _db.Missions.Count, "mission");
            Assert.AreEqual(30, _db.MissionChests.Count, "mission_chest");
            Assert.AreEqual(4, _db.Events.Count, "event");
            Assert.AreEqual(10, _db.Mails.Count, "mail");
            Assert.AreEqual(10, _db.AffLevels.Count, "aff_level");
            Assert.AreEqual(11, _db.CollectBonuses.Count, "collect_bonus");
            Assert.AreEqual(9, _db.QuestLines.Count, "quest_line");
            Assert.AreEqual(151, _db.Quests.Count, "quest");
            Assert.AreEqual(48, _db.StoryEpisodes.Count, "story_ep");
            Assert.AreEqual(318, _db.StoryLines.Count, "story_line");
            Assert.AreEqual(75, _db.HardStages.Count, "stage_hard");
        }

        /// <summary>
        /// `goto` 와 `base` 는 C# 예약어라 칸 이름을 바꿔 달았다. JsonProperty 가 빠지면
        /// 컴파일도 되고 표도 읽히는데 **그 칸만 조용히 비어** 들어온다 — 퀘스트의 [이동]이
        /// 아무 데도 안 가고, 심화 스테이지가 맵을 못 찾는다.
        /// </summary>
        [Test]
        public void RenamedKeywordColumnsStillBind()
        {
            var q = _db.Quests[0];
            Assert.IsFalse(string.IsNullOrEmpty(q.gotoTab),
                "quest.goto 가 비어 들어왔습니다 — JsonProperty(\"goto\") 가 빠졌습니다.");

            var h = _db.HardStages[0];
            Assert.Greater(h.baseStage, 0,
                "stage_hard.base 가 0 으로 들어왔습니다 — JsonProperty(\"base\") 가 빠졌습니다.");
            Assert.IsNotNull(_db.StageById(h.baseStage),
                "심화 스테이지가 가리키는 보통 스테이지가 없습니다.");
        }

        /// <summary>표 사이의 참조가 실제로 이어지는지 — 읽혔다고 해서 맞는 건 아니다.</summary>
        [Test]
        public void CrossTableReferencesResolve()
        {
            foreach (var ep in _db.StoryEpisodes)
                Assert.IsTrue(_db.Units.ContainsKey(ep.unit),
                    $"스토리 {ep.id} 가 없는 가전 '{ep.unit}' 을 가리킵니다.");

            foreach (var ep in _db.StoryEpisodes)
                Assert.IsNotEmpty(_db.StoryLinesOf(ep.id),
                    $"스토리 {ep.id} 에 대사가 한 줄도 없습니다.");

            foreach (var h in _db.HardStages)
                Assert.IsNotNull(_db.StageById(h.baseStage),
                    $"심화 {h.no} 의 base {h.baseStage} 가 보통 스테이지에 없습니다.");
        }

        /// <summary>
        /// 호감도 단계는 누적 경험치라 반드시 오름차순이어야 한다. 뒤집히면 레벨이
        /// 한 번에 뛰거나 영영 오르지 않는다.
        /// </summary>
        [Test]
        public void AffinityLevelsAreAscending()
        {
            var levels = _db.AffLevels;
            for (int i = 1; i < levels.Count; i++)
            {
                Assert.Greater(levels[i].lv, levels[i - 1].lv, "호감도 레벨이 오름차순이 아닙니다.");
                Assert.GreaterOrEqual(levels[i].xp, levels[i - 1].xp, "누적 경험치가 줄었습니다.");
            }
        }
    }
}
