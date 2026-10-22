// A hand-written v3 file run through the real SaveService, not a synthetic in-memory SaveData -
// the migrator reads JSON, and a JSON shape is exactly the thing a unit test building a SaveData
// object directly cannot exercise.
using System.Collections.Generic;
using DustZero.Core;
using DustZero.Core.Meta;
using DustZero.Data;
using NUnit.Framework;

namespace DustZero.Tests
{
    public class SaveMigrationTests
    {
        GameDatabase _db;

        [SetUp]
        public void SetUp() => _db = TestData.Db;

        /// <summary>
        /// A save file as 10.14-d would have written it: no pass/shop/att containers, since
        /// those arrived with the 10.15-h screens. "daily" is the old single-slot attendance
        /// field the wheel (AttendanceState) replaced.
        /// </summary>
        const string V3Json = @"{
            ""version"": 3,
            ""items"": { ""1"": 5000.0, ""2"": 50.0 },
            ""owned"": { ""vac"": { ""lv"": 5, ""star"": 1 } },
            ""deck"": [ ""vac"" ],
            ""best"": 3,
            ""idleSince"": 1700000000000,
            ""idleBank"": { ""coin"": 10.5, ""part"": 2.0, ""crystal"": 0.0 },
            ""stars"": { ""101"": 3, ""102"": 2 },
            ""starClaimed"": { ""9001"": true },
            ""speed"": 1,
            ""rep"": ""vac"",
            ""daily"": ""2026-10-10"",
            ""tut"": { ""1"": true, ""2"": true },
            ""tutOff"": 0,
            ""tutOpen"": 3,
            ""surv"": {
                ""day"": ""2026-10-10"", ""runs"": 1, ""bestT"": 120, ""bestKills"": 20,
                ""bestEver"": 120, ""leader"": ""vac"", ""marks"": [60]
            }
        }";

        SaveService NewServiceWith(string json, out MemoryFileStore store)
        {
            store = new MemoryFileStore();
            var svc = new SaveService("mem", _db, new FixedRandom(0f), store);
            store.Files[svc.MainPath] = json;
            return svc;
        }

        [Test]
        public void V3FileMigratesToV4WithEmptyNewContainers()
        {
            var svc = NewServiceWith(V3Json, out _);
            var result = svc.Load();

            Assert.AreEqual(LoadStatus.Loaded, result.Status, "유효한 v3 파일이 손상으로 처리됐습니다.");
            var data = result.Data;

            Assert.AreEqual(SaveData.CurrentVersion, data.version, "마이그레이션 후 버전이 올라가지 않았습니다.");
            Assert.IsNotNull(data.pass, "pass 컨테이너가 생성되지 않았습니다.");
            Assert.IsNotNull(data.pass.gf);
            Assert.IsNotNull(data.pass.gp);
            Assert.AreEqual(0, data.pass.xp, "v3 파일에 없던 패스 경험치가 생겨났습니다.");
            Assert.AreEqual(0, data.pass.paid, "v3 파일에 없던 프리미엄 패스가 켜졌습니다.");

            Assert.IsNotNull(data.shop, "shop 컨테이너가 생성되지 않았습니다.");
            Assert.AreEqual(0, data.shop.noAds);
            Assert.IsNotNull(data.shop.dc);
            Assert.IsNotNull(data.shop.ac);
        }

        /// <summary>
        /// The one piece of real information a v3 file carries for the new wheel: a player who
        /// had already claimed today under the old single-slot system must not be asked to
        /// claim a second time just because the wheel is new.
        /// </summary>
        [Test]
        public void V3DailyClaimSeedsAttendanceWheelWithoutDoubleGranting()
        {
            var svc = NewServiceWith(V3Json, out _);
            var data = svc.Load().Data;

            Assert.AreEqual("2026-10-10", data.att.last,
                "출석 휠의 last 가 예전 daily 필드에서 이어지지 않았습니다.");
            Assert.AreEqual(1, data.att.n,
                "예전 daily 출석 기록이 휠의 진행 칸으로 옮겨지지 않았습니다.");
        }

