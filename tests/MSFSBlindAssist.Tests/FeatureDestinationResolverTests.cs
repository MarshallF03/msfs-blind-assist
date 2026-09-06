using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class FeatureDestinationResolverTests
{
    private static AirportFeature F(FeatureKind k, string name, double lat, double lon)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Scenery };
    private static ParkingSpot S(int type, double lat, double lon, string name = "Parking", int number = 1, double hdg = 90)
        => new() { Type = type, Latitude = lat, Longitude = lon, Name = name, Number = number, Heading = hdg };
    private static NearestNode? NoNode(double lat, double lon) => null;
    private static NearestNode? NodeAt(double lat, double lon) => new(42, lat + 0.0002, lon, 22.0);

    // 0.0009° lat ≈ 100 m.
    [Fact]
    public void Fbo_prefers_the_nearest_ga_stand_over_a_closer_gate()
    {
        var fbo = F(FeatureKind.Fbo, "Narrows Aviation", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(10, 47.2703, -122.5700, "A", 5), S(4, 47.2708, -122.5700, "Parking", 12) };
        var d = FeatureDestinationResolver.Resolve(fbo, spots, NoNode);
        Assert.NotNull(d);
        Assert.Equal(12, d!.Spot!.Number);
        Assert.Equal(47.2708, d.Lat);
        Assert.Equal(90.0, d.HeadingDeg);
        Assert.InRange(d.DistanceMetres, 85, 95);
        Assert.Equal("Narrows Aviation, FBO, Parking 12", FeatureDestinationResolver.Label(d));
    }

    [Fact]
    public void Falls_back_to_any_non_vehicle_stand_when_no_preferred_type_is_close()
    {
        var hangar = F(FeatureKind.Hangar, "ATP Hangar", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(10, 47.2703, -122.5700, "A", 5), S(17, 47.2701, -122.5700, "V", 1), S(4, 47.2720, -122.5700) };
        var d = FeatureDestinationResolver.Resolve(hangar, spots, NoNode);
        Assert.Equal(5, d!.Spot!.Number);   // the gate: vehicles excluded, GA stand at 220 m is beyond 150 m
    }

    [Fact]
    public void Fuel_resolves_to_a_fuel_stand()
    {
        var fuel = F(FeatureKind.Fuel, "Fuel", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(4, 47.2701, -122.5700), S(16, 47.2706, -122.5700, "Parking", 3) };
        Assert.Equal(16, FeatureDestinationResolver.Resolve(fuel, spots, NoNode)!.Spot!.Type);
    }

    [Fact]
    public void Node_fallback_within_100m_when_no_stand_is_within_150m()
    {
        var hangar = F(FeatureKind.Hangar, "Cessna Service Hangar", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(4, 47.2720, -122.5700) };
        var d = FeatureDestinationResolver.Resolve(hangar, spots, NodeAt);
        Assert.NotNull(d);
        Assert.Null(d!.Spot);
        Assert.Equal(42, d.NodeId);
        Assert.Equal("Cessna Service Hangar, hangar, end of taxiway", FeatureDestinationResolver.Label(d));
        Assert.Null(FeatureDestinationResolver.Resolve(hangar, spots, NoNode));
        Assert.Null(FeatureDestinationResolver.Resolve(hangar, spots, (la, lo) => new NearestNode(7, la, lo, 130.0)));
    }

    [Theory]
    [InlineData(FeatureKind.Tower)]
    [InlineData(FeatureKind.Helipad)]
    [InlineData(FeatureKind.Apron)]
    [InlineData(FeatureKind.Other)]
    public void Non_routable_kinds_never_resolve(FeatureKind kind)
    {
        Assert.False(FeatureDestinationResolver.IsRoutable(kind));
        Assert.Null(FeatureDestinationResolver.Resolve(F(kind, "X", 47.27, -122.57), new List<ParkingSpot> { S(4, 47.27, -122.57) }, NodeAt));
    }
}
