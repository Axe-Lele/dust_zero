// Durable save/load.
//
// The browser original wrote straight to localStorage, where a half-written value is not a
// failure mode. A file on disk is: a crash or a pulled power cable mid-write leaves a
// truncated JSON that would otherwise wipe the profile. So writes go to a temp file, are
// flushed, and only then replace the live file, with the previous good copy kept as .bak.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DustZero.Data;
using Newtonsoft.Json;

namespace DustZero.Core.Meta
{
    /// <summary>Abstracts the filesystem so save logic is testable without a player.</summary>
    public interface IFileStore
    {
        bool Exists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void Replace(string sourcePath, string destPath, string backupPath);
        void Delete(string path);
        void Move(string from, string to);
    }

    public sealed class DiskFileStore : IFileStore
    {
        public bool Exists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public void WriteAllText(string path, string contents)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var w = new StreamWriter(fs, new UTF8Encoding(false));
            w.Write(contents);
            w.Flush();
            fs.Flush(true);
        }

        public void Replace(string sourcePath, string destPath, string backupPath)
        {
            if (!File.Exists(destPath)) { File.Move(sourcePath, destPath); return; }
            // File.Replace is atomic where the platform supports it and keeps the old copy.
            File.Replace(sourcePath, destPath, backupPath, ignoreMetadataErrors: true);
        }

