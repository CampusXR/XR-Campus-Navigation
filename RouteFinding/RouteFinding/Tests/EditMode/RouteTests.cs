using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CampusNav.Routing;
using NUnit.Framework;
using static CampusNav.Routing.Tests.TestGraphs;

namespace CampusNav.Routing.Tests
{
    public class RouteServiceTests
    {
        static string[] Ids(NavGraph g, Route r) =>
            r.Points.Where(p => p.Node >= 0).Select(p => g.Nodes[p.Node].Id).ToArray();

        [Test]
        public void DefaultProfileUsesStairs()
        {
            var g = Campus();
            var r = new RouteService(g).FindRoute("GATE", "ROBOTICS_201");
            Assert.IsTrue(r.Found, r.Message);
            CollectionAssert.AreEqual(new[] { "GATE", "J1", "J2", "ENG_ENT", "ENG_LOBBY", "ENG_C2", "ENG_STAIR_0", "ENG_STAIR_1", "ENG_F1_HALL", "ROBOTICS_201" }, Ids(g, r));
            Assert.IsTrue(r.Instructions.Any(i => i.Type == Maneuver.StairsUp && i.TargetFloor == 1));
            var arrive = r.Instructions.Last();
            Assert.AreEqual(Maneuver.Arrive, arrive.Type);
            Assert.AreEqual("Arrive at Robotics Lab 201", arrive.Text);
            Assert.AreEqual(r.Length, arrive.DistanceFromStart, 1e-3f);
            Assert.Greater(r.EstimatedSeconds, r.Length / 1.3f - 1f);
        }

        [Test]
        public void StepFreeUsesRampAndLift()
        {
            var g = Campus();
            var svc = new RouteService(g, RoutingProfile.Accessible());
            var r = svc.FindRoute("GATE", "ROBOTICS_201");
            Assert.IsTrue(r.Found, r.Message);
            var ids = Ids(g, r);
            CollectionAssert.Contains(ids, "RAMP_BEND");
            CollectionAssert.Contains(ids, "ENG_LIFT_1");
            Assert.IsFalse(r.Edges.Any(e => g.Edges[e].Type == EdgeType.Stairs));
            Assert.IsTrue(r.Instructions.Any(i => i.Type == Maneuver.Elevator && i.Text == "Take the elevator to First Floor"));
        }

        [Test]
        public void ChangingProfileFieldsInvalidatesCache()
        {
            var g = Campus();
            var svc = new RouteService(g);
            Assert.IsTrue(svc.FindRoute("GATE", "ROBOTICS_201").Edges.Any(e => g.Edges[e].Type == EdgeType.Stairs));
            svc.Profile.StepFree = true;
            Assert.IsFalse(svc.FindRoute("GATE", "ROBOTICS_201").Edges.Any(e => g.Edges[e].Type == EdgeType.Stairs));
        }

        [Test]
        public void BothAlgorithmsAgreeOnEveryPair([Values(false, true)] bool stepFree)
        {
            var g = Campus();
            var profile = stepFree ? RoutingProfile.Accessible() : RoutingProfile.Default();
            var a = new RouteService(g, profile) { Algorithm = RouteAlgorithm.AStar };
            var b = new RouteService(g, profile) { Algorithm = RouteAlgorithm.BidirectionalDijkstra };
            foreach (var s in g.Nodes)
                foreach (var t in g.Nodes)
                {
                    var ra = a.FindRoute(s.Id, t.Id);
                    var rb = b.FindRoute(s.Id, t.Id);
                    Assert.AreEqual(ra.Found, rb.Found, $"{s.Id}->{t.Id}");
                    if (ra.Found) Assert.AreEqual(ra.Cost, rb.Cost, 1e-3f, $"{s.Id}->{t.Id}");
                }
        }

        [Test]
        public void ClosuresBlockAndReopen()
        {
            var g = Campus();
            var svc = new RouteService(g);
            Assert.IsTrue(svc.FindRoute("GATE", "CAF").Found);
            g.SetClosedBetween("J1", "J2", true);
            var r = svc.FindRoute("GATE", "CAF");
            Assert.AreEqual(RouteStatus.NoRoute, r.Status);
            g.SetClosedBetween("J1", "J2", false);
            Assert.IsTrue(svc.FindRoute("GATE", "CAF").Found);
        }

        [Test]
        public void StartsFromSnappedPosition()
        {
            var g = Campus();
            var r = new RouteService(g).FindRoute(new Vector3(2, 1.5f, 20), 0, "LIB_ENT");
            Assert.IsTrue(r.Found, r.Message);
            Assert.AreEqual(-1, r.Points[0].Node);
            Assert.AreEqual(new Vector3(0, 0, 20), r.Points[0].Position);
            Assert.AreEqual(2f, r.StartSnapDistance, 1e-4f);
            CollectionAssert.AreEqual(new[] { "J1", "LIB_ENT" }, Ids(g, r));
            Assert.AreEqual(40f, r.Length, 1e-3f);
            Assert.AreEqual(40f, r.Cost, 1e-3f);
        }

