// The scripted first battle: a forced-tile step must stay completable.
//
// These steps pause the simulation, which also freezes power regeneration, so anything that
// lets the player spend power elsewhere - or that pins the lesson to a tile nothing can go on
// - wedges the run permanently. The original guards both cases; these tests hold us to it.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class TutorialTests
    {
        GameDatabase _db;
        StageDef _stage;
        SaveData _save;

        /// <summary>The scripted stage the tutorial's tile cells were written against.</summary>
        const int ScriptedStageId = 101;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _stage = _db.Stages.First(s => s.Id == ScriptedStageId);
            var svc = new SaveService("mem", _db, new FixedRandom(0f), new MemoryFileStore());
            _save = svc.NewProfile();
            _save.tutOff = 0;
        }

        /// <summary>A deck wide enough that every role the first group teaches resolves.</summary>
        BattleSim NewSim() => new(_db, _stage, new[] { "vac", "gen", "ac", "fan" },
                                  new FlatPower(), new FixedRandom(0.5f), new PresentationBus());

        static void Advance(TutorialDirector tut, float seconds, BattleSim sim = null)
        {
            for (float t = 0; t < seconds; t += 1f / 60f)
            {
                if (sim != null && !tut.HoldsSimulation) sim.Tick(1f / 60f);
                tut.Tick(t);
            }
        }

        [Test]
        public void FirstStepPinsATileThatTheTaughtUnitCanActuallyUse()
        {
            var sim = NewSim();
            var tut = new TutorialDirector(_db, _save, sim);
            Advance(tut, 6f, sim);

            Assert.IsNotNull(tut.Active, "첫 전투의 배치 안내가 뜨지 않았습니다.");
            var a = tut.Active;
            Assert.IsNotNull(a.ForcedTile, "배치 스텝이 칸을 지정하지 않았습니다.");
            Assert.IsNotNull(a.Target, "배치 대상 가전이 정해지지 않았습니다.");
            Assert.IsTrue(sim.CanPlace(a.Target, a.ForcedTile.Value.X, a.ForcedTile.Value.Y),
                $"지정 칸 {a.ForcedTile}에 {a.Target} 를 놓을 수 없습니다 — 영구 교착입니다.");
            Assert.IsTrue(a.LockCard, "고정 칸 스텝은 다른 카드를 잠가야 합니다.");
            Assert.IsTrue(a.Pauses, "검사 전제: 이 스텝은 시뮬레이션을 멈춥니다.");
        }

        [Test]
        public void DeployingOnTheForcedTileAdvancesPastTheStep()
        {
            var sim = NewSim();
            var tut = new TutorialDirector(_db, _save, sim);
            Advance(tut, 6f, sim);

            var a = tut.Active;
            int id = a.Step.id;
            Assert.IsTrue(sim.Affordable(a.Target), "안내가 살 수 없는 가전을 요구했습니다.");
            sim.Deploy(a.Target, a.ForcedTile.Value.X, a.ForcedTile.Value.Y);

            tut.Tick(6.1f);
            Assert.IsTrue(_save.SawTutorial(id), "지정 칸에 배치했는데 스텝이 끝나지 않았습니다.");
            Assert.IsFalse(tut.HoldsSimulation && tut.Active?.Step.id == id,
                "완료한 스텝이 전투를 계속 붙잡고 있습니다.");
        }

        [Test]
        public void AnOccupiedForcedTileReleasesTheLockInsteadOfWedging()
        {
            var sim = NewSim();
            var probe = new TutorialDirector(_db, _save, sim);
            Advance(probe, 6f, sim);
            var cell = probe.Active.ForcedTile.Value;
            string target = probe.Active.Target;

            // Replay the same opening with that cell already taken by the same unit type.
            var sim2 = NewSim();
            sim2.Deploy(target, cell.X, cell.Y);
            var save2 = new SaveService("mem", _db, new FixedRandom(0f), new MemoryFileStore()).NewProfile();
            save2.tutOff = 0;
            var tut = new TutorialDirector(_db, save2, sim2);
            Advance(tut, 6f, sim2);

            if (tut.Active == null) Assert.Pass("칸이 막혀 해당 스텝을 건너뛰었습니다.");
            Assert.IsFalse(tut.Active.ForcedTile.HasValue && tut.Active.ForcedTile.Value == cell,
                "이미 점유된 칸을 계속 지정하고 있습니다 — 완료할 방법이 없습니다.");
        }

        [Test]
        public void AStepWhoseUnitIsMissingDoesNotStallTheOnesBehindIt()
        {
            // A deck with no furniture appliance: the "place a ranged unit" beat can never run,
            // and every afterPrev step behind it would wait on it forever.
            var sim = new BattleSim(_db, _stage, new[] { "vac" },
                                    new FlatPower(), new FixedRandom(0.5f), new PresentationBus());
            var tut = new TutorialDirector(_db, _save, sim);

            int shown = 0;
            string last = null;
            for (float t = 0; t < 40f; t += 1f / 60f)
            {
                if (!tut.HoldsSimulation && !sim.Finished) sim.Tick(1f / 60f);
                tut.Tick(t);
                var a = tut.Active;
                if (a == null) continue;
                if (a.Step.id.ToString() != last) { last = a.Step.id.ToString(); shown++; }
                // Clear whatever the step waits on so the queue keeps moving.
                if (a.NeedsTap) tut.Complete();
                else if (a.ForcedTile.HasValue && sim.CanPlace(a.Target, a.ForcedTile.Value.X, a.ForcedTile.Value.Y)
                         && sim.Affordable(a.Target))
                    sim.Deploy(a.Target, a.ForcedTile.Value.X, a.ForcedTile.Value.Y);
            }

            Assert.Greater(shown, 1,
                "덱에 없는 가전을 기다리느라 튜토리얼이 첫 스텝에서 멈췄습니다.");
        }
    }
}
