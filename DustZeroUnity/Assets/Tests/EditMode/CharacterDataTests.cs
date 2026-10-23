// Adding an appliance must be a table edit, not a code edit.
//
// Two things used to be hardcoded per appliance: which sprite it draws and which skill kit it
// runs. Both moved to 17_Character.xlsx (unit_art / unit_behaviour). These tests hold that
// line - they fail if a kit stops being reachable through the data, or if an appliance is
// added to unit without the art and behaviour rows that make it playable.
using System.Linq;
using DustZero.Core;
using DustZero.Core.Defense;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class CharacterDataTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp() => _db = TestData.Db;

        BattleSim NewSim(params string[] deck)
            => new(_db, _db.Stages[0], deck, new FlatPower(), new FixedRandom(0.5f),
                   new PresentationBus());

        // ------------------------------------------------------------------ coverage

        [Test]
        public void EveryApplianceHasArtAndBehaviourRows()
        {
            foreach (var id in _db.UnitOrder)
            {
                var u = _db.Units[id];
                Assert.IsNotNull(u.Art, $"unit_art 에 '{id}' 행이 없습니다.");
                Assert.IsNotNull(u.Behaviour, $"unit_behaviour 에 '{id}' 행이 없습니다.");
                Assert.IsNotEmpty(u.Art.chibi, $"unit_art: '{id}' 의 치비 파일명이 비어 있습니다.");
                Assert.Greater(u.RoomSpeed, 0f, $"unit_art: '{id}' 의 roomSpeed 가 0 이하입니다.");
            }
        }

        /// <summary>
        /// A kit that reads skill_param must find those values under the appliance's own id.
        /// Borrowing a kit means supplying your own numbers for it, and the table says which.
        /// </summary>
        [Test]
        public void EveryBehaviourFindsTheSkillParamsItDeclares()
        {
            foreach (var id in _db.UnitOrder)
            {
                var b = _db.Units[id].Behaviour;
                foreach (var raw in (b.skp ?? string.Empty).Split(','))
                {
                    var key = raw.Trim();
                    if (key.Length == 0) continue;
                    Assert.IsTrue(_db.HasSkp(id, key),
                        $"skill_param: '{id}' 에 '{key}' 가 없습니다. " +
                        $"unit_behaviour.skp 가 요구하는 값입니다.");
                }
            }
        }

        // ------------------------------------------------------------------ dispatch

        /// <summary>
        /// The generator's regen is reached through its behaviour key, not its id. This is the
        /// one kit whose effect is a plain readable number, so it is the cleanest proof that
        /// behaviour-keyed dispatch still arrives at the right code.
        /// </summary>
        [Test]
        public void GeneratorRegenComesFromItsBehaviourAndItsOwnParams()
        {
            // Asserted against the table rather than against "gen", so the test is a real
            // gate in both directions: if the dispatch went back to hardcoding the id, the
            // regen would keep arriving after the table stopped asking for it.
            bool tableSaysGen = _db.Units["gen"].DefenseBehaviour == "gen";

            var bare = NewSim("vac");
            var withGen = NewSim("vac", "gen");
            Place(withGen, "gen");

            float rate = _db.Skp("gen", "regenPerSec");
            Assert.Greater(rate, 0f, "skill_param 에 gen.regenPerSec 이 있어야 합니다.");

            float expected = bare.PowerRegen() + (tableSaysGen ? rate : 0f);
            Assert.AreEqual(expected, withGen.PowerRegen(), 0.001f,
                tableSaysGen
                    ? "발전기를 깔아도 전력 회복이 늘지 않았습니다 — 동작 연결이 끊겼습니다."
                    : "unit_behaviour 가 gen 동작을 쓰지 않는다고 했는데도 전력이 회복됩니다 — " +
                      "코드가 테이블 대신 가전 ID 를 보고 있습니다.");
        }

        /// <summary>
        /// An appliance whose defense column is blank must still fight. The fallback is what
        /// makes a half-finished appliance playable rather than inert.
        /// </summary>
        [Test]
        public void AnApplianceWithNoKitStillAttacks()
        {
            var plain = _db.UnitOrder.Select(id => _db.Units[id])
                .FirstOrDefault(u => string.IsNullOrEmpty(u.DefenseBehaviour));
            Assert.IsNotNull(plain, "전용 동작이 없는 가전이 한 종은 있어야 이 경로를 검사할 수 있습니다.");
            Assert.Greater(plain.Atk, 0f, $"'{plain.Id}' 는 전용 동작이 없으므로 평타 공격력이 필요합니다.");

            var sim = NewSim(plain.Id);
            var unit = Place(sim, plain.Id);
            Assert.IsNotNull(unit, $"'{plain.Id}' 를 배치하지 못했습니다.");
            Assert.AreEqual(string.Empty, unit.Behaviour);
            // Run long enough for the first enemies to reach it and be shot at.
            float lifeBefore = sim.Life;
            for (int i = 0; i < 600 && !sim.Finished; i++) sim.Tick(1f / 30f);
            Assert.AreNotEqual(BattleOutcome.Lost, sim.Outcome,
                $"'{plain.Id}' 는 전용 동작이 없지만 평타로는 싸울 수 있어야 합니다. " +
                $"(생명 {lifeBefore} -> {sim.Life})");
        }

        /// <summary>
        /// The names a unit_behaviour row may use are the ones the dispatch actually handles.
        /// A typo here would quietly demote an appliance to a plain attacker, which reads as a
        /// balance bug rather than a data one - so the loader rejects it instead.
        /// </summary>
        [Test]
        public void BehaviourNamesAreAllReachable()
        {
            var kits = _db.UnitOrder.Select(id => _db.Units[id].DefenseBehaviour)
                .Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
            CollectionAssert.IsSubsetOf(kits,
                new[] { "vac", "melee", "fridge", "ac", "micro", "washer", "pc", "fan", "gen" },
                "unit_behaviour.defense 에 코드가 모르는 동작 이름이 있습니다.");
            Assert.IsNotEmpty(kits);
        }

        // ------------------------------------------------------------------ art

        /// <summary>
        /// Chatter length is unit.lineCount, not a constant: a unit with fewer lines used to
        /// roll a missing one and print the raw string key into the speech bubble.
        /// </summary>
        [Test]
        public void ChatterLinesMatchTheDeclaredCount()
        {
            foreach (var id in _db.UnitOrder)
            {
                var u = _db.Units[id];
                Assert.Greater(u.Lines.Length, 0, $"'{id}' 의 대사가 하나도 없습니다.");
                for (int i = 0; i < u.Lines.Length; i++)
                    Assert.IsTrue(_db.HasText($"unit.{id}.line{i + 1}"),
                        $"string_ko 에 'unit.{id}.line{i + 1}' 이 없습니다.");
                Assert.IsFalse(_db.HasText($"unit.{id}.line{u.Lines.Length + 1}"),
                    $"'{id}' 에 lineCount({u.Lines.Length}) 보다 많은 대사가 있습니다. " +
                    $"남는 줄은 게임에 절대 나오지 않습니다.");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Places an appliance on the first tile it is allowed to stand on.</summary>
        static BattleUnit Place(BattleSim sim, string type)
        {
            for (int r = 0; r < sim.Field.Rows; r++)
                for (int c = 0; c < sim.Field.Cols; c++)
                    if (sim.CanPlace(type, c, r))
                        return sim.Deploy(type, c, r);
            return null;
        }
    }
}
