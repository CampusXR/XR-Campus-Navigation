using System;
using System.Collections.Generic;
using System.Numerics;
using CampusNav.Routing;
using NUnit.Framework;

namespace CampusNav.Routing.Tests
{
    public class MinHeapTests
    {
        [Test]
        public void PopsInKeyOrder()
        {
            var rnd = new Random(7);
            var heap = new MinHeap(2); // small capacity forces growth
            var keys = new List<float>();
            for (int i = 0; i < 500; i++) { float k = (float)rnd.NextDouble() * 100; keys.Add(k); heap.Push(i, k); }
            keys.Sort();
            foreach (float expected in keys) { heap.Pop(out float k); Assert.AreEqual(expected, k); }
            Assert.AreEqual(0, heap.Count);
        }

        [Test]
        public void EmptyHeapThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new MinHeap().Pop(out _));
            Assert.Throws<InvalidOperationException>(() => new MinHeap().PeekKey());
        }
    }

    /// <summary>The original tests, kept, with the edge-cost helper fixed for parallel edges.</summary>
    public class BiDijkstraTests
    {
        static List<(int to, float w)>[] Graph(int n, params (int a, int b, float w)[] edges)
        {
            var g = new List<(int, float)>[n];
            for (int i = 0; i < n; i++) g[i] = new List<(int, float)>();
            foreach (var (a, b, w) in edges) { g[a].Add((b, w)); g[b].Add((a, w)); }
            return g;
        }

        // Fixed: the original used Find(), which returns the FIRST matching edge. Random graphs contain
        // parallel edges, and the search correctly takes the cheapest one, so Find() over-counted.
        internal static float Cost(List<(int to, float w)>[] g, List<int> p)
        {
            float c = 0;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                float best = float.PositiveInfinity;
                foreach (var (to, w) in g[p[i]]) if (to == p[i + 1] && w < best) best = w;
                c += best;
            }
            return c;
        }

        /// <summary>Plain Dijkstra over a CostGraph, the reference answer.</summary>
        internal static float Reference(CostGraph g, int s, int t)
        {
            var d = new float[g.NodeCount];
            for (int i = 0; i < d.Length; i++) d[i] = float.PositiveInfinity;
            d[s] = 0;
            var q = new MinHeap();
            q.Push(s, 0);
            while (q.Count > 0)
            {
                int u = q.Pop(out float du);
                if (du > d[u]) continue;
                foreach (var a in g.Forward[u]) if (du + a.Cost < d[a.To]) { d[a.To] = du + a.Cost; q.Push(a.To, d[a.To]); }
            }
            return d[t];
        }

        [Test] public void SameNode() => CollectionAssert.AreEqual(new[] { 3 }, BiDijkstra.Path(Graph(5), 3, 3));

        [Test] public void Unreachable() => Assert.IsEmpty(BiDijkstra.Path(Graph(4, (0, 1, 1), (2, 3, 1)), 0, 3));

        [Test]
        public void FirstMeetingIsNotShortest()
        {
            var g = Graph(4, (0, 1, 1), (1, 2, 10), (0, 3, 2), (3, 2, 2));
            CollectionAssert.AreEqual(new[] { 0, 3, 2 }, BiDijkstra.Path(g, 0, 2));
        }

        // Stronger version: the first node settled by BOTH searches (M) is not on the shortest path.
        //   0 -3- M -3- 5   (cost 6, M is settled by both sides first)
        //   0 -2- A -2- B -1.5- 5   (cost 5.5, the real answer)
        [Test]
        public void FirstNodeSettledByBothSidesIsNotShortest()
        {
            var g = Graph(6, (0, 1, 3), (1, 5, 3), (0, 2, 2), (2, 3, 2), (3, 5, 1.5f));
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 5 }, BiDijkstra.Path(g, 0, 5));
        }

        [Test]
        public void OneWayEdgesRespectDirection()
        {
            // 0 -> 1 -> 2 one way (cheap), 2 <-> 0 two way (expensive)
            var adj = new List<(int to, float w)>[3];
            for (int i = 0; i < 3; i++) adj[i] = new List<(int, float)>();
            adj[0].Add((1, 1)); adj[1].Add((2, 1));
            adj[2].Add((0, 10)); adj[0].Add((2, 10));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, BiDijkstra.Path(adj, 0, 2));
            CollectionAssert.AreEqual(new[] { 2, 0 }, BiDijkstra.Path(adj, 2, 0)); // can't go 2->1->0
        }

        [Test]
        public void RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BiDijkstra.Path(Graph(3), 0, 7));
            Assert.Throws<ArgumentException>(() => BiDijkstra.Path(Graph(2, (0, 1, -1)), 0, 1));
            Assert.Throws<ArgumentException>(() => BiDijkstra.Path(Graph(2, (0, 1, float.NaN)), 0, 1));
        }

        [Test]
        public void SeedsActLikeAVirtualNode()
        {
            // line 0-1-2-3, start "between 1 and 2" (0.25 from 1), goal between 3 and nothing: node 3 + 0.5
            var cg = CostGraph.FromAdjacency(Graph(4, (0, 1, 1), (1, 2, 1), (2, 3, 1)));
            var r = BiDijkstra.Search(cg, new[] { new Seed(1, 0.25f), new Seed(2, 0.75f) }, new[] { new Seed(3, 0.5f) });
            CollectionAssert.AreEqual(new[] { 2, 3 }, r.Nodes);
            Assert.AreEqual(0.75f + 1f + 0.5f, r.Cost, 1e-5f);
        }

        [Test]
        public void MatchesDijkstraOnRandomGraphs()
        {
            var rnd = new Random(1);
            for (int run = 0; run < 300; run++)
            {
                int n = 30;
                var edges = new List<(int, int, float)>();
                for (int i = 0; i < 60; i++) edges.Add((rnd.Next(n), rnd.Next(n), 1 + rnd.Next(20)));
                var g = Graph(n, edges.ToArray());
                int s = rnd.Next(n), t = rnd.Next(n);
                var p = BiDijkstra.Path(g, s, t);
                float want = Reference(CostGraph.FromAdjacency(g), s, t);
                if (float.IsPositiveInfinity(want)) Assert.IsEmpty(p);
                else
                {
                    Assert.AreEqual(s, p[0]);
                    Assert.AreEqual(t, p[p.Count - 1]);
                    Assert.AreEqual(want, Cost(g, p), 1e-4f);
                }
            }
        }

        [Test]
        public void MatchesDijkstraOnRandomDirectedGraphs()
        {
            var rnd = new Random(2);
            for (int run = 0; run < 300; run++)
            {
                int n = 25;
                var cg = new CostGraph(n);
                for (int i = 0; i < 70; i++) cg.AddArc(rnd.Next(n), rnd.Next(n), (float)rnd.NextDouble() * 10);
                int s = rnd.Next(n), t = rnd.Next(n);
                var r = BiDijkstra.Search(cg, s, t);
                float want = Reference(cg, s, t);
                if (float.IsPositiveInfinity(want)) Assert.IsFalse(r.Found);
                else Assert.AreEqual(want, r.Cost, 1e-3f);
            }
        }
    }

    public class AStarTests
    {
        /// <summary>Random geometric graph: nodes in a 100x100 square, edges cost >= their length.</summary>
        static (CostGraph g, Vector3[] pos) RandomGeometric(Random rnd, int n, int m, bool directed)
        {
            var pos = new Vector3[n];
            for (int i = 0; i < n; i++) pos[i] = new Vector3((float)rnd.NextDouble() * 100, 0, (float)rnd.NextDouble() * 100);
            var g = new CostGraph(n);
            float k = float.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                int a = rnd.Next(n), b = rnd.Next(n);
                float len = Vector3.Distance(pos[a], pos[b]);
                float cost = len * (1 + (float)rnd.NextDouble() * 2) + (float)rnd.NextDouble();
                g.AddArc(a, b, cost);
                if (!directed) g.AddArc(b, a, cost);
                if (len > 1e-3f) k = Math.Min(k, cost / len);
            }
            g.HeuristicScale = float.IsPositiveInfinity(k) ? 0 : k * 0.999f;
            return (g, pos);
        }

        [Test]
        public void MatchesDijkstraOnRandomGeometricGraphs([Values(false, true)] bool directed)
        {
            var rnd = new Random(directed ? 11 : 10);
            for (int run = 0; run < 300; run++)
            {
                var (g, pos) = RandomGeometric(rnd, 40, 110, directed);
                int s = rnd.Next(40), t = rnd.Next(40);
                float want = BiDijkstraTests.Reference(g, s, t);
                var a = AStar.Search(g, pos, s, t);
                var b = BiDijkstra.Search(g, s, t);
                if (float.IsPositiveInfinity(want)) { Assert.IsFalse(a.Found); Assert.IsFalse(b.Found); continue; }
                Assert.AreEqual(want, a.Cost, 1e-3f, "A*");
                Assert.AreEqual(want, b.Cost, 1e-3f, "BiDijkstra");
                Assert.AreEqual(s, a.Nodes[0]);
                Assert.AreEqual(t, a.Nodes[a.Nodes.Count - 1]);
                Assert.AreEqual(a.Nodes.Count - 1, a.Edges.Count);
            }
        }

        [Test]
        public void HeuristicReducesWork()
        {
            var rnd = new Random(5);
            var (g, pos) = RandomGeometric(rnd, 400, 2400, false);
            int expandedAStar = 0, expandedDijkstra = 0;
            for (int i = 0; i < 50; i++)
            {
                int s = rnd.Next(400), t = rnd.Next(400);
                expandedAStar += AStar.Search(g, pos, s, t).Expanded;
                expandedDijkstra += AStar.Search(g, null, s, t).Expanded; // no positions = Dijkstra
            }
            Assert.Less(expandedAStar, expandedDijkstra);
        }

        [Test]
        public void MultipleTargetsPicksCheapestIncludingExitCost()
        {
            var g = new CostGraph(3);
            g.AddArc(0, 1, 1); g.AddArc(0, 2, 2);
            var pos = new[] { Vector3.Zero, new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
            g.HeuristicScale = 0.999f;
            var r = AStar.Search(g, pos, new[] { new Seed(0, 0) }, new[] { new Seed(1, 5), new Seed(2, 0.5f) });
            CollectionAssert.AreEqual(new[] { 0, 2 }, r.Nodes);
            Assert.AreEqual(2.5f, r.Cost, 1e-5f);
        }
    }
}
