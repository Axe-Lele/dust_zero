// Engine-free primitives.
//
// DustZero.Core deliberately has noEngineReferences, so the rules can be unit tested without
// a running player. These stand in for the handful of UnityEngine types the simulation needs.
using System;

namespace DustZero.Core
{
    public readonly struct Vec2
    {
        public readonly float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);

        public float Length => MathF.Sqrt(X * X + Y * Y);
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    public static class Mathf2
    {
        public static float Hypot(float x, float y) => MathF.Sqrt(x * x + y * y);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Sign(float v) => v > 0 ? 1 : v < 0 ? -1 : 0;
    }

    /// <summary>
    /// Injectable randomness. Gameplay never touches System.Random directly so tests can pin
    /// outcomes (gacha pity, reward rolls, spawn jitter) to a seed.
    /// </summary>
    public interface IRandom
    {
        /// <summary>Uniform in [0,1).</summary>
        float Value { get; }
    }

    public sealed class SystemRandom : IRandom
    {
        readonly Random _r;
        public SystemRandom() : this(Environment.TickCount) { }
        public SystemRandom(int seed) { _r = new Random(seed); }
        public float Value => (float)_r.NextDouble();
    }

    /// <summary>Always returns the same value; handy for deterministic tests.</summary>
    public sealed class FixedRandom : IRandom
    {
        readonly float _v;
        public FixedRandom(float v) { _v = v; }
        public float Value => _v;
    }
}
