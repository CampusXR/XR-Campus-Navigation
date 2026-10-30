using System;
using System.Collections.Generic;
using System.Numerics;

namespace CampusNav.Routing
{
    public enum Maneuver
    {
        Depart,
        SlightLeft, SlightRight, Left, Right, SharpLeft, SharpRight, UTurn,
        StairsUp, StairsDown, EscalatorUp, EscalatorDown, Elevator,
        EnterBuilding, ExitBuilding,
        Arrive,
    }

    public sealed class RouteInstruction
    {
        public Maneuver Type;
        /// <summary>Index into <see cref="Route.Points"/> where the maneuver happens.</summary>
        public int PointIndex;
        public float DistanceFromStart;
        /// <summary>Metres from this maneuver to the next one (0 for Arrive).</summary>
        public float DistanceToNext;
        /// <summary>Signed turn angle in degrees, + = right. 0 for non-turns.</summary>
        public float Angle;
        /// <summary>Floor the maneuver starts on.</summary>
        public int FromFloor;
        /// <summary>Floor reached by stairs/escalator/elevator maneuvers (same as FromFloor otherwise).</summary>
        public int TargetFloor;
        /// <summary>Name of the place where the maneuver happens, if the map names it.</summary>
        public string Landmark;
        /// <summary>English text without distance ("Turn left at Library"). Localise from Type/params if needed.</summary>
        public string Text;

        public override string ToString() => $"[{DistanceFromStart:0}m] {Text}";
    }

    /// <summary>
    /// Turns a route polyline into turn-by-turn maneuvers. Straight-ish corners (under
    /// <see cref="StraightThreshold"/> degrees) are not announced; floor changes, building
    /// entry/exit and arrival always are.
    /// </summary>
    public static class InstructionBuilder
    {
        public const float StraightThreshold = 25f;
        const float SlightLimit = 50f, TurnLimit = 130f, SharpLimit = 165f;
        /// <summary>Segments shorter than this (XZ) are ignored when measuring turn direction.</summary>
        const float MinDirectionLength = 0.75f;

        public static void Build(Route route, NavGraph graph)
        {
            var list = route.Instructions;
            list.Clear();
            var pts = route.Points;
            int n = pts.Count;
            if (n == 0) return;

            list.Add(Make(route, graph, Maneuver.Depart, 0, 0, pts[0].Floor));

            int i = 0;
            while (i < n - 1)
            {
                var p = pts[i];
                bool outVertical = IsVertical(p.NextType);

                if (outVertical)
                {
                    int j = i;
                    while (j < n - 1 && pts[j].NextType == p.NextType) j++;
                    int fromFloor = p.Floor, toFloor = pts[j].Floor;
                    bool up = toFloor > fromFloor || (toFloor == fromFloor && pts[j].Position.Y > p.Position.Y);
                    Maneuver m = p.NextType == EdgeType.Elevator ? Maneuver.Elevator
                               : p.NextType == EdgeType.Escalator ? (up ? Maneuver.EscalatorUp : Maneuver.EscalatorDown)
                               : (up ? Maneuver.StairsUp : Maneuver.StairsDown);
                    list.Add(Make(route, graph, m, i, 0, toFloor));
                    i = j;
                    continue;
                }
                if (i == 0) { i++; continue; } // only a floor change can happen at the very start

                if (pts[i - 1].NextOutdoor != p.NextOutdoor)
                    list.Add(Make(route, graph, p.NextOutdoor ? Maneuver.ExitBuilding : Maneuver.EnterBuilding, i, 0, p.Floor));

                // No turn after arriving by lift/stairs: the user's facing direction is unknown there.
                if (!IsVertical(pts[i - 1].NextType)
                    && TryDirection(pts, i, -1, out var dirIn) && TryDirection(pts, i, +1, out var dirOut))
                {
                    float angle = SignedAngle(dirIn, dirOut);
                    var m = Classify(angle);
                    if (m.HasValue) list.Add(Make(route, graph, m.Value, i, angle, p.Floor));
                }
                i++;
            }

            list.Add(Make(route, graph, Maneuver.Arrive, n - 1, 0, pts[n - 1].Floor));

            for (int k = 0; k < list.Count - 1; k++)
                list[k].DistanceToNext = Math.Max(0, list[k + 1].DistanceFromStart - list[k].DistanceFromStart);
        }

