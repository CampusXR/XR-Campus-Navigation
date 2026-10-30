# CampusNav Route Finding

Route-finding module for the XR campus navigation app (Unity + AR Foundation). Takes the user's position and a destination, returns the path to draw, turn-by-turn instructions, distance and ETA, and keeps track of the user as they walk (progress, off-route, rerouting, arrival).

Replaces the original `BiDijkstra.cs` / `BiDijkstraTests.cs`. Review of those files: `Docs/REVIEW.md`.

## Folder layout (drop the whole folder into `Assets/`)

```
RouteFinding/
├─ Runtime/                         asmdef CampusNav.Routing (no UnityEngine dependency)
│  ├─ Core/       MinHeap, CostGraph (+Seed, PathResult), BiDijkstra, AStar
│  ├─ Graph/      NavGraphData (JSON DTOs), NavGraph, NavGraphBuilder (validation), RoutingProfile
│  ├─ Spatial/    EdgeSnapper (position -> nearest walkable edge, grid index)
│  ├─ Geo/        GeoProjection (lat/lon <-> map metres)
│  ├─ Routing/    RouteService (entry point), Route, InstructionBuilder, RouteProgressTracker
│  └─ Unity/                        asmdef CampusNav.Routing.Unity
│     ├─ NavigationController.cs    MonoBehaviour: load map, navigate, track, reroute, events
│     └─ NavGraphDebugDraw.cs       Scene-view gizmos for the graph and route
├─ Tests/EditMode/                  asmdef CampusNav.Routing.Tests (NUnit, Unity Test Framework)
├─ Samples/campus_graph.sample.json 21-node demo campus (2 floors, stairs, lift, ramp)
└─ Docs/  REVIEW.md, GRAPH_DATA_FORMAT.md
```

