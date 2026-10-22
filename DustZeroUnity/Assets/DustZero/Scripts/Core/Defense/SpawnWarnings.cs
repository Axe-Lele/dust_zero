using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Defense
{
    /// <summary>One enemy kind inside an entrance warning: which enemy, and how many of it.</summary>
    public readonly struct SpawnWarningKind
    {
        public readonly string EnemyId;
        public readonly int Count;
        public readonly bool Boss;
        public readonly bool Fly;

        public SpawnWarningKind(string enemyId, int count, bool boss, bool fly)
        {
            EnemyId = enemyId; Count = count; Boss = boss; Fly = fly;
        }

        public bool IsEmpty => string.IsNullOrEmpty(EnemyId);
    }

    /// <summary>
    /// What the board draws over one entrance in 10.17-e's regen warning: the enemies due out
    /// of it within <c>config.spawnWarnSec</c>, and how long until the first of them.
    /// </summary>
    public readonly struct SpawnWarning
    {
        public readonly int PathIndex;
        /// <summary>Seconds until the next spawn. The view rounds it up for the badge.</summary>
        public readonly float Seconds;
        public readonly SpawnWarningKind First;
        public readonly SpawnWarningKind Second;
        /// <summary>True when more kinds are due than the two the bubble has room for.</summary>
        public readonly bool HasMore;

        public SpawnWarning(int pathIndex, float seconds, SpawnWarningKind first,
                            SpawnWarningKind second, bool hasMore)
        {
            PathIndex = pathIndex; Seconds = seconds;
            First = first; Second = second; HasMore = hasMore;
        }

        public int KindCount => Second.IsEmpty ? 1 : 2;

        /// <summary>A boss among the shown kinds paints the bubble gold.</summary>
        public bool Boss => First.Boss || Second.Boss;

        /// <summary>Otherwise a flyer paints it blue; plain ground stays red.</summary>
        public bool Air => !Boss && (First.Fly || Second.Fly);
    }

    /// <summary>
    /// Builds <see cref="SpawnWarning"/>s from a running battle's schedule.
    ///
    /// Kept apart from the view, and from <see cref="BattleSim"/>, for two reasons: the
    /// grouping rules are worth testing on their own, and the board asks for this every frame
    /// so it must not allocate. One instance is reused; the scratch lists are cleared per call.
    /// </summary>
    public sealed class SpawnWarningBoard
    {
        readonly List<string> _ids = new();
        readonly List<int> _counts = new();
        readonly List<EnemyDef> _defs = new();

        /// <summary>
        /// The warning for one entrance, or false when nothing is due there inside
        /// <paramref name="window"/> seconds.
        ///
        /// Mirrors the original: the next spawn for this entrance decides whether the warning
        /// shows at all, and everything scheduled within <paramref name="window"/> seconds
        /// <em>of that spawn</em> is grouped into it - so a trickle that starts inside the
        /// window keeps counting past it rather than being cut off mid-wave.
        /// </summary>
        public bool TryBuild(GameDatabase db, BattleSim sim, int pathIndex, float window,
                             out SpawnWarning warning)
        {
            warning = default;
            if (db == null || sim == null) return false;

            var schedule = sim.Stage.Schedule;
            int cursor = sim.ScheduleCursor;

            float firstTime = 0f;
            bool found = false;
            for (int i = cursor; i < schedule.Length; i++)
            {
                if (schedule[i].PathIndex != pathIndex) continue;
                firstTime = schedule[i].Time;
                found = true;
                break;
            }
            if (!found || firstTime - sim.Time > window) return false;

            _ids.Clear(); _counts.Clear(); _defs.Clear();
            for (int i = cursor; i < schedule.Length; i++)
            {
                // The schedule is in time order, so the first entry past the window ends it
                // for every entrance - this is why the loop is not filtered first.
                if (schedule[i].Time - firstTime > window) break;
                if (schedule[i].PathIndex != pathIndex) continue;

                string id = schedule[i].EnemyId;
                int at = _ids.IndexOf(id);
                if (at >= 0) { _counts[at]++; continue; }
                _ids.Add(id);
                _counts.Add(1);
                _defs.Add(db.Enemy(id));
            }
            if (_ids.Count == 0) return false;

            SortBossThenFlyFirst();

            var first = KindAt(0);
            var second = _ids.Count > 1 ? KindAt(1) : default;
            warning = new SpawnWarning(pathIndex, Math.Max(0f, firstTime - sim.Time),
                                       first, second, _ids.Count > 2);
            return true;
        }

        SpawnWarningKind KindAt(int i)
        {
            var def = _defs[i];
            return new SpawnWarningKind(_ids[i], _counts[i],
                                        def != null && def.Boss, def != null && def.Fly);
        }

        /// <summary>
        /// Bosses first, then flyers, then ground - the order the original sorts by, so the
        /// bubble's colour comes from the most alarming kind. Insertion sort keeps it stable,
        /// which matters: equal kinds must stay in schedule order.
        /// </summary>
        void SortBossThenFlyFirst()
        {
            for (int i = 1; i < _ids.Count; i++)
            {
                for (int j = i; j > 0 && Rank(j) < Rank(j - 1); j--)
                {
                    (_ids[j], _ids[j - 1]) = (_ids[j - 1], _ids[j]);
                    (_counts[j], _counts[j - 1]) = (_counts[j - 1], _counts[j]);
                    (_defs[j], _defs[j - 1]) = (_defs[j - 1], _defs[j]);
                }
            }
        }

        int Rank(int i)
        {
            var d = _defs[i];
            if (d == null) return 2;
            if (d.Boss) return 0;
            return d.Fly ? 1 : 2;
        }
    }
}
