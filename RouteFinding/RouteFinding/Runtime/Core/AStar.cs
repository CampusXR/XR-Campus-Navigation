using System;
using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    /// <summary>
    /// A* with a Euclidean heuristic scaled by <see cref="CostGraph.HeuristicScale"/>.
    /// Supports several sources and several targets (each with an entry/exit cost), so a route can
    /// start or end part-way along an edge. With HeuristicScale = 0 it is plain Dijkstra.
    /// </summary>
    public static class AStar
    {
        public static PathResult Search(CostGraph g, Vector3[] positions, int start, int goal)
        {
            return Search(g, positions, new[] { new Seed(start, 0) }, new[] { new Seed(goal, 0) });
        }

        public static PathResult Search(CostGraph g, Vector3[] positions, IReadOnlyList<Seed> sources, IReadOnlyList<Seed> targets)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            SearchUtil.CheckSeeds(g, sources, nameof(sources));
            SearchUtil.CheckSeeds(g, targets, nameof(targets));
            float k = g.HeuristicScale;
            if (positions == null || positions.Length != g.NodeCount) k = 0; // no positions: fall back to Dijkstra

            int n = g.NodeCount;
            var dist = new float[n];
            var prev = new int[n];
            var prevEdge = new int[n];
            var closed = new bool[n];
            var exitCost = new float[n];
            Array.Fill(dist, float.PositiveInfinity);
            Array.Fill(prev, -1);
            Array.Fill(prevEdge, -1);
            Array.Fill(exitCost, float.PositiveInfinity);

            var targetPos = new List<Vector3>();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t.Cost < exitCost[t.Node]) exitCost[t.Node] = t.Cost;
                if (k > 0) targetPos.Add(positions[t.Node]);
            }

            float H(int node)
            {
                if (k <= 0) return 0;
                var p = positions[node];
                float m = float.PositiveInfinity;
                for (int i = 0; i < targetPos.Count; i++)
                {
                    float d = Vector3.Distance(p, targetPos[i]);
                    if (d < m) m = d;
                }
                return m * k;
            }

            var open = new MinHeap();
            float best = float.PositiveInfinity;
            int bestNode = -1;

            void Reach(int node, float gCost)
            {
                float total = gCost + exitCost[node];
                if (total < best) { best = total; bestNode = node; }
            }

            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s.Cost < dist[s.Node])
                {
                    dist[s.Node] = s.Cost;
                    open.Push(s.Node, s.Cost + H(s.Node));
                    Reach(s.Node, s.Cost);
                }
            }

            int expanded = 0;
            while (open.Count > 0)
            {
                if (open.PeekKey() >= best) break; // admissible heuristic: nothing left can beat best
                int u = open.Pop(out _);
                if (closed[u]) continue;
                closed[u] = true;
                expanded++;

                float du = dist[u];
                var arcs = g.Forward[u];
                for (int i = 0; i < arcs.Count; i++)
                {
                    var a = arcs[i];
                    float nd = du + a.Cost;
                    if (nd >= dist[a.To]) continue;
                    dist[a.To] = nd;
                    closed[a.To] = false; // reopen: stays correct even if float rounding breaks consistency
                    prev[a.To] = u;
                    prevEdge[a.To] = a.Edge;
                    open.Push(a.To, nd + H(a.To));
                    Reach(a.To, nd);
                }
            }

            if (bestNode < 0) return new PathResult(new List<int>(), new List<int>(), float.PositiveInfinity, expanded);

            var nodes = new List<int>();
            var edges = new List<int>();
            for (int x = bestNode; x != -1; x = prev[x])
            {
                nodes.Add(x);
                if (prev[x] != -1) edges.Add(prevEdge[x]);
            }
            nodes.Reverse();
            edges.Reverse();
            return new PathResult(nodes, edges, best, expanded);
        }
    }
}