        [Test]
        public void SnappedStartCanGoEitherWay()
        {
            var g = Campus();
            var r = new RouteService(g).FindRoute(new Vector3(0, 0, 5), 0, "GATE");
            Assert.IsTrue(r.Found);
            Assert.AreEqual(5f, r.Length, 1e-3f);
            Assert.AreEqual(2, r.Points.Count);
        }

        [Test]
        public void StartAndGoalOnSameEdgeWalkDirectly()
        {
            var g = Campus();
            var r = new RouteService(g).FindRoute(RouteEndpoint.AtPosition(new Vector3(0, 0, 10), 0),
                                                  RouteEndpoint.AtPosition(new Vector3(0, 0, 30), 0));
            Assert.IsTrue(r.Found, r.Message);
            Assert.AreEqual(2, r.Points.Count);
            Assert.AreEqual(20f, r.Length, 1e-3f);
            Assert.AreEqual(-1, r.GoalNode);
        }

        [Test]
        public void FailuresExplainThemselves()
        {
            var svc = new RouteService(Campus());
            Assert.AreEqual(RouteStatus.UnknownGoal, svc.FindRoute("GATE", "NOPE").Status);
            Assert.AreEqual(RouteStatus.UnknownStart, svc.FindRoute("NOPE", "GATE").Status);
            Assert.AreEqual(RouteStatus.StartOffNetwork, svc.FindRoute(new Vector3(900, 0, 900), 0, "GATE").Status);
        }

        [Test]
        public void OneWayEdgesAreRespected()
        {
            var g = Build(new[] { Node("A", 0, 0, 0), Node("B", 0, 0, 10), Node("C", 0, 0, 20) },
                          new[] { Edge("A", "B", "door", oneWay: true), Edge("B", "C") });
            var svc = new RouteService(g);
            Assert.IsTrue(svc.FindRoute("A", "C").Found);
            Assert.AreEqual(RouteStatus.NoRoute, svc.FindRoute("C", "A").Status);
            // snapped on the one-way edge: can only continue forwards
            Assert.IsTrue(svc.FindRoute(new Vector3(0, 0, 5), 0, "C").Found);
            Assert.AreEqual(RouteStatus.NoRoute, svc.FindRoute(new Vector3(0, 0, 5), 0, "A").Status);
        }

        [Test]
        public void SameNodeRouteHasOnePointAndArrives()
        {
            var r = new RouteService(Campus()).FindRoute("CAF", "CAF");
            Assert.IsTrue(r.Found);
            Assert.AreEqual(1, r.Points.Count);
            Assert.AreEqual(0f, r.Length);
            Assert.AreEqual(Maneuver.Arrive, r.Instructions.Last().Type);
        }
    }

    public class InstructionTests
    {
        // A(0,0) north to B(0,10), east to C(10,10), north to D(10,20), straight on to E(10,30)
        static NavGraph Zigzag() => Build(
            new[] { Node("A", 0, 0, 0), Node("B", 0, 0, 10, name: "Fountain"), Node("C", 10, 0, 10), Node("D", 10, 0, 20), Node("E", 10, 0, 30, name: "Lab", destination: true) },
            new[] { Edge("A", "B"), Edge("B", "C"), Edge("C", "D"), Edge("D", "E") });

        [Test]
        public void LeftAndRightTurns()
        {
            var r = new RouteService(Zigzag()).FindRoute("A", "E");
            var types = r.Instructions.Select(i => i.Type).ToArray();
            CollectionAssert.AreEqual(new[] { Maneuver.Depart, Maneuver.Right, Maneuver.Left, Maneuver.Arrive }, types);
            Assert.AreEqual("Turn right at Fountain", r.Instructions[1].Text);
            Assert.AreEqual(10f, r.Instructions[0].DistanceToNext, 1e-4f);
            Assert.AreEqual(10f, r.Instructions[1].DistanceToNext, 1e-4f);
            Assert.AreEqual(20f, r.Instructions[2].DistanceToNext, 1e-4f);
            Assert.AreEqual("Arrive at Lab", r.Instructions[3].Text);
        }

        [Test]
        public void SignedAngleConvention()
        {
            var north = new System.Numerics.Vector2(0, 1);
            Assert.AreEqual(90f, InstructionBuilder.SignedAngle(north, new System.Numerics.Vector2(1, 0)), 1e-3f);  // east = right
            Assert.AreEqual(-90f, InstructionBuilder.SignedAngle(north, new System.Numerics.Vector2(-1, 0)), 1e-3f); // west = left
            Assert.AreEqual(180f, System.Math.Abs(InstructionBuilder.SignedAngle(north, new System.Numerics.Vector2(0, -1))), 1e-3f);
        }

