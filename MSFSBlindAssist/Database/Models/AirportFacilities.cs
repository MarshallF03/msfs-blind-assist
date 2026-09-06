using System.Globalization;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Database.Models;

public readonly record struct ComFrequency(string Type, int FrequencyHz, string Name);

/// <summary>
/// The airport-level navdata columns the surroundings feature reads and nothing else did:
/// fuel flags, helipads, COM frequencies, the airport bounding box (the OSM radius-fallback
/// filter) and scenery_local_path (which packages the scenery scan may open).
/// airport.tower_lonx/laty is deliberately NOT here: NULL on every row of the current
/// navdatareader build (41,866 of 41,866, measured 2026-09-06).
/// </summary>
public sealed class AirportFacilities
{
    public string Icao { get; init; } = "";
    public bool HasAvgas { get; init; }
    public bool HasJetFuel { get; init; }
    public List<LatLon> Helipads { get; } = new();
    public List<ComFrequency> Coms { get; } = new();
    public double LeftLon { get; init; }
    public double RightLon { get; init; }
    public double TopLat { get; init; }
    public double BottomLat { get; init; }
    /// <summary>navdatareader's comma-separated package list, e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows".</summary>
    public string SceneryLocalPath { get; init; } = "";

    public bool ContainsPoint(double lat, double lon)
        => lat <= TopLat && lat >= BottomLat && lon >= LeftLon && lon <= RightLon;

    /// <summary>"Avgas and jet fuel. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95." or "".</summary>
    public string DescribeFacts()
    {
        var parts = new List<string>();
        string fuel = (HasAvgas, HasJetFuel) switch
        {
            (true, true) => "Avgas and jet fuel",
            (true, false) => "Avgas",
            (false, true) => "Jet fuel",
            _ => "",
        };
        if (fuel.Length > 0) parts.Add(fuel + ".");

        var freqs = new List<string>();
        foreach (var (type, label) in new[] { ("T", "Tower"), ("G", "Ground"), ("ATIS", "ATIS"), ("CTAF", "CTAF"), ("UC", "UNICOM"), ("AWOS", "AWOS"), ("ASOS", "ASOS") })
        {
            var com = Coms.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase));
            if (com.FrequencyHz > 0)
                freqs.Add($"{label} {FormatMhz(com.FrequencyHz)}");
        }
        if (freqs.Count > 0) parts.Add(string.Join(", ", freqs) + ".");
        return string.Join(" ", parts);
    }

    /// <summary>118500000 → "118.5"; 124050000 → "124.05"; 122950000 → "122.95" (trailing zeros trimmed, at least one decimal).</summary>
    public static string FormatMhz(int hz)
    {
        double mhz = hz / 1_000_000.0;
        string s = mhz.ToString("0.000", CultureInfo.InvariantCulture).TrimEnd('0');
        if (s.EndsWith('.')) s += "0";
        return s;
    }
}
