using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// One airport's merged, deduplicated feature list. Immutable. Built off the UI thread by
/// SurroundingsCatalogCache; consumers only read. Version is the invalidation token the cache
/// stamped it with (gate-list token + online fetch generation + scenery index stamp).
/// </summary>
public sealed class AirportFeatureCatalog
{
    public string Icao { get; }
    public string Version { get; }
    public IReadOnlyList<AirportFeature> Features { get; }

    private AirportFeatureCatalog(string icao, string version, List<AirportFeature> features)
    {
        Icao = icao; Version = version; Features = features;
    }

    public static AirportFeatureCatalog Empty(string icao) => new(icao, "", new List<AirportFeature>());

    /// <summary>Named beats unnamed; among named, OSM > Scenery > GSX > Navdata.</summary>
    public static int Rank(AirportFeature f)
    {
        if (!f.HasName) return 0;
        return f.Source switch
        {
            FeatureSource.Osm => 40,
            FeatureSource.Scenery => 30,
            FeatureSource.Gsx => 20,
            _ => 10,
        };
    }

    public static double MergeRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Tower => 100.0,
        FeatureKind.Terminal or FeatureKind.Concourse => 150.0,
        FeatureKind.Hangar => 40.0,
        FeatureKind.Fuel => 60.0,
        _ => 50.0,
    };

    /// <summary>Same kind and (concourse letter match, or within the kind's merge radius).</summary>
    public static bool SameFeature(AirportFeature a, AirportFeature b)
    {
        if (a.Kind != b.Kind) return false;
        if (a.Kind == FeatureKind.Concourse && a.HasName && b.HasName
            && string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        return TaxiGeo.HaversineMeters(a.Lat, a.Lon, b.Lat, b.Lon) <= MergeRadiusMetres(a.Kind);
    }

    public static AirportFeatureCatalog Build(string icao, string version, IEnumerable<AirportFeature> features)
    {
        var kept = new List<AirportFeature>();
        // Highest rank first so the first feature standing in a cluster is the winner.
        foreach (var f in features.Where(f => f != null && (f.HasName || f.Kind != FeatureKind.Other)).OrderByDescending(Rank))
        {
            int i = kept.FindIndex(k => SameFeature(k, f));
            if (i < 0) { kept.Add(f); continue; }
            var winner = kept[i];
            if (winner.Footprint == null && f.Footprint != null || winner.Detail == null && f.Detail != null)
            {
                kept[i] = new AirportFeature
                {
                    Kind = winner.Kind, Name = winner.Name, Lat = winner.Lat, Lon = winner.Lon, Source = winner.Source,
                    Footprint = winner.Footprint ?? f.Footprint, Detail = winner.Detail ?? f.Detail,
                };
            }
        }
        var sorted = kept.OrderBy(f => (int)f.Kind).ThenBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase).ToList();
        return new AirportFeatureCatalog(icao, version, sorted);
    }
}
