// Rewards paying exactly once, idle accrual and its cap, and save durability.
using System;
using System.Collections.Generic;
using System.Linq;
using DustZero.Core;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    /// <summary>In-memory filesystem so corruption and recovery can be provoked deliberately.</summary>
    sealed class MemoryFileStore : IFileStore
    {
        public readonly Dictionary<string, string> Files = new();
        /// <summary>Set to make the next WriteAllText fail, simulating a crash mid-save.</summary>
        public bool FailNextWrite;

        public bool Exists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) => Files[path];

        public void WriteAllText(string path, string contents)
        {
            if (FailNextWrite) { FailNextWrite = false; throw new IOException_("디스크 쓰기 실패(테스트)"); }
            Files[path] = contents;
        }

        public void Replace(string sourcePath, string destPath, string backupPath)
        {
            if (Files.ContainsKey(destPath)) Files[backupPath] = Files[destPath];
            Files[destPath] = Files[sourcePath];
            Files.Remove(sourcePath);
        }

        public void Delete(string path) => Files.Remove(path);

        public void Move(string from, string to)
        {
            Files[to] = Files[from];
            Files.Remove(from);
        }

        public sealed class IOException_ : Exception
        {
            public IOException_(string m) : base(m) { }
        }
    }

    public class ProgressionTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = TestData.Db;
            SaveClock.UtcNow = () => DateTime.UnixEpoch.AddDays(10_000);
        }

        [TearDown]
        public void TearDown() => SaveClock.UtcNow = () => DateTime.UtcNow;

        PlayerProfile NewProfile(float roll = 0.0f)
        {
            var svc = new SaveService("mem", _db, new FixedRandom(roll), new MemoryFileStore());
            return new PlayerProfile(_db, svc.NewProfile(), new FixedRandom(roll));
        }

        // ------------------------------------------------------------------ rewards

        [Test]
        public void StarRewardPaysOnceAndNeverAgain()
        {
            var p = NewProfile();
            var chapter = _db.StarRewards.Keys.First();
            var row = _db.StarRewards[chapter].OrderBy(r => r.needStars).First();

            // Give the chapter enough stars to qualify.
            foreach (var st in _db.Stages.Where(s => s.Chapter == chapter)) p.Data.SetStars(st.Id, 3);

            Assert.IsTrue(p.ClaimableStarRewards(chapter).Any(r => r.id == row.id));

            long coinBefore = p.Coin;
            var first = p.ClaimStarReward(row);
            Assert.IsNotEmpty(first, "첫 수령에서 보상이 지급되어야 합니다.");
            long coinAfter = p.Coin;

            var second = p.ClaimStarReward(row);
            Assert.IsEmpty(second, "이미 받은 별 보상이 다시 지급되었습니다.");
            Assert.AreEqual(coinAfter, p.Coin, "중복 수령으로 재화가 더 늘었습니다.");
            Assert.IsFalse(p.ClaimableStarRewards(chapter).Any(r => r.id == row.id));
            Assert.AreNotEqual(coinBefore, coinAfter, "보상 그룹이 실제로 지급되어야 합니다.");
        }

        [Test]
        public void FirstClearRewardIsNotPaidOnReplay()
        {
            var p = NewProfile();
            var stage = _db.Stages[0];

            var first = p.ApplyStageResult(stage, won: true, life: _db.Config.lifeMax);
            Assert.IsTrue(first.FirstClear, "첫 클리어로 인식되어야 합니다.");
            Assert.IsNotEmpty(first.Rewards, "첫 클리어 보상이 지급되어야 합니다.");
            Assert.AreEqual(stage.Order, p.Data.best);

            long coinAfterFirst = p.Coin;
            var replay = p.ApplyStageResult(stage, won: true, life: _db.Config.lifeMax);

            Assert.IsFalse(replay.FirstClear, "재도전이 첫 클리어로 잡혔습니다.");
            Assert.IsEmpty(replay.Rewards, "재도전에서 첫 클리어 보상이 또 지급되었습니다.");
            Assert.AreEqual(coinAfterFirst, p.Coin, "재도전으로 재화가 늘었습니다.");
        }

        [Test]
        public void StarRecordOnlyImprovesNeverRegresses()
        {
            var p = NewProfile();
            var stage = _db.Stages[0];

            p.ApplyStageResult(stage, won: true, life: _db.Config.lifeMax);
            Assert.AreEqual(3, p.Data.StarsOf(stage.Id));

            // A worse replay must not lower the record.
            p.ApplyStageResult(stage, won: true, life: 1);
            Assert.AreEqual(3, p.Data.StarsOf(stage.Id), "기록이 더 낮은 결과로 덮어써졌습니다.");
        }

        [Test]
        public void AttendanceIsOncePerDay()
        {
            var p = NewProfile();
            Assert.IsTrue(p.AttendanceAvailable);
            Assert.IsNotEmpty(p.ClaimAttendance());

            long coin = p.Coin;
            Assert.IsFalse(p.AttendanceAvailable);
            Assert.IsEmpty(p.ClaimAttendance(), "같은 날 출석 보상이 두 번 지급되었습니다.");
            Assert.AreEqual(coin, p.Coin);

            // Next day it is available again.
            SaveClock.UtcNow = () => DateTime.UnixEpoch.AddDays(10_001);
            Assert.IsTrue(p.AttendanceAvailable);
            Assert.IsNotEmpty(p.ClaimAttendance());
        }

        [Test]
        public void DuplicateUnitBecomesLimitBreakThenConverts()
        {
            var p = NewProfile();
            var unit = _db.UnitOrder[0];
            p.Data.owned.Remove(unit);

            Assert.AreEqual(DuplicateResult.New, p.AddUnit(unit));
            for (int i = 0; i < _db.Config.starMax; i++)
                Assert.AreEqual(DuplicateResult.LimitBreak, p.AddUnit(unit), $"{i + 1}번째 중복은 돌파여야 합니다.");

            Assert.AreEqual(_db.Config.starMax, p.Data.owned[unit].star);
            Assert.AreEqual(DuplicateResult.Converted, p.AddUnit(unit), "돌파 최대 이후에는 전환되어야 합니다.");
            Assert.AreEqual(_db.Config.starMax, p.Data.owned[unit].star, "돌파가 최대치를 넘었습니다.");
        }

        [Test]
        public void PowerScalesWithLevelAndLimitBreak()
        {
            var p = NewProfile();
            var unit = _db.UnitOrder[0];
            p.Data.owned[unit] = new OwnedUnit { lv = 1, star = 0 };
            Assert.AreEqual(1f, p.PowerOf(unit), 1e-4f);

            p.Data.owned[unit] = new OwnedUnit { lv = 3, star = 2 };
            float expected = 1f + _db.Config.lvBonus * 2 + _db.Config.starBonus * 2;
            Assert.AreEqual(expected, p.PowerOf(unit), 1e-4f);

            Assert.AreEqual(1f, p.PowerOf("__not_owned__"), 1e-4f, "미보유 가전은 배율 1이어야 합니다.");
        }

        // ------------------------------------------------------------------ idle

        [Test]
        public void IdleAccrualIsCappedAtConfiguredHours()
        {
            var p = NewProfile();
            p.ApplyStageResult(_db.Stages[0], won: true, life: _db.Config.lifeMax);
            Assert.IsTrue(p.IdleUnlocked, "1-1 클리어 후 자동 청소가 열려야 합니다.");

            var start = DateTime.UnixEpoch.AddDays(10_000);
            p.Data.idleSince = (long)(start - DateTime.UnixEpoch).TotalMilliseconds;
            p.Data.idleBank = new IdleBank();

            // Far beyond the cap.
            SaveClock.UtcNow = () => start.AddHours(_db.Config.idleCapHours + 50);

            var pending = p.IdlePendingNow();
            Assert.IsNotNull(pending);
            Assert.AreEqual(_db.Config.idleCapHours, pending.Value.Hours, 1e-3,
                "누적 시간이 상한을 넘었습니다.");

            var rate = p.IdleRateStage;
            Assert.AreEqual(rate.IdleCoin * _db.Config.idleCapHours, pending.Value.Coin, 1e-2,
                "상한 시점의 코인이 시급 × 상한과 다릅니다.");
        }

        [Test]
        public void ClaimingIdleKeepsTheFractionalRemainder()
        {
            var p = NewProfile();
            p.ApplyStageResult(_db.Stages[0], won: true, life: _db.Config.lifeMax);

            var start = DateTime.UnixEpoch.AddDays(10_000);
            p.Data.idleSince = (long)(start - DateTime.UnixEpoch).TotalMilliseconds;
            p.Data.idleBank = new IdleBank();
            SaveClock.UtcNow = () => start.AddMinutes(90);

            var pending = p.IdlePendingNow().Value;
            var got = p.ClaimIdle();
            Assert.IsNotNull(got);

            double expectedRemainder = pending.Coin - Math.Floor(pending.Coin);
            Assert.AreEqual(expectedRemainder, p.Data.idleBank.coin, 1e-6,
                "소수점 아래가 버려졌습니다.");
            Assert.AreEqual(0d, p.IdlePendingNow().Value.Hours, 1e-6,
                "수령 후 누적 시간이 0으로 돌아가야 합니다.");
        }

        [Test]
        public void IdleBankFreezesOldRateBeforeTheRateImproves()
        {
            var p = NewProfile();
            p.ApplyStageResult(_db.Stages[0], won: true, life: _db.Config.lifeMax);
            var oldRate = p.IdleRateStage;

            var start = DateTime.UnixEpoch.AddDays(10_000);
            p.Data.idleSince = (long)(start - DateTime.UnixEpoch).TotalMilliseconds;
            p.Data.idleBank = new IdleBank();
            SaveClock.UtcNow = () => start.AddHours(2);

            // Clearing the next stage banks the two hours at the old rate first.
            var result = p.ApplyStageResult(_db.Stages[1], won: true, life: _db.Config.lifeMax);
            Assert.IsTrue(result.FirstClear);

            Assert.AreEqual(oldRate.IdleCoin * 2, p.Data.idleBank.coin, 1e-2,
                "시급이 오르기 전 몫이 옛 시급으로 확정되지 않았습니다.");
            Assert.Greater(p.IdleRateStage.IdleCoin, oldRate.IdleCoin, "시급이 올라야 합니다.");
        }

        // ------------------------------------------------------------------ save

        [Test]
        public void SaveRoundTripsThroughDisk()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);

            var data = svc.NewProfile();
            data.best = 4;
            // NewProfile already paid the start reward group, so compare against that baseline.
            double coinBefore = data.Item(PlayerProfile.CoinItemId);
            data.AddItem(PlayerProfile.CoinItemId, 12_345);
            double coinExpected = coinBefore + 12_345;
            data.SetStars(_db.Stages[0].Id, 2);
            data.MarkStarRewardClaimed(_db.StarRewards.Values.First()[0].id);
            data.MarkTutorial(_db.Tutorials[0].id);
            data.tutOpen = 3;
            svc.Write(data);

            var loaded = svc.Load();
            Assert.AreEqual(LoadStatus.Loaded, loaded.Status);
            Assert.AreEqual(4, loaded.Data.best);
            Assert.AreEqual(coinExpected, loaded.Data.Item(PlayerProfile.CoinItemId), 1e-6);
            Assert.AreEqual(2, loaded.Data.StarsOf(_db.Stages[0].Id));
            Assert.IsTrue(loaded.Data.IsStarRewardClaimed(_db.StarRewards.Values.First()[0].id));
            Assert.IsTrue(loaded.Data.SawTutorial(_db.Tutorials[0].id));
            Assert.AreEqual(3, loaded.Data.tutOpen);
            Assert.AreEqual(SaveData.CurrentVersion, loaded.Data.version);
        }

        [Test]
        public void TruncatedSaveFallsBackToBackup()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);

            var data = svc.NewProfile();
            data.best = 7;
            svc.Write(data);          // creates main
            data.best = 9;
            svc.Write(data);          // main=9, backup=7

            // Simulate a torn write.
            fs.Files[svc.MainPath] = "{ \"version\": 3, \"best\": ";

            var loaded = svc.Load();
            Assert.AreEqual(LoadStatus.RecoveredFromBackup, loaded.Status,
                "손상된 저장 파일에서 백업으로 복구되어야 합니다.");
            Assert.AreEqual(7, loaded.Data.best, "백업 시점의 진행도가 복원되어야 합니다.");
            Assert.IsTrue(fs.Exists(svc.CorruptPath), "손상 파일은 보관되어야 합니다.");
        }

        [Test]
        public void BothCopiesCorruptStartsFreshWithoutThrowing()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);
            svc.Write(svc.NewProfile());

            fs.Files[svc.MainPath] = "not json at all";
            fs.Files[svc.BackupPath] = "also broken {{{";

            var loaded = svc.Load();
            Assert.AreEqual(LoadStatus.Corrupted, loaded.Status);
            Assert.IsNotNull(loaded.Data);
            Assert.AreEqual(0, loaded.Data.best, "새 프로필로 시작해야 합니다.");
        }

        [Test]
        public void SanitizeDropsUnitsThatNoLongerExist()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);

            var data = svc.NewProfile();
            data.owned["__deleted_appliance__"] = new OwnedUnit { lv = 5 };
            data.deck.Add("__deleted_appliance__");
            data.rep = "__deleted_appliance__";

            svc.Sanitize(data);

            Assert.IsFalse(data.owned.ContainsKey("__deleted_appliance__"), "없어진 가전이 남았습니다.");
            Assert.IsFalse(data.deck.Contains("__deleted_appliance__"), "편성에 없어진 가전이 남았습니다.");
            Assert.IsTrue(data.rep == null || _db.Units.ContainsKey(data.rep), "대표가 유효하지 않습니다.");
        }

        [Test]
        public void DeckIsCappedAtDeckMax()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);
            var data = svc.NewProfile();

            foreach (var id in _db.UnitOrder) data.owned[id] = new OwnedUnit();
            data.deck = _db.UnitOrder.ToList();

            svc.Sanitize(data);
            Assert.LessOrEqual(data.deck.Count, _db.Config.deckMax, "편성이 최대 수를 넘었습니다.");
        }

        [Test]
        public void ExportImportPreservesProgress()
        {
            var fs = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), fs);

            var data = svc.NewProfile();
            data.best = 6;
            data.AddItem(PlayerProfile.CrystalItemId, 999);
            double crystalExpected = data.Item(PlayerProfile.CrystalItemId);

            var json = svc.Export(data);
            var imported = svc.Import(json, out var err);

            Assert.IsNull(err, $"내보낸 데이터를 다시 읽지 못했습니다: {err}");
            Assert.AreEqual(6, imported.best);
            Assert.AreEqual(crystalExpected, imported.Item(PlayerProfile.CrystalItemId), 1e-6);
        }

        [Test]
        public void ImportRejectsGarbageWithAReason()
        {
            var svc = new SaveService("mem", _db, new FixedRandom(0f), new MemoryFileStore());
            var imported = svc.Import("<<<not json>>>", out var err);
            Assert.IsNull(imported);
            Assert.IsNotNull(err, "실패 이유가 설명되어야 합니다.");
        }

        // ------------------------------------------------------------------ gacha

        [Test]
        public void PullChargesExactlyOnceAndReturnsTheRequestedCount()
        {
            var p = NewProfile();
            var banner = _db.Banners[0];
            p.Data.AddItem(banner.costItem, banner.costCount * 5);

            long before = p.ItemCount(banner.costItem);
            var result = p.Pull(banner);

            Assert.AreEqual(banner.pulls, result.Count, "뽑기 횟수가 상품과 다릅니다.");
            Assert.AreEqual(before - banner.costCount, p.ItemCount(banner.costItem),
                "비용이 정확히 한 번 차감되어야 합니다.");
        }

        [Test]
        public void PullIsRefusedWhenCurrencyIsShort()
        {
            var p = NewProfile();
            var banner = _db.Banners[0];
            p.Data.items[banner.costItem.ToString()] = 0d;

            Assert.IsFalse(p.CanPull(banner));
            Assert.IsEmpty(p.Pull(banner), "재화가 없는데 뽑기가 진행되었습니다.");
            Assert.AreEqual(0, p.ItemCount(banner.costItem), "재화가 음수로 내려갔습니다.");
        }

        [Test]
        public void TenPullGuaranteesTheConfiguredRarity()
        {
            var banner = _db.Banners.FirstOrDefault(b => b.guaranteeRarity != 0);
            if (banner == null) Assert.Ignore("확정 등급이 있는 상품이 없습니다.");

            // Roll near 0 so the weighted pick lands on the first (lowest) pool entry.
            var svc = new SaveService("mem", _db, new FixedRandom(0f), new MemoryFileStore());
            var p = new PlayerProfile(_db, svc.NewProfile(), new FixedRandom(0f));
            p.Data.AddItem(banner.costItem, banner.costCount);

            var result = p.Pull(banner);
            int need = banner.guaranteeRarity == _db.Enum("Rarity", "SSR")
                ? (int)Rarity.SSR
                : banner.guaranteeRarity == _db.Enum("Rarity", "SR") ? (int)Rarity.SR : (int)Rarity.R;

            Assert.IsTrue(result.Any(r => (int)_db.Units[r.unitId].Rarity >= need),
                "확정 등급이 보장되지 않았습니다.");
        }

        // ------------------------------------------------------------------ unlocks

        [Test]
        public void SurvivorStaysLockedUntilItsConditionIsMet()
        {
            var p = NewProfile();
            if (!_db.Unlocks.TryGetValue("survivor", out var unlock) || unlock.cond == 0)
                Assert.Ignore("대청소 해금 조건이 설정되어 있지 않습니다.");

            p.Data.best = 0;
            Assert.IsFalse(p.SurvivorUnlocked, "조건 전에 대청소가 열려 있습니다.");

            var cond = _db.Conditions[unlock.cond];
            var required = _db.StageById(int.Parse(cond.p1.AsString()));
            p.Data.best = required.Order;
            Assert.IsTrue(p.SurvivorUnlocked, "조건을 채웠는데 대청소가 잠겨 있습니다.");
        }

        /// <summary>
        /// 전력 스킬은 스테이지 클리어로 열린다. 진행도를 올리면 레벨이 따라 올라야 하고,
        /// 결과가 "방금 열렸다" 를 보고해야 한다 — 결과 화면이 그걸로 한 줄을 쓴다.
        /// </summary>
        [Test]
        public void ClearingAStageOpensItsPowerSkillAndTheResultSaysSo()
        {
            var profile = NewProfile();
            var db = profile.Db;

            // 어떤 전력 스킬이 어느 판에서 열리는지 표에서 직접 읽는다.
            var gated = db.PowerSkills
                .Select(kv => new { Id = kv.Key, Row = kv.Value.FirstOrDefault(r => r.unlockStage != 0) })
                .FirstOrDefault(x => x.Row != null);
            Assert.IsNotNull(gated, "unlockStage 로 열리는 전력 스킬이 표에 없습니다.");

            var stage = db.StageById(gated.Row.unlockStage);
            Assert.IsNotNull(stage, $"전력 스킬 {gated.Id} 의 해금 스테이지 {gated.Row.unlockStage} 가 없습니다.");

            profile.Data.best = stage.Order - 1;
            Assert.AreEqual(0, profile.PowerSkillLevel(gated.Id),
                "아직 안 깬 판인데 전력 스킬이 열려 있습니다.");

            var result = profile.ApplyStageResult(stage, won: true, life: db.Config.lifeMax);
            Assert.Greater(profile.PowerSkillLevel(gated.Id), 0,
                "해금 스테이지를 깼는데 전력 스킬이 열리지 않았습니다.");
            CollectionAssert.Contains(result.PowerSkills.Select(p => p.Id).ToList(), gated.Id,
                "결과가 전력 스킬 해금을 보고하지 않았습니다.");
        }

        /// <summary>첫 클리어와 재도전의 패스 경험치가 config 대로 달라야 한다.</summary>
        [Test]
        public void FirstClearPaysMorePassXpThanAReplay()
        {
            var profile = NewProfile();
            var db = profile.Db;
            var stage = db.StageByOrder(1);

            profile.Data.best = 0;
            var first = profile.ApplyStageResult(stage, won: true, life: db.Config.lifeMax);
            Assert.AreEqual(db.Config.passXpStageFirst, first.PassXp, "첫 클리어 경험치가 다릅니다.");
            Assert.IsTrue(first.FirstClear);

            var again = profile.ApplyStageResult(stage, won: true, life: db.Config.lifeMax);
            Assert.AreEqual(db.Config.passXpStageRepeat, again.PassXp, "재도전 경험치가 다릅니다.");

            var lost = profile.ApplyStageResult(stage, won: false, life: 0);
            Assert.AreEqual(0, lost.PassXp, "졌는데 패스 경험치를 줬습니다.");
        }

    }
}
