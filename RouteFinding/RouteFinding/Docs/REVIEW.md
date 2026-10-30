# Review: BiDijkstra.cs, BiDijkstraTests.cs, BiDijkstra_Summary.md

Reviewed 20 Oct 2026. Overall the algorithm is right: the stopping rule (`topF + topB >= best`) is the correct one, stale entries are skipped, and `best`/`meet` are updated on every relaxation, so the route is shortest, not just the first meeting. The problems are around it.

## Must fix

| # | File | Problem | Fix (in this delivery) |
|---|------|---------|------------------------|
| 1 | BiDijkstra.cs, tests | **Does not compile in Unity.** `System.Collections.Generic.PriorityQueue<,>` is a .NET 6+ type. Unity (including Unity 6) only offers the .NET Standard 2.1 and .NET Framework 4.8 API profiles, neither of which has it. The summary's "Unity 2021.2+" note is wrong. | Own binary heap, `Runtime/Core/MinHeap.cs`. |
| 2 | BiDijkstraTests.cs | **`MatchesDijkstraOnRandomGraphs` can fail on a correct path.** 60 random edges on 30 nodes produce parallel edges between the same pair (several per graph). The search uses the cheapest one; `Cost()` uses `Find()`, which returns the *first* one. Any route through a pair whose first edge is the heavier one is reported as too expensive. | `Cost()` now takes the cheapest parallel edge. |
| 3 | BiDijkstra.cs | **One-way edges give wrong answers silently.** The backward search walks `adj` forwards. The summary says to "build a reverse list" but the API has nowhere to pass it. | `CostGraph` stores forward and reverse adjacency; the backward search uses the reverse. The old `Path(adj, start, goal)` signature still works and is now correct for directed input. |

## Should fix

| # | Problem | Fix |
|---|---------|-----|
| 4 | No input checks: an out-of-range `start`/`goal` throws `IndexOutOfRangeException`; a negative or NaN weight silently breaks Dijkstra. | `ArgumentOutOfRangeException` / `ArgumentException` with a message. |
| 5 | Only node-to-node routing. The user is never standing exactly on a node. | Searches accept *seeds* (node + entry cost), so a route can start or end part-way along an edge. `RouteService` snaps positions to the nearest edge and seeds both ends. |
| 6 | Returns node ids only. Callers can't tell which edge was used (stairs vs lift between the same two places) and can't build instructions. | `PathResult` has `Nodes`, `Edges`, `Cost`, `Expanded`. |
| 7 | `FirstMeetingIsNotShortest` catches stopping at the first *touch*, but not the more common textbook mistake of stopping when the first node is *settled by both* searches (traced by hand: that version returns 0-3-2 on this graph and passes). | Kept, plus `FirstNodeSettledByBothSidesIsNotShortest`, which that mistake fails (it returns 0-1-5, cost 6, instead of 0-2-3-5, cost 5.5). |
| 8 | Class is in the global namespace. | `CampusNav.Routing`. |
| 9 | Summary says the code was "not compiled or run". | Still true for this delivery (no .NET SDK in my environment either). Run the EditMode tests in Unity before merging; see README "Running the tests". |

## Fine as is

- Choosing the side with the smaller queue is a reasonable balancing rule.
- Allocating `dist`/`prev` per call is fine at campus size (thousands of nodes, sub-millisecond). If profiling ever shows GC spikes, pool the arrays.
- Random cross-check against plain Dijkstra is the right kind of test; kept and extended (directed graphs, A*).
