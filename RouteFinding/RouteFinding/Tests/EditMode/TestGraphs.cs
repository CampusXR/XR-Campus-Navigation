using System.Collections.Generic;
using CampusNav.Routing;

namespace CampusNav.Routing.Tests
{
    /// <summary>Graphs shared by the tests. Campus() mirrors Samples/campus_graph.sample.json.</summary>
    public static class TestGraphs
    {
        public static NavNodeData Node(string id, float x, float y, float z, int floor = 0, string name = null,
                                       bool destination = false, string building = null, string[] tags = null)
            => new NavNodeData { id = id, x = x, y = y, z = z, floor = floor, name = name, destination = destination, building = building, tags = tags };

        public static NavEdgeData Edge(string from, string to, string type = "walk", bool outdoor = false, bool oneWay = false, float length = 0)
            => new NavEdgeData { from = from, to = to, type = type, outdoor = outdoor, oneWay = oneWay, length = length };

        public static NavGraphData Data(NavNodeData[] nodes, NavEdgeData[] edges, FloorData[] floors = null)
            => new NavGraphData { campus = "test", nodes = nodes, edges = edges, floors = floors };

        public static NavGraph Build(NavNodeData[] nodes, NavEdgeData[] edges, List<string> warnings = null)
            => NavGraphBuilder.Build(Data(nodes, edges), warnings);

        public static NavGraphData CampusData() => new NavGraphData
        {
            campus = "Sample Campus",
            floors = new[]
            {
                new FloorData { level = 0, elevation = 0f, name = "Ground Floor" },
                new FloorData { level = 1, elevation = 4.6f, name = "First Floor" },
            },
            nodes = new[]
            {
                Node("GATE", 0f, 0f, 0f, 0, "Main Gate", true, null, new[] { "entrance", "security" }),
                Node("J1", 0f, 0f, 40f, 0, null, false, null, null),
                Node("J2", 40f, 0f, 40f, 0, null, false, null, null),
                Node("J3", 0f, 0f, 90f, 0, null, false, null, null),
                Node("HOSTEL", 0f, 0f, 130f, 0, "Hostel Block", true, "Hostel", new[] { "residence" }),
                Node("LIB_ENT", -20f, 0f, 40f, 0, "Library Entrance", true, "Central Library", null),
                Node("LIB_DESK", -35f, 0f, 40f, 0, "Library Help Desk", true, "Central Library", new[] { "library", "books" }),
                Node("LIB_WC", -35f, 0f, 52f, 0, "Library Washroom", true, "Central Library", new[] { "washroom", "toilet", "restroom" }),
                Node("CAF", 60f, 0f, 40f, 0, "Cafeteria", true, "Cafeteria", new[] { "food", "canteen" }),
                Node("RAMP_BEND", 25f, 0f, 55f, 0, null, false, null, null),
                Node("ENG_ENT", 40f, 0.6f, 60f, 0, "Engineering Block Entrance", true, "Engineering Block", null),
                Node("ENG_LOBBY", 40f, 0.6f, 72f, 0, "Engineering Lobby", false, "Engineering Block", null),
                Node("ENG_C2", 58f, 0.6f, 72f, 0, null, false, "Engineering Block", null),
                Node("SEMINAR_101", 70f, 0.6f, 72f, 0, "Seminar Hall 101", true, "Engineering Block", new[] { "room 101", "lecture" }),
                Node("ENG_LIFT_0", 36f, 0.6f, 80f, 0, "Engineering Lift (Ground)", false, "Engineering Block", null),
                Node("ENG_STAIR_0", 58f, 0.6f, 80f, 0, "Main Staircase", false, "Engineering Block", null),
                Node("ENG_STAIR_1", 58f, 4.6f, 88f, 1, "Main Staircase (First Floor)", false, "Engineering Block", null),
                Node("ENG_LIFT_1", 36f, 4.6f, 80f, 1, "Engineering Lift (First Floor)", false, "Engineering Block", null),
                Node("ENG_F1_HALL", 48f, 4.6f, 88f, 1, null, false, "Engineering Block", null),
                Node("ROBOTICS_201", 70f, 4.6f, 88f, 1, "Robotics Lab 201", true, "Engineering Block", new[] { "room 201", "lab" }),
                Node("ENG_F1_WC", 36f, 4.6f, 92f, 1, "First Floor Washroom", true, "Engineering Block", new[] { "washroom", "toilet", "restroom" }),
            },
            edges = new[]
            {
                Edge("GATE", "J1", "walk", outdoor: true),
                Edge("J1", "J2", "walk", outdoor: true),
                Edge("J1", "J3", "walk", outdoor: true),
                Edge("J3", "HOSTEL", "walk", outdoor: true),
                Edge("J1", "LIB_ENT", "walk", outdoor: true),
                Edge("LIB_ENT", "LIB_DESK", "door", outdoor: false),
                Edge("LIB_DESK", "LIB_WC", "walk", outdoor: false),
                Edge("J2", "CAF", "door", outdoor: true),
                Edge("J2", "ENG_ENT", "stairs", outdoor: true),
                Edge("J2", "RAMP_BEND", "ramp", outdoor: true),
                Edge("RAMP_BEND", "ENG_ENT", "ramp", outdoor: true),
                Edge("ENG_ENT", "ENG_LOBBY", "door", outdoor: false),
                Edge("ENG_LOBBY", "ENG_C2", "walk", outdoor: false),
                Edge("ENG_C2", "SEMINAR_101", "walk", outdoor: false),
                Edge("ENG_LOBBY", "ENG_LIFT_0", "walk", outdoor: false),
                Edge("ENG_C2", "ENG_STAIR_0", "walk", outdoor: false),
                Edge("ENG_STAIR_0", "ENG_STAIR_1", "stairs", outdoor: false),
                Edge("ENG_LIFT_0", "ENG_LIFT_1", "elevator", outdoor: false),
                Edge("ENG_LIFT_1", "ENG_F1_HALL", "walk", outdoor: false),
                Edge("ENG_STAIR_1", "ENG_F1_HALL", "walk", outdoor: false),
                Edge("ENG_F1_HALL", "ROBOTICS_201", "walk", outdoor: false),
                Edge("ENG_LIFT_1", "ENG_F1_WC", "walk", outdoor: false),
            },
        };

        public static NavGraph Campus(List<string> warnings = null) => NavGraphBuilder.Build(CampusData(), warnings);
    }
}