        public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
        public void Move(string from, string to) { File.Move(from, to); }
    }

    public enum LoadStatus
    {
        /// <summary>No save present; a fresh profile was created.</summary>
        CreatedNew,
        Loaded,
        /// <summary>The main file was unreadable and the backup was used instead.</summary>
        RecoveredFromBackup,
        /// <summary>Both copies were unreadable; a fresh profile was created and the bad files kept.</summary>
        Corrupted,
    }

    public readonly struct LoadResult
    {
        public readonly SaveData Data;
        public readonly LoadStatus Status;
        public readonly string Detail;
        public LoadResult(SaveData d, LoadStatus s, string detail = null) { Data = d; Status = s; Detail = detail; }
    }

    public sealed class SaveService
    {
        public const string FileName = "dustzero-save.json";

        readonly IFileStore _fs;
        readonly string _dir;
        readonly GameDatabase _db;
        readonly IRandom _rng;

        public string MainPath => Path.Combine(_dir, FileName);
        public string TempPath => MainPath + ".tmp";
        public string BackupPath => MainPath + ".bak";
        public string CorruptPath => MainPath + ".corrupt";

        static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
        };

        public SaveService(string directory, GameDatabase db, IRandom rng, IFileStore fs = null)
        {
            _dir = directory;
            _db = db;
            _rng = rng;
            _fs = fs ?? new DiskFileStore();
        }

        // ------------------------------------------------------------------ write

        public void Write(SaveData data)
        {
            data.version = SaveData.CurrentVersion;
            var json = JsonConvert.SerializeObject(data, Settings);

            _fs.WriteAllText(TempPath, json);
            _fs.Replace(TempPath, MainPath, BackupPath);
        }

        // ------------------------------------------------------------------ read

        public LoadResult Load()
        {
            if (_fs.Exists(MainPath))
            {
                if (TryRead(MainPath, out var data, out var err))
                    return new LoadResult(Sanitize(data), LoadStatus.Loaded);

                if (_fs.Exists(BackupPath) && TryRead(BackupPath, out var backup, out _))
                {
                    // Keep the unreadable file for inspection instead of silently dropping it.
                    SafeStash(MainPath);
                    var recovered = Sanitize(backup);
                    Write(recovered);
                    return new LoadResult(recovered, LoadStatus.RecoveredFromBackup, err);
                }

                SafeStash(MainPath);
                var fresh = NewProfile();
                Write(fresh);
                return new LoadResult(fresh, LoadStatus.Corrupted, err);
            }

            if (_fs.Exists(BackupPath) && TryRead(BackupPath, out var onlyBackup, out _))
            {
                var recovered = Sanitize(onlyBackup);
                Write(recovered);
                return new LoadResult(recovered, LoadStatus.RecoveredFromBackup, "본 저장 파일이 없어 백업에서 복구했습니다.");
            }

            var created = NewProfile();
            Write(created);
            return new LoadResult(created, LoadStatus.CreatedNew);
        }

        bool TryRead(string path, out SaveData data, out string error)
        {
            data = null; error = null;
            try
            {
                var text = _fs.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) { error = "파일이 비어 있습니다."; return false; }
                data = JsonConvert.DeserializeObject<SaveData>(text, Settings);
                if (data == null) { error = "JSON 을 해석하지 못했습니다."; return false; }
                SaveMigrator.Migrate(data);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        void SafeStash(string path)
        {
            try { _fs.Delete(CorruptPath); _fs.Move(path, CorruptPath); }
            catch { /* stashing is best-effort; never let it block starting the game */ }
        }

        // ------------------------------------------------------------------ new profile

        /// <summary>Creates a starter profile and pays the configured start reward group.</summary>
        public SaveData NewProfile()
        {
            var data = new SaveData();
            var profile = new PlayerProfile(_db, data, _rng);
            profile.Grant(_db.Config.startRewardGroup);
            if (string.IsNullOrEmpty(data.rep) && data.owned.Count > 0)
                data.rep = data.owned.Keys.First();
            return Sanitize(data);
        }

        /// <summary>
        /// Repairs anything that references content no longer in the tables - a unit removed
        /// from unit.xlsx, a deck longer than deckMax, a representative that was never owned.
        /// Without this a table edit would strand the save behind a null reference.
        /// </summary>
        public SaveData Sanitize(SaveData data)
        {
            data.items ??= new Dictionary<string, double>();
            data.owned ??= new Dictionary<string, OwnedUnit>();
            data.deck ??= new List<string>();
            data.stars ??= new Dictionary<string, int>();
            data.starClaimed ??= new Dictionary<string, bool>();
            data.tut ??= new Dictionary<string, bool>();
            data.idleBank ??= new IdleBank();
            data.surv ??= new SurvivorRecord();
            data.opt ??= new AudioOptions();
            data.opt.bgm = Mathf2.Clamp(data.opt.bgm, 0, 100);

            foreach (var key in data.owned.Keys.Where(k => !_db.Units.ContainsKey(k)).ToList())
                data.owned.Remove(key);

            data.deck = data.deck
                .Where(t => _db.Units.ContainsKey(t) && data.owned.ContainsKey(t))
                .Distinct()
                .Take(_db.Config.deckMax)
                .ToList();

            if (string.IsNullOrEmpty(data.rep) || !data.owned.ContainsKey(data.rep))
                data.rep = data.owned.Count > 0 ? data.owned.Keys.First() : null;

            foreach (var o in data.owned.Values)
            {
                o.lv = Mathf2.Clamp(o.lv, 1, _db.Config.lvMax);
                o.star = Mathf2.Clamp(o.star, 0, _db.Config.starMax);
            }

            data.best = Mathf2.Clamp(data.best, 0, _db.Stages.Count);
            data.speed = data.speed == 2 ? 2 : 1;

            // Drop star records for stages that no longer exist.
            foreach (var key in data.stars.Keys.ToList())
                if (!int.TryParse(key, out var id) || _db.StageById(id) == null) data.stars.Remove(key);

            if (data.idleSince < 0) data.idleSince = 0;
            return data;
        }

        // ------------------------------------------------------------------ export / import

        /// <summary>
        /// Serializes the profile for manual transfer. Browser saves are NOT migrated
        /// automatically - this is the deliberate, user-driven path between the two builds.
        /// </summary>
        public string Export(SaveData data) => JsonConvert.SerializeObject(data, Settings);

        /// <summary>Parses an exported profile. Returns null with a reason when it cannot be used.</summary>
        public SaveData Import(string json, out string error)
        {
            error = null;
            try
            {
                var data = JsonConvert.DeserializeObject<SaveData>(json, Settings);
                if (data == null) { error = "JSON 을 해석하지 못했습니다."; return null; }
                SaveMigrator.Migrate(data);
                return Sanitize(data);
            }
            catch (Exception e) { error = e.Message; return null; }
        }
    }

    public static class SaveMigrator
    {
        /// <summary>
        /// Brings an older file up to the current shape. Versions 1 and 2 are the browser
        /// layouts; they are only reachable through the import path, never read from disk.
        /// </summary>
        public static void Migrate(SaveData data)
        {
            if (data.version >= SaveData.CurrentVersion) { data.version = SaveData.CurrentVersion; return; }

            // Captured before the version bump at the bottom, since that is the only reliable
            // way to tell "this file predates v4" apart from "this file already has an empty
            // attendance wheel". SaveData.att has a field initializer (= new()), so Newtonsoft
            // runs the constructor before overlaying a v3 file's JSON onto it - a v3 file has
            // no "att" key, but the object it deserializes into is never actually null, only an
            // empty default. A `data.att == null` check here looks reasonable and never fires.
            int fromVersion = data.version;

            // v2 -> v3: idleSince was a JS Date.now() in local-epoch milliseconds, which is
            // already Unix ms, so it carries over as-is. The remaining work is filling in
            // containers that did not exist in the browser build.
            data.idleBank ??= new IdleBank();
            data.surv ??= new SurvivorRecord();
            data.tut ??= new Dictionary<string, bool>();
            data.stars ??= new Dictionary<string, int>();
            data.starClaimed ??= new Dictionary<string, bool>();

            // v3 -> v4: the pass, shop and attendance-wheel containers arrived with the
            // 10.15-h screens. An older file simply starts them empty; the one piece of
            // information it does hold is the last attendance day, which seeds the wheel so
            // a returning player is not asked to claim twice in a day.
            data.pass ??= new PassState();
            data.shop ??= new ShopState();
            if (fromVersion < 4)
            {
                data.att = new AttendanceState { last = data.daily ?? "" };
                if (!string.IsNullOrEmpty(data.daily)) data.att.n = 1;
            }
            data.att ??= new AttendanceState();
            data.pass.gf ??= new Dictionary<string, bool>();
            data.pass.gp ??= new Dictionary<string, bool>();

            // v5 -> v6: the daily log (10.18-h) replaced the survival time brackets. An
            // older file starts the log empty; surv.marks is left alone because it is a
            // per-day list that simply stops being written.
            data.pool ??= new PoolState();
            data.pool.got ??= new List<string>();

            // v4 -> v5: the gear system. An older file simply has no gear, which is the same
            // state as a new player who has not pulled any - so there is nothing to convert,
            // only containers to create. Newtonsoft runs the field initializers before
            // overlaying the file, so these are empty rather than null either way; the ??=
            // is for hand-written or imported JSON that set them explicitly to null.
            data.gear ??= new List<OwnedGear>();
            data.excl ??= new Dictionary<string, ExclusiveGear>();
            // uid 0 would collide with "no gear equipped", and a file that somehow carried
            // gear without a counter must not reissue ids that are already in use.
            int highest = 0;
            foreach (var g in data.gear) if (g != null && g.uid > highest) highest = g.uid;
            if (data.gearUid <= highest) data.gearUid = highest + 1;
            if (data.gearUid < 1) data.gearUid = 1;

            // v6 -> v7: 협동 청소(10.18-h). 예전 파일에는 기록이 없으니 비워 둔 채
            // 시작한다 — 최고 웨이브 0 은 "아직 안 해 봤다" 와 같은 뜻이다.
            data.coop ??= new CoopRecord();
            data.coop.best ??= new Dictionary<string, int>();
            if (string.IsNullOrEmpty(data.coop.last)) data.coop.last = "rush";

            // v7 -> v8: 10.22-g 가 한꺼번에 들여온 것들 — 퀘스트 · 미션/업적 · 이벤트 ·
            // 스토리 · 호감도 · 우편 · 먼지 도감 · 심화 모드 · 소환 천장.
            //
            // **옮길 값이 없다.** 전부 "아직 아무것도 안 했다"가 맞는 시작 상태이고,
            // 그릇만 만들어 주면 된다. 억지로 과거를 추정해 채우면(예: best 로 퀘스트를
            // 소급 완료 처리) 보상을 두 번 주거나 건너뛰게 된다 — 원본도 그렇게 하지 않는다.
            //
            // 딱 두 가지만 예외로 손을 댄다.
            data.aff ??= new Dictionary<string, Affinity>();
            data.codex ??= new CodexState();
            data.codex.seen ??= new Dictionary<string, bool>();
            data.codex.kill ??= new Dictionary<string, double>();
            data.codex.got ??= new Dictionary<string, bool>();
            data.hstars ??= new Dictionary<string, int>();
            data.unlSeen ??= new Dictionary<string, bool>();
            data.mail ??= new MailState();
            data.mail.sent ??= new Dictionary<string, bool>();
            data.mail.list ??= new List<MailItem>();
            data.quest ??= new QuestState();
            data.quest.got ??= new Dictionary<string, bool>();
            data.story ??= new StoryState();
            data.story.tab ??= new Dictionary<string, bool>();
            data.story.done ??= new Dictionary<string, bool>();
            data.story.game ??= new Dictionary<string, StoryGame>();
            data.ms ??= new MissionState();
            data.ms.st ??= new Dictionary<string, double>();
            data.ms.d ??= new MissionBucket();
            data.ms.w ??= new MissionBucket();
            data.ms.a ??= new AchieveState();
            data.ms.a.got ??= new Dictionary<string, bool>();
            data.ms.ev ??= new Dictionary<string, MissionBucket>();
            if (fromVersion < 8)
            {
                // ① 이미 연 콘텐츠의 '새 콘텐츠 열림' 안내는 다시 띄우지 않는다. 예전
                //    파일은 그 콘텐츠를 이미 쓰고 있었으므로 지금 알리는 것은 거짓말이다.
                foreach (var id in new[] { "survivor", "coop", "tower", "league" })
                    data.unlSeen[id] = true;
                // ② 리그 배속은 예전에 없던 칸이라 0 으로 읽힌다. 0 배속은 멈춤이므로
                //    1 로 세운다. 다른 수치 칸은 0 이 곧 "아직 없음"이라 그대로 둔다.
                if (data.lgSpeed < 1) data.lgSpeed = 1;
            }
            data.opt ??= new AudioOptions();

            // 원본 `S.plog`(플레이 기록, 10.22-d)는 일부러 옮기지 않았다. 개발용 진단
            // 기록이고 우리는 PlayMode 증거와 Unity 로그로 같은 일을 한다. 세이브에
            // 넣으면 파일만 커진다. 필요해지면 그때 칸을 만든다.

            data.version = SaveData.CurrentVersion;
        }
    }
}
