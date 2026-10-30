using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CampusNav.Routing
{
    public sealed class NavGraphException : Exception
    {
        public readonly IReadOnlyList<string> Errors;
        public NavGraphException(List<string> errors)
            : base("Invalid campus graph:\n - " + string.Join("\n - ", errors)) { Errors = errors; }
    }

    /// <summary>
    /// Turns <see cref="NavGraphData"/> (from JSON) into a validated <see cref="NavGraph"/>.
    /// Hard errors throw <see cref="NavGraphException"/> listing every problem at once;
    /// suspicious-but-usable data is reported in <c>warnings</c>.
    /// </summary>
    public static class NavGraphBuilder
    {
        const float LongEdgeWarning = 150f;

        public static NavGraph Build(NavGraphData data, List<string> warnings = null)
        {
            var errors = new List<string>();
            warnings = warnings ?? new List<string>();
            if (data == null) throw new NavGraphException(new List<string> { "No data (JSON could not be parsed?)." });
            if (data.nodes == null || data.nodes.Length == 0) throw new NavGraphException(new List<string> { "Graph has no nodes." });

            GeoProjection geo = data.origin != null && (data.origin.latitude != 0 || data.origin.longitude != 0)
                ? new GeoProjection(data.origin) : null;

            // ---- nodes
            var nodes = new List<NavNode>(data.nodes.Length);
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < data.nodes.Length; i++)
            {
                var d = data.nodes[i];
                if (d == null) { errors.Add($"nodes[{i}] is null."); continue; }
                if (string.IsNullOrWhiteSpace(d.id)) { errors.Add($"nodes[{i}] has no id."); continue; }
                if (ids.ContainsKey(d.id)) { errors.Add($"Duplicate node id '{d.id}'."); continue; }

                var pos = new Vector3(d.x, d.y, d.z);
                if (d.lat != 0 || d.lon != 0)
                {
                    if (geo == null) { errors.Add($"Node '{d.id}' uses lat/lon but the file has no origin."); continue; }
                    pos = geo.ToLocal(d.lat, d.lon, d.y);
                }
                if (!Finite(pos)) { errors.Add($"Node '{d.id}' has a non-finite position."); continue; }
                if (d.destination && string.IsNullOrWhiteSpace(d.name))
                    warnings.Add($"Destination '{d.id}' has no name; the UI will show its id.");

                ids[d.id] = nodes.Count;
                nodes.Add(new NavNode
                {
                    Index = nodes.Count, Id = d.id, Name = d.name, Position = pos, Floor = d.floor,
                    Building = d.building, Tags = d.tags ?? Array.Empty<string>(), IsDestination = d.destination
                });
            }

            // ---- edges
            var edges = new List<NavEdge>();
            var seenPairs = new HashSet<(int, int, EdgeType)>();
            var edgeData = data.edges ?? Array.Empty<NavEdgeData>();
            for (int i = 0; i < edgeData.Length; i++)
            {
                var d = edgeData[i];
                if (d == null) { errors.Add($"edges[{i}] is null."); continue; }
                string label = $"edges[{i}] {d.from} -> {d.to}";
                if (d.from == null || !ids.TryGetValue(d.from, out int a)) { errors.Add($"{label}: unknown 'from' node."); continue; }
                if (d.to == null || !ids.TryGetValue(d.to, out int b)) { errors.Add($"{label}: unknown 'to' node."); continue; }
                if (a == b) { errors.Add($"{label}: edge connects a node to itself."); continue; }
                if (!TryParseType(d.type, out EdgeType type)) { errors.Add($"{label}: unknown type '{d.type}'."); continue; }
                if (float.IsNaN(d.length) || d.length < 0) { errors.Add($"{label}: length must be >= 0."); continue; }

                var na = nodes[a]; var nb = nodes[b];
                float straight = Vector3.Distance(na.Position, nb.Position);
                float length = d.length;
                if (length <= 0) length = straight;
                else if (length < straight * 0.99f)
                {
                    warnings.Add($"{label}: length {length:0.##} m is shorter than the straight-line distance {straight:0.##} m; using {straight:0.##} m.");
                    length = straight;
                }
                if (length > LongEdgeWarning) warnings.Add($"{label}: unusually long ({length:0} m). Check the coordinates or add intermediate nodes.");

                bool floorChange = na.Floor != nb.Floor;
                bool vertical = type == EdgeType.Stairs || type == EdgeType.Escalator || type == EdgeType.Elevator;
                if (floorChange && !vertical && type != EdgeType.Ramp)
                    warnings.Add($"{label}: joins floor {na.Floor} to {nb.Floor} but type is '{type}'. Use stairs/elevator/escalator/ramp.");
                if (!floorChange && type == EdgeType.Elevator)
                    warnings.Add($"{label}: elevator edge on a single floor.");

                var key = a < b ? (a, b, type) : (b, a, type);
                if (!seenPairs.Add(key)) warnings.Add($"{label}: duplicate {type} edge between the same nodes.");

                edges.Add(new NavEdge
                {
                    Index = edges.Count, From = a, To = b, Type = type, OneWay = d.oneWay, Length = length,
                    Outdoor = d.outdoor, NoWheelchair = d.noWheelchair, Closed = d.closed
                });
            }

            if (errors.Count > 0) throw new NavGraphException(errors);

            var graph = new NavGraph(data.campus, data.origin, data.floors, nodes, edges);
            CheckConnectivity(graph, warnings);
            return graph;
        }

        public static bool TryParseType(string s, out EdgeType type)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "": case "walk": case "path": case "corridor": type = EdgeType.Walk; return true;
                case "ramp": type = EdgeType.Ramp; return true;
                case "stairs": case "stair": type = EdgeType.Stairs; return true;
                case "escalator": type = EdgeType.Escalator; return true;
                case "elevator": case "lift": type = EdgeType.Elevator; return true;
                case "door": type = EdgeType.Door; return true;
                default: type = EdgeType.Walk; return false;
            }
        }

        /// <summary>Warns about isolated nodes and parts of the map that can't reach each other.</summary>
        static void CheckConnectivity(NavGraph g, List<string> warnings)
        {
            int n = g.Nodes.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var e in g.Edges) { int ra = Find(e.From), rb = Find(e.To); if (ra != rb) parent[ra] = rb; }

            var size = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) { int r = Find(i); size[r] = size.TryGetValue(r, out int s) ? s + 1 : 1; }
            if (size.Count <= 1) return;

            int mainRoot = -1, mainSize = 0;
            foreach (var kv in size) if (kv.Value > mainSize) { mainSize = kv.Value; mainRoot = kv.Key; }

            var sb = new StringBuilder();
            int listed = 0;
            for (int i = 0; i < n && listed < 15; i++)
                if (Find(i) != mainRoot) { sb.Append(listed == 0 ? "" : ", ").Append(g.Nodes[i].Id); listed++; }
            warnings.Add($"Map has {size.Count} disconnected parts; {n - mainSize} node(s) can't be reached from the main network, e.g. {sb}.");
        }

        static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
    }
}
