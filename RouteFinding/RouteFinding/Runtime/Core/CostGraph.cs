using System;
using System.Collections.Generic;

namespace CampusNav.Routing
{
    /// <summary>One directed arc in a <see cref="CostGraph"/>.</summary>
    public readonly struct Arc
    {
        public readonly int To;
        public readonly float Cost;
        /// <summary>Index of the source edge in the NavGraph, or -1 if the graph was built from a raw list.</summary>
        public readonly int Edge;

        public Arc(int to, float cost, int edge) { To = to; Cost = cost; Edge = edge; }
    }

    /// <summary>
    /// A search start or end point with an extra entry cost. Used to start a search from a point
    /// in the middle of an edge (the user's snapped position): seed both edge ends with the
    /// cost of walking from the point to that end.
    /// </summary>
    public readonly struct Seed
    {
        public readonly int Node;
        public readonly float Cost;
        public Seed(int node, float cost) { Node = node; Cost = cost; }
    }

    /// <summary>
    /// Directed weighted graph with both forward and reverse adjacency, so the backward half of a
    /// bidirectional search is correct for one-way edges too.
    /// </summary>
    public sealed class CostGraph
    {
        public readonly List<Arc>[] Forward;
        public readonly List<Arc>[] Backward;
        public int NodeCount => Forward.Length;

        /// <summary>
        /// Largest k such that every arc cost >= k * straight-line distance between its endpoints.
        /// A* multiplies Euclidean distance by this, which keeps the heuristic admissible and
        /// consistent whatever penalties the routing profile applies. 0 when positions are unknown.
        /// </summary>
        public float HeuristicScale { get; set; }

        public CostGraph(int nodeCount)
        {
            if (nodeCount < 0) throw new ArgumentOutOfRangeException(nameof(nodeCount));
            Forward = new List<Arc>[nodeCount];
            Backward = new List<Arc>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                Forward[i] = new List<Arc>();
                Backward[i] = new List<Arc>();
            }
        }

        public void AddArc(int from, int to, float cost, int edge = -1)
        {
            if ((uint)from >= (uint)NodeCount) throw new ArgumentOutOfRangeException(nameof(from));
            if ((uint)to >= (uint)NodeCount) throw new ArgumentOutOfRangeException(nameof(to));
            if (float.IsNaN(cost) || cost < 0 || float.IsInfinity(cost))
                throw new ArgumentException($"Arc {from}->{to} has invalid cost {cost}. Costs must be finite and >= 0.");
            Forward[from].Add(new Arc(to, cost, edge));
            Backward[to].Add(new Arc(from, cost, edge));
        }

        /// <summary>Builds a CostGraph from a plain adjacency list (each entry is a directed arc).</summary>
        public static CostGraph FromAdjacency(List<(int to, float w)>[] adj)
        {
            if (adj == null) throw new ArgumentNullException(nameof(adj));
            var g = new CostGraph(adj.Length);
            for (int u = 0; u < adj.Length; u++)
            {
                if (adj[u] == null) continue;
                foreach (var (v, w) in adj[u]) g.AddArc(u, v, w);
            }
            return g;
        }
    }

    /// <summary>Result of a graph search.</summary>
    public sealed class PathResult
    {
        public static readonly PathResult NotFound = new PathResult(new List<int>(), new List<int>(), float.PositiveInfinity, 0);

        /// <summary>Node indices from the first (seed) node to the last (target) node.</summary>
        public readonly List<int> Nodes;
        /// <summary>Edge index for each step; Edges[i] joins Nodes[i] and Nodes[i+1]. Count = Nodes.Count - 1.</summary>
        public readonly List<int> Edges;
        /// <summary>Total cost including source and target seed costs.</summary>
        public readonly float Cost;
        /// <summary>Number of nodes settled. Useful for comparing algorithms.</summary>
        public readonly int Expanded;

        public bool Found => Nodes.Count > 0;

        public PathResult(List<int> nodes, List<int> edges, float cost, int expanded)
        {
            Nodes = nodes; Edges = edges; Cost = cost; Expanded = expanded;
        }
    }

    internal static class SearchUtil
    {
        internal static void CheckSeeds(CostGraph g, IReadOnlyList<Seed> seeds, string name)
        {
            if (seeds == null) throw new ArgumentNullException(name);
            for (int i = 0; i < seeds.Count; i++)
            {
                var s = seeds[i];
                if ((uint)s.Node >= (uint)g.NodeCount)
                    throw new ArgumentOutOfRangeException(name, $"Seed node {s.Node} is outside 0..{g.NodeCount - 1}.");
                if (float.IsNaN(s.Cost) || s.Cost < 0 || float.IsInfinity(s.Cost))
                    throw new ArgumentException($"Seed cost {s.Cost} for node {s.Node} must be finite and >= 0.", name);
            }
        }
    }
}
