using System;
using System.Collections.Generic;

namespace CampusNav.Routing
{
    /// <summary>
    /// Bidirectional Dijkstra. Forward search from the sources over <see cref="CostGraph.Forward"/>,
    /// backward search from the targets over <see cref="CostGraph.Backward"/>; stops when
    /// topF + topB >= best, which guarantees the shortest path (not just the first meeting).
    /// </summary>
    public static class BiDijkstra
    {
        /// <summary>
        /// Original API, kept for compatibility. Each adj entry is treated as a directed arc,
        /// so pass both directions for walkable two-way edges (one-way edges now work too).
        /// Returns node ids start..goal, or an empty list if unreachable.
        /// </summary>
        public static List<int> Path(List<(int to, float w)>[] adj, int start, int goal)
        {
            var g = CostGraph.FromAdjacency(adj);
            if ((uint)start >= (uint)g.NodeCount) throw new ArgumentOutOfRangeException(nameof(start));
            if ((uint)goal >= (uint)g.NodeCount) throw new ArgumentOutOfRangeException(nameof(goal));
            return Search(g, start, goal).Nodes;
        }

        public static PathResult Search(CostGraph g, int start, int goal)
        {
            return Search(g, new[] { new Seed(start, 0) }, new[] { new Seed(goal, 0) });
        }

        public static PathResult Search(CostGraph g, IReadOnlyList<Seed> sources, IReadOnlyList<Seed> targets)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            SearchUtil.CheckSeeds(g, sources, nameof(sources));
            SearchUtil.CheckSeeds(g, targets, nameof(targets));

            int n = g.NodeCount;
            var dist = new[] { new float[n], new float[n] };
            var prev = new[] { new int[n], new int[n] };
            var prevEdge = new[] { new int[n], new int[n] };
            var pq = new[] { new MinHeap(), new MinHeap() };
            for (int s = 0; s < 2; s++)
            {
                Array.Fill(dist[s], float.PositiveInfinity);
                Array.Fill(prev[s], -1);
                Array.Fill(prevEdge[s], -1);
            }

            for (int i = 0; i < sources.Count; i++) SeedSide(0, sources[i]);
            for (int i = 0; i < targets.Count; i++) SeedSide(1, targets[i]);

            // A node seeded on both sides is already a complete route.
            float best = float.PositiveInfinity;
            int meet = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                int v = targets[i].Node;
                float total = dist[0][v] + dist[1][v];
                if (total < best) { best = total; meet = v; }
            }

            int expanded = 0;
            while (pq[0].Count > 0 && pq[1].Count > 0)
            {
                if (pq[0].PeekKey() + pq[1].PeekKey() >= best) break; // no shorter route can still appear

                int side = pq[0].Count <= pq[1].Count ? 0 : 1;
                int u = pq[side].Pop(out float d);
                if (d > dist[side][u]) continue; // stale entry
                expanded++;

                var arcs = side == 0 ? g.Forward[u] : g.Backward[u];
                var distS = dist[side];
                var distO = dist[1 - side];
                for (int i = 0; i < arcs.Count; i++)
                {
                    var a = arcs[i];
                    float nd = d + a.Cost;
                    if (nd >= distS[a.To]) continue;
                    distS[a.To] = nd;
                    prev[side][a.To] = u;
                    prevEdge[side][a.To] = a.Edge;
                    pq[side].Push(a.To, nd);

                    float total = nd + distO[a.To];
                    if (total < best) { best = total; meet = a.To; }
                }
            }

            if (meet < 0) return new PathResult(new List<int>(), new List<int>(), float.PositiveInfinity, expanded);

            var nodes = new List<int>();
            var edges = new List<int>();
            for (int x = meet; x != -1; x = prev[0][x])
            {
                nodes.Add(x);
                if (prev[0][x] != -1) edges.Add(prevEdge[0][x]);
            }
            nodes.Reverse();
            edges.Reverse();
            for (int x = meet; prev[1][x] != -1; x = prev[1][x])
            {
                edges.Add(prevEdge[1][x]);
                nodes.Add(prev[1][x]);
            }
            return new PathResult(nodes, edges, best, expanded);

            void SeedSide(int side, Seed s)
            {
                if (s.Cost < dist[side][s.Node])
                {
                    dist[side][s.Node] = s.Cost;
                    pq[side].Push(s.Node, s.Cost);
                }
            }
        }
    }
}
