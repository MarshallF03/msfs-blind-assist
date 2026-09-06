using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Surroundings;

public sealed record NearbyFeature(AirportFeature Feature, double DistanceMetres, double RelativeBearingDeg);

/// <summary>
/// Pure composer for the two readout surfaces. Compose() is ONE utterance: the Where-Am-I
/// line the caller already has, the zone, then the nearest features. Distances go through
/// the caller's formatter (DistanceFormatter on GroundDistanceUnit in production).
/// </summary>
public static class SurroundingsReport
{
    public const double SpeakRadiusMetres = 600.0;
    public const int MaxSpoken = 4;
    public const double ZoneNearMetres = 120.0;
    public const double WindowRadiusMetres = 1000.0;

    public static List<NearbyFeature> Rank(AirportFeatureCatalog cat, double lat, double lon, double hdgTrue, double maxMetres)
    {
        var list = new List<NearbyFeature>();
        foreach (var f in cat.Features)
        {
            double d = SurroundingsGeometry.DistanceMetres(lat, lon, f);
            if (d > maxMetres) continue;
            double rel = SurroundingsGeometry.RelativeBearingDeg(lat, lon, hdgTrue, f.Lat, f.Lon);
            list.Add(new NearbyFeature(f, d, rel));
        }
        return list.OrderBy(n => n.DistanceMetres).ToList();
    }

    public static AirportFeature? Zone(AirportFeatureCatalog cat, double lat, double lon)
    {
        foreach (var f in cat.Features)
            if ((f.Kind == FeatureKind.Apron || f.Kind == FeatureKind.DeicePad) && f.Footprint != null
                && SurroundingsGeometry.Contains(f.Footprint, lat, lon))
                return f;
        AirportFeature? best = null; double bestD = ZoneNearMetres;
        foreach (var f in cat.Features)
        {
            if (f.Kind != FeatureKind.Concourse && f.Kind != FeatureKind.Terminal) continue;
            double d = SurroundingsGeometry.DistanceMetres(lat, lon, f);
            if (d <= bestD) { bestD = d; best = f; }
        }
        return best;
    }

    public static string Compose(string whereAmILine, string icao, AirportFeatureCatalog? cat, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var parts = new List<string> { whereAmILine.Trim() };
        if (cat == null || cat.Features.Count == 0)
        {
            parts.Add($"No surroundings data for {icao}.");
            return string.Join(" ", parts);
        }

        var zone = Zone(cat, lat, lon);
        if (zone != null)
            parts.Add(zone.Kind is FeatureKind.Apron or FeatureKind.DeicePad ? $"On the {zone.SpokenName}." : $"At {zone.SpokenName}.");

        var ranked = Rank(cat, lat, lon, hdgTrue, SpeakRadiusMetres).Where(n => !ReferenceEquals(n.Feature, zone)).ToList();
        if (ranked.Count == 0)
        {
            parts.Add($"Nothing within {formatDistance(SpeakRadiusMetres)}.");
            return string.Join(" ", parts);
        }

        var spoken = new List<NearbyFeature>();
        var kindsUsed = new HashSet<FeatureKind>();
        NearbyFeature? firstUnnamedHangar = null; int unnamedHangars = 0;
        foreach (var n in ranked)
        {
            if (spoken.Count >= MaxSpoken) break;
            var f = n.Feature;
            if (f.Kind == FeatureKind.Hangar && !f.HasName)
            {
                unnamedHangars++;
                if (firstUnnamedHangar == null) { firstUnnamedHangar = n; spoken.Add(n); }
                continue;
            }
            bool repeatable = f.Kind is FeatureKind.Hangar or FeatureKind.Fbo;
            if (!repeatable && !kindsUsed.Add(f.Kind)) continue;
            spoken.Add(n);
        }

        foreach (var n in spoken)
        {
            string name = ReferenceEquals(n, firstUnnamedHangar) && unnamedHangars > 1 ? "Hangars" : n.Feature.SpokenName;
            parts.Add($"{name}, {RelativeDirection.Describe(n.RelativeBearingDeg)}, {formatDistance(n.DistanceMetres)}.");
        }
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<InfoSection> BuildSections(string icao, AirportFeatureCatalog cat, string facts, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var sections = new List<InfoSection>();
        if (!string.IsNullOrWhiteSpace(facts))
            sections.Add(new InfoSection("Airport", new[] { facts.Trim() }));

        var ranked = Rank(cat, lat, lon, hdgTrue, WindowRadiusMetres);
        var items = ranked.Select(n =>
        {
            string detail = string.IsNullOrWhiteSpace(n.Feature.Detail) ? "" : $", {n.Feature.Detail}";
            return $"{n.Feature.SpokenName}{detail}, {RelativeDirection.Describe(n.RelativeBearingDeg)}, {formatDistance(n.DistanceMetres)}";
        }).ToList();
        sections.Add(new InfoSection(items.Count == 0 ? "Nearby, nothing within 1 kilometre" : $"Nearby, {items.Count} items", items));
        return sections;
    }
}
