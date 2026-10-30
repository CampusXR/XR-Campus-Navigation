# Campus graph data format (v1)

The route finder reads one JSON file describing the walkable network: **nodes** (points people walk between) and **edges** (the walkable connections). Example: `Samples/campus_graph.sample.json`.

## Coordinates

- Metres, Unity convention: **+X right/east, +Y up, +Z forward/north** (or whatever the map's +Z is, see `origin.headingDeg`).
- Coordinates are *map space*: local to the `MapRoot` transform in the scene. The localization system moves MapRoot so the map lines up with the real world.
- `y` is the height of the walking surface (floor level, not eye level).
- Alternatively give `lat`/`lon` per node and an `origin`; `x`/`z` are then computed.

## Top level

```json
{
  "version": 1,
  "campus": "Name shown in logs",
  "origin": { "latitude": 0, "longitude": 0, "headingDeg": 0 },
  "floors": [ { "level": 0, "elevation": 0, "name": "Ground Floor" } ],
  "nodes": [ ... ],
  "edges": [ ... ]
}
```

| Field | Required | Meaning |
|-------|----------|---------|
| `origin` | only for lat/lon nodes or GPS | WGS84 position of map (0,0,0) and compass bearing of map +Z. |
| `floors` | recommended | Used to work out the user's floor from height. `elevation` is map-space Y of that floor. |

## Nodes

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `id` | string | yes | Unique and **stable**: UI, analytics and saved favourites refer to it. Use readable ids (`LIB_ENT`, `ENG_201`). |
| `name` | string | for destinations | Shown in search and instructions ("Turn left at *Library Entrance*"). Leave empty for plain junctions. |
| `x`,`y`,`z` | number | yes* | Map-space metres. |
| `lat`,`lon` | number | alternative* | If either is non-zero, x/z are computed from `origin`. |
| `floor` | int | yes (default 0) | 0 = ground. Outdoor nodes are floor 0. |
| `building` | string | no | Used in "Enter *Engineering Block*" and search. |
| `tags` | string[] | no | Search keywords: `washroom`, `food`, `room 201`… |
| `destination` | bool | no | true if users may pick it as a destination. |

Where to put nodes: at every junction, every corner of a path, every door, the top and bottom of every staircase/ramp/escalator, every lift stop (one node per floor), and every destination. Straight corridors need no intermediate nodes, but keep edges under ~100 m (the loader warns above 150 m).

## Edges

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `from`, `to` | node id | — | |
| `type` | string | `walk` | `walk`, `ramp`, `stairs`, `escalator`, `elevator`, `door`. |
| `oneWay` | bool | false | Only `from → to` (exit-only doors, turnstiles, escalators). |
| `length` | number | straight-line | Set it for curved paths. Values shorter than straight-line are replaced (and warned about). |
| `outdoor` | bool | false | Drives "Enter/Exit building" instructions and the rain penalty. |
| `noWheelchair` | bool | false | Not stairs but still not step-free (kerb, narrow gate). |
| `closed` | bool | false | Closed at load. Can also be changed at runtime. |

Rules the loader checks:

- **Errors** (file rejected, all listed at once): duplicate/missing ids, edges to unknown nodes, self-loops, unknown types, negative lengths, lat/lon without origin.
- **Warnings** (loads, logged): walk edges that change floor, single-floor elevators, duplicate edges, very long edges, nameless destinations, disconnected parts of the map.

Stairs between floors: one `stairs` edge from the bottom node (floor N) to the top node (floor N+1). A lift: one node per floor stop, joined by `elevator` edges between consecutive floors. A few outdoor steps on the same floor: a `stairs` edge whose ends are both floor 0 (instructions say "Take the steps up").

## Authoring tips

1. Start from a scaled floor plan or satellite image in Unity, place empty GameObjects, then export, or draw in a spreadsheet and convert. (A small editor export tool is a good follow-up task.)
2. Add `NavigationController` + `NavGraphDebugDraw` to see the graph as coloured lines in the Scene view.
3. Keep `SampleDataTests` pointing at the real file so CI catches broken edits.
