using System;
using System.Collections.Generic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace CampusNav.Routing.Unity
{
    public enum FloorSource
    {
        /// <summary>Floor is the nearest entry in the map's "floors" table to the user's foot height.</summary>
        FromHeight,
        /// <summary>Floor is set from outside (UI picker, beacon, localization service) via SetManualFloor.</summary>
        Manual,
    }

    /// <summary>
    /// Unity front end for the route-finding module. Loads the campus graph, works out where the
    /// user is on it, requests routes, tracks progress every few frames, reroutes when the user
    /// goes off route, and raises events for the AR rendering / UI layers.
    ///
    /// Contract with the localization team: graph coordinates are local to <see cref="MapRoot"/>.
    /// Whatever localizes the device (image markers, Immersal/area targets, ARCore Geospatial…)
    /// must move/rotate MapRoot so the map lines up with the real world. MapRoot must have scale 1.
    /// </summary>
    [DisallowMultipleComponent]
    public class NavigationController : MonoBehaviour
    {
        [Header("Map")]
        [SerializeField] TextAsset graphJson;
        [Tooltip("Parent of the map. Graph coordinates are local to this transform (scale must be 1). Leave empty if the map is in world space.")]
        [SerializeField] Transform mapRoot;

        [Header("User")]
        [Tooltip("Usually the AR camera.")]
        [SerializeField] Transform userCamera;
        [Tooltip("Camera height above the floor, used to work out the floor from height.")]
        [SerializeField] float eyeHeight = 1.5f;
        [SerializeField] FloorSource floorSource = FloorSource.FromHeight;
        [SerializeField] int manualFloor;

        [Header("Routing")]
        [SerializeField] bool stepFree;
        [SerializeField] RouteAlgorithm algorithm = RouteAlgorithm.AStar;
        [Tooltip("Metres. Users further than this from any path get 'you are off the map'.")]
        [SerializeField] float maxSnapDistance = 25f;

        [Header("Tracking")]
        [SerializeField] float offRouteDistance = 5f;
        [SerializeField] float offRouteDelay = 3f;
        [SerializeField] float arrivalDistance = 3f;
        [Tooltip("Minimum seconds between automatic reroutes.")]
        [SerializeField] float rerouteCooldown = 5f;
        [Tooltip("Seconds between progress updates (0 = every frame).")]
        [SerializeField] float updateInterval = 0.2f;

        // ------------------------------------------------------------------ events
        /// <summary>A new route is active (first route or a reroute). Redraw the path.</summary>
        public event Action<Route> RouteChanged;
        /// <summary>A route request failed. Route.Status / Route.Message say why.</summary>
        public event Action<Route> RouteFailed;
        /// <summary>The upcoming maneuver changed. Second argument: metres until it.</summary>
        public event Action<RouteInstruction, float> InstructionChanged;
        public event Action<RouteProgress> ProgressUpdated;
        public event Action Arrived;
        public event Action NavigationStopped;

        // ------------------------------------------------------------------ state
        public NavGraph Graph { get; private set; }
        public RouteService Router { get; private set; }
        public Route CurrentRoute { get; private set; }
        public RouteProgress Progress { get; private set; }
        public bool IsNavigating { get; private set; }
        public string DestinationId { get; private set; }
        public IReadOnlyList<string> LoadWarnings => loadWarnings;
        public TextAsset GraphJson => graphJson;
        public Transform MapRoot => mapRoot;

        readonly List<string> loadWarnings = new List<string>();
        RouteProgressTracker tracker;
        float lastUpdate = float.NegativeInfinity;
        float lastReroute = float.NegativeInfinity;

        public bool StepFree
        {
            get => stepFree;
            set
            {
                if (stepFree == value) return;
                stepFree = value;
                if (Router != null) Router.Profile.StepFree = value;
                if (IsNavigating) Reroute(true);
            }
        }

        void Awake()
        {
            if (userCamera == null && Camera.main != null) userCamera = Camera.main.transform;
            if (graphJson != null) LoadGraph(graphJson.text);
        }

        /// <summary>Loads a graph from JSON text (see GRAPH_DATA_FORMAT.md). Returns false and logs errors on failure.</summary>
        public bool LoadGraph(string json)
        {
            Stop();
            loadWarnings.Clear();
            try
            {
                var data = JsonUtility.FromJson<NavGraphData>(json);
                Graph = NavGraphBuilder.Build(data, loadWarnings);
            }
            catch (Exception ex)
            {
                Graph = null;
                Router = null;
                Debug.LogError($"[CampusNav] Could not load campus graph: {ex.Message}", this);
                return false;
            }

            foreach (var w in loadWarnings) Debug.LogWarning("[CampusNav] " + w, this);
            var profile = RoutingProfile.Default();
            profile.StepFree = stepFree;
            Router = new RouteService(Graph, profile) { Algorithm = algorithm, MaxSnapDistance = maxSnapDistance };
            Debug.Log($"[CampusNav] Loaded '{Graph.Campus}': {Graph.Nodes.Count} nodes, {Graph.Edges.Count} edges.", this);
            return true;
        }

        public List<NavNode> SearchDestinations(string query, int max = 20) =>
            Graph != null ? Graph.FindDestinations(query, max) : new List<NavNode>();

        /// <summary>Start navigating from the user's current position to a destination node id.</summary>
        public bool NavigateTo(string destinationId)
        {
            if (Router == null) { Fail(Route.Fail(RouteStatus.UnknownGoal, "No campus map loaded.")); return false; }
            if (userCamera == null) { Fail(Route.Fail(RouteStatus.UnknownStart, "No user camera assigned.")); return false; }

            DestinationId = destinationId;
            var route = Router.FindRoute(ToN(UserMapPosition()), CurrentFloor, destinationId);
            if (!route.Found) { Fail(route); return false; }
            Activate(route);
            return true;
        }

        /// <summary>Route between two named places without tracking (preview, "show me the way from A to B").</summary>
        public Route PreviewRoute(string fromId, string toId) => Router?.FindRoute(fromId, toId);

        public void Stop()
        {
            bool was = IsNavigating || CurrentRoute != null;
            IsNavigating = false;
            CurrentRoute = null;
            tracker = null;
            DestinationId = null;
            if (was) NavigationStopped?.Invoke();
        }

        /// <summary>Recompute the route from the user's current position (also called automatically when off route).</summary>
        public bool Reroute(bool force = false)
        {
            if (!IsNavigating || DestinationId == null || Router == null) return false;
            if (!force && Time.time - lastReroute < rerouteCooldown) return false;
            lastReroute = Time.time;
            var route = Router.FindRoute(ToN(UserMapPosition()), CurrentFloor, DestinationId);
            if (!route.Found) { Fail(route); return false; }
            Activate(route);
            return true;
        }

        /// <summary>Close or reopen the path between two nodes (construction, events). Reroutes if navigating.</summary>
        public void SetPathClosed(string nodeA, string nodeB, bool closed)
        {
            if (Graph == null) return;
            if (Graph.SetClosedBetween(nodeA, nodeB, closed) > 0 && IsNavigating) Reroute(true);
        }

        public void SetManualFloor(int floor)
        {
            manualFloor = floor;
            floorSource = FloorSource.Manual;
        }

        public int CurrentFloor
        {
            get
            {
                if (floorSource == FloorSource.Manual || Graph == null || userCamera == null) return manualFloor;
                return Graph.FloorFromHeight(UserMapPosition().y - eyeHeight, manualFloor);
            }
        }

        /// <summary>User position in map space.</summary>
        public Vector3 UserMapPosition()
        {
            if (userCamera == null) return Vector3.zero;
            return mapRoot != null ? mapRoot.InverseTransformPoint(userCamera.position) : userCamera.position;
        }

        /// <summary>Current route in Unity world space, ready for a LineRenderer or arrow placement.</summary>
        public Vector3[] GetWorldPathPoints()
        {
            if (CurrentRoute == null) return Array.Empty<Vector3>();
            var pts = CurrentRoute.Points;
            var result = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) result[i] = MapToWorld(pts[i].Position);
            return result;
        }

        public Vector3 MapToWorld(NVector3 p)
        {
            var v = ToU(p);
            return mapRoot != null ? mapRoot.TransformPoint(v) : v;
        }

        void Update()
        {
            if (!IsNavigating || tracker == null) return;
            if (Time.time - lastUpdate < updateInterval) return;
            lastUpdate = Time.time;

            var p = tracker.Update(ToN(UserMapPosition()), CurrentFloor, Time.time);
            Progress = p;
            ProgressUpdated?.Invoke(p);

            if (p.InstructionChanged && p.NextInstructionIndex >= 0)
                InstructionChanged?.Invoke(CurrentRoute.Instructions[p.NextInstructionIndex], p.DistanceToNextInstruction);

            if (p.JustArrived)
            {
                IsNavigating = false;
                Arrived?.Invoke();
                return;
            }

            if (tracker.State == NavState.OffRoute) Reroute();
        }

        void Activate(Route route)
        {
            CurrentRoute = route;
            tracker = new RouteProgressTracker(route)
            {
                OffRouteDistance = offRouteDistance, OffRouteDelay = offRouteDelay, ArrivalDistance = arrivalDistance
            };
            IsNavigating = true;
            lastUpdate = float.NegativeInfinity;
            RouteChanged?.Invoke(route);
        }

        void Fail(Route route)
        {
            Debug.LogWarning($"[CampusNav] Route failed ({route.Status}): {route.Message}", this);
            RouteFailed?.Invoke(route);
        }

        void OnValidate()
        {
            if (Router != null)
            {
                Router.Algorithm = algorithm;
                Router.MaxSnapDistance = maxSnapDistance;
                Router.Profile.StepFree = stepFree;
            }
        }

        public static NVector3 ToN(Vector3 v) => new NVector3(v.x, v.y, v.z);
        public static Vector3 ToU(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
