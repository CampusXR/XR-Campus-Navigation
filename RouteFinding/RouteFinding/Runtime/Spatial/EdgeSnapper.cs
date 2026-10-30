using System;
using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    /// <summary>Where a free position lands on the walking network.</summary>
    public struct SnapResult
    {
        public bool Found;
        public int Edge;
        /// <summary>0 at the edge's From node, 1 at its To node.</summary>
        public float T;
        /// <summary>Closest point on the edge (map space, at path height).</summary>
        public Vector3 Point;
        /// <summary>Horizontal (XZ) distance from the query position to <see cref="Point"/>.</summary>
        public float Distance;
    }

    /// <summary>
    /// Finds the nearest walkable edge to a position on a given floor. Uses a uniform grid per floor
    /// so a query only looks at nearby edges. Distances are horizontal (XZ) because AR/GPS height is
    /// unreliable and the camera is at eye level, not on the floor.
    /// Elevator edges are never snapped to; stairs/escalators only match on their end floors.
    /// </summary>
    public sealed class EdgeSnapper
    {
        readonly NavGraph graph;
        readonly float cell;
        readonly Dictionary<(int floor, int cx, int cz), List<int>> grid = new Dictionary<(int, int, int), List<int>>();

        public EdgeSnapper(NavGraph graph, float cellSize = 10f)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            cell = cellSize > 0.5f ? cellSize : 10f;
            foreach (var e in graph.Edges)
            {
                if (e.Type == EdgeType.Elevator) continue;
                var a = graph.Positions[e.From];
                var b = graph.Positions[e.To];
                int fa = graph.Nodes[e.From].Floor, fb = graph.Nodes[e.To].Floor;
                Insert(fa, a, b, e.Index);
                if (fb != fa) Insert(fb, a, b, e.Index);
            }
        }

        void Insert(int floor, Vector3 a, Vector3 b, int edge)
        {
            int x0 = C(Math.Min(a.X, b.X)), x1 = C(Math.Max(a.X, b.X));
            int z0 = C(Math.Min(a.Z, b.Z)), z1 = C(Math.Max(a.Z, b.Z));
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    var key = (floor, cx, cz);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(edge);
                }
        }

        int C(float v) => (int)Math.Floor(v / cell);

        /// <param name="allow">Optional filter, e.g. profile.Allows, so step-free users never snap onto stairs.</param>
        public SnapResult Snap(Vector3 position, int floor, float maxDistance, Func<NavEdge, bool> allow = null)
        {
            var best = new SnapResult { Found = false, Edge = -1, Distance = float.PositiveInfinity };
            if (maxDistance <= 0 || float.IsNaN(position.X) || float.IsNaN(position.Z)) return best;

            int x0 = C(position.X - maxDistance), x1 = C(position.X + maxDistance);
            int z0 = C(position.Z - maxDistance), z1 = C(position.Z + maxDistance);
            HashSet<int> seen = null;
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!grid.TryGetValue((floor, cx, cz), out var list)) continue;
                    foreach (int ei in list)
                    {
                        if (seen == null) seen = new HashSet<int>();
                        if (!seen.Add(ei)) continue;
                        var e = graph.Edges[ei];
                        if (allow != null && !allow(e)) continue;
                        var a = graph.Positions[e.From];
                        var b = graph.Positions[e.To];
                        float t = ProjectXZ(position, a, b);
                        var p = Vector3.Lerp(a, b, t);
                        float d = DistXZ(position, p);
                        // tie-break towards edges whose height is closer (overlapping stairs / landings)
                        if (d < best.Distance - 1e-4f || (Math.Abs(d - best.Distance) <= 1e-4f && Math.Abs(p.Y - position.Y) < Math.Abs(best.Point.Y - position.Y)))
                            best = new SnapResult { Found = true, Edge = ei, T = t, Point = p, Distance = d };
                    }
                }
            if (best.Distance > maxDistance) return new SnapResult { Found = false, Edge = -1, Distance = best.Distance };
            return best;
        }

        /// <summary>Parameter t in [0,1] of the point on segment ab closest to p, measured in XZ.</summary>
        public static float ProjectXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float len2 = dx * dx + dz * dz;
            if (len2 < 1e-8f) return 0f;
            float t = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2;
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        public static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
