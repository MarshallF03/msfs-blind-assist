using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public static class SurroundingsGeometry
{
    /// <summary>Metres from (lat,lon) to the feature: 0 inside a footprint, else the nearest
    /// polygon edge; a point feature is plain haversine to its representative point.</summary>
    public static double DistanceMetres(double lat, double lon, AirportFeature f)
    {
        var fp = f.Footprint;
        if (fp == null || fp.Count < 3)
            return TaxiGeo.HaversineMeters(lat, lon, f.Lat, f.Lon);
        if (Contains(fp, lat, lon)) return 0.0;
        double best = double.MaxValue;
        for (int i = 0; i < fp.Count; i++)
        {
            var a = fp[i];
            var b = fp[(i + 1) % fp.Count];
            double d = TaxiGeo.PointToSegmentMeters(lat, lon, a.Lat, a.Lon, b.Lat, b.Lon);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Ray-casting point-in-polygon on raw degrees (fine at airport scale; no antimeridian airports).</summary>
    public static bool Contains(IReadOnlyList<LatLon> polygon, double lat, double lon)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            double yi = polygon[i].Lat, xi = polygon[i].Lon;
            double yj = polygon[j].Lat, xj = polygon[j].Lon;
            bool crosses = (yi > lat) != (yj > lat);
            if (!crosses) continue;
            double xAt = xj + (lat - yj) * (xi - xj) / (yi - yj);
            if (lon < xAt) inside = !inside;
        }
        return inside;
    }

    /// <summary>Signed relative bearing, -180..180: negative = left of the nose.</summary>
    public static double RelativeBearingDeg(double ownLat, double ownLon, double ownHeadingTrue, double lat, double lon)
    {
        double brg = TaxiGeo.BearingDeg(ownLat, ownLon, lat, lon);
        double rel = ((brg - ownHeadingTrue) % 360.0 + 540.0) % 360.0 - 180.0;
        return rel;
    }

    public static LatLon Centroid(IReadOnlyList<LatLon> pts)
    {
        if (pts == null || pts.Count == 0) return new LatLon(0, 0);
        double lat = 0, lon = 0;
        foreach (var p in pts) { lat += p.Lat; lon += p.Lon; }
        return new LatLon(lat / pts.Count, lon / pts.Count);
    }
}