        [Test]
        public void OutdoorToIndoorAnnouncesEntry()
        {
            var r = new RouteService(Campus()).FindRoute("GATE", "LIB_DESK");
            Assert.IsTrue(r.Instructions.Any(i => i.Type == Maneuver.EnterBuilding && i.Text.StartsWith("Enter Central Library")));
        }

        [Test]
        public void SameFloorStepsSaySteps()
        {
            var r = new RouteService(Campus()).FindRoute("J2", "ENG_LOBBY");
            Assert.IsTrue(r.Instructions.Any(i => i.Type == Maneuver.StairsUp && i.Text.StartsWith("Take the steps up")));
        }
    }

    public class RouteProgressTrackerTests
    {
        static Route Straight(out NavGraph g)
        {
            g = Build(new[] { Node("A", 0, 0, 0), Node("B", 0, 0, 50, name: "Corner"), Node("C", 50, 0, 50, name: "End", destination: true) },
                      new[] { Edge("A", "B"), Edge("B", "C") });
            return new RouteService(g).FindRoute("A", "C");
        }

        [Test]
        public void ReportsProgressAndNextInstruction()
        {
            var t = new RouteProgressTracker(Straight(out _));
            var p = t.Update(new Vector3(1, 1.5f, 20), 0, 0);
            Assert.AreEqual(NavState.OnRoute, p.State);
            Assert.AreEqual(20f, p.DistanceAlong, 1e-3f);
            Assert.AreEqual(80f, p.DistanceRemaining, 1e-3f);
            Assert.AreEqual(1f, p.DistanceFromRoute, 1e-3f);
            Assert.AreEqual(Maneuver.Right, t.Route.Instructions[p.NextInstructionIndex].Type);
            Assert.AreEqual(30f, p.DistanceToNextInstruction, 1e-3f);
            Assert.IsTrue(p.InstructionChanged);

            p = t.Update(new Vector3(0, 0, 25), 0, 1);
            Assert.IsFalse(p.InstructionChanged);

            p = t.Update(new Vector3(10, 0, 50), 0, 2); // past the corner
            Assert.AreEqual(Maneuver.Arrive, t.Route.Instructions[p.NextInstructionIndex].Type);
            Assert.IsTrue(p.InstructionChanged);
        }

        [Test]
        public void OffRouteNeedsDistanceAndTime()
        {
            var t = new RouteProgressTracker(Straight(out _)) { OffRouteDistance = 5, OffRouteDelay = 3 };
            Assert.IsFalse(t.Update(new Vector3(12, 0, 20), 0, 10).ShouldReroute);
            Assert.IsFalse(t.Update(new Vector3(12, 0, 21), 0, 12).ShouldReroute);
            var p = t.Update(new Vector3(12, 0, 22), 0, 13.1f);
            Assert.IsTrue(p.ShouldReroute);
            Assert.AreEqual(NavState.OffRoute, p.State);
            Assert.IsFalse(t.Update(new Vector3(12, 0, 22), 0, 14).ShouldReroute); // fires once
            Assert.AreEqual(NavState.OnRoute, t.Update(new Vector3(1, 0, 22), 0, 15).State);
        }

        [Test]
        public void JitterDoesNotTriggerReroute()
        {
            var t = new RouteProgressTracker(Straight(out _)) { OffRouteDistance = 5, OffRouteDelay = 3 };
            for (int i = 0; i < 20; i++)
            {
                float x = i % 2 == 0 ? 8 : 1; // spikes off route every other update
                Assert.IsFalse(t.Update(new Vector3(x, 0, 10 + i), 0, i * 0.5f).ShouldReroute);
            }
        }

        [Test]
        public void WrongFloorCountsAsOffRoute()
        {
            var t = new RouteProgressTracker(Straight(out _)) { OffRouteDelay = 1 };
            t.Update(new Vector3(0, 4, 10), 1, 0);
            Assert.IsTrue(t.Update(new Vector3(0, 4, 10), 1, 1.5f).ShouldReroute);
        }

        [Test]
        public void ArrivesNearTheEnd()
        {
            var t = new RouteProgressTracker(Straight(out _));
            t.Update(new Vector3(0, 0, 10), 0, 0);
            var p = t.Update(new Vector3(48, 0, 50), 0, 60);
            Assert.IsTrue(p.JustArrived);
            Assert.AreEqual(NavState.Arrived, t.State);
            Assert.IsFalse(t.Update(new Vector3(49, 0, 50), 0, 61).JustArrived);
        }
    }
}
