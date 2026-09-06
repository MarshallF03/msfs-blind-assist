using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// GSX's uiTerminalName per stand, grouped. Read from the SELECTABLE list (GetSelectableGates)
/// — GetNamedSpots deliberately does not carry TerminalName. Outranks navdata's letter
/// inference in the catalog (GSX is right where navdata's letter is wrong, measured KJFK).
/// </summary>
public static class GsxTerminalFeatureSource
{
    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> selectableGates)
    {
        var result = new List<AirportFeature>();
        var groups = selectableGates
            .Where(s => s.Source == GateSource.Gsx && !string.IsNullOrWhiteSpace(ParkingSpot.SpeakableTerminalName(s.TerminalName)))
            .GroupBy(s => ParkingSpot.SpeakableTerminalName(s.TerminalName).Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            var c = SurroundingsGeometry.Centroid(members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            bool concourse = g.Key.Contains("Concourse", StringComparison.OrdinalIgnoreCase);
            result.Add(new AirportFeature { Kind = concourse ? FeatureKind.Concourse : FeatureKind.Terminal, Name = g.Key, Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Gsx });
        }
        return result;
    }
}
