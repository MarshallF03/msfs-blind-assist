using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class GsxTerminalFeatureSourceTests
{
    private static ParkingSpot Gsx(string terminal, double lat, double lon)
        => new() { Source = GateSource.Gsx, TerminalName = terminal, Latitude = lat, Longitude = lon, Number = 1, Name = "B" };

    [Fact]
    public void Groups_gsx_stands_by_terminal_name_and_types_concourses()
    {
        var spots = new List<ParkingSpot>
        {
            Gsx("Terminal 4 - Concourse B", 40.6440, -73.7820), Gsx("Terminal 4 - Concourse B", 40.6442, -73.7820),
            Gsx("A-Platform =< Medium ", 52.31, 4.76), Gsx("A-Platform =< Medium ", 52.311, 4.76),
            Gsx("Lonely", 1, 1),
            new ParkingSpot { Source = GateSource.Navdata, TerminalName = "Terminal 4 - Concourse B", Latitude = 40.7, Longitude = -73.7 },
        };
        var f = GsxTerminalFeatureSource.Read(spots);
        Assert.Equal(2, f.Count);
        var b = f.Single(x => x.Kind == FeatureKind.Concourse);
        Assert.Equal("Terminal 4 - Concourse B", b.Name);
        Assert.InRange(b.Lat, 40.6440, 40.6442);
        Assert.Equal(FeatureSource.Gsx, b.Source);
        Assert.Equal("A-Platform", f.Single(x => x.Kind == FeatureKind.Terminal).Name);
    }
}
