// In-battle tutorial sequencing.
//
// Tutorial pacing is by battles played, not by stage: each run opens at most one new
// unconditional group, and steps whose moment never arrived roll forward to the next run.
// Boss and map-specific groups are checked ahead of the general ones because their moment
// is the point of them.
using System.Collections.Generic;
using System.Linq;
using DustZero.Core.Meta;
using DustZero.Data;

namespace DustZero.Core.Defense
{
    /// <summary>A step currently on screen, plus what it is waiting for.</summary>
    public sealed class ActiveTutorial
    {
        public TutorialRow Step;
        /// <summary>Resolved unit id for {unit} and for Select/Deploy completion.</summary>
        public string Target;
        /// <summary>Presentation time when it appeared, for the Timer close condition.</summary>
        public float ShownAt;
        /// <summary>How many of Target were already deployed, so Deploy needs a new one.</summary>
        public int DeployedAtShow;
        public string Text;
        /// <summary>UI element to highlight, e.g. "card:vac", "hud:life", "btn:speed".</summary>
        public string Focus;
        /// <summary>
        /// Tile the step insists on, or null when the player may choose. Scripted stages use
        /// this so the taught lesson (block this lane, cool that unit) actually happens.
        /// </summary>
        public (int X, int Y)? ForcedTile;
        /// <summary>
        /// True while only <see cref="Target"/> may be selected. A forced-tile step pauses the
        /// simulation, so without this the player could spend the frozen power on other cards
        /// and then never afford the one the step is waiting for.
        /// </summary>
        public bool LockCard;
        /// <summary>True while the step should hold the simulation still.</summary>
        public bool Pauses;
        /// <summary>True when the step closes on an explicit tap.</summary>
        public bool NeedsTap;
    }

    public sealed class TutorialDirector
    {
        readonly GameDatabase _db;
        readonly SaveData _save;
        readonly BattleSim _sim;
        readonly List<TutorialRow> _planned = new();

        readonly int _tStart, _tNext, _tTime, _tSpawn, _tPower, _tHeat, _tSkill, _tBoss, _tLeak;
        readonly int _uTap, _uSelect, _uDeploy, _uTimer;

        public ActiveTutorial Active { get; private set; }

        /// <summary>The blocker whose heat bar should be ringed, for focus "unit:heat".</summary>
        public BattleUnit HighlightUnit { get; private set; }

        public bool HoldsSimulation => Active != null && Active.Pauses;
        public bool AnyPlanned => _planned.Count > 0;

        public TutorialDirector(GameDatabase db, SaveData save, BattleSim sim)
        {
            _db = db; _save = save; _sim = sim;

            _tStart = db.Enum("TutTrigger", "Start");
            _tNext = db.Enum("TutTrigger", "Next");
            _tTime = db.Enum("TutTrigger", "Time");
            _tSpawn = db.Enum("TutTrigger", "Spawn");
            _tPower = db.Enum("TutTrigger", "Power");
            _tHeat = db.Enum("TutTrigger", "Heat");
            _tSkill = db.Enum("TutTrigger", "Skill");
            _tBoss = db.Enum("TutTrigger", "Boss");
            _tLeak = db.Enum("TutTrigger", "Leak");

            _uTap = db.Enum("TutUntil", "Tap");
            _uSelect = db.Enum("TutUntil", "Select");
            _uDeploy = db.Enum("TutUntil", "Deploy");
            _uTimer = db.Enum("TutUntil", "Timer");

            Plan();
        }

        /// <summary>Maps a unit id, or the roles "melee"/"ranged", onto something in the deck.</summary>
        string Resolve(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value == "melee")
                return _sim.Deck.FirstOrDefault(t => _db.Units[t].Tile == TileKind.Ground);
            if (value == "ranged")
                return _sim.Deck.FirstOrDefault(t =>
                {
                    var u = _db.Units[t];
                    return u.Tile == TileKind.Furniture && u.Atk > 0 && ((int)u.AtkTarget & 2) != 0;
                });
            return _sim.Deck.Contains(value) ? value : null;
        }