        /// <summary>A v3 file that never claimed attendance must migrate to an untouched wheel,
        /// not a false "day 1 already done".</summary>
        [Test]
        public void V3WithNoPriorAttendanceStartsWheelFresh()
        {
            string json = V3Json.Replace(@"""daily"": ""2026-10-10"",", "");
            var svc = NewServiceWith(json, out _);
            var data = svc.Load().Data;

            Assert.AreEqual("", data.att.last);
            Assert.AreEqual(0, data.att.n);
        }

        [Test]
        public void V3ExistingProgressSurvivesMigrationUntouched()
        {
            var svc = NewServiceWith(V3Json, out _);
            var data = svc.Load().Data;

            Assert.AreEqual(5000.0, data.Item(1), "코인이 마이그레이션 중 바뀌었습니다.");
            Assert.AreEqual(50.0, data.Item(2), "크리스탈이 마이그레이션 중 바뀌었습니다.");
            Assert.IsTrue(data.Owns("vac"));
            Assert.AreEqual(5, data.Own("vac").lv);
            Assert.AreEqual(1, data.Own("vac").star);
            Assert.AreEqual(3, data.best);
            Assert.AreEqual(1700000000000L, data.idleSince);
            Assert.AreEqual(10.5, data.idleBank.coin);
            Assert.AreEqual(3, data.StarsOf(101));
            Assert.IsTrue(data.IsStarRewardClaimed(9001));
            Assert.AreEqual("vac", data.rep);
            Assert.IsTrue(data.SawTutorial(1));
            Assert.AreEqual(3, data.tutOpen);
            Assert.AreEqual(120, data.surv.bestEver);
        }

        /// <summary>
        /// Migration must survive the full read path once more on the very next load - a
        /// player who quits right after their first post-update session should not re-trigger
        /// the v3 backfill (and, if it ever did, it must still be idempotent).
        /// </summary>
        /// <summary>
        /// Sound settings arrived after v4 shipped, so they ride on the field initializer
        /// rather than a migration step. That only holds because Newtonsoft leaves an absent
        /// key alone - the same behaviour that defeated the attendance check above, working
        /// in our favour here - so it is worth pinning rather than assuming.
        /// </summary>
        [Test]
        public void FileWithoutAudioOptionsGetsAudibleDefaults()
        {
            var svc = NewServiceWith(V3Json, out _);
            var data = svc.Load().Data;

            Assert.IsNotNull(data.opt, "오디오 설정이 비어 있습니다.");
            Assert.AreEqual(60, data.opt.bgm, "예전 세이브의 기본 음량이 60이 아닙니다.");
            Assert.AreEqual(0, data.opt.mute, "예전 세이브가 음소거 상태로 열렸습니다.");
        }

        [Test]
        public void StoredAudioOptionsSurviveAReload()
        {
            var svc = NewServiceWith(V3Json, out var store);
            var data = svc.Load().Data;
            data.opt.bgm = 35;
            data.opt.mute = 1;
            svc.Write(data);

            var again = NewServiceWith(store.Files[svc.MainPath], out _);
            var reloaded = again.Load().Data;

            Assert.AreEqual(35, reloaded.opt.bgm);
            Assert.AreEqual(1, reloaded.opt.mute);
        }

        /// <summary>A hand-edited or corrupted file must not hand the mixer a volume above 1.</summary>
        [Test]
        public void OutOfRangeVolumeIsClampedOnLoad()
        {
            string json = V3Json.Replace(@"""version"": 3,", @"""version"": 4, ""opt"": { ""bgm"": 400, ""mute"": 0 },");
            var svc = NewServiceWith(json, out _);
            var data = svc.Load().Data;

            Assert.AreEqual(100, data.opt.bgm, "음량이 100 으로 제한되지 않았습니다.");
        }

        [Test]
        public void MigratedFileStaysAtV4OnASecondLoad()
        {
            var svc = NewServiceWith(V3Json, out var store);
            svc.Load();

            var rewritten = store.Files[svc.MainPath];
            var again = NewServiceWith(rewritten, out _);
            var second = again.Load();

            Assert.AreEqual(LoadStatus.Loaded, second.Status);
            Assert.AreEqual(SaveData.CurrentVersion, second.Data.version);
            Assert.AreEqual(1, second.Data.att.n, "재적재에서 출석 칸이 다시 늘었습니다.");
        }

        // ------------------------------------------------------------------ v4 -> v5 (장비)

        /// <summary>
        /// A v4 file predates the gear system entirely. There is nothing to convert - the
        /// correct outcome is a player with no gear, which is the same state as someone who
        /// has not pulled any. What must not happen is a null container or a uid counter that
        /// starts where it would collide with "nothing equipped".
        /// </summary>
        [Test]
        public void V4FileMigratesToV5WithAnEmptyBagAndAUsableUidCounter()
        {
            var svc = NewServiceWith(V4Json, out _);
            var result = svc.Load();

            Assert.AreEqual(LoadStatus.Loaded, result.Status, "유효한 v4 파일이 손상으로 처리됐습니다.");
            var data = result.Data;

            Assert.AreEqual(SaveData.CurrentVersion, data.version);
            Assert.IsNotNull(data.gear, "gear 목록이 null 입니다.");
            Assert.IsEmpty(data.gear, "v4 파일에 없던 장비가 생겨났습니다.");
            Assert.IsNotNull(data.excl, "excl 목록이 null 입니다.");
            Assert.IsEmpty(data.excl, "v4 파일에 없던 전용장비가 생겨났습니다.");
            Assert.GreaterOrEqual(data.gearUid, 1,
                "uid 0 은 '장착 안 함'을 뜻하므로 첫 uid 가 될 수 없습니다.");
        }

        /// <summary>
        /// A v4 file's progress is untouched by the gear migration. The appliance records grow
        /// an `eq` field, and it must default to 0 - a stray non-zero would make an appliance
        /// claim gear that does not exist.
        /// </summary>
        [Test]
        public void V4ProgressSurvivesTheGearMigrationUntouched()
        {
            var svc = NewServiceWith(V4Json, out _);
            var data = svc.Load().Data;

            Assert.AreEqual(2, data.owned["vac"].lv, "레벨이 바뀌었습니다.");
            Assert.AreEqual(1, data.owned["vac"].star, "돌파가 바뀌었습니다.");
            Assert.AreEqual(4321d, data.Item(1), 0.001d, "코인이 바뀌었습니다.");
            foreach (var kv in data.owned)
                Assert.AreEqual(0, kv.Value.eq,
                    $"'{kv.Key}' 가 있지도 않은 장비를 장착한 상태로 올라왔습니다.");
        }

        /// <summary>
        /// A file that somehow carries gear with a stale counter must not reissue ids that are
        /// already in use: two pieces sharing a uid would make equipping one move the other.
        /// </summary>
        [Test]
        public void GearUidCounterIsPushedPastAnyIdAlreadyInTheFile()
        {
            const string json = @"{""version"":4,""items"":{""1"":10},""owned"":{},""deck"":[],
                ""gear"":[{""uid"":7,""id"":1101,""lv"":0,""sp"":0}],""gearUid"":1}";
            var svc = NewServiceWith(json, out _);
            var data = svc.Load().Data;

            Assert.Greater(data.gearUid, 7,
                "이미 쓰인 uid 를 다시 내주면 장비 두 개가 같은 번호를 갖게 됩니다.");
        }

        /// <summary>A v4-shaped file: has the 10.15-h containers, has no gear.</summary>
        const string V4Json = @"{
            ""version"": 4,
            ""items"": { ""1"": 4321, ""2"": 99 },
            ""owned"": { ""vac"": { ""lv"": 2, ""star"": 1 }, ""ac"": { ""lv"": 1, ""star"": 0 } },
            ""deck"": [ ""vac"", ""ac"" ],
            ""best"": 3,
            ""pass"": { ""season"": 1, ""xp"": 40, ""paid"": 0, ""gf"": {}, ""gp"": {} },
            ""shop"": { ""noAds"": 0, ""dc"": {}, ""ac"": {} },
            ""att"": { ""n"": 2, ""last"": ""2026-10-10"" }
        }";
    }
}
