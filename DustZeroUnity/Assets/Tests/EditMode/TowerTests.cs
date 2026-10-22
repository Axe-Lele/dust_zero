// 먼지 타워 회귀 검사.
//
// 전투는 리그 판을 그대로 쓰므로(LeagueTests 가 덮는다) 여기서는 **타워만의 것**을 본다:
// 층 규칙, 출전 수와 놓을 수 있는 칸, 상한 없는 계정 성장, 보너스 목표, 한 번만 주는 보상.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core;
using DustZero.Core.League;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class TowerTests
    {
        GameDatabase _db;
        ConfigRow _cfg;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            _cfg = _db.Config;
        }

        PlayerProfile Account(out SaveData data)
        {
            var rng = new LeagueRandom(99);
            data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);
            // 타워는 보유 가전으로만 나간다. 리그 능력이 있는 가전을 넉넉히 쥐여 준다.
            foreach (var id in _db.UnitOrder)
                if (_db.LeagueUnits.ContainsKey(id) && data.Own(id) == null)
                    data.owned[id] = new OwnedUnit { lv = 1, star = 0 };
            data.deck = new List<string>(data.owned.Keys);
            return profile;
        }

        LeagueSim Floor(int number, bool autoFill = true)
        {
            var profile = Account(out _);
            var row = _db.TowerFloor(number);
            Assume.That(row, Is.Not.Null, $"{number}층이 표에 없습니다.");
            return new LeagueSim(_db, profile, row, new LeagueRandom(number * 31), autoFill);
        }

        LeagueSim FloorWithRule(string rule, out TowerFloorRow row)
        {
            row = _db.TowerFloors.FirstOrDefault(f => f.rule == rule);
            Assume.That(row, Is.Not.Null, $"'{rule}' 규칙을 쓰는 층이 없습니다.");
            var profile = Account(out _);
            return new LeagueSim(_db, profile, row, new LeagueRandom(7));
        }

        // ------------------------------------------------------------------ 층 열기

        [Test]
        public void AFloorOpensWithItsOwnSlotCountAndNoShop()
        {
            var lg = Floor(1);
            Assert.IsTrue(lg.IsTower, "타워로 열리지 않았습니다.");
            Assert.AreEqual(_db.TowerFloor(1).slots, lg.Level, "출전 수가 층의 값과 다릅니다.");
            Assert.AreEqual(0, lg.Gold, "타워에는 코인이 없습니다.");
            Assert.AreEqual(LeaguePhase.Prep, lg.Phase);
        }

        [Test]
        public void TheBoardIsFilledUpToTheSlotCount()
        {
            var lg = Floor(1);
            Assert.AreEqual(lg.Level, lg.OnBoard, "빈자리가 자동으로 채워지지 않았습니다.");
            Assert.IsTrue(lg.Board.Where(p => p != null).All(p => p.Star == 1),
                          "타워에는 ★ 승급이 없습니다.");
        }

        [Test]
        public void AutoFillPrefersTwoMeleeWhenTheFloorAllows()
        {
            var lg = Floor(1);
            Assume.That(lg.Level, Is.GreaterThanOrEqualTo(2));
            int melee = lg.Board.Count(p => p != null
                                            && _db.Units[p.Type].Tile == TileKind.Ground);
            Assert.GreaterOrEqual(melee, 2, "근접 가전이 둘보다 적습니다.");
        }

        [Test]
        public void TogglingTakesAnApplianceOutAndBackIn()
        {
            var lg = Floor(1);
            var type = lg.Board.First(p => p != null).Type;

            Assert.AreEqual("out", lg.TowerToggle(type), "빼지 못했습니다.");
            Assert.IsFalse(lg.Board.Any(p => p != null && p.Type == type));

            Assert.AreEqual("in", lg.TowerToggle(type), "다시 넣지 못했습니다.");
            Assert.IsTrue(lg.Board.Any(p => p != null && p.Type == type));
        }

        [Test]
        public void TheBoardRefusesMoreThanTheSlotCount()
        {
            var lg = Floor(1);
            var spare = lg.TowerRoster().FirstOrDefault(t => !lg.Board.Any(p => p != null && p.Type == t));
            Assume.That(spare, Is.Not.Null, "남는 가전이 없습니다.");
            Assert.AreEqual("cap", lg.TowerToggle(spare), "출전 수를 넘겨 넣었습니다.");
        }

        // ------------------------------------------------------------------ 층 규칙

        [Test]
        public void TheNarrowFloorOnlyAllowsTheMiddleColumns()
        {
            var lg = FloorWithRule("narrow", out var row);
            var rule = _db.TowerRules["narrow"];

            for (int i = 0; i < LeagueSim.BoardCells; i++)
            {
                int col = i % LeagueSim.Cols;
                bool expected = col >= rule.p1 && col <= rule.p2;
                Assert.AreEqual(expected, lg.TowerCellOk(i), $"{i}번 칸 판정이 다릅니다.");
            }
            Assert.IsTrue(lg.Board.Select((p, i) => (p, i)).Where(x => x.p != null)
                            .All(x => lg.TowerCellOk(x.i)),
                          "놓을 수 없는 칸에 가전이 있습니다.");
        }

        [Test]
        public void TheGuardFloorKeepsThePurifierCellEmpty()
        {
            var lg = FloorWithRule("guard", out _);
            Assert.IsFalse(lg.TowerCellOk(17), "청정기 자리가 비어 있지 않습니다.");
            Assert.IsNull(lg.Board[17], "청정기 자리에 가전이 놓였습니다.");
        }

        [Test]
        public void TheSoloFloorCutsTheSquadAndDoublesHealth()
        {
            var lg = FloorWithRule("solo", out var row);
            var rule = _db.TowerRules["solo"];

            Assert.AreEqual((int)rule.p1, lg.Level, "단독 층의 출전 수가 규칙과 다릅니다.");

            var piece = lg.Board.First(p => p != null);
            var baseRow = _db.LeagueUnits[piece.Type];
            var st = lg.UnitStats(piece);
            Assert.AreEqual(baseRow.hp * rule.p2, st.Hp, baseRow.hp * 0.01f,
                            "단독 층 체력 배수가 규칙과 다릅니다.");
        }

        [Test]
        public void TheHeatFloorLiftsSeasonAppliancesAndSlowsTheRest()
        {
            var lg = FloorWithRule("heat", out _);
            var rule = _db.TowerRules["heat"];

            foreach (var p in lg.Board.Where(x => x != null))
            {
                var b = _db.LeagueUnits[p.Type];
                var st = lg.UnitStats(p);
                if (_db.Units[p.Type].Family == 4)
                    Assert.AreEqual(b.atk * (1f + rule.p1), st.Atk, b.atk * 0.02f,
                                    $"{p.Type} 의 공격력이 열기 보정과 다릅니다.");
                else
                    Assert.AreEqual(b.itv * (1f + rule.p2), st.Itv, 0.001f,
                                    $"{p.Type} 의 공격 간격이 열기 보정과 다릅니다.");
            }
        }

        [Test]
        public void TheGuestFloorLendsAnApplianceTheAccountDoesNotOwn()
        {
            var row = _db.TowerFloors.FirstOrDefault(
                f => f.rule == "guest" && _db.LeagueUnits.ContainsKey(f.extra));
            Assume.That(row, Is.Not.Null, "손님 규칙을 쓰는 층이 없습니다.");

            // 손님 가전만 **없는** 계정을 만든다.
            var rng = new LeagueRandom(5);
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, rng);
            profile.Grant(_cfg.startRewardGroup);
            foreach (var id in _db.UnitOrder)
                if (_db.LeagueUnits.ContainsKey(id) && id != row.extra && data.Own(id) == null)
                    data.owned[id] = new OwnedUnit { lv = 1, star = 0 };
            data.owned.Remove(row.extra);
            data.deck = new List<string>(data.owned.Keys);

            var lg = new LeagueSim(_db, profile, row, rng);
            Assert.AreEqual(row.extra, lg.Tower.Guest, "손님이 정해지지 않았습니다.");
            Assert.IsTrue(lg.TowerRoster().Contains(row.extra), "손님이 출전 목록에 없습니다.");
            Assert.IsTrue(lg.Board.Any(p => p != null && p.Type == row.extra),
                          "손님이 판에 서지 않았습니다.");
            Assert.GreaterOrEqual(lg.Tower.GuestPower, 1f, "손님 성장이 1 보다 작습니다.");
        }

        [Test]
        public void TheMirrorFloorCopiesOurFrontUnitsToTheDustSide()
        {
            var lg = FloorWithRule("mirror", out _);
            var rule = _db.TowerRules["mirror"];

            int ours = lg.OnBoard;
            lg.StartBattle();

            int mirrors = lg.Units.Count(u => u.Side == 1 && u.Mirror);
            Assert.AreEqual(System.Math.Min((int)rule.p1, ours), mirrors,
                            "거울 적 수가 규칙과 다릅니다.");

            foreach (var m in lg.Units.Where(u => u.Mirror))
                Assert.Less(m.Y, LeagueSim.OurRows, "거울 적이 우리 쪽에 섰습니다.");
        }

        [Test]
        public void TheBlackoutFloorHoldsManaBackAndAGeneratorShortensIt()
        {
            var lg = FloorWithRule("blackout", out var row);
            var rule = _db.TowerRules["blackout"];
            bool hasGen = lg.Board.Any(p => p != null && p.Type == "gen");

            lg.StartBattle();
            var lockField = typeof(LeagueSim).GetField("_manaLock",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            float manaLock = (float)lockField.GetValue(lg);

            Assert.AreEqual(hasGen ? rule.p2 : rule.p1, manaLock, 0.001f,
                            "정전 시간이 규칙과 다릅니다.");

            // 잠금 동안에는 때려도 마나가 차지 않는다.
            var ally = lg.Units.First(u => u.Side == 0);
            ally.Mana = 0f;
            for (int i = 0; i < 30; i++) lg.Step(1f / 30f);
            Assert.AreEqual(0f, ally.Mana, 0.001f, "정전 중에 마나가 찼습니다.");
        }

        [Test]
        public void TheGuardFloorStandsUpAPurifierAndWinsByOutlastingTheClock()
        {
            var lg = FloorWithRule("guard", out _);
            var rule = _db.TowerRules["guard"];

            lg.StartBattle();
            Assert.IsNotNull(lg.Tower.Purifier, "공기청정기가 서지 않았습니다.");
            Assert.AreEqual(rule.p1, lg.Tower.GoalTime, 0.001f, "버텨야 하는 시간이 다릅니다.");

            lg.RunBattleToEnd();
            Assert.IsTrue(lg.BattleOver, "전투가 끝나지 않았습니다.");
            // 청정기가 살아 있으면 이기고, 꺼졌으면 진다 — 둘 중 하나로만 끝나야 한다.
            if (lg.Tower.Purifier.Alive)
                Assert.IsTrue(lg.BattleWin == true || lg.Units.Count(u => u.Side == 0 && u.Alive) == 1,
                              "청정기가 살았는데 이기지도 지지도 않았습니다.");
            else
                Assert.IsFalse(lg.BattleWin == true, "청정기가 꺼졌는데 이겼습니다.");
        }

        [Test]
        public void ThePurifierLosingAllItsHealthEndsTheFloor()
        {
            var lg = FloorWithRule("guard", out _);
            lg.StartBattle();

            lg.Tower.Purifier.Hp = 0f;
            lg.Tower.Purifier.Alive = false;
            lg.Step(1f / 30f);

            Assert.IsTrue(lg.BattleOver);
            Assert.IsFalse(lg.BattleWin == true, "청정기가 꺼졌는데 이겼습니다.");
            StringAssert.Contains("공기청정기", lg.TowerWhy());
        }

        [Test]
        public void TheGoldenDustRunsAwayWhenItsTimeIsUp()
        {
            var lg = FloorWithRule("golden", out _);
            var rule = _db.TowerRules["golden"];

            lg.StartBattle();
            Assert.IsNotNull(lg.Tower.Gold, "황금 먼지가 없습니다.");
            Assert.IsTrue(lg.Tower.Gold.Flees, "황금 먼지가 도망 다니지 않습니다.");

            // 다른 먼지를 먼저 치운다 — 황금 먼지가 남아 있는 한 전투는 이어진다.
            foreach (var u in lg.Units.Where(x => x.Side == 1 && !x.Flees)) u.Alive = false;

            // 제 시간이 지나면 사라지고, 잡은 것으로 치지 않는다.
            // 도망가기 전에 잡히면 전투가 끝나고 BattleTime 이 멈춘다 — 그러면 시간 조건만으로는
            // 영영 안 끝난다. 그래서 체력을 채워 두고 돌리며, 전투가 끝나면 루프도 끝낸다.
            while (!lg.BattleOver && !lg.Tower.Gold.Fled && lg.BattleTime < rule.p1 + 0.5f)
            {
                lg.Tower.Gold.Hp = lg.Tower.Gold.MaxHp;
                lg.Step(1f / 30f);
            }
            Assert.IsFalse(lg.Tower.Gold.Alive, "시간이 지나도 남아 있습니다.");
            Assert.IsTrue(lg.Tower.Gold.Fled, "도망간 것으로 기록되지 않았습니다.");
            Assert.IsFalse(lg.Tower.GoldCaught, "도망갔는데 잡은 것으로 셌습니다.");
        }

        // ------------------------------------------------------------------ 화면이 쓰는 문구

        [Test]
        public void ARuleDescriptionTakesItsNumbersFromTheRuleRow()
        {
            // {p1}/{p2}/{p1_pct} 가 남아 있으면 표의 수치가 들어가지 않은 것이다.
            foreach (var floor in _db.TowerFloors)
            {
                string desc = TowerText.RuleDesc(_db, floor);
                Assert.IsNotEmpty(desc, $"{floor.floor}층 규칙 설명이 비었습니다.");
                StringAssert.DoesNotContain("{p1", desc, $"{floor.floor}층에 수치가 안 들어갔습니다.");
                StringAssert.DoesNotContain("{p2", desc, $"{floor.floor}층에 수치가 안 들어갔습니다.");
                StringAssert.DoesNotContain("{guest}", desc, $"{floor.floor}층 손님 이름이 비었습니다.");
            }
        }

        [Test]
        public void ABonusGoalSpellsOutItsOwnNumber()
        {
            foreach (var floor in _db.TowerFloors)
            {
                if (string.IsNullOrEmpty(floor.bonus)) continue;
                string text = TowerText.BonusText(_db, floor);
                Assert.IsNotEmpty(text, $"{floor.floor}층 보너스 문구가 비었습니다.");
                StringAssert.DoesNotContain("{n}", text, $"{floor.floor}층 보너스 수가 안 들어갔습니다.");
            }
        }

        [Test]
        public void TheSoloFloorTakesItsSlotCountFromItsRuleNotTheFloor()
        {
            var row = _db.TowerFloors.FirstOrDefault(f => f.rule == "solo");
            Assume.That(row, Is.Not.Null);
            Assert.AreEqual((int)_db.TowerRules["solo"].p1, TowerText.Slots(_db, row),
                            "단독 층의 출전 수가 규칙을 따르지 않습니다.");
        }

        [Test]
        public void TheGuardFloorCountsDownItsOwnHoldingTime()
        {
            var row = _db.TowerFloors.FirstOrDefault(f => f.rule == "guard");
            Assume.That(row, Is.Not.Null);
            Assert.AreEqual(_db.TowerRules["guard"].p1, TowerText.TimeLimit(_db, row), 0.001f,
                            "지키기 층의 제한 시간이 규칙과 다릅니다.");

            var plain = _db.TowerFloors.First(f => string.IsNullOrEmpty(f.rule));
            Assert.AreEqual(_cfg.towerTime, TowerText.TimeLimit(_db, plain), 0.001f,
                            "보통 층의 제한 시간이 config.towerTime 과 다릅니다.");
        }

        [Test]
        public void TheRecommendedMarkFollowsTheFloorsRule()
        {
            var heat = _db.TowerFloors.FirstOrDefault(f => f.rule == "heat");
            Assume.That(heat, Is.Not.Null);
            Assert.IsTrue(TowerText.Recommended(_db, heat, "ac", null),
                          "열기 층에서 냉방 가전이 추천되지 않습니다.");
            Assert.IsFalse(TowerText.Recommended(_db, heat, "gen", null),
                           "열기 층에서 발전기가 추천됩니다.");

            var dark = _db.TowerFloors.FirstOrDefault(f => f.rule == "blackout");
            Assume.That(dark, Is.Not.Null);
            Assert.IsTrue(TowerText.Recommended(_db, dark, "gen", null),
                          "정전 층에서 발전기가 추천되지 않습니다.");

            var guest = _db.TowerFloors.FirstOrDefault(f => f.rule == "guest");
            Assume.That(guest, Is.Not.Null);
            Assert.IsTrue(TowerText.Recommended(_db, guest, guest.extra, guest.extra),
                          "손님 층에서 빌린 가전이 추천되지 않습니다.");
        }

        [Test]
        public void EveryFloorNamesWhatItPaysOnTheFirstClear()
        {
            foreach (var floor in _db.TowerFloors)
                Assert.IsNotEmpty(TowerText.RewardLine(_db, floor),
                                  $"{floor.floor}층 첫 클리어 보상 줄이 비었습니다.");
        }

        // ------------------------------------------------------------------ 보너스와 보상

        [Test]
        public void TheNoDownBonusFailsOnceAnApplianceFalls()
        {
            var row = _db.TowerFloors.FirstOrDefault(f => f.bonus == "noDown");
            Assume.That(row, Is.Not.Null);
            var profile = Account(out _);
            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(3));

            lg.StartBattle();
            foreach (var u in lg.Units.Where(x => x.Side == 1)) u.Alive = false;
            lg.Step(1f / 30f);
            Assume.That(lg.BattleWin, Is.True, "이기지 못해 보너스를 볼 수 없습니다.");
            Assert.IsTrue(lg.TowerBonusOk(), "아무도 안 쓰러졌는데 보너스가 아닙니다.");

            lg.Tower.Down = 1;
            Assert.IsFalse(lg.TowerBonusOk(), "쓰러졌는데 보너스가 남아 있습니다.");
        }

        [Test]
        public void AFloorPaysItsFirstClearOnlyOnce()
        {
            var profile = Account(out _);
            var row = _db.TowerFloor(1);
            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(21));

            lg.StartBattle();
            foreach (var u in lg.Units.Where(x => x.Side == 1)) u.Alive = false;
            lg.Step(1f / 30f);
            Assume.That(lg.BattleWin, Is.True);

            long crystal = profile.Crystal;
            var first = lg.TowerReward();
            Assert.IsTrue(first.First, "첫 클리어로 기록되지 않았습니다.");
            // 즉사시켰으므로 아무도 안 쓰러졌다 — 보너스(noDown)도 같이 들어온다.
            long expected = crystal + row.crystal + (first.Bonus ? row.bonusCrystal : 0);
            Assert.AreEqual(expected, profile.Crystal, "첫 클리어 크리스탈이 다릅니다.");
            Assert.IsTrue(profile.TowerCleared(1));
            Assert.AreEqual(2, profile.TowerTopFloor(), "다음 층이 열리지 않았습니다.");

            long after = profile.Crystal;
            var again = lg.TowerReward();
            Assert.IsFalse(again.First, "같은 층을 두 번 첫 클리어로 쳤습니다.");
            Assert.AreEqual(after, profile.Crystal, "두 번째 클리어가 또 보상을 줬습니다.");
        }

        [Test]
        public void TheBonusCrystalIsAlsoPaidOnlyOnce()
        {
            var profile = Account(out _);
            var row = _db.TowerFloors.First(f => f.bonus == "noDown" && f.bonusCrystal > 0);
            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(23));

            lg.StartBattle();
            foreach (var u in lg.Units.Where(x => x.Side == 1)) u.Alive = false;
            lg.Step(1f / 30f);
            Assume.That(lg.BattleWin, Is.True);

            long crystal = profile.Crystal;
            var got = lg.TowerReward();
            Assert.IsTrue(got.Bonus, "보너스를 받지 못했습니다.");
            Assert.AreEqual(crystal + row.crystal + row.bonusCrystal, profile.Crystal,
                            "첫 클리어 + 보너스 크리스탈이 맞지 않습니다.");

            long after = profile.Crystal;
            var again = lg.TowerReward();
            Assert.IsFalse(again.Bonus, "보너스를 두 번 줬습니다.");
            Assert.AreEqual(after, profile.Crystal);
        }

        [Test]
        public void LosingPaysNothing()
        {
            var profile = Account(out _);
            var row = _db.TowerFloor(1);
            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(29));

            lg.StartBattle();
            foreach (var u in lg.Units.Where(x => x.Side == 0)) u.Alive = false;
            lg.Step(1f / 30f);
            Assume.That(lg.BattleWin, Is.False);

            long crystal = profile.Crystal;
            var got = lg.TowerReward();
            Assert.IsFalse(got.First);
            Assert.IsEmpty(got.Got, "졌는데 보상이 있습니다.");
            Assert.AreEqual(crystal, profile.Crystal);
            Assert.IsFalse(profile.TowerCleared(1), "졌는데 클리어로 기록됐습니다.");
        }

        [Test]
        public void AccountGrowthGoesInWithoutTheLeaguesCap()
        {
            var profile = Account(out var data);
            var row = _db.TowerFloor(1);

            // 한 가전을 크게 키운다. 리그라면 leagueGrowthMax 에서 잘린다.
            var lg0 = new LeagueSim(_db, profile, row, new LeagueRandom(31));
            var type = lg0.Board.First(p => p != null).Type;
            data.owned[type] = new OwnedUnit { lv = 30, star = 3 };

            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(31));
            var piece = lg.Board.FirstOrDefault(p => p != null && p.Type == type);
            Assume.That(piece, Is.Not.Null);

            var st = lg.UnitStats(piece);
            float power = profile.PowerOf(type);
            Assert.Greater(power - 1f, _cfg.leagueGrowthMax,
                           "검사 전제가 깨졌습니다 — 성장이 리그 상한보다 작습니다.");
            Assert.AreEqual(power - 1f, st.Growth, 0.001f, "타워 성장이 계정 성장과 다릅니다.");
            Assert.AreEqual(_db.LeagueUnits[type].hp * power, st.Hp,
                            _db.LeagueUnits[type].hp * 0.01f, "체력에 성장이 그대로 들어가지 않았습니다.");
        }

        [Test]
        public void TheFormationIsRememberedForTheNextFloor()
        {
            var profile = Account(out _);
            var row = _db.TowerFloor(1);
            var lg = new LeagueSim(_db, profile, row, new LeagueRandom(37));

            var placed = lg.Board.Select((p, i) => (Cell: i, Type: p?.Type))
                           .Where(x => x.Type != null)
                           .Select(x => (x.Cell, x.Type))
                           .ToList();
            profile.SaveTowerFormation(placed);

            var next = new LeagueSim(_db, profile, _db.TowerFloor(2), new LeagueRandom(41));
            foreach (var (cell, type) in placed)
                if (next.TowerCellOk(cell))
                    Assert.AreEqual(type, next.Board[cell]?.Type,
                                    $"{cell}번 칸의 지난 편성이 되살아나지 않았습니다.");
        }
    }
}