        string TargetSpec(TutorialRow st)
        {
            if (st.until == _uSelect || st.until == _uDeploy) return st.untilParam.AsString();
            var focus = st.focus ?? string.Empty;
            return focus.StartsWith("card:") ? focus[5..] : string.Empty;
        }

        /// <summary>
        /// Chooses which steps this battle may show. At most one new group opens per run, and
        /// only once the already-opened group's opening beats have actually been seen - so
        /// quitting out early does not pile several groups onto the next battle.
        /// </summary>
        void Plan()
        {
            _planned.Clear();
            if (_save.tutOff != 0) return;

            var free = _db.Tutorials.Where(r => string.IsNullOrEmpty(r.cond)).ToList();
            var groups = free.Select(r => r.group).Distinct().OrderBy(g => g).ToList();

            bool corePending = free.Any(r =>
                r.group <= _save.tutOpen && !_save.SawTutorial(r.id) &&
                (r.trigger == _tStart || r.trigger == _tNext) &&
                (string.IsNullOrEmpty(TargetSpec(r)) || Resolve(TargetSpec(r)) != null));

            if (!corePending)
            {
                int? next = groups.Cast<int?>()
                    .FirstOrDefault(g => g > _save.tutOpen && free.Any(r => r.group == g && !_save.SawTutorial(r.id)));
                if (next.HasValue) _save.tutOpen = next.Value;
            }

            bool CondOk(string c) => c == "boss"
                ? _sim.Stage.Boss
                : c.StartsWith("map:") && _sim.Stage.MapId == c[4..];

            _planned.AddRange(_db.Tutorials
                .Where(r => !_save.SawTutorial(r.id) &&
                            (string.IsNullOrEmpty(r.cond) ? r.group <= _save.tutOpen : CondOk(r.cond)))
                // Conditional (boss/map) steps first: their moment is specific and fleeting.
                .OrderBy(r => string.IsNullOrEmpty(r.cond) ? 1 : 0));
        }

        bool Fires(TutorialRow step, bool prevDone)
        {
            // afterPrev holds a step back until the previous one is finished, whatever its
            // own trigger is - so a power threshold reached early does not jump the queue.
            if (step.afterPrev != 0 && !prevDone) return false;
            if (step.trigger == _tStart) return true;
            if (step.trigger == _tNext) return prevDone;
            if (step.trigger == _tTime) return _sim.Time >= step.param.AsFloat();
            if (step.trigger == _tSpawn) return _sim.Spawned.Contains(step.param.AsString());
            if (step.trigger == _tPower) return _sim.Power >= step.param.AsFloat();
            if (step.trigger == _tHeat)
            {
                float ratio = step.param.AsFloat();
                return _sim.Units.Any(u => u.IsBlocker && u.Heat >= u.MaxHeat * ratio);
            }
            if (step.trigger == _tSkill)
            {
                var p = step.param.AsString();
                return _sim.SkillsUsed.Contains(string.IsNullOrEmpty(p) ? string.Empty : p);
            }
            if (step.trigger == _tBoss)
                return _sim.Spawned.Any(t => _db.Enemies.TryGetValue(t, out var e) && e.Boss);
            if (step.trigger == _tLeak) return _sim.Leaked > 0;
            return false;
        }

        /// <summary>
        /// A step is showable only if its subject exists. Select/Deploy steps additionally wait
        /// until the unit is actually affordable, so the player is never told to place something
        /// they cannot pay for.
        /// </summary>
        bool CardReady(TutorialRow step)
        {
            var spec = TargetSpec(step);
            if (string.IsNullOrEmpty(spec)) return true;
            var t = Resolve(spec);
            if (t == null) return false;
            if (step.until == _uSelect || step.until == _uDeploy) return _sim.Affordable(t);
            return true;
        }

