// The lobby room: owned appliances wander about and clean up dust.
//
// A port of the original's roomInit/roomStep. It is a simulation rather than an animation
// because that is what the original is - each appliance picks the nearest unclaimed speck,
// walks to it and works on it - and keeping it as plain C# means the lobby view only has to
// draw positions, exactly like the battle board.
//
// Coordinates are an abstract unit box: x and y run across the floor rectangle the view
// gives it, so the view can resize without the simulation knowing about pixels.
using System;
using System.Collections.Generic;

namespace DustZero.Core.Lobby
{
    /// <summary>What an appliance is doing right now.</summary>
    public enum RoomState { Idle, Walk, Clean }

    public sealed class RoomAgent
    {
        public string Type;
        public float X, Y;
        public float TargetX, TargetY;
        public RoomState State = RoomState.Idle;
        public float Wait;
        /// <summary>-1 facing left, 1 facing right.</summary>
        public float Face = 1f;
        public float Speed;
        /// <summary>Counts down a small hop, used when the appliance speaks.</summary>
        public float Hop;
        /// <summary>Counts down the lean-in while scrubbing.</summary>
        public float Kick;
        public RoomDust Dust;
    }

    public sealed class RoomDust
    {
        public float X, Y;
        /// <summary>1 when fresh, 0 when cleaned away.</summary>
        public float Amount = 1f;
        public float Radius;
        public float Seed;
        /// <summary>The agent that claimed it, so two do not scrub the same speck.</summary>
        public RoomAgent ClaimedBy;
    }

    /// <summary>A line of chatter currently on screen.</summary>
    public readonly struct RoomSpeech
    {
        public readonly RoomAgent Agent;
        public readonly string Text;
        public RoomSpeech(RoomAgent agent, string text) { Agent = agent; Text = text; }
    }

    public sealed class RoomSim
    {
        readonly IRandom _rng;
        readonly List<RoomAgent> _agents = new();
        readonly List<RoomDust> _dust = new();

        float _spawnTimer;
        float _chatTimer = 6f;

        /// <summary>Floor rectangle in simulation units; the view maps it to the screen.</summary>
        public float Left = 0.08f, Right = 0.92f, Top = 0.1f, Bottom = 0.9f;

        public IReadOnlyList<RoomAgent> Agents => _agents;
        public IReadOnlyList<RoomDust> Dust => _dust;

        /// <summary>Total specks cleaned since the room opened. Shown nowhere; useful in tests.</summary>
        public int Cleaned { get; private set; }

        /// <summary>The current speech bubble, or null.</summary>
        public RoomSpeech? Speech { get; private set; }
        float _speechTime;

        /// <summary>
        /// Supplies a line of chatter for an appliance type. A plain delegate rather than an
        /// event: there is exactly one provider, and the simulation needs its return value.
        /// </summary>
        public Func<string, string> LineFor { get; set; }

        /// <summary>True when <paramref name="d"/> is still on the floor.</summary>
        public bool Holds(RoomDust d) => d != null && _dust.Contains(d);

        /// <summary>True when <paramref name="a"/> is still part of the cast.</summary>
        public bool Holds(RoomAgent a) => a != null && _agents.Contains(a);

        public RoomSim(IRandom rng) { _rng = rng; }

        float Range(float lo, float hi) => lo + _rng.Value * (hi - lo);

        /// <summary>
        /// Rebuilds the cast for the appliances the player owns. Agents already on the floor
        /// keep their position, so refreshing the lobby does not teleport everyone.
        /// </summary>
        public void SetCast(IReadOnlyList<string> types, Func<string, float> speedFor)
        {
            var kept = new List<RoomAgent>();
            foreach (var t in types)
            {
                var existing = _agents.Find(a => a.Type == t);
                if (existing != null) { kept.Add(existing); continue; }
                kept.Add(new RoomAgent
                {
                    Type = t,
                    X = Range(Left + 0.06f, Right - 0.06f),
                    Y = Range(Top, Bottom),
                    Wait = Range(0f, 1.5f),
                    Face = _rng.Value < 0.5f ? -1f : 1f,
                    Speed = speedFor != null ? speedFor(t) : 0.18f,
                });
            }
            _agents.Clear();
            _agents.AddRange(kept);

            // Drop claims held by agents that are no longer here.
            foreach (var d in _dust) if (d.ClaimedBy != null && !_agents.Contains(d.ClaimedBy)) d.ClaimedBy = null;

            int want = 4 + _agents.Count;
            while (_dust.Count < want) SpawnDust();
        }