Requirements: Unity 2021.2 or newer (C# 9, .NET Standard 2.1), Unity Test Framework package for the tests. No other packages.

## How it fits together

```
JSON map ─► NavGraphBuilder ─► NavGraph ──┐
                                          ▼
user position + floor ─► RouteService ─ EdgeSnapper (snap start/goal onto nearest edge)
                           │           ─ RoutingProfile (edge costs, step-free rules)
                           │           ─ AStar  or  BiDijkstra  (on a cached CostGraph)
                           ▼
                         Route (polyline, length, ETA, instructions)
                           │
every 0.2 s ─► RouteProgressTracker ─► progress / next instruction / off-route / arrived
                           │
NavigationController ─ events ─► AR path renderer, arrows, UI, voice
```

Both search algorithms return identical costs (tested on every pair of the sample campus and on hundreds of random graphs). A* is the default: it expands fewer nodes. Bidirectional Dijkstra is kept and selectable (`RouteService.Algorithm`).

## Quick start in Unity

1. Copy `RouteFinding/` into `Assets/`.
2. Create an empty GameObject `MapRoot`. Whatever does localization (image marker, VPS, Geospatial…) must position and rotate this so map coordinates line up with the world. Scale must stay 1.
3. Add **NavigationController** to any GameObject. Assign: Graph Json = `campus_graph.sample`, Map Root, User Camera = AR camera. Optionally add **NavGraphDebugDraw** to see the graph in the Scene view.
4. From UI code:

```csharp
using CampusNav.Routing;
using CampusNav.Routing.Unity;

nav.RouteChanged += route => pathRenderer.SetPositions(nav.GetWorldPathPoints());
nav.InstructionChanged += (ins, metres) => hud.Show($"In {metres:0} m: {ins.Text}");
nav.Arrived += () => hud.Show("You have arrived");
nav.RouteFailed += route => hud.ShowError(route.Message);

var hits = nav.SearchDestinations("wash");   // search box
nav.NavigateTo(hits[0].Id);                   // start
nav.StepFree = true;                          // accessibility toggle, reroutes automatically
nav.SetPathClosed("J1", "J2", true);          // live closure, reroutes automatically
```

Without Unity (tools, server, tests):

```csharp
var graph = NavGraphBuilder.Build(data, warnings);
var router = new RouteService(graph, RoutingProfile.Accessible());
Route r = router.FindRoute(new Vector3(2, 1.5f, 20), 0, "LIB_DESK");  // position, floor, destination id
foreach (var i in r.Instructions) Console.WriteLine(i);   // [0m] Start ..., [20m] Turn left at ...
var tracker = new RouteProgressTracker(r);
RouteProgress p = tracker.Update(userPos, userFloor, timeSeconds);
```

## What each part does

| Piece | Responsibility | Key behaviour |
|-------|----------------|---------------|
| `MinHeap` | Priority queue | Replaces `PriorityQueue`, which Unity doesn't have. |
| `CostGraph` | Directed arcs, forward + reverse | Rejects negative/NaN costs. Computes the A* heuristic scale from the data so the heuristic is always admissible. |
| `BiDijkstra`, `AStar` | Shortest path | Multi-source/multi-target with entry costs (*seeds*), so routes start part-way along an edge. Return nodes, edges, cost, nodes expanded. |
| `NavGraphBuilder` | Load + validate map | All errors reported at once; warnings for suspicious data (disconnected areas, floor changes on walk edges…). |
| `RoutingProfile` | Edge costs | Stairs ×2, ramp ×1.2, lift 30 m wait + ×1.5, door +2 m, optional outdoor penalty. Step-free removes stairs/escalators/`noWheelchair`. Costs are in "walking metres", so ETA = cost / speed. |
| `EdgeSnapper` | Position → path | Horizontal distance, same floor only, respects the profile (a wheelchair user is never snapped onto stairs), 25 m limit. |
| `RouteService` | Entry point | Node or position endpoints, one-way aware, same-edge shortcut, cost graph cached and rebuilt on closures or profile changes, clear failure statuses. |
| `InstructionBuilder` | Turn-by-turn | Slight/normal/sharp/U-turns (straight under 25° not announced), stairs/escalator up/down, lift to floor, enter/exit building, arrive. Landmarks from node names. |
| `RouteProgressTracker` | While walking | Distance along/remaining, next instruction and distance to it, off-route only after 5 m for 3 s (ignores jitter), fires reroute once, arrival within 3 m on the goal floor. |
| `NavigationController` | Unity glue | Camera → map space via MapRoot, floor from height or set manually, throttled updates, reroute cooldown, C# events. |

## Running the tests

Window → General → Test Runner → EditMode → Run All. 50+ tests covering heap, both searches (random cross-checks against plain Dijkstra, directed graphs, seeds), loader validation, snapping, geo projection, routing (stairs vs lift, step-free, closures, one-way, snapped starts), instructions and tracking.

**These files have not been compiled yet.** My environment had no .NET SDK, so I reviewed them by hand against C# 9 / .NET Standard 2.1. Run the tests in Unity first; if anything fails to compile, the error message plus file name is all I need to fix it.

## What other parts of the app must provide

| Needed from | What | Why |
|-------------|------|-----|
| Localization | Pose of `MapRoot` in AR world space (and keep it updated as tracking refines) | Routing works in map space. Without alignment the drawn path won't match the corridors. |
| Localization / UI | Current floor, if height-based detection isn't reliable (lifts, AR drift) | Call `SetManualFloor`. |
| Map data | The real campus JSON (see `Docs/GRAPH_DATA_FORMAT.md`) | The sample is invented. |
| AR rendering | Draw `GetWorldPathPoints()`, place arrows at `Route.Instructions[i].PointIndex` | Rendering is outside this module. |
| UI | Destination search box, accessibility toggle, instruction banner | All exposed by `NavigationController`. |

## Not included / next steps

- **Real campus data** and an editor tool to author/export it (the biggest remaining job).
- Localization itself (markers, VPS, ARCore Geospatial).
- Path rendering, arrows, voice.
- Multi-stop routes, "nearest washroom" (easy: target seeds on every node with the tag), time-dependent closures (building hours).
