using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public readonly record struct NearestNode(int NodeId, double Lat, double Lon, double DistanceMetres);

/// <summary>
/// Where the route actually ends when the pilot asks for a PLACE. Spot null → the nearest
/// taxi node; Lat/Lon/HeadingDeg are the stand's (or node's), never the building's.
/// </summary>
public sealed record PlaceDestination(AirportFeature Feature, ParkingSpot? Spot, int NodeId, double Lat, double Lon, double HeadingDeg, double DistanceMetres);

/// <summary>
/// A feature is never a route target itself (spec invariant: features are readout-only and
/// never enter TaxiGraph). It RESOLVES onto navdata pavement: the nearest stand within 150 m,
/// preferring the stand type that matches the place (GA ramp for an FBO/hangar, FUEL for fuel,
/// cargo for cargo), else any non-vehicle stand in range, else a taxi node within 100 m, else
/// not routable. Same anti-grass rule OSM aliases and GSX stands already follow.
/// </summary>
public static class FeatureDestinationResolver
{
    public const double MaxSpotMetres = 150.0;
    public const double MaxNodeMetres = 100.0;

    public static bool IsRoutable(FeatureKind kind) => kind is FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Fuel
        or FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Cargo or FeatureKind.FireStation
        or FeatureKind.DeicePad or FeatureKind.Office;

    private static bool IsPreferredStand(FeatureKind kind, int type) => kind switch
    {
        FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Office or FeatureKind.FireStation => type is 2 or 3 or 4 or 5 or 12 or 15,
        FeatureKind.Fuel => type == 16,
        FeatureKind.Cargo => type is 6 or 7,
        FeatureKind.Terminal or FeatureKind.Concourse => type is 9 or 10 or 11 or 13 or 14,
        _ => true,
    };

    public static PlaceDestination? Resolve(AirportFeature feature, IReadOnlyList<ParkingSpot> spots, Func<double, double, NearestNode?> nearestNode)
    {
        if (!IsRoutable(feature.Kind)) return null;

        ParkingSpot? best = null; double bestD = double.MaxValue; bool bestPreferred = false;
        foreach (var s in spots)
        {
            if (s.Type == 17) continue;                               // vehicles: never a place to park an aircraft
            double d = TaxiGeo.HaversineMeters(feature.Lat, feature.Lon, s.Latitude, s.Longitude);
            if (d > MaxSpotMetres) continue;
            bool preferred = IsPreferredStand(feature.Kind, s.Type);
            if (best == null || (preferred && !bestPreferred) || (preferred == bestPreferred && d < bestD))
            { best = s; bestD = d; bestPreferred = preferred; }
        }
        if (best != null)
            return new PlaceDestination(feature, best, -1, best.Latitude, best.Longitude, best.Heading, bestD);

        var node = nearestNode(feature.Lat, feature.Lon);
        if (node is NearestNode n && n.DistanceMetres <= MaxNodeMetres)
            return new PlaceDestination(feature, null, n.NodeId, n.Lat, n.Lon, TaxiGeo.BearingDeg(n.Lat, n.Lon, feature.Lat, feature.Lon), n.DistanceMetres);
        return null;
    }

    /// <summary>"Narrows Aviation, FBO, Parking 12" — the place, its kind, and the stand you are actually guided to.</summary>
    public static string Label(PlaceDestination d)
    {
        string kind = FeatureKindWords.Generic(d.Feature.Kind);
        string kindWord = kind == "FBO" ? kind : kind.ToLowerInvariant();
        string where;
        if (d.Spot == null) where = "end of taxiway";
        else
        {
            string ident = $"{d.Spot.Number}{d.Spot.Suffix}".Trim();
            where = string.IsNullOrWhiteSpace(d.Spot.Name)
                ? (IsGateType(d.Spot.Type) ? $"Gate {ident}" : $"Spot {ident}")
                : $"{d.Spot.Name} {ident}".Trim();
        }
        return $"{d.Feature.SpokenName}, {kindWord}, {where}";
    }

    private static bool IsGateType(int type) => type is 9 or 10 or 11 or 13 or 14;
}