        void SpawnDust(float? x = null, float? y = null)
        {
            if (_dust.Count >= 6 + _agents.Count * 2) return;
            _dust.Add(new RoomDust
            {
                X = x ?? Range(Left + 0.03f, Right - 0.03f),
                Y = y ?? Range(Top, Bottom),
                Radius = Range(0.012f, 0.024f),
                Seed = Range(0f, 10f),
            });
        }

        /// <summary>Makes an appliance speak, as the original does when a reward lands.</summary>
        public void Say(string text, string type = null)
        {
            if (_agents.Count == 0 || string.IsNullOrEmpty(text)) return;
            var agent = (type != null ? _agents.Find(a => a.Type == type) : null) ?? _agents[0];
            Speech = new RoomSpeech(agent, text);
            _speechTime = 3.2f;
            agent.Hop = 0.4f;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f) { _spawnTimer = Range(1.2f, 2.8f); SpawnDust(); }

            foreach (var a in _agents) StepAgent(a, dt);

            if (_speechTime > 0f)
            {
                _speechTime -= dt;
                if (_speechTime <= 0f) Speech = null;
            }
            else
            {
                _chatTimer -= dt;
                if (_chatTimer <= 0f)
                {
                    _chatTimer = Range(10f, 20f);
                    var a = _agents.Count > 0 ? _agents[(int)(_rng.Value * _agents.Count) % _agents.Count] : null;
                    var line = a != null ? LineFor?.Invoke(a.Type) : null;
                    if (line != null) Say(line, a.Type);
                }
            }
        }

        void StepAgent(RoomAgent a, float dt)
        {
            a.Hop = Math.Max(0f, a.Hop - dt);
            a.Kick = Math.Max(0f, a.Kick - dt);

            switch (a.State)
            {
                case RoomState.Clean:
                    {
                        var d = a.Dust;
                        a.Wait -= dt;
                        if (d != null)
                        {
                            d.Amount = Math.Max(0f, d.Amount - dt / 1.3f);
                            if (_rng.Value < dt * 3f) a.Kick = 0.15f;
                        }
                        if (d == null || d.Amount <= 0f)
                        {
                            if (d != null) { _dust.Remove(d); Cleaned++; }
                            a.Dust = null;
                            a.State = RoomState.Idle;
                            a.Wait = Range(0.4f, 1.6f);
                        }
                        return;
                    }

                case RoomState.Idle:
                    {
                        a.Wait -= dt;
                        if (a.Wait > 0f) return;

                        // Nearest speck nobody has claimed; otherwise just amble somewhere.
                        RoomDust best = null;
                        float bestDist = float.MaxValue;
                        foreach (var d in _dust)
                        {
                            if (d.ClaimedBy != null) continue;
                            float dd = Dist(d.X - a.X, d.Y - a.Y);
                            if (dd < bestDist) { bestDist = dd; best = d; }
                        }

                        if (best != null && _rng.Value < 0.85f)
                        {
                            best.ClaimedBy = a;
                            a.Dust = best;
                            a.TargetX = best.X - (best.X < a.X ? -1f : 1f) * 0.05f;
                            a.TargetY = best.Y;
                        }
                        else
                        {
                            a.Dust = null;
                            a.TargetX = Range(Left + 0.05f, Right - 0.05f);
                            a.TargetY = Range(Top, Bottom);
                        }
                        a.State = RoomState.Walk;
                        return;
                    }

                case RoomState.Walk:
                    {
                        float dx = a.TargetX - a.X, dy = a.TargetY - a.Y;
                        float dist = Dist(dx, dy);
                        float step = a.Speed * dt;
                        if (Math.Abs(dx) > 0.004f) a.Face = Math.Sign(dx);

                        if (dist <= step || dist <= 0.0001f)
                        {
                            a.X = a.TargetX;
                            a.Y = a.TargetY;
                            if (a.Dust != null && _dust.Contains(a.Dust))
                            {
                                a.State = RoomState.Clean;
                                a.Wait = 1.3f;
                                float face = Math.Sign(a.Dust.X - a.X);
                                if (face != 0f) a.Face = face;
                            }
                            else
                            {
                                a.Dust = null;
                                a.State = RoomState.Idle;
                                a.Wait = Range(0.8f, 3.0f);
                            }
                        }
                        else
                        {
                            a.X += dx / dist * step;
                            a.Y += dy / dist * step;
                        }
                        return;
                    }
            }
        }

        static float Dist(float dx, float dy) => (float)Math.Sqrt(dx * dx + dy * dy);
    }
}