        static RouteInstruction Make(Route route, NavGraph graph, Maneuver m, int pointIndex, float angle, int targetFloor)
        {
            var p = route.Points[pointIndex];
            string landmark = null;
            if (p.Node >= 0 && !string.IsNullOrWhiteSpace(graph.Nodes[p.Node].Name)) landmark = graph.Nodes[p.Node].Name;
            if (m == Maneuver.Arrive && landmark == null && route.GoalNode >= 0) landmark = graph.Nodes[route.GoalNode].DisplayName;

            var ins = new RouteInstruction
            {
                Type = m, PointIndex = pointIndex, DistanceFromStart = route.Cumulative[pointIndex],
                Angle = angle, FromFloor = p.Floor, TargetFloor = targetFloor, Landmark = landmark,
            };
            ins.Text = Describe(ins, graph, p.Node >= 0 ? graph.Nodes[p.Node].Building : null);
            return ins;
        }

        public static string Describe(RouteInstruction ins, NavGraph graph, string building = null)
        {
            string at = ins.Landmark != null ? " at " + ins.Landmark : "";
            string floor = FloorName(graph, ins.TargetFloor);
            switch (ins.Type)
            {
                case Maneuver.Depart: return ins.Landmark != null ? "Start at " + ins.Landmark : "Start walking along the path";
                case Maneuver.SlightLeft: return "Bear left" + at;
                case Maneuver.SlightRight: return "Bear right" + at;
                case Maneuver.Left: return "Turn left" + at;
                case Maneuver.Right: return "Turn right" + at;
                case Maneuver.SharpLeft: return "Turn sharp left" + at;
                case Maneuver.SharpRight: return "Turn sharp right" + at;
                case Maneuver.UTurn: return "Turn around" + at;
                case Maneuver.StairsUp: return ins.FromFloor == ins.TargetFloor ? "Take the steps up" + at : "Take the stairs up to " + floor;
                case Maneuver.StairsDown: return ins.FromFloor == ins.TargetFloor ? "Take the steps down" + at : "Take the stairs down to " + floor;
                case Maneuver.EscalatorUp: return "Take the escalator up to " + floor;
                case Maneuver.EscalatorDown: return "Take the escalator down to " + floor;
                case Maneuver.Elevator: return "Take the elevator to " + floor;
                case Maneuver.EnterBuilding: return string.IsNullOrEmpty(building) ? "Enter the building" + at : "Enter " + building + at;
                case Maneuver.ExitBuilding: return "Exit the building" + at;
                case Maneuver.Arrive: return ins.Landmark != null ? "Arrive at " + ins.Landmark : "You have arrived";
                default: return ins.Type.ToString();
            }
        }

        static string FloorName(NavGraph graph, int level)
        {
            foreach (var f in graph.Floors)
                if (f.level == level && !string.IsNullOrWhiteSpace(f.name)) return f.name;
            return "floor " + level;
        }

        static bool IsVertical(EdgeType t) => t == EdgeType.Stairs || t == EdgeType.Escalator || t == EdgeType.Elevator;

        /// <summary>
        /// Horizontal direction of travel just before (step -1) or after (step +1) point i, skipping
        /// segments too short to give a reliable direction.
        /// </summary>
        static bool TryDirection(List<RoutePoint> pts, int i, int step, out Vector2 dir)
        {
            dir = default;
            int a = i, b = i + step;
            while (b >= 0 && b < pts.Count)
            {
                var pa = pts[a].Position; var pb = pts[b].Position;
                var d = step > 0 ? new Vector2(pb.X - pa.X, pb.Z - pa.Z) : new Vector2(pa.X - pb.X, pa.Z - pb.Z);
                // stop looking through a floor change
                if (pts[a].Floor != pts[b].Floor) return false;
                if (d.Length() >= MinDirectionLength) { dir = Vector2.Normalize(d); return true; }
                b += step;
                // keep 'a' fixed at i so short jogs add up from the corner
            }
            return false;
        }

        /// <summary>Signed angle from a to b in degrees on the XZ plane; positive = clockwise seen from above = right turn.</summary>
        public static float SignedAngle(Vector2 a, Vector2 b)
        {
            // Vectors are (x, z). Heading measured clockwise from +Z: atan2(x, z).
            double ha = Math.Atan2(a.X, a.Y), hb = Math.Atan2(b.X, b.Y);
            double d = (hb - ha) * 180.0 / Math.PI;
            while (d > 180) d -= 360;
            while (d <= -180) d += 360;
            return (float)d;
        }

        static Maneuver? Classify(float angle)
        {
            float a = Math.Abs(angle);
            if (a < StraightThreshold) return null;
            bool right = angle > 0;
            if (a < SlightLimit) return right ? Maneuver.SlightRight : Maneuver.SlightLeft;
            if (a < TurnLimit) return right ? Maneuver.Right : Maneuver.Left;
            if (a < SharpLimit) return right ? Maneuver.SharpRight : Maneuver.SharpLeft;
            return Maneuver.UTurn;
        }
    }
}
