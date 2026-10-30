using System;

namespace CampusNav.Routing
{
    // Serialisable map data. Field names match the JSON format in Docs/GRAPH_DATA_FORMAT.md.
    // Kept as plain [Serializable] classes with public fields so Unity's JsonUtility can read them
    // (JsonUtility ignores properties, dictionaries and enums-as-strings).

    [Serializable]
    public class NavGraphData
    {
        public int version = 1;
        public string campus;
        /// <summary>Optional. Needed only if nodes are given as lat/lon or you convert GPS fixes.</summary>
        public GeoOrigin origin;
        /// <summary>Optional. Floor elevations, used to work out the user's floor from height.</summary>
        public FloorData[] floors;
        public NavNodeData[] nodes;
        public NavEdgeData[] edges;
    }

    [Serializable]
    public class GeoOrigin
    {
        /// <summary>WGS84 latitude of the map's (0,0,0).</summary>
        public double latitude;
        /// <summary>WGS84 longitude of the map's (0,0,0).</summary>
        public double longitude;
        /// <summary>Compass bearing (degrees clockwise from true north) of the map's +Z axis.</summary>
        public float headingDeg;
    }

    [Serializable]
    public class FloorData
    {
        public int level;
        /// <summary>Map-space Y (metres) of this floor's walking surface.</summary>
        public float elevation;
        public string name;
    }

    [Serializable]
    public class NavNodeData
    {
        /// <summary>Unique, stable id, e.g. "LIB_ENTRANCE". Other teams store these, so don't renumber.</summary>
        public string id;
        /// <summary>Human-readable name. Shown in search and in instructions ("Turn left at ...").</summary>
        public string name;
        /// <summary>Map-space position in metres. Unity convention: X right/east, Y up, Z forward/north.</summary>
        public float x, y, z;
        /// <summary>Optional. If either is non-zero, x/z are computed from these using <see cref="NavGraphData.origin"/>.</summary>
        public double lat, lon;
        public int floor;
        public string building;
        /// <summary>Free-form search tags, e.g. ["washroom", "accessible"].</summary>
        public string[] tags;
        /// <summary>True for places users can pick as a destination (rooms, entrances, POIs).</summary>
        public bool destination;
    }

    [Serializable]
    public class NavEdgeData
    {
        public string from;
        public string to;
        /// <summary>"walk" (default), "ramp", "stairs", "escalator", "elevator" or "door".</summary>
        public string type;
        /// <summary>True: only from -> to may be used (exit-only doors, turnstiles, escalators).</summary>
        public bool oneWay;
        /// <summary>Metres. 0 or missing = straight-line distance between the nodes.</summary>
        public float length;
        public bool outdoor;
        /// <summary>Not usable in a wheelchair even though it is not stairs (kerb, narrow gate).</summary>
        public bool noWheelchair;
        /// <summary>Closed at load time (construction etc.). Can also be changed at runtime.</summary>
        public bool closed;
    }
}
