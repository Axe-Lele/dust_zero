// The board: tiles, routes, BFS distance fields and placement legality.
//
// Ground enemies navigate by the same two distance fields the original used - one over the
// bare map (DistAll) and one that also treats occupied melee tiles as walls (DistFree).
// Comparing the two is what produces "walk around the blocker if a way exists, otherwise
// fight it", so no NavMesh or physics is involved anywhere.
using System;
using System.Collections.Generic;
using DustZero.Data;

namespace DustZero.Core.Defense
{
    public sealed class BattleField
    {
        public const char Wall = 'X';
        public const char Furniture = 'H';
        public const char Floor = 'P';
        public const char Stove = 'F';
        public const char Wet = 'W';
        public const char Goal = 'E';

        public StageDef Stage { get; }
        public int Cols { get; }
        public int Rows { get; }
        public int GoalX { get; }
        public int GoalY { get; }

        /// <summary>Steps to the goal ignoring units. Infinity where unreachable.</summary>
        public float[,] DistAll { get; private set; }
        /// <summary>Steps to the goal treating active melee units as walls.</summary>
        public float[,] DistFree { get; private set; }

        static readonly (int dc, int dr)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        /// <summary>Probe order used when picking the next tile; matches the original's [[0,1],[1,0],[-1,0],[0,-1]].</summary>
        static readonly (int dc, int dr)[] StepOrder = { (0, 1), (1, 0), (-1, 0), (0, -1) };

        readonly Queue<(int c, int r)> _bfsQueue = new();

        public BattleField(StageDef stage)
        {
            Stage = stage;
            Cols = stage.Cols;
            Rows = stage.Rows;
            GoalX = stage.GoalX;
            GoalY = stage.GoalY;
            DistAll = Bfs(null);
            DistFree = DistAll;
        }

        public char TileAt(int c, int r) => Stage.Tiles[r][c];

        public bool InBounds(int c, int r) => r >= 0 && r < Rows && c >= 0 && c < Cols;

        /// <summary>Tiles an enemy may stand on: floor, stove, wet and the goal.</summary>
        public bool Walkable(int c, int r)
            => InBounds(c, r) && "PFWE".IndexOf(Stage.Tiles[r][c]) >= 0;

        /// <summary>Tiles a melee unit may occupy, before entrance/goal exclusion zones.</summary>
        public static bool IsGroundTile(char ch) => ch == Floor || ch == Stove || ch == Wet;

        /// <summary>Breadth-first flood from the goal. <paramref name="blocked"/> tiles are impassable.</summary>
        public float[,] Bfs(HashSet<int> blocked)
        {
            var d = new float[Rows, Cols];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    d[r, c] = float.PositiveInfinity;

            _bfsQueue.Clear();
            d[GoalY, GoalX] = 0f;
            _bfsQueue.Enqueue((GoalX, GoalY));

            while (_bfsQueue.Count > 0)
            {
                var (c, r) = _bfsQueue.Dequeue();
                foreach (var (dc, dr) in Neighbours)
                {
                    int nc = c + dc, nr = r + dr;
                    if (!Walkable(nc, nr)) continue;
                    if (!float.IsPositiveInfinity(d[nr, nc])) continue;
                    if (blocked != null && blocked.Contains(Key(nc, nr))) continue;
                    d[nr, nc] = d[r, c] + 1f;
                    _bfsQueue.Enqueue((nc, nr));
                }
            }
            return d;
        }

        public static int Key(int c, int r) => (r << 8) | c;

        /// <summary>Recomputes DistFree against the tiles currently held by active melee units.</summary>
        public void RefreshFree(IReadOnlyList<BattleUnit> units)
        {
            var occ = new HashSet<int>();
            foreach (var u in units)
                if (u.IsBlocker && !u.Overheated)
                    occ.Add(Key(u.Col, u.Row));
            DistFree = Bfs(occ);
        }

        public static readonly (int dc, int dr)[] Probe = StepOrder;

        /// <summary>World position at distance <paramref name="d"/> along a flight path, tile-centred.</summary>
        public Vec2 PositionAt(float d, int pathIndex)
        {
            var p = Stage.Paths[pathIndex];
            d = Mathf2.Clamp(d, 0f, p.Length);
            foreach (var s in p.Segments)
            {
                if (d <= s.Start + s.Length)
                {
                    float f = s.Length <= 0f ? 0f : (d - s.Start) / s.Length;
                    return new Vec2(s.X1 + (s.X2 - s.X1) * f + 0.5f, s.Y1 + (s.Y2 - s.Y1) * f + 0.5f);
                }
            }
            var last = p.Segments[^1];
            return new Vec2(last.X2 + 0.5f, last.Y2 + 0.5f);
        }

        /// <summary>Facing of the path leg containing <paramref name="d"/>; used to offset flyers sideways.</summary>
        public Vec2 SegmentDirection(float d, int pathIndex)
        {
            var p = Stage.Paths[pathIndex];
            foreach (var s in p.Segments)
                if (d <= s.Start + s.Length)
                    return new Vec2(Math.Sign(s.X2 - s.X1), Math.Sign(s.Y2 - s.Y1));
            return new Vec2(0, 1);
        }

        /// <summary>Distance along a path at which it passes over (c,r), or null if it never does.</summary>
        public float? TileDistance(int c, int r, int pathIndex)
        {
            foreach (var s in Stage.Paths[pathIndex].Segments)
            {
                if (c >= Math.Min(s.X1, s.X2) && c <= Math.Max(s.X1, s.X2) &&
                    r >= Math.Min(s.Y1, s.Y2) && r <= Math.Max(s.Y1, s.Y2))
                    return s.Start + Math.Abs(c - s.X1) + Math.Abs(r - s.Y1);
            }
            return null;
        }

        /// <summary>
        /// True when (c,r) falls inside an entrance or goal exclusion zone. Melee units may not
        /// be placed there, so players cannot wall off a spawn point or camp the objective.
        /// </summary>
        public bool NoDeploy(int c, int r)
        {
            for (int pi = 0; pi < Stage.Paths.Length; pi++)
            {
                var d = TileDistance(c, r, pi);
                if (d == null) continue;
                if (d.Value < Stage.NoDeployFromEntrance) return true;
                if (d.Value > Stage.Paths[pi].Length - 1 - Stage.NoDeployBeforeGoal) return true;
            }
            return false;
        }

        /// <summary>
        /// Placement legality for a unit type, independent of cost and cooldown:
        /// melee on free ground outside the exclusion zones, ranged on furniture, never stacked.
        /// </summary>
        public bool CanPlace(UnitDef def, int c, int r, IReadOnlyList<BattleUnit> units)
        {
            if (!InBounds(c, r)) return false;
            char ch = TileAt(c, r);
            bool tileOk = def.Tile == TileKind.Ground
                ? IsGroundTile(ch) && !NoDeploy(c, r)
                : ch == Furniture;
            if (!tileOk) return false;
            foreach (var u in units)
                if (u.Col == c && u.Row == r) return false;
            return true;
        }
    }
}