        /// <summary>Advances the tutorial. <paramref name="presentationTime"/> is view time, not sim time.</summary>
        public void Tick(float presentationTime)
        {
            if (_save.tutOff != 0) { Active = null; HighlightUnit = null; return; }

            if (Active != null)
            {
                var st = Active.Step;

                if (st.focus == "unit:heat")
                {
                    BattleUnit best = null;
                    foreach (var u in _sim.Units)
                        if (u.IsBlocker && (best == null || u.HeatRatio > best.HeatRatio)) best = u;
                    HighlightUnit = best;
                }

                if (st.until == _uSelect && SelectedCard == Active.Target) Complete();
                else if (st.until == _uDeploy &&
                         _sim.Units.Count(u => u.Type == Active.Target) > Active.DeployedAtShow) Complete();
                else if (st.until == _uTimer &&
                         presentationTime - Active.ShownAt >= st.untilParam.AsFloat()) Complete();
                return;
            }

            foreach (var st in _planned)
            {
                if (_save.SawTutorial(st.id)) continue;

                var prev = _db.Tutorials.FirstOrDefault(r => r.group == st.group && r.seq == st.seq - 1);
                // A previous step whose subject is not in this deck was skipped, not pending -
                // otherwise every afterPrev step behind it would wait forever.
                bool prevSkipped = prev != null && !string.IsNullOrEmpty(TargetSpec(prev)) &&
                                   Resolve(TargetSpec(prev)) == null;
                bool prevDone = prev == null || _save.SawTutorial(prev.id) || prevSkipped;

                if (st.trigger == _tNext && !prevDone) continue;
                if (!Fires(st, prevDone)) continue;
                if (!CardReady(st)) continue;

                var target = Resolve(TargetSpec(st));

                // "Place a vacuum" is pointless if one is already down and this step was not
                // the scripted opening beat; mark it seen and move on.
                if (st.until == _uDeploy && st.trigger != _tStart && st.trigger != _tNext &&
                    target != null && _sim.Units.Any(u => u.Type == target))
                {
                    _save.MarkTutorial(st.id);
                    SaveDirty?.Invoke();
                    continue;
                }

                Show(st, target, presentationTime);
                return;
            }
        }

        /// <summary>
        /// Parses the "x,y" tile cell, but only for the stage it was written for, only for a
        /// step that is waiting on a deploy, and only while that tile can actually take the
        /// unit. A cell that is already occupied stops being forced rather than wedging the run.
        /// </summary>
        (int, int)? ForcedTileOf(TutorialRow st, string target)
        {
            if (string.IsNullOrWhiteSpace(st.tile) || target == null) return null;
            if (st.until != _uDeploy) return null;
            if (st.tileStage != 0 && st.tileStage != _sim.Stage.Id) return null;
            var parts = st.tile.Split(',');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0].Trim(), out var x)) return null;
            if (!int.TryParse(parts[1].Trim(), out var y)) return null;
            return _sim.CanPlace(target, x, y) ? ((int, int)?)(x, y) : null;
        }

        void Show(TutorialRow st, string target, float presentationTime)
        {
            var forced = ForcedTileOf(st, target);
            Active = new ActiveTutorial
            {
                ForcedTile = forced,
                LockCard = forced.HasValue,
                Step = st,
                Target = target,
                ShownAt = presentationTime,
                DeployedAtShow = _sim.Units.Count(u => u.Type == target),
                Text = _db.Text(st.textKey).Replace("{unit}", target != null ? _db.Units[target].Name : string.Empty),
                Focus = st.focus,
                Pauses = st.pause != 0,
                NeedsTap = st.until == _uTap,
            };
        }

        /// <summary>Marks the current step seen and clears it.</summary>
        public void Complete(bool disableAll = false)
        {
            if (Active == null) return;
            _save.MarkTutorial(Active.Step.id);
            if (disableAll) { _save.tutOff = 1; _planned.Clear(); }
            Active = null;
            HighlightUnit = null;
            SaveDirty?.Invoke();
        }

        /// <summary>Hides the current step without marking it seen (used when leaving a battle).</summary>
        public void Hide() { Active = null; HighlightUnit = null; }

        /// <summary>Set by the battle presenter so Select steps can see the chosen card.</summary>
        public string SelectedCard { get; set; }

        /// <summary>Raised when tutorial progress changed and the profile should be written.</summary>
        public event System.Action SaveDirty;
    }
}
