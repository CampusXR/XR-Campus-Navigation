using System;
using System.Numerics;

namespace CampusNav.Routing
{
    public enum NavState { OnRoute, OffRoute, Arrived }

    public struct RouteProgress
    {
        public NavState State;
        /// <summary>Metres travelled along the route polyline.</summary>
        public float DistanceAlong;
        public float DistanceRemaining;
        /// <summary>Horizontal distance from the user to the route (infinity if the user's floor is not on the route).</summary>
        public float DistanceFromRoute;
        /// <summary>Closest point on the route, map space. Useful for drawing the line from the user.</summary>
        public Vector3 PointOnRoute;
        public int SegmentIndex;
        /// <summary>Index into Route.Instructions of the next maneuver ahead, or -1 once past the last one.</summary>
        public int NextInstructionIndex;
        public float DistanceToNextInstruction;
        /// <summary>True on the single update where the next instruction changed.</summary>
        public bool InstructionChanged;
        /// <summary>True on the single update where the user was confirmed off route. Request a new route.</summary>
        public bool ShouldReroute;
        /// <summary>True on the single update where the user arrived.</summary>
        public bool JustArrived;
        public float EstimatedSecondsRemaining;
    }

    /// <summary>
    /// Follows the user along a route: progress, next instruction, off-route detection (with a
    /// distance and a time threshold so AR/GPS jitter doesn't trigger reroutes) and arrival.
    /// Feed it a position every frame or every few frames.
    /// </summary>
    public sealed class RouteProgressTracker
    {
        /// <summary>Further than this from the route counts as "possibly off route" (metres).</summary>
        public float OffRouteDistance = 5f;
        /// <summary>...for at least this long (seconds) before <see cref="RouteProgress.ShouldReroute"/> fires.</summary>
        public float OffRouteDelay = 3f;
        /// <summary>Within this distance of the end (and on the goal floor) counts as arrived.</summary>
        public float ArrivalDistance = 3f;
        /// <summary>How strongly to prefer staying near the last known progress when the route passes the same place twice.</summary>
        public float JumpPenaltyPerMetre = 0.05f;

        public Route Route { get; }
        public NavState State { get; private set; } = NavState.OnRoute;

        float lastAlong;
        float offSince = -1;
        int lastInstruction = -2;
        readonly float secondsPerMetre;

        public RouteProgressTracker(Route route)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            if (!route.Found || route.Points.Count == 0) throw new ArgumentException("Route has no points.", nameof(route));
            secondsPerMetre = route.Length > 0.01f ? route.EstimatedSeconds / route.Length : 1f / 1.3f;
        }

        public RouteProgress Update(Vector3 position, int floor, float timeSeconds)
        {
            var pts = Route.Points;
            var cum = Route.Cumulative;
            var res = new RouteProgress { State = State, DistanceFromRoute = float.PositiveInfinity, SegmentIndex = -1, NextInstructionIndex = -1 };

            if (State == NavState.Arrived)
            {
                res.DistanceAlong = Route.Length;
                res.PointOnRoute = pts[pts.Count - 1].Position;
                res.DistanceFromRoute = EdgeSnapper.DistXZ(position, res.PointOnRoute);
                return res;
            }

            // --- project onto the route
            float bestScore = float.PositiveInfinity;
            if (pts.Count == 1)
            {
                if (pts[0].Floor == floor)
                {
                    res.DistanceFromRoute = EdgeSnapper.DistXZ(position, pts[0].Position);
                    res.PointOnRoute = pts[0].Position;
                    res.SegmentIndex = 0;
                }
            }
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                var a = pts[k]; var b = pts[k + 1];
                if (a.Floor != floor && b.Floor != floor) continue;
                float t;
                float dx = b.Position.X - a.Position.X, dz = b.Position.Z - a.Position.Z;
                if (dx * dx + dz * dz < 1e-4f) t = b.Floor == floor ? 1f : 0f; // lift shaft / vertical segment
                else t = EdgeSnapper.ProjectXZ(position, a.Position, b.Position);
                var p = Vector3.Lerp(a.Position, b.Position, t);
                float d = EdgeSnapper.DistXZ(position, p);
                float along = cum[k] + t * (cum[k + 1] - cum[k]);
                float score = d + JumpPenaltyPerMetre * Math.Max(0, Math.Abs(along - lastAlong) - 10f);
                if (score < bestScore)
                {
                    bestScore = score;
                    res.DistanceFromRoute = d;
                    res.PointOnRoute = p;
                    res.SegmentIndex = k;
                    res.DistanceAlong = along;
                }
            }
            if (res.SegmentIndex >= 0) lastAlong = res.DistanceAlong;
            else res.DistanceAlong = lastAlong;

            res.DistanceRemaining = Math.Max(0, Route.Length - res.DistanceAlong);
            res.EstimatedSecondsRemaining = res.DistanceRemaining * secondsPerMetre;

            // --- off route?
            if (res.DistanceFromRoute > OffRouteDistance)
            {
                if (offSince < 0) offSince = timeSeconds;
                if (State != NavState.OffRoute && timeSeconds - offSince >= OffRouteDelay)
                {
                    State = NavState.OffRoute;
                    res.ShouldReroute = true;
                }
            }
            else
            {
                offSince = -1;
                State = NavState.OnRoute;
            }

            // --- arrived?
            var goal = pts[pts.Count - 1];
            if (State == NavState.OnRoute && floor == goal.Floor && res.DistanceRemaining <= ArrivalDistance
                && EdgeSnapper.DistXZ(position, goal.Position) <= ArrivalDistance + 1f)
            {
                State = NavState.Arrived;
                res.JustArrived = true;
            }

            // --- next instruction
            var ins = Route.Instructions;
            int next = -1;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].Type == Maneuver.Depart) continue;
                if (ins[i].DistanceFromStart > res.DistanceAlong + 0.5f) { next = i; break; }
            }
            if (next < 0 && State != NavState.Arrived && ins.Count > 0) next = ins.Count - 1; // within 0.5 m of Arrive
            res.NextInstructionIndex = State == NavState.Arrived ? -1 : next;
            res.DistanceToNextInstruction = res.NextInstructionIndex >= 0
                ? Math.Max(0, ins[res.NextInstructionIndex].DistanceFromStart - res.DistanceAlong) : 0;
            res.InstructionChanged = res.NextInstructionIndex != lastInstruction;
            lastInstruction = res.NextInstructionIndex;

            res.State = State;
            return res;
        }
    }
}
