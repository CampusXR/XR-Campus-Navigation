using System;
using UnityEngine;

namespace CampusNav.Routing.Unity
{
    /// <summary>
    /// Draws the campus graph and the active route as Scene-view gizmos, in edit mode and play mode.
    /// For map authoring and debugging only; has no effect in builds.
    /// Edge colours: grey walk, cyan ramp, orange stairs, yellow escalator, magenta elevator,
    /// white door, red closed. Green spheres are destinations. The active route is drawn in blue.
    /// </summary>
    [RequireComponent(typeof(NavigationController))]
    public class NavGraphDebugDraw : MonoBehaviour
    {
        [SerializeField] bool drawGraph = true;
        [SerializeField] bool drawRoute = true;
        [SerializeField] bool onlyFloor;
        [SerializeField] int floor;
        [SerializeField] float nodeSize = 0.3f;

        NavGraph editorGraph;
        string editorGraphSource;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var nav = GetComponent<NavigationController>();
            if (nav == null) return;
            var graph = nav.Graph ?? EditorGraph(nav);
            if (graph == null) return;

            if (drawGraph)
            {
                foreach (var e in graph.Edges)
                {
                    var a = graph.Nodes[e.From]; var b = graph.Nodes[e.To];
                    if (onlyFloor && a.Floor != floor && b.Floor != floor) continue;
                    Gizmos.color = e.Closed ? Color.red : ColorFor(e.Type);
                    Gizmos.DrawLine(nav.MapToWorld(a.Position), nav.MapToWorld(b.Position));
                }
                foreach (var n in graph.Nodes)
                {
                    if (onlyFloor && n.Floor != floor) continue;
                    Gizmos.color = n.IsDestination ? Color.green : new Color(0.8f, 0.8f, 0.8f);
                    Gizmos.DrawSphere(nav.MapToWorld(n.Position), n.IsDestination ? nodeSize * 1.5f : nodeSize * 0.5f);
                }
            }

            if (drawRoute && nav.CurrentRoute != null)
            {
                var pts = nav.GetWorldPathPoints();
                Gizmos.color = new Color(0.1f, 0.4f, 1f);
                var lift = Vector3.up * 0.15f;
                for (int i = 0; i + 1 < pts.Length; i++) Gizmos.DrawLine(pts[i] + lift, pts[i + 1] + lift);
            }
        }
#endif

        NavGraph EditorGraph(NavigationController nav)
        {
            var json = nav.GraphJson;
            if (json == null) return null;
            string text = json.text;
            if (editorGraph != null && string.Equals(text, editorGraphSource, StringComparison.Ordinal)) return editorGraph;
            editorGraphSource = text;
            try { editorGraph = NavGraphBuilder.Build(JsonUtility.FromJson<NavGraphData>(text)); }
            catch (Exception) { editorGraph = null; }
            return editorGraph;
        }

        static Color ColorFor(EdgeType t)
        {
            switch (t)
            {
                case EdgeType.Ramp: return Color.cyan;
                case EdgeType.Stairs: return new Color(1f, 0.55f, 0f);
                case EdgeType.Escalator: return Color.yellow;
                case EdgeType.Elevator: return Color.magenta;
                case EdgeType.Door: return Color.white;
                default: return Color.grey;
            }
        }
    }
}
