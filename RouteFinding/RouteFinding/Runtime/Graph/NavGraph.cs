using System;
using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    public enum EdgeType { Walk, Ramp, Stairs, Escalator, Elevator, Door }

    public sealed class NavNode
    {
        public int Index { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public Vector3 Position { get; internal set; }
        public int Floor { get; internal set; }
        public string Building { get; internal set; }
        public string[] Tags { get; internal set; }
        public bool IsDestination { get; internal set; }

        public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;
        public override string ToString() => $"{Id} ({DisplayName}, floor {Floor})";
    }

    public sealed class NavEdge
    {
        public int Index { get; internal set; }
        public int From { get; internal set; }
        public int To { get; internal set; }
        public EdgeType Type { get; internal set; }
        public bool OneWay { get; internal set; }
        /// <summary>Metres, never shorter than the straight-line distance between the nodes.</summary>
        public float Length { get; internal set; }
        public bool Outdoor { get; internal set; }
        public bool NoWheelchair { get; internal set; }
        public bool Closed { get; internal set; }

        public bool IsVerticalType => Type == EdgeType.Stairs || Type == EdgeType.Escalator || Type == EdgeType.Elevator;
        public int Other(int node) => node == From ? To : From;
    }

    /// <summary>
    /// The campus walking network. Build it with <see cref="NavGraphBuilder.Build"/>.
    /// Node and edge lists are fixed after loading; only edge closures can change at runtime.
    /// </summary>
    public sealed class NavGraph
    {
        readonly List<NavNode> nodes;
        readonly List<NavEdge> edges;
        readonly List<int>[] incident;
        readonly Dictionary<string, int> indexById;

        public IReadOnlyList<NavNode> Nodes => nodes;
        public IReadOnlyList<NavEdge> Edges => edges;
        public IReadOnlyList<FloorData> Floors { get; }
        public GeoOrigin Origin { get; }
        public string Campus { get; }
        /// <summary>Node positions indexed by node index (shared array, do not modify).</summary>
        public Vector3[] Positions { get; }
        /// <summary>Increments whenever an edge is opened or closed. Cached cost graphs compare against it.</summary>
        public int Version { get; private set; }

        internal NavGraph(string campus, GeoOrigin origin, IReadOnlyList<FloorData> floors, List<NavNode> nodes, List<NavEdge> edges)
        {
            Campus = campus;
            Origin = origin;
            Floors = floors ?? Array.Empty<FloorData>();
            this.nodes = nodes;
            this.edges = edges;
            Positions = new Vector3[nodes.Count];
            indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            incident = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                Positions[i] = nodes[i].Position;
                indexById[nodes[i].Id] = i;
                incident[i] = new List<int>();
            }
            foreach (var e in edges)
            {
                incident[e.From].Add(e.Index);
                incident[e.To].Add(e.Index);
            }
        }

        public int IndexOf(string id) => id != null && indexById.TryGetValue(id, out int i) ? i : -1;

        public bool TryGetNode(string id, out NavNode node)
        {
            int i = IndexOf(id);
            node = i >= 0 ? nodes[i] : null;
            return node != null;
        }

        /// <summary>Indices of edges touching this node (either direction).</summary>
        public IReadOnlyList<int> EdgesAt(int node) => incident[node];

        public void SetEdgeClosed(int edgeIndex, bool closed)
        {
            var e = edges[edgeIndex];
            if (e.Closed == closed) return;
            e.Closed = closed;
            Version++;
        }

        /// <summary>Opens or closes every edge between two nodes. Returns how many edges changed.</summary>
        public int SetClosedBetween(string idA, string idB, bool closed)
        {
            int a = IndexOf(idA), b = IndexOf(idB), changed = 0;
            if (a < 0 || b < 0) return 0;
            foreach (int ei in incident[a])
            {
                var e = edges[ei];
                if (e.Other(a) == b && e.Closed != closed) { SetEdgeClosed(ei, closed); changed++; }
            }
            return changed;
        }

        /// <summary>
        /// Destination search for the UI: matches id, name, building and tags (case-insensitive).
        /// Exact and prefix name matches come first.
        /// </summary>
        public List<NavNode> FindDestinations(string query, int max = 20)
        {
            var hits = new List<(int score, NavNode node)>();
            string q = (query ?? "").Trim();
            foreach (var n in nodes)
            {
                if (!n.IsDestination) continue;
                int score = q.Length == 0 ? 3 : Score(n, q);
                if (score > 0) hits.Add((score, n));
            }
            hits.Sort((x, y) => x.score != y.score ? y.score.CompareTo(x.score)
                                                    : string.Compare(x.node.DisplayName, y.node.DisplayName, StringComparison.OrdinalIgnoreCase));
            var result = new List<NavNode>();
            for (int i = 0; i < hits.Count && i < max; i++) result.Add(hits[i].node);
            return result;
        }

        static int Score(NavNode n, string q)
        {
            var cmp = StringComparison.OrdinalIgnoreCase;
            string name = n.DisplayName;
            if (string.Equals(name, q, cmp) || string.Equals(n.Id, q, cmp)) return 100;
            if (name.StartsWith(q, cmp)) return 50;
            if (name.IndexOf(q, cmp) >= 0) return 30;
            if (n.Id.IndexOf(q, cmp) >= 0) return 20;
            if (n.Tags != null)
                foreach (var t in n.Tags)
                    if (t != null && t.IndexOf(q, cmp) >= 0) return 15;
            if (n.Building != null && n.Building.IndexOf(q, cmp) >= 0) return 10;
            return 0;
        }

        /// <summary>
        /// Builds the directed cost graph used by the search algorithms. Closed edges and edges the
        /// profile forbids are left out.
        /// </summary>
        public CostGraph BuildCostGraph(RoutingProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var g = new CostGraph(nodes.Count);
            float k = float.PositiveInfinity;
            foreach (var e in edges)
            {
                if (!profile.TryGetCost(e, out float cost)) continue;
                g.AddArc(e.From, e.To, cost, e.Index);
                if (!e.OneWay) g.AddArc(e.To, e.From, cost, e.Index);
                float straight = Vector3.Distance(Positions[e.From], Positions[e.To]);
                if (straight > 1e-3f) k = Math.Min(k, cost / straight);
            }
            g.HeuristicScale = float.IsPositiveInfinity(k) ? 0f : k * 0.999f;
            return g;
        }

        /// <summary>Floor level for a map-space height, using <see cref="Floors"/>. Returns fallback if no floors are defined.</summary>
        public int FloorFromHeight(float footY, int fallback = 0)
        {
            if (Floors.Count == 0) return fallback;
            int best = fallback;
            float bestD = float.PositiveInfinity;
            foreach (var f in Floors)
            {
                float d = Math.Abs(f.elevation - footY);
                if (d < bestD) { bestD = d; best = f.level; }
            }
            return best;
        }
    }
}
