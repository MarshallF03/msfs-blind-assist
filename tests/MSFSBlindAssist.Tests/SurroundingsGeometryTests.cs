using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsGeometryTests
{
    // KTIW threshold 17 area; a 100 m x 100 m square apron.
    private static readonly LatLon[] Square =
    {
        new(47.2680, -122.5760), new(47.2680, -122.5747),
        new(47.2689, -122.5747), new(47.2689, -122.5760),
    };

    private static AirportFeature Point(FeatureKind k, double lat, double lon, string name = "X")
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Osm };

    [Fact]
    public void Contains_is_true_inside_and_false_outside()
    {
        Assert.True(SurroundingsGeometry.Contains(Square, 47.26845, -122.57535));
        Assert.False(SurroundingsGeometry.Contains(Square, 47.2700, -122.5800));
    }

    [Fact]
    public void Distance_to_a_footprint_feature_is_zero_inside_and_edge_distance_outside()
    {
        var apron = new AirportFeature
        {
            Kind = FeatureKind.Apron, Name = "Ramp", Lat = 47.26845, Lon = -122.57535,
            Footprint = Square, Source = FeatureSource.Osm
        };
        Assert.Equal(0.0, SurroundingsGeometry.DistanceMetres(47.26845, -122.57535, apron));
        // 47.2680 is the south edge; 0.0009 deg lat ≈ 100 m south of it.
        double d = SurroundingsGeometry.DistanceMetres(47.2671, -122.57535, apron);
        Assert.InRange(d, 95.0, 105.0);
    }

    [Fact]
    public void Distance_to_a_point_feature_is_haversine()
    {
        var f = Point(FeatureKind.Tower, 47.2680, -122.5760);
        double d = SurroundingsGeometry.DistanceMetres(47.2689, -122.5760, f);
        Assert.InRange(d, 95.0, 105.0);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // heading north, target due north → 0
    [InlineData(90.0, -90.0)]   // heading east, target due north → -90 (left)
    [InlineData(270.0, 90.0)]   // heading west, target due north → +90 (right)
    [InlineData(180.0, 180.0)]  // heading south, target north → behind
    public void RelativeBearing_is_signed_and_wrapped(double heading, double expected)
    {
        double rel = SurroundingsGeometry.RelativeBearingDeg(47.0, -122.0, heading, 47.01, -122.0);
        if (Math.Abs(expected) == 180.0) Assert.InRange(Math.Abs(rel), 179.5, 180.5);   // -180 and 180 are the same direction
        else Assert.InRange(rel, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public void Centroid_is_the_vertex_mean()
    {
        var c = SurroundingsGeometry.Centroid(Square);
        Assert.InRange(c.Lat, 47.26844, 47.26846);
        Assert.InRange(c.Lon, -122.57536, -122.57534);
    }

    [Fact]
    public void SpokenName_falls_back_to_the_kind_word()
    {
        Assert.Equal("Hangar", Point(FeatureKind.Hangar, 0, 0, "").SpokenName);
        Assert.Equal("Control tower", Point(FeatureKind.Tower, 0, 0, " ").SpokenName);
        Assert.Equal("Narrows Aviation", Point(FeatureKind.Fbo, 0, 0, "Narrows Aviation").SpokenName);
    }
}
