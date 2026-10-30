using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    public enum RouteStatus
    {
        Ok,
        UnknownStart,
        UnknownGoal,
        /// <summary>The start position is further than MaxSnapDistance from any usable path on that floor.</summary>
        StartOffNetwork,
        GoalOffNetwork,
        /// <summary>Both ends are valid but no usable connection exists (closures, step-free restrictions, missing edges).</summary>
        NoRoute,
    }

    /// <summary>A corner of the route polyline. The segment to the next point has type <see cref="NextType"/>.</summary>
    public struct RoutePoint
    {
        public Vector3 Position;
        public int Floor;
        /// <summary>Graph node index, or -1 for a snapped point part-way along an edge.</summary>
        public int Node;
        public EdgeType NextType;
        public bool NextOutdoor;
    }

    public sealed class Route
    {
        public RouteStatus Status;
        public string Message;
        public bool Found => Status == RouteStatus.Ok;

        /// <summary>Polyline from the (snapped) start to the goal, in map space.</summary>
        public readonly List<RoutePoint> Points = new List<RoutePoint>();
        /// <summary>Distance along the polyline at each point; Cumulative[0] = 0.</summary>
        public float[] Cumulative = new float[0];
        /// <summary>Walking distance in metres (polyline length).</summary>
        public float Length;
        /// <summary>Search cost (walking-metre equivalents, includes stair/lift penalties).</summary>
        public float Cost;
        public float EstimatedSeconds;
        /// <summary>Horizontal distance from the requested start position to the snapped start (0 for node starts).</summary>
        public float StartSnapDistance;
        public int GoalNode = -1;
        /// <summary>Graph edges used, in order (for highlighting or analytics).</summary>
        public readonly List<int> Edges = new List<int>();
        public readonly List<RouteInstruction> Instructions = new List<RouteInstruction>();
        /// <summary>Nodes settled by the search, for profiling.</summary>
        public int Expanded;

        public static Route Fail(RouteStatus status, string message) => new Route { Status = status, Message = message };

        /// <summary>Point positions only, e.g. for a LineRenderer.</summary>
        public Vector3[] Positions()
        {
            var a = new Vector3[Points.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Points[i].Position;
            return a;
        }

        internal void ComputeCumulative()
        {
            Cumulative = new float[Points.Count];
            for (int i = 1; i < Points.Count; i++)
                Cumulative[i] = Cumulative[i - 1] + Vector3.Distance(Points[i - 1].Position, Points[i].Position);
            Length = Points.Count > 0 ? Cumulative[Points.Count - 1] : 0;
        }
    }

    /// <summary>A start or end of a route: either a graph node or a free position on a floor.</summary>
    public readonly struct RouteEndpoint
    {
        public readonly string NodeId;
        public readonly Vector3 Position;
        public readonly int Floor;
        public bool IsNode => NodeId != null;

        RouteEndpoint(string id, Vector3 pos, int floor) { NodeId = id; Position = pos; Floor = floor; }

        public static RouteEndpoint AtNode(string nodeId) => new RouteEndpoint(nodeId ?? "", default, 0);
        public static RouteEndpoint AtPosition(Vector3 mapPosition, int floor) => new RouteEndpoint(null, mapPosition, floor);
    }
}
