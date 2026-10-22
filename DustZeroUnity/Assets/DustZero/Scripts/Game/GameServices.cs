// Single place where the data layer, the profile and persistence are wired together.
//
// The original kept all of this in module-scope variables inside one IIFE. Here it is one
// object that the screens read; nothing else constructs a GameDatabase or a SaveService.
using System;
using System.Collections.Generic;
using System.IO;
using DustZero.Core;
using DustZero.Core.Meta;
using DustZero.Data;
using UnityEngine;

namespace DustZero.Game
{
    /// <summary>Reads the shipped tables out of StreamingAssets.</summary>
    public sealed class StreamingAssetsTableSource : ITableSource
    {
        readonly string _dir;
        public StreamingAssetsTableSource(string dir) { _dir = dir; }

        public string Read(string tableName)
        {
            var path = Path.Combine(_dir, tableName + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    public sealed class GameServices
    {
        public static GameServices Instance { get; private set; }

        public GameDatabase Db { get; private set; }
        public PlayerProfile Profile { get; private set; }
        public SaveService Saves { get; private set; }
        public IRandom Rng { get; private set; }
        public GuideDirector Guide { get; private set; }

        /// <summary>How the profile was obtained; the boot screen surfaces recovery to the player.</summary>
        public LoadStatus SaveStatus { get; private set; }
        public string SaveDetail { get; private set; }

        /// <summary>Problems that stopped the game from starting, in a form worth showing on screen.</summary>
        public IReadOnlyList<string> FatalProblems { get; private set; }
        public bool Ready => Db != null && Profile != null;

        public static string DataDirectory => Path.Combine(Application.streamingAssetsPath, "data");

        /// <summary>
        /// Loads tables and the profile. On a table problem this returns a non-ready instance
        /// carrying the messages rather than throwing, so the boot screen can print them.
        /// </summary>
        public static GameServices Boot()
        {
            var s = new GameServices { Rng = new SystemRandom() };
            try
            {
                s.Db = TableLoader.LoadAndBuild(new StreamingAssetsTableSource(DataDirectory));
            }
            catch (TableValidationException e)
            {
                s.FatalProblems = e.Problems;
                Debug.LogError(e.Message);
                Instance = s;
                return s;
            }
            catch (Exception e)
            {
                s.FatalProblems = new[] { e.Message };
                Debug.LogException(e);
                Instance = s;
                return s;
            }

            s.Saves = new SaveService(Application.persistentDataPath, s.Db, s.Rng);
            var load = s.Saves.Load();
            s.SaveStatus = load.Status;
            s.SaveDetail = load.Detail;

            s.Profile = new PlayerProfile(s.Db, load.Data, s.Rng);
            // Writing on every change keeps the file current without a save button anywhere.
            s.Profile.Changed += s.Persist;
            s.Guide = new GuideDirector(s.Db, s.Profile.Data);
            s.Guide.Changed += s.Persist;

            Instance = s;
            return s;
        }

        bool _writing;

        public void Persist()
        {
            // Grant() can mutate the profile while a write is already in flight; ignore re-entry.
            if (_writing || Profile == null) return;
            try
            {
                _writing = true;
                Saves.Write(Profile.Data);
            }
            catch (Exception e)
            {
                Debug.LogError($"저장 실패: {e.Message}");
            }
            finally { _writing = false; }
        }

        /// <summary>Wipes progress and starts a fresh profile, as the lobby's reset button does.</summary>
        public void ResetProfile()
        {
            var fresh = Saves.NewProfile();
            Profile = new PlayerProfile(Db, fresh, Rng);
            Profile.Changed += Persist;
            Guide = new GuideDirector(Db, Profile.Data);
            Guide.Changed += Persist;
            Persist();
        }

        public void ReplaceProfile(SaveData data)
        {
            Profile = new PlayerProfile(Db, data, Rng);
            Profile.Changed += Persist;
            Guide = new GuideDirector(Db, Profile.Data);
            Guide.Changed += Persist;
            Persist();
        }
    }
}
