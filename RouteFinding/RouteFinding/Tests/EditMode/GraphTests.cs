using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CampusNav.Routing;
using NUnit.Framework;
using static CampusNav.Routing.Tests.TestGraphs;

namespace CampusNav.Routing.Tests
{
    public class NavGraphBuilderTests
    {
        [Test]
        public void SampleCampusLoadsCleanly()
        {
            var warnings = new List<string>();
            var g = Campus(warnings);
            Assert.AreEqual(21, g.Nodes.Count);
            Assert.AreEqual(22, g.Edges.Count);
            CollectionAssert.IsEmpty(warnings, string.Join("\n", warnings));
        }

        [Test]
        public void ReportsAllHardErrorsAtOnce()
        {
            var ex = Assert.Throws<NavGraphException>(() => Build(
                new[] { Node("A", 0, 0, 0), Node("A", 1, 0, 0), Node("B", 2, 0, 0) },
                new[] { Edge("A", "X"), Edge("B", "B"), Edge("A", "B", "teleporter") }));
            Assert.AreEqual(4, ex.Errors.Count, string.Join("\n", ex.Errors));
        }

        [Test]
        public void AutoLengthAndTooShortLengthFixed()
        {
            var warnings = new List<string>();
            var g = Build(new[] { Node("A", 0, 0, 0), Node("B", 3, 0, 4), Node("C", 3, 0, 14) },
                          new[] { Edge("A", "B"), Edge("B", "C", length: 2) }, warnings);
            Assert.AreEqual(5f, g.Edges[0].Length, 1e-4f);
            Assert.AreEqual(10f, g.Edges[1].Length, 1e-4f);
            Assert.IsTrue(warnings.Any(w => w.Contains("shorter than the straight-line")));
        }

        [Test]
        public void WarnsAboutDisconnectedParts()
        {
            var warnings = new List<string>();
            Build(new[] { Node("A", 0, 0, 0), Node("B", 1, 0, 0), Node("C", 5, 0, 0) }, new[] { Edge("A", "B") }, warnings);
            Assert.IsTrue(warnings.Any(w => w.Contains("disconnected") && w.Contains("C")));
        }

        [Test]
        public void WarnsAboutFloorChangeOnWalkEdge()
        {
            var warnings = new List<string>();
            Build(new[] { Node("A", 0, 0, 0, 0), Node("B", 0, 4, 5, 1) }, new[] { Edge("A", "B") }, warnings);
            Assert.IsTrue(warnings.Any(w => w.Contains("joins floor")));
        }

        [Test]
        public void LatLonNodesNeedOrigin()
        {
            var data = Data(new[] { new NavNodeData { id = "A", lat = 13.0, lon = 80.2 } }, new NavEdgeData[0]);
            Assert.Throws<NavGraphException>(() => NavGraphBuilder.Build(data));

            data.origin = new GeoOrigin { latitude = 13.0, longitude = 80.2 };
            data.nodes = new[] { new NavNodeData { id = "A", lat = 13.0009, lon = 80.2 } };
            var g = NavGraphBuilder.Build(data);
            Assert.AreEqual(100f, g.Nodes[0].Position.Z, 1f); // 0.0009 deg of latitude ~ 100 m north
        }

        [Test]
        public void DestinationSearchRanksNameMatchesFirst()
        {
            var g = Campus();
            var hits = g.FindDestinations("wash");
            Assert.AreEqual(2, hits.Count);
            Assert.IsTrue(hits.All(h => h.Name.Contains("Washroom")));

            Assert.AreEqual("LIB_DESK", g.FindDestinations("books")[0].Id);           // tag
            Assert.AreEqual("ROBOTICS_201", g.FindDestinations("Robotics Lab 201")[0].Id); // exact
            Assert.IsEmpty(g.FindDestinations("J1"));                                 // junctions aren't destinations
        }

        [Test]
        public void ClosingAnEdgeBumpsVersion()
        {
            var g = Campus();
            int v = g.Version;
            Assert.AreEqual(1, g.SetClosedBetween("J1", "J2", true));
            Assert.Greater(g.Version, v);
            Assert.AreEqual(0, g.SetClosedBetween("J1", "J2", true)); // already closed
        }

        [Test]
        public void FloorFromHeightUsesNearestFloor()
        {
            var g = Campus();
            Assert.AreEqual(0, g.FloorFromHeight(0.4f));
            Assert.AreEqual(1, g.FloorFromHeight(4.1f));
        }
    }

    public class EdgeSnapperTests
    {
        [Test]
        public void SnapsToNearestEdgeOnTheFloor()
        {
            var g = Campus();
            var s = new EdgeSnapper(g).Snap(new Vector3(2, 1.5f, 20), 0, 25);
            Assert.IsTrue(s.Found);
            var e = g.Edges[s.Edge];
            Assert.AreEqual("GATE", g.Nodes[e.From].Id);
            Assert.AreEqual("J1", g.Nodes[e.To].Id);
            Assert.AreEqual(0.5f, s.T, 1e-4f);
            Assert.AreEqual(2f, s.Distance, 1e-4f);
        }

        [Test]
        public void RespectsFloorAndMaxDistance()
        {
            var g = Campus();
            var snapper = new EdgeSnapper(g);
            Assert.IsFalse(snapper.Snap(new Vector3(2, 0, 20), 1, 25).Found);   // nothing on floor 1 there
            Assert.IsFalse(snapper.Snap(new Vector3(500, 0, 500), 0, 25).Found);
        }

        [Test]
        public void FilterSkipsForbiddenEdges()
        {
            var g = Campus();
            var stepFree = RoutingProfile.Accessible();
            // middle of the outdoor steps J2 -> ENG_ENT
            var s = new EdgeSnapper(g).Snap(new Vector3(40, 0, 50), 0, 25, stepFree.Allows);
            Assert.IsTrue(s.Found);
            Assert.AreNotEqual(EdgeType.Stairs, g.Edges[s.Edge].Type);
        }
    }

    public class GeoProjectionTests
    {
        [Test]
        public void RoundTrips([Values(0f, 37f, 90f, 200f)] float heading)
        {
            var geo = new GeoProjection(new GeoOrigin { latitude = 12.97, longitude = 80.22, headingDeg = heading });
            var local = geo.ToLocal(12.9712, 80.2231);
            geo.ToGeo(local, out double lat, out double lon);
            Assert.AreEqual(12.9712, lat, 1e-7);
            Assert.AreEqual(80.2231, lon, 1e-7);
        }

        [Test]
        public void HeadingRotatesMapFrame()
        {
            // map +Z points east: a point due east of the origin is straight ahead (+Z)
            var geo = new GeoProjection(new GeoOrigin { latitude = 0, longitude = 0, headingDeg = 90 });
            var p = geo.ToLocal(0, 0.001);
            Assert.AreEqual(0f, p.X, 0.01f);
            Assert.AreEqual(111.3f, p.Z, 0.5f);
        }
    }
}
