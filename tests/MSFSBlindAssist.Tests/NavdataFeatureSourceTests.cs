using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class NavdataFeatureSourceTests
{
    private static ParkingSpot Spot(string name, int number, int type, double lat, double lon, string airlines = "")
        => new() { Name = name, Number = number, Type = type, Latitude = lat, Longitude = lon, AirlineCodes = airlines };

    [Fact]
    public void Two_or_more_lettered_gates_become_a_concourse_at_their_centroid()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("B", 1, 10, 33.640, -84.430, "DAL"),
            Spot("B", 2, 10, 33.642, -84.430, "DAL"),
            Spot("B", 3, 11, 33.644, -84.430, "AAL"),
            Spot("C", 9, 10, 33.650, -84.420),           // alone: no concourse
        };
        var features = NavdataFeatureSource.Read(spots, null);
        var b = Assert.Single(features, f => f.Kind == FeatureKind.Concourse);
        Assert.Equal("Concourse B", b.Name);
        Assert.InRange(b.Lat, 33.6419, 33.6421);
        Assert.Equal("Delta gates", b.Detail);          // 2 of 3 coded gates = 67 % ≥ 60 %
        Assert.Equal(FeatureSource.Navdata, b.Source);
    }

    [Fact]
    public void Airline_detail_is_omitted_below_the_majority_threshold()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("A", 1, 10, 33.640, -84.430, "DAL"),
            Spot("A", 2, 10, 33.641, -84.430, "AAL"),
        };
        var a = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Null(a.Detail);
    }

    [Fact]
    public void Directional_ramps_become_named_aprons()
    {
        var spots = new List<ParkingSpot> { Spot("North", 1, 4, 47.27, -122.57), Spot("North", 2, 4, 47.271, -122.57) };
        var apron = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Equal(FeatureKind.Apron, apron.Kind);
        Assert.Equal("North ramp", apron.Name);
    }

    [Fact]
    public void Fuel_stands_cluster_into_one_fuel_feature_with_the_fuel_types()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 16, 47.2700, -122.5700),
            Spot("Parking", 2, 16, 47.2701, -122.5700),   // ~11 m away → same cluster
            Spot("Parking", 3, 16, 47.2750, -122.5700),   // 550 m away → second cluster
        };
        var fac = new AirportFacilities { Icao = "KTIW", HasAvgas = true, HasJetFuel = true };
        var fuel = NavdataFeatureSource.Read(spots, fac).Where(f => f.Kind == FeatureKind.Fuel).ToList();
        Assert.Equal(2, fuel.Count);
        Assert.All(fuel, f => Assert.Equal("Fuel", f.Name));
        Assert.All(fuel, f => Assert.Equal("avgas and jet fuel", f.Detail));
    }

    [Fact]
    public void Cargo_and_ga_ramps_cluster_and_vehicles_are_ignored()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 6, 33.62, -84.44), Spot("Parking", 2, 6, 33.6201, -84.44),
            Spot("Parking", 3, 3, 47.27, -122.58), Spot("Parking", 4, 3, 47.2701, -122.58), Spot("Parking", 5, 4, 47.2702, -122.58),
            Spot("Parking", 6, 17, 47.27, -122.58),
        };
        var features = NavdataFeatureSource.Read(spots, null);
        Assert.Single(features, f => f.Kind == FeatureKind.Cargo && f.Name == "Cargo ramp");
        Assert.Single(features, f => f.Kind == FeatureKind.Apron && f.Name == "GA ramp");
        Assert.DoesNotContain(features, f => f.Kind == FeatureKind.Other);
    }

    [Fact]
    public void Helipads_come_from_facilities_and_are_numbered_only_when_several()
    {
        var one = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1) } };
        Assert.Equal("Helipad", Assert.Single(NavdataFeatureSource.Read(new List<ParkingSpot>(), one)).Name);
        var two = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1), new LatLon(1.001, 1) } };
        var names = NavdataFeatureSource.Read(new List<ParkingSpot>(), two).Select(f => f.Name).ToList();
        Assert.Equal(new[] { "Helipad 1", "Helipad 2" }, names);
    }

    [Fact]
    public void Facts_line_lists_fuel_and_the_common_frequencies_in_mhz()
    {
        var fac = new AirportFacilities
        {
            Icao = "KTIW", HasAvgas = true, HasJetFuel = false,
            Coms = { new ComFrequency("T", 118500000, "TACOMA"), new ComFrequency("G", 121800000, "TACOMA"),
                     new ComFrequency("ATIS", 124050000, "KTIW"), new ComFrequency("UC", 122950000, "TACOMA"),
                     new ComFrequency("D", 120100000, "SEATTLE") }
        };
        Assert.Equal("Avgas. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95.", fac.DescribeFacts());
        Assert.Equal("", new AirportFacilities { Icao = "X" }.DescribeFacts());
    }
}
