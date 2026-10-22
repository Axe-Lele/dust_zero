// Keys the UI builds at runtime, checked against the shipped table.
//
// Db.Text returns the key it was handed when the lookup misses, so a wrong key does not
// throw or log - it quietly renders as "room.vac.1" in a speech bubble. Only a test that
// asserts the key exists catches that.
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class StringKeyTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp() => _db = TestData.Db;

        /// <summary>
        /// The key RoomView actually composes must resolve. This asks RoomView for the key
        /// rather than restating the pattern, so it fails if the composition regresses -
        /// restating "unit.{id}.line{n}" here would pass against the room.* bug it exists to
        /// catch, since the table is fine either way and only the caller was wrong.
        /// </summary>
        [Test]
        public void RoomChatterKeysResolveForEveryUnit()
        {
            foreach (var id in _db.Units.Keys)
                for (int n = 1; n <= DustZero.Game.RoomView.LinesPerUnit; n++)
                {
                    var key = DustZero.Game.RoomView.ChatterKey(id, n);
                    Assert.IsTrue(_db.HasText(key),
                        $"RoomView 가 만든 키가 문자열 표에 없습니다: {key}");
                }
        }

        /// <summary>Names are what the deck, roster and room labels draw.</summary>
        [Test]
        public void EveryUnitHasAName()
        {
            foreach (var id in _db.Units.Keys)
                Assert.IsTrue(_db.HasText($"unit.{id}.name"), $"이름 문자열이 없습니다: unit.{id}.name");
        }
    }
}
