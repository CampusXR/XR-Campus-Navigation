using System;

namespace CampusNav.Routing
{
    /// <summary>
    /// How edges are weighted. Costs are in "metres of flat walking", so
    /// cost / <see cref="WalkSpeed"/> gives an estimated time in seconds.
    /// </summary>
    public sealed class RoutingProfile
    {
        /// <summary>Wheelchair / pram / heavy luggage: no stairs, escalators or noWheelchair edges.</summary>
        public bool StepFree;
        /// <summary>Metres per second on flat ground.</summary>
        public float WalkSpeed = 1.3f;
        /// <summary>Per metre of stair length (3D). 2 = a stair metre feels like two flat metres.</summary>
        public float StairsMultiplier = 2.0f;
        public float RampMultiplier = 1.2f;
        public float EscalatorMultiplier = 1.0f;
        /// <summary>Per metre of vertical travel in a lift.</summary>
        public float ElevatorMultiplier = 1.5f;
        /// <summary>Fixed cost for waiting for a lift, in walking metres (30 m ~ 23 s).</summary>
        public float ElevatorWait = 30f;
        /// <summary>Fixed cost for opening a door, in walking metres.</summary>
        public float DoorPenalty = 2f;
        /// <summary>Raise above 1 to prefer indoor routes (rain, heat).</summary>
        public float OutdoorMultiplier = 1.0f;

        public static RoutingProfile Default() => new RoutingProfile();

        public static RoutingProfile Accessible() => new RoutingProfile { StepFree = true, WalkSpeed = 1.0f, ElevatorWait = 20f };

        public RoutingProfile Clone() => (RoutingProfile)MemberwiseClone();

        public bool Allows(NavEdge e)
        {
            if (e.Closed) return false;
            if (StepFree && (e.Type == EdgeType.Stairs || e.Type == EdgeType.Escalator || e.NoWheelchair)) return false;
            return true;
        }

        /// <summary>Full cost of traversing the edge, or false if the edge may not be used.</summary>
        public bool TryGetCost(NavEdge e, out float cost)
        {
            cost = 0;
            if (!Allows(e)) return false;
            float len = e.Length;
            switch (e.Type)
            {
                case EdgeType.Ramp: cost = len * Pos(RampMultiplier); break;
                case EdgeType.Stairs: cost = len * Pos(StairsMultiplier); break;
                case EdgeType.Escalator: cost = len * Pos(EscalatorMultiplier); break;
                case EdgeType.Elevator: cost = Pos(ElevatorWait) + len * Pos(ElevatorMultiplier); break;
                case EdgeType.Door: cost = len + Pos(DoorPenalty); break;
                default: cost = len; break;
            }
            if (e.Outdoor) cost *= Pos(OutdoorMultiplier);
            return true;
        }

        public float SecondsFor(float cost) => cost / Math.Max(0.1f, WalkSpeed);

        static float Pos(float v) => float.IsNaN(v) || v < 0 ? 0 : v;
    }
}
