using System;
using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    public enum RouteAlgorithm { AStar, BidirectionalDijkstra }

    /// <summary>
    /// Entry point for route finding. Resolves start/goal (node ids or free positions snapped to the
    /// nearest path), runs the search on a cached cost graph and returns a <see cref="Route"/> with
    /// polyline, distance, ETA and turn-by-turn instructions.
    /// Not thread-safe; use one instance per thread.
    /// </summary>
    public sealed class RouteService
    {
        const float MergeEpsilon = 0.05f;

        public NavGraph Graph { get; }
        public RouteAlgorithm Algorithm = RouteAlgorithm.AStar;
        /// <summary>A start/goal position further than this (metres, horizontal) from any usable path is rejected.</summary>
        public float MaxSnapDistance = 25f;

        RoutingProfile profile;
        RoutingProfile cachedProfile;
        CostGraph cachedCostGraph;
        int cachedVersion = -1;
        EdgeSnapper snapper;

        public RouteService(NavGraph graph, RoutingProfile profile = null)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            this.profile = profile ?? RoutingProfile.Default();
        }

        /// <summary>The active profile. Changing its fields is detected automatically on the next query.</summary>
        public RoutingProfile Profile
        {
            get => profile;
            set => profile = value ?? RoutingProfile.Default();
        }

        public EdgeSnapper Snapper => snapper ?? (snapper = new EdgeSnapper(Graph));

        public CostGraph CurrentCostGraph()
        {
            if (cachedCostGraph == null || cachedVersion != Graph.Version || !SameProfile(cachedProfile, profile))
            {
                cachedCostGraph = Graph.BuildCostGraph(profile);
                cachedVersion = Graph.Version;
                cachedProfile = profile.Clone();
            }
            return cachedCostGraph;
        }

        public Route FindRoute(string fromNodeId, string toNodeId) =>
            FindRoute(RouteEndpoint.AtNode(fromNodeId), RouteEndpoint.AtNode(toNodeId));

        public Route FindRoute(Vector3 fromMapPosition, int fromFloor, string toNodeId) =>
            FindRoute(RouteEndpoint.AtPosition(fromMapPosition, fromFloor), RouteEndpoint.AtNode(toNodeId));

        public Route FindRoute(RouteEndpoint from, RouteEndpoint to)
        {
            var cg = CurrentCostGraph();

            if (!Resolve(from, true, out var start, out var failStart)) return failStart;
            if (!Resolve(to, false, out var goal, out var failGoal)) return failGoal;

            PathResult result = Algorithm == RouteAlgorithm.BidirectionalDijkstra
                ? BiDijkstra.Search(cg, start.Seeds, goal.Seeds)
                : AStar.Search(cg, Graph.Positions, start.Seeds, goal.Seeds);

            // Start and goal on the same edge: walking straight along it may beat any graph route.
            if (start.Edge >= 0 && start.Edge == goal.Edge && TryDirectCost(start, goal, out float direct) && direct <= result.Cost)
                return BuildDirect(start, goal, direct, result.Expanded);

            if (!result.Found)
                return Route.Fail(RouteStatus.NoRoute, profile.StepFree
                    ? "No step-free route found. Some paths may be closed or only reachable by stairs."
                    : "No route found. Some paths may be closed or this place is not connected to the map.");

            return BuildRoute(start, goal, result);
        }

        // ---------------------------------------------------------------- endpoint resolution

        sealed class Resolved
        {
            public List<Seed> Seeds = new List<Seed>();
            public int Edge = -1;
            public float T;
            public Vector3 Point;
            public int Floor;
            public float SnapDistance;
        }

        bool Resolve(RouteEndpoint ep, bool isStart, out Resolved r, out Route fail)
        {
            r = new Resolved();
            fail = null;
            if (ep.IsNode)
            {
                int idx = Graph.IndexOf(ep.NodeId);
                if (idx < 0)
                {
                    fail = Route.Fail(isStart ? RouteStatus.UnknownStart : RouteStatus.UnknownGoal, $"Unknown place '{ep.NodeId}'.");
                    return false;
                }
                r.Seeds.Add(new Seed(idx, 0));
                r.Point = Graph.Positions[idx];
                r.Floor = Graph.Nodes[idx].Floor;
                return true;
            }

            var snap = Snapper.Snap(ep.Position, ep.Floor, MaxSnapDistance, profile.Allows);
            if (!snap.Found)
            {
                string where = isStart ? "You are" : "The destination is";
                fail = Route.Fail(isStart ? RouteStatus.StartOffNetwork : RouteStatus.GoalOffNetwork,
                    $"{where} more than {MaxSnapDistance:0} m from any mapped path on floor {ep.Floor}.");
                return false;
            }

            var e = Graph.Edges[snap.Edge];
            profile.TryGetCost(e, out float c);
            r.Edge = snap.Edge;
            r.T = snap.T;
            r.Point = snap.Point;
            r.Floor = ep.Floor;
            r.SnapDistance = snap.Distance;

            // Start: walk from the point to an edge end. Goal: walk from an edge end to the point.
            // One-way edges only allow From -> To travel.
            if (isStart)
            {
                r.Seeds.Add(new Seed(e.To, (1 - snap.T) * c));
                if (!e.OneWay) r.Seeds.Add(new Seed(e.From, snap.T * c));
            }
            else
            {
                r.Seeds.Add(new Seed(e.From, snap.T * c));
                if (!e.OneWay) r.Seeds.Add(new Seed(e.To, (1 - snap.T) * c));
            }
            return true;
        }

        bool TryDirectCost(Resolved s, Resolved g, out float cost)
        {
            cost = float.PositiveInfinity;
            var e = Graph.Edges[s.Edge];
            if (g.T < s.T && e.OneWay) return false;
            if (!profile.TryGetCost(e, out float c)) return false;
            cost = Math.Abs(g.T - s.T) * c;
            return true;
        }

        // ---------------------------------------------------------------- route assembly

        Route BuildDirect(Resolved s, Resolved g, float cost, int expanded)
        {
            var e = Graph.Edges[s.Edge];
            var route = new Route { Status = RouteStatus.Ok, Cost = cost, Expanded = expanded, StartSnapDistance = s.SnapDistance };
            route.Points.Add(new RoutePoint { Position = s.Point, Floor = s.Floor, Node = -1, NextType = e.Type, NextOutdoor = e.Outdoor });
            AddPoint(route, new RoutePoint { Position = g.Point, Floor = g.Floor, Node = -1, NextType = EdgeType.Walk });
            route.Edges.Add(e.Index);
            Finish(route);
            return route;
        }

        Route BuildRoute(Resolved s, Resolved g, PathResult r)
        {
            var route = new Route { Status = RouteStatus.Ok, Cost = r.Cost, Expanded = r.Expanded, StartSnapDistance = s.SnapDistance };

            if (s.Edge >= 0)
            {
                var se = Graph.Edges[s.Edge];
                route.Points.Add(new RoutePoint { Position = s.Point, Floor = s.Floor, Node = -1, NextType = se.Type, NextOutdoor = se.Outdoor });
                route.Edges.Add(se.Index);
            }

            for (int i = 0; i < r.Nodes.Count; i++)
            {
                int node = r.Nodes[i];
                var p = new RoutePoint { Position = Graph.Positions[node], Floor = Graph.Nodes[node].Floor, Node = node, NextType = EdgeType.Walk };
                if (i < r.Edges.Count)
                {
                    var e = Graph.Edges[r.Edges[i]];
                    p.NextType = e.Type;
                    p.NextOutdoor = e.Outdoor;
                    route.Edges.Add(e.Index);
                }
                else if (g.Edge >= 0)
                {
                    var ge = Graph.Edges[g.Edge];
                    p.NextType = ge.Type;
                    p.NextOutdoor = ge.Outdoor;
                }
                AddPoint(route, p);
            }

            if (g.Edge >= 0)
            {
                if (route.Edges.Count == 0 || route.Edges[route.Edges.Count - 1] != g.Edge) route.Edges.Add(g.Edge);
                AddPoint(route, new RoutePoint { Position = g.Point, Floor = g.Floor, Node = -1, NextType = EdgeType.Walk });
            }

            // The first real node can coincide with the snapped start; drop the duplicate edge entry too.
            if (route.Edges.Count >= 2 && route.Edges[0] == route.Edges[1]) route.Edges.RemoveAt(0);

            route.GoalNode = g.Edge >= 0 ? -1 : r.Nodes[r.Nodes.Count - 1];
            Finish(route);
            return route;
        }

        /// <summary>Adds a point, merging it with the previous one if they are at the same place on the same floor.</summary>
        static void AddPoint(Route route, RoutePoint p)
        {
            int n = route.Points.Count;
            if (n > 0)
            {
                var last = route.Points[n - 1];
                if (last.Floor == p.Floor && Vector3.Distance(last.Position, p.Position) < MergeEpsilon)
                {
                    // keep the graph node (it has a name), but keep the outgoing segment info of the new point
                    if (last.Node < 0) last.Node = p.Node;
                    last.Position = p.Node >= 0 ? p.Position : last.Position;
                    last.NextType = p.NextType;
                    last.NextOutdoor = p.NextOutdoor;
                    route.Points[n - 1] = last;
                    return;
                }
            }
            route.Points.Add(p);
        }

        void Finish(Route route)
        {
            route.ComputeCumulative();
            route.EstimatedSeconds = profile.SecondsFor(route.Cost);
            InstructionBuilder.Build(route, Graph);
        }

        static bool SameProfile(RoutingProfile a, RoutingProfile b)
        {
            if (a == null || b == null) return false;
            return a.StepFree == b.StepFree && a.WalkSpeed == b.WalkSpeed && a.StairsMultiplier == b.StairsMultiplier
                && a.RampMultiplier == b.RampMultiplier && a.EscalatorMultiplier == b.EscalatorMultiplier
                && a.ElevatorMultiplier == b.ElevatorMultiplier && a.ElevatorWait == b.ElevatorWait
                && a.DoorPenalty == b.DoorPenalty && a.OutdoorMultiplier == b.OutdoorMultiplier;
        }
    }
}
