using System;
using System.Numerics;

namespace CampusNav.Routing
{
    /// <summary>
    /// Converts WGS84 lat/lon to map-space metres and back, around <see cref="GeoOrigin"/>.
    /// Local tangent-plane (equirectangular) approximation: error is well under 1 cm per 100 m,
    /// which is far below GPS noise and fine for a campus a few km across.
    /// Map axes: +X right, +Z forward; the origin's headingDeg is the compass bearing of +Z.
    /// </summary>
    public sealed class GeoProjection
    {
        const double EarthRadius = 6378137.0; // WGS84 equatorial radius, metres
        const double Deg = Math.PI / 180.0;

        readonly double lat0, lon0, cosLat0, sinH, cosH;

        public GeoProjection(GeoOrigin origin)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            lat0 = origin.latitude;
            lon0 = origin.longitude;
            cosLat0 = Math.Cos(lat0 * Deg);
            double h = origin.headingDeg * Deg;
            sinH = Math.Sin(h);
            cosH = Math.Cos(h);
        }

        /// <summary>Map-space position (Y = height) of a lat/lon.</summary>
        public Vector3 ToLocal(double latitude, double longitude, float height = 0)
        {
            double east = (longitude - lon0) * Deg * EarthRadius * cosLat0;
            double north = (latitude - lat0) * Deg * EarthRadius;
            // rotate east/north into the map frame whose +Z points at bearing headingDeg
            double x = east * cosH - north * sinH;
            double z = east * sinH + north * cosH;
            return new Vector3((float)x, height, (float)z);
        }

        public void ToGeo(Vector3 local, out double latitude, out double longitude)
        {
            double east = local.X * cosH + local.Z * sinH;
            double north = -local.X * sinH + local.Z * cosH;
            latitude = lat0 + north / EarthRadius / Deg;
            longitude = lon0 + east / (EarthRadius * cosLat0) / Deg;
        }
    }
}
