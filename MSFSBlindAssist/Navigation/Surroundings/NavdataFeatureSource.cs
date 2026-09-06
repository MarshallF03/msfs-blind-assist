using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Tier 1: features derivable from navdata alone, offline, at every airport. Concourses are
/// INFERRED from the gate letters (navdata has no building table); fuel/cargo/GA stands
/// cluster into one feature per group; helipads come from the helipad table. Pure.
/// </summary>
public static class NavdataFeatureSource
{
    public const double ClusterRadiusMetres = 60.0;
    public const double AirlineMajority = 0.60;
    private static readonly string[] Directional = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };

    private static readonly Dictionary<string, string> AirlineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DAL"] = "Delta", ["AAL"] = "American", ["UAL"] = "United", ["SWA"] = "Southwest", ["JBU"] = "JetBlue",
        ["ASA"] = "Alaska", ["FFT"] = "Frontier", ["NKS"] = "Spirit", ["BAW"] = "British Airways", ["DLH"] = "Lufthansa",
        ["AFR"] = "Air France", ["KLM"] = "KLM", ["RYR"] = "Ryanair", ["EZY"] = "easyJet", ["UAE"] = "Emirates",
        ["QTR"] = "Qatar", ["ACA"] = "Air Canada", ["QFA"] = "Qantas", ["FDX"] = "FedEx", ["UPS"] = "UPS",
    };

    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> spots, AirportFacilities? facilities)
    {
        var result = new List<AirportFeature>();
        spots ??= Array.Empty<ParkingSpot>();

        // Concourses: gate-type stands sharing a single-letter Name.
        foreach (var group in spots.Where(s => IsGateType(s.Type) && IsConcourseLetter(s.Name)).GroupBy(s => s.Name.ToUpperInvariant()))
        {
            var members = group.ToList();
            if (members.Count < 2) continue;
            var c = SurroundingsGeometry.Centroid(members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            result.Add(new AirportFeature
            {
                Kind = FeatureKind.Concourse, Name = $"Concourse {group.Key}", Lat = c.Lat, Lon = c.Lon,
                Source = FeatureSource.Navdata, Detail = MajorityAirline(members),
            });
        }

        // Directional ramps ("North" from NP etc.) → one apron per direction.
        foreach (var group in spots.Where(s => Directional.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var c = SurroundingsGeometry.Centroid(group.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            result.Add(new AirportFeature { Kind = FeatureKind.Apron, Name = $"{Capitalize(group.Key)} ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });
        }

        string? fuelDetail = facilities == null ? null : (facilities.HasAvgas, facilities.HasJetFuel) switch
        {
            (true, true) => "avgas and jet fuel", (true, false) => "avgas", (false, true) => "jet fuel", _ => null,
        };
        foreach (var c in Clusters(spots.Where(s => s.Type == 16), 1))
            result.Add(new AirportFeature { Kind = FeatureKind.Fuel, Name = "Fuel", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata, Detail = fuelDetail });
        foreach (var c in Clusters(spots.Where(s => s.Type == 6), 1))
            result.Add(new AirportFeature { Kind = FeatureKind.Cargo, Name = "Cargo ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });
        foreach (var c in Clusters(spots.Where(s => s.Type is 2 or 3 or 4 or 5 or 15 && !IsDirectionalName(s.Name)), 3))
            result.Add(new AirportFeature { Kind = FeatureKind.Apron, Name = "GA ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });

        if (facilities != null)
        {
            for (int i = 0; i < facilities.Helipads.Count; i++)
            {
                var h = facilities.Helipads[i];
                string name = facilities.Helipads.Count == 1 ? "Helipad" : $"Helipad {i + 1}";
                result.Add(new AirportFeature { Kind = FeatureKind.Helipad, Name = name, Lat = h.Lat, Lon = h.Lon, Source = FeatureSource.Navdata });
            }
        }
        return result;
    }

    internal static bool IsGateType(int type) => type is 9 or 10 or 11 or 13 or 14;
    internal static bool IsConcourseLetter(string? name) => name != null && name.Length == 1 && char.IsLetter(name[0]);
    private static bool IsDirectionalName(string? name) => name != null && Directional.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>"Delta gates" when ≥ 60 % of the airline-coded gates share one code; else null.</summary>
    internal static string? MajorityAirline(IReadOnlyList<ParkingSpot> gates)
    {
        var codes = gates.SelectMany(g => (g.AirlineCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                         .Select(c => c.ToUpperInvariant()).ToList();
        if (codes.Count == 0) return null;
        var top = codes.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
        if (top.Count() < codes.Count * AirlineMajority) return null;
        string name = AirlineNames.TryGetValue(top.Key, out var n) ? n : top.Key;
        return $"{name} gates";
    }

    /// <summary>Greedy clustering: a spot joins the first cluster whose centroid is within ClusterRadiusMetres.</summary>
    internal static List<LatLon> Clusters(IEnumerable<ParkingSpot> spots, int minSize)
    {
        var clusters = new List<List<LatLon>>();
        foreach (var s in spots)
        {
            var p = new LatLon(s.Latitude, s.Longitude);
            var home = clusters.FirstOrDefault(c =>
            {
                var cen = SurroundingsGeometry.Centroid(c);
                return TaxiGeo.HaversineMeters(cen.Lat, cen.Lon, p.Lat, p.Lon) <= ClusterRadiusMetres;
            });
            if (home == null) clusters.Add(new List<LatLon> { p }); else home.Add(p);
        }
        return clusters.Where(c => c.Count >= minSize).Select(SurroundingsGeometry.Centroid).ToList();
    }
}
