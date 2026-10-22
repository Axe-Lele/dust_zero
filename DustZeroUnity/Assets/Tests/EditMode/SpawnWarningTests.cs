// 10.17-e's regen warning: which entrances warn, what they group, and in what order.
//
// The grouping rules are the part that is easy to get subtly wrong and impossible to see in
// a screenshot - a window measured from the wrong instant, or an unstable sort, still draws
// a plausible-looking bubble with the wrong contents.
using System.Linq;
using System.Reflection;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class SpawnWarningTests
    {
        GameDatabase _db;
        SpawnWarningBoard _board;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _board = new SpawnWarningBoard();
        }

        BattleSim NewSim(StageDef stage)
            => new(_db, stage, new[] { "vac", "ac" },
                   new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        /// <summary>The first stage whose schedule has something on the given entrance.</summary>
        StageDef StageWithEntrance(int pathIndex)
            => _db.Stages.FirstOrDefault(s => s.Paths.Length > pathIndex &&
                                              s.Schedule.Any(e => e.PathIndex == pathIndex));

        [Test]
        public void TheWindowIsConfigurableAndZeroTurnsWarningsOff()
        {
            var stage = StageWithEntrance(0);
            Assert.IsNotNull(stage, "입구 0 에 적이 나오는 스테이지가 있어야 합니다.");
            var sim = NewSim(stage);

            Assert.IsFalse(_board.TryBuild(_db, sim, 0, 0f, out _),
                           "창이 0 이면 예고가 꺼져야 합니다.");

            float first = stage.Schedule.First(e => e.PathIndex == 0).Time;
            // A window that cannot reach the first spawn must stay silent; one that can must not.
            Assert.IsFalse(_board.TryBuild(_db, sim, 0, first - 0.5f, out _),
                           "첫 등장보다 짧은 창에서는 예고가 뜨면 안 됩니다.");
            Assert.IsTrue(_board.TryBuild(_db, sim, 0, first + 0.5f, out var w),
                          "첫 등장을 포함하는 창에서는 예고가 떠야 합니다.");
            Assert.AreEqual(first, w.Seconds, 0.001f, "남은 초는 첫 등장까지의 시간입니다.");
        }

        [Test]
        public void TheCountdownNeverGoesNegativeAndTheWarningStopsOnceTheSpawnIsConsumed()
        {
            var stage = StageWithEntrance(0);
            var sim = NewSim(stage);
            float first = stage.Schedule.First(e => e.PathIndex == 0).Time;

            // Run right up to - but not past - the first spawn.
            while (sim.Time < first - 0.02f && !sim.Finished) sim.Tick(1f / 60f);
            Assert.IsTrue(_board.TryBuild(_db, sim, 0, 5f, out var near));
            Assert.GreaterOrEqual(near.Seconds, 0f, "남은 초는 음수가 될 수 없습니다.");

            int before = sim.ScheduleCursor;
            while (sim.ScheduleCursor == before && !sim.Finished) sim.Tick(1f / 60f);
            Assert.Greater(sim.ScheduleCursor, before, "일정이 소비되어야 합니다.");

            // Whatever it reports now, it must be about a LATER spawn - never the one that
            // already walked onto the board.
            if (_board.TryBuild(_db, sim, 0, 5f, out var after))
                Assert.GreaterOrEqual(after.Seconds, 0f,
                                      "이미 나온 적을 다시 예고하면 안 됩니다.");
        }

        [Test]
        public void OnlyTheAskedEntranceIsCounted()
        {
            var stage = _db.Stages.FirstOrDefault(
                s => s.Paths.Length > 1 &&
                     s.Schedule.Any(e => e.PathIndex == 0) &&
                     s.Schedule.Any(e => e.PathIndex == 1));
            if (stage == null) Assert.Ignore("입구가 둘 이상인 스테이지가 없습니다.");
            var sim = NewSim(stage);

            // A window wide enough to cover the whole stage: every entrance still only ever
            // reports its own enemies.
            float span = stage.Schedule.Max(e => e.Time) + 1f;
            for (int pi = 0; pi < stage.Paths.Length; pi++)
            {
                if (!_board.TryBuild(_db, sim, pi, span, out var w)) continue;
                Assert.AreEqual(pi, w.PathIndex);
                int total = w.First.Count + (w.KindCount > 1 ? w.Second.Count : 0);
                int mine = stage.Schedule.Count(e => e.PathIndex == pi);
                Assert.LessOrEqual(total, mine,
                                   $"입구 {pi} 가 자기 것보다 많은 적을 셌습니다.");
            }
        }

        [Test]
        public void BossesComeFirstThenFlyersAndTheColourFollows()
        {
            var sim = BuildSyntheticSim(out _);
            // dust (ground) is scheduled first, then fly, then a boss - so insertion order
            // alone would show dust. The sort must put the boss in front.
            Assert.IsTrue(_board.TryBuild(_db, sim, 0, 5f, out var w));
            Assert.AreEqual("boss_dust", w.First.EnemyId, "보스가 맨 앞이어야 합니다.");
            Assert.AreEqual("fly", w.Second.EnemyId, "그 다음이 공중입니다.");
            Assert.IsTrue(w.HasMore, "세 번째 종류가 있으면 '+' 가 떠야 합니다.");
            Assert.IsTrue(w.Boss, "보스가 있으면 금색입니다.");
            Assert.IsFalse(w.Air, "보스 색이 공중 색보다 우선합니다.");
            Assert.AreEqual(2, w.KindCount, "칸은 최대 두 개입니다.");
        }

        [Test]
        public void SameKindIsCountedNotRepeated()
        {
            var sim = BuildSyntheticSim(out _);
            Assert.IsTrue(_board.TryBuild(_db, sim, 0, 5f, out var w));
            // Three dust are scheduled, but dust is the third kind so it is not shown; the
            // two that ARE shown each have their own count rather than a row apiece.
            Assert.AreEqual(1, w.First.Count, "보스는 한 마리입니다.");
            Assert.AreEqual(2, w.Second.Count, "초파리 두 마리는 한 칸에 ×2 로 묶입니다.");
        }

        [Test]
        public void TheGroupWindowIsMeasuredFromTheFirstSpawnNotFromNow()
        {
            // dust at t=10, then another at t=13. At t=0 with a 5s window the warning must
            // not fire at all; at t=6 it fires and groups BOTH, because the window is
            // measured from the first spawn's time, not from now.
            var stage = SyntheticStage(new[]
            {
                (10f, "dust"), (13f, "dust"),
            });
            var sim = NewSim(stage);
            Assert.IsFalse(_board.TryBuild(_db, sim, 0, 5f, out _));

            while (sim.Time < 6f && !sim.Finished) sim.Tick(1f / 60f);
            Assert.IsTrue(_board.TryBuild(_db, sim, 0, 5f, out var w));
            Assert.AreEqual(2, w.First.Count,
                            "첫 등장 기준 5초 안의 적은 지금 기준으로 5초를 넘어도 함께 셉니다.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// A one-entrance stage with a schedule written for the test, so the ordering rules
        /// can be checked without hunting for a table row that happens to have the shape.
        /// </summary>
        BattleSim BuildSyntheticSim(out StageDef stage)
        {
            stage = SyntheticStage(new[]
            {
                (1.0f, "dust"), (1.2f, "fly"), (1.4f, "boss_dust"),
                (1.6f, "fly"), (1.8f, "dust"), (2.0f, "dust"),
            });
            return NewSim(stage);
        }

        StageDef SyntheticStage((float t, string id)[] spawns)
        {
            // Borrow a real stage's map and paths; only the schedule is ours.
            var basis = _db.Stages.First(s => s.Paths.Length >= 1);
            // A field-for-field copy, so the map and paths are real while the schedule is
            // ours. Done by reflection rather than by adding a Clone to StageDef: the
            // production type has no reason to grow an API only a test needs.
            var clone = typeof(StageDef).GetMethod("MemberwiseClone",
                            BindingFlags.Instance | BindingFlags.NonPublic);
            var stage = (StageDef)clone.Invoke(basis, null);
            stage.Schedule = spawns
                .Select(s => new SpawnEvent(s.t, s.id, 0, 1f))
                .ToArray();
            return stage;
        }
    }
}
