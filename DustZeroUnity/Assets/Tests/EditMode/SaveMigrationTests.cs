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

        // ------------------------------------------------------------------ v7 -> v8

        /// <summary>
        /// v8 은 10.22-g 가 한꺼번에 들여온 것들(퀘스트 · 미션 · 이벤트 · 스토리 · 호감도 ·
        /// 우편 · 먼지 도감 · 심화 · 소환 천장)의 그릇을 만든다. **옮길 값은 없다** —
        /// 전부 "아직 안 했다"가 맞는 시작 상태다. 그릇이 null 로 남으면 처음 쓰는 순간
        /// NullReference 로 터지고, 그 자리는 저장이 아니라 플레이 중이다.
        /// </summary>
        [Test]
        public void V7FileMigratesToV8WithEmptyContainers()
        {
            var svc = NewServiceWith(V7Json, out _);
            var result = svc.Load();

            Assert.AreEqual(LoadStatus.Loaded, result.Status, "유효한 v7 파일이 손상으로 처리됐습니다.");
            var d = result.Data;
            Assert.AreEqual(8, d.version);

            Assert.IsNotNull(d.quest?.got, "퀘스트 그릇이 null 입니다.");
            Assert.IsNotNull(d.ms?.d?.c, "일일 미션 그릇이 null 입니다.");
            Assert.IsNotNull(d.ms?.w?.got, "주간 미션 그릇이 null 입니다.");
            Assert.IsNotNull(d.ms?.a?.got, "업적 그릇이 null 입니다.");
            Assert.IsNotNull(d.ms?.ev, "이벤트 그릇이 null 입니다.");
            Assert.IsNotNull(d.story?.done, "스토리 그릇이 null 입니다.");
            Assert.IsNotNull(d.story?.game, "미니게임 그릇이 null 입니다.");
            Assert.IsNotNull(d.aff, "호감도 그릇이 null 입니다.");
            Assert.IsNotNull(d.codex?.seen, "도감 그릇이 null 입니다.");
            Assert.IsNotNull(d.mail?.list, "우편 그릇이 null 입니다.");
            Assert.IsNotNull(d.hstars, "심화 별 그릇이 null 입니다.");

            Assert.IsEmpty(d.quest.got, "없던 퀘스트 보상이 받은 것으로 생겨났습니다.");
            Assert.IsEmpty(d.ms.a.got, "없던 업적이 받은 것으로 생겨났습니다.");
            Assert.IsEmpty(d.story.done, "없던 스토리가 본 것으로 생겨났습니다.");
            Assert.IsEmpty(d.mail.list, "없던 우편이 생겨났습니다.");
            Assert.AreEqual(0, d.gpity, "소환 천장이 0 에서 시작하지 않습니다.");
            Assert.AreEqual(0, d.hbest, "심화 진행이 0 에서 시작하지 않습니다.");
        }

        /// <summary>
        /// 예전 파일은 이미 대청소·협동·타워·리그를 열어 놓고 쓰던 계정이다. 거기에
        /// 새 콘텐츠가 열렸다고 알리면 거짓말이다. 그래서 그 넷은 이미 본 것으로 적는다.
        /// </summary>
        [Test]
        public void V7FileDoesNotReannounceContentItAlreadyHad()
        {
            var d = NewServiceWith(V7Json, out _).Load().Data;
            foreach (var id in new[] { "survivor", "coop", "tower", "league" })
                Assert.IsTrue(d.unlSeen.TryGetValue(id, out var seen) && seen,
                    $"{id} 해금 안내를 다시 띄웁니다 — 이미 쓰던 콘텐츠입니다.");
        }

        /// <summary>
        /// 새로 생긴 수치 칸은 0 으로 읽히는데, 리그 배속만은 0 이 멈춤이라 뜻이 다르다.
        /// 나머지 칸은 0 이 곧 "아직 없음"이므로 손대면 안 된다.
        /// </summary>
        [Test]
        public void LeagueSpeedNeverMigratesToZero()
        {
            var d = NewServiceWith(V7Json, out _).Load().Data;
            Assert.GreaterOrEqual(d.lgSpeed, 1, "0 배속은 판이 멈춘다는 뜻입니다.");
        }

        /// <summary>
        /// 10.22-b 설정 셋은 원본이 **0 일 때만** 끈다(S.opt.vib===0). 예전 파일에는
        /// 그 칸이 아예 없으므로 켜짐으로 읽혀야 한다. 0 으로 읽으면 업데이트 한 번에
        /// 모든 플레이어의 진동·흔들림·피해 숫자가 조용히 꺼진다.
        /// </summary>
        [Test]
        public void V7FileKeepsTheNewTogglesOn()
        {
            var d = NewServiceWith(V7Json, out _).Load().Data;
            Assert.AreEqual(1, d.opt.vib, "진동이 꺼진 채로 올라왔습니다.");
            Assert.AreEqual(1, d.opt.shk, "화면 흔들림이 꺼진 채로 올라왔습니다.");
            Assert.AreEqual(1, d.opt.dmg, "피해 숫자가 꺼진 채로 올라왔습니다.");
            Assert.AreNotEqual("lo", d.opt.fx, "효과 줄이기가 켜진 채로 올라왔습니다.");
            Assert.AreEqual(60, d.opt.bgm, "BGM 설정이 사라졌습니다.");
        }

        /// <summary>v8 이 들어와도 예전 진행은 한 글자도 바뀌지 않는다.</summary>
        [Test]
        public void V7ProgressSurvivesTheV8Migration()
        {
            var d = NewServiceWith(V7Json, out _).Load().Data;

            Assert.AreEqual(12, d.best, "진행도가 바뀌었습니다.");
            Assert.AreEqual(65105d, d.Item(1), "코인이 바뀌었습니다.");
            Assert.AreEqual(3, d.owned["vac"].lv, "레벨이 바뀌었습니다.");
            Assert.AreEqual(2, d.StarsOf(101), "별이 바뀌었습니다.");
            Assert.AreEqual(4, d.tower.best, "타워 기록이 바뀌었습니다.");
            Assert.AreEqual(7, d.coop.best["rush"], "협동 기록이 바뀌었습니다.");
            Assert.AreEqual(1, d.gear.Count, "장비가 사라졌습니다.");
        }

        /// <summary>
        /// Clone 은 세이브를 고치기 전에 뜨는 사본이다. 새 그릇을 얕게 베끼면 사본을
        /// 고칠 때 원본이 같이 바뀌어, 되돌리기가 되돌리지 못한다.
        /// </summary>
        [Test]
        public void CloneDeepCopiesTheV8Containers()
        {
            var d = NewServiceWith(V7Json, out _).Load().Data;
            d.quest.got["q1"] = true;
            d.ms.d.c["kill"] = 5d;
            d.story.game["mg1"] = new StoryGame { best = 10d, ok = 1 };
            d.aff["vac"] = new Affinity { xp = 30, lv = 2 };
            d.mail.list.Add(new MailItem { id = "m1", groups = new List<int> { 1 } });

            var c = d.Clone();
            c.quest.got["q2"] = true;
            c.ms.d.c["kill"] = 99d;
            c.story.game["mg1"].best = 99d;
            c.aff["vac"].xp = 99;
            c.mail.list[0].groups.Add(2);

            Assert.IsFalse(d.quest.got.ContainsKey("q2"), "퀘스트 그릇을 같이 씁니다.");
            Assert.AreEqual(5d, d.ms.d.c["kill"], "미션 그릇을 같이 씁니다.");
            Assert.AreEqual(10d, d.story.game["mg1"].best, "미니게임 기록을 같이 씁니다.");
            Assert.AreEqual(30, d.aff["vac"].xp, "호감도를 같이 씁니다.");
            Assert.AreEqual(1, d.mail.list[0].groups.Count, "우편 보상 목록을 같이 씁니다.");
        }

        /// <summary>
        /// 한 번 올린 파일을 다시 읽어도 v8 그대로여야 한다. 마이그레이션이 두 번 돌면
        /// unlSeen 처럼 한 번만 적는 값이 사용자의 선택을 덮어쓴다.
        /// </summary>
        [Test]
        public void MigratedFileStaysAtV8OnASecondLoad()
        {
            var svc = NewServiceWith(V7Json, out _);
            var first = svc.Load().Data;
            first.unlSeen["survivor"] = false;      // 사용자가 다시 보기로 한 상태
            svc.Write(first);

            var again = svc.Load().Data;
            Assert.AreEqual(8, again.version);
            Assert.IsFalse(again.unlSeen["survivor"],
                "두 번째 읽기에서 마이그레이션이 또 돌아 사용자의 선택을 덮었습니다.");
        }

        /// <summary>
        /// 10.22-g 를 쓰던 계정이 그대로 열려야 한다. 모르는 칸(plog)은 버리고 나머지는
        /// 읽는다 — plog 는 일부러 옮기지 않은 개발용 기록이다.
        /// </summary>
        [Test]
        public void V8FileLoadsWithItsNewFieldsIntact()
        {
            var d = NewServiceWith(V8Json, out _).Load().Data;

            Assert.AreEqual(8, d.version);
            Assert.AreEqual(37, d.gpity, "소환 천장 횟수가 사라졌습니다.");
            Assert.AreEqual(9, d.hbest, "심화 진행이 사라졌습니다.");
            Assert.AreEqual(3, d.hstars["2001"], "심화 별이 사라졌습니다.");
            Assert.IsTrue(d.quest.got["1001"], "받은 퀘스트가 사라졌습니다.");
            Assert.AreEqual(12d, d.ms.d.c["clear"], "일일 미션 진행이 사라졌습니다.");
            Assert.AreEqual("2026-10-23", d.ms.d.k, "일일 미션의 날짜가 사라졌습니다.");
            Assert.AreEqual(2, d.aff["vac"].lv, "호감도 레벨이 사라졌습니다.");
            Assert.AreEqual(40, d.aff["vac"].xp, "호감도 경험치가 사라졌습니다.");
            Assert.AreEqual(1, d.mail.list.Count, "우편이 사라졌습니다.");
            Assert.AreEqual("m1", d.mail.list[0].id);
            Assert.AreEqual(5101, d.mail.list[0].groups[0], "우편 첨부가 사라졌습니다.");
            Assert.IsTrue(d.story.done["101"], "본 스토리가 사라졌습니다.");
            Assert.AreEqual(1, d.story.game["mg_vac"].ok, "미니게임 성공 기록이 사라졌습니다.");
            Assert.IsTrue(d.codex.seen["dust"], "도감 발견이 사라졌습니다.");
            Assert.AreEqual(0, d.opt.vib, "사용자가 끈 진동이 다시 켜졌습니다.");
            Assert.AreEqual("lo", d.opt.fx, "사용자가 켠 효과 줄이기가 꺼졌습니다.");
        }

        /// <summary>v7 파일: 10.18-h 까지의 그릇은 다 있고 10.22-g 의 것은 하나도 없다.</summary>
        const string V7Json = @"{
            ""version"": 7,
            ""items"": { ""1"": 65105.0, ""2"": 120.0 },
            ""owned"": { ""vac"": { ""lv"": 3, ""star"": 1, ""eq"": 0 } },
            ""deck"": [ ""vac"" ],
            ""best"": 12,
            ""stars"": { ""101"": 2 },
            ""starClaimed"": {},
            ""speed"": 1,
            ""rep"": ""vac"",
            ""tut"": {},
            ""tutOff"": 0,
            ""guide"": { ""first"": 4 },
            ""gear"": [ { ""uid"": 3, ""id"": 1101, ""lv"": 2, ""sp"": 0 } ],
            ""gearUid"": 4,
            ""excl"": {},
            ""surv"": { ""day"": ""2026-10-20"", ""runs"": 1, ""bestEver"": 100 },
            ""coop"": { ""best"": { ""rush"": 7 }, ""last"": ""rush"" },
            ""league"": { ""tierMax"": 1, ""best"": {}, ""cleared"": {} },
            ""tower"": { ""best"": 4, ""cleared"": {}, ""bonus"": {}, ""who"": {} },
            ""pass"": { ""season"": 1, ""xp"": 40, ""paid"": 0, ""gf"": {}, ""gp"": {} },
            ""shop"": { ""noAds"": 0, ""dc"": {}, ""ac"": {} },
            ""att"": { ""n"": 2, ""last"": ""2026-10-10"" },
            ""pool"": { ""day"": ""2026-10-20"", ""pts"": 12, ""got"": [] },
            ""opt"": { ""bgm"": 60, ""mute"": 0 }
        }";

        /// <summary>v8 파일: 10.22-g 를 쓰던 계정. plog 는 우리가 읽지 않고 버리는 칸이다.</summary>
        const string V8Json = @"{
            ""version"": 8,
            ""items"": { ""1"": 1000.0 },
            ""owned"": { ""vac"": { ""lv"": 1, ""star"": 0, ""eq"": 0 } },
            ""deck"": [ ""vac"" ],
            ""best"": 20,
            ""gpity"": 37,
            ""hbest"": 9,
            ""hstars"": { ""2001"": 3 },
            ""lgSpeed"": 2,
            ""attAuto"": ""2026-10-23"",
            ""quest"": { ""got"": { ""1001"": true } },
            ""ms"": { ""st"": {}, ""d"": { ""k"": ""2026-10-23"", ""c"": { ""clear"": 12.0 }, ""got"": {}, ""ch"": {} },
                      ""w"": { ""k"": ""2026-10-19"", ""c"": {}, ""got"": {}, ""ch"": {} },
                      ""a"": { ""got"": {} }, ""ev"": {} },
            ""story"": { ""tab"": {}, ""done"": { ""101"": true },
                         ""game"": { ""mg_vac"": { ""best"": 8.0, ""ok"": 1 } } },
            ""aff"": { ""vac"": { ""xp"": 40, ""lv"": 2, ""day"": ""2026-10-23"", ""talk"": 1, ""gift"": 2 } },
            ""codex"": { ""seen"": { ""dust"": true }, ""kill"": { ""dust"": 120.0 }, ""got"": {} },
            ""mail"": { ""sent"": { ""1"": true }, ""seq"": 1,
                        ""list"": [ { ""id"": ""m1"", ""from"": ""청소 본부"", ""title"": ""환영"",
                                      ""body"": ""반가워요"", ""groups"": [ 5101 ],
                                      ""at"": 1700000000000, ""exp"": 0, ""read"": 0, ""done"": 0 } ] },
            ""unlSeen"": { ""survivor"": true },
            ""plog"": [ { ""t"": 1, ""x"": ""버리는 칸"" } ],
            ""opt"": { ""bgm"": 60, ""mute"": 0, ""vib"": 0, ""shk"": 1, ""dmg"": 1, ""fx"": ""lo"" }
        }";

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
