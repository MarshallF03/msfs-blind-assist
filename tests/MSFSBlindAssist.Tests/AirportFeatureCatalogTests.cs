using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class AirportFeatureCatalogTests
{
    private static AirportFeature F(FeatureKind k, string name, double lat, double lon, FeatureSource src, string? detail = null, IReadOnlyList<LatLon>? fp = null)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = src, Detail = detail, Footprint = fp };

    [Fact]
    public void Same_kind_within_radius_collapses_to_the_higher_rank()
    {
        var osm = F(FeatureKind.Tower, "Control Tower", 47.2700, -122.5700, FeatureSource.Osm);
        var scenery = F(FeatureKind.Tower, "Control Tower 1", 47.2705, -122.5700, FeatureSource.Scenery); // ~55 m
        var cat = AirportFeatureCatalog.Build("KTIW", "v", new[] { scenery, osm });
        var only = Assert.Single(cat.Features);
        Assert.Equal("Control Tower", only.Name);
        Assert.Equal(FeatureSource.Osm, only.Source);
    }

    [Fact]
    public void Loser_donates_footprint_and_detail_the_winner_lacks()
    {
        var square = new[] { new LatLon(0, 0), new LatLon(0, 0.001), new LatLon(0.001, 0.001), new LatLon(0.001, 0) };
        var navdata = F(FeatureKind.Concourse, "Concourse B", 0.0005, 0.0005, FeatureSource.Navdata, detail: "Delta gates");
        var osm = F(FeatureKind.Concourse, "Concourse B", 0.0004, 0.0005, FeatureSource.Osm, fp: square);
        var cat = AirportFeatureCatalog.Build("X", "v", new[] { navdata, osm });
        var only = Assert.Single(cat.Features);
        Assert.Equal(FeatureSource.Osm, only.Source);
        Assert.Equal("Delta gates", only.Detail);
        Assert.NotNull(only.Footprint);
    }

    [Fact]
    public void Concourses_match_on_letter_regardless_of_distance()
    {
        var a = F(FeatureKind.Concourse, "Concourse B", 33.640, -84.430, FeatureSource.Navdata);
        var b = F(FeatureKind.Concourse, "Concourse B", 33.645, -84.425, FeatureSource.Scenery); // ~700 m
        Assert.Single(AirportFeatureCatalog.Build("KATL", "v", new[] { a, b }).Features);
    }

    [Fact]
    public void Different_kinds_never_merge_and_unnamed_hangars_stay_separate_beyond_40m()
    {
        var h1 = F(FeatureKind.Hangar, "", 47.2700, -122.5700, FeatureSource.Osm);
        var h2 = F(FeatureKind.Hangar, "", 47.2705, -122.5700, FeatureSource.Osm);   // 55 m
        var fuel = F(FeatureKind.Fuel, "Fuel", 47.2700, -122.5700, FeatureSource.Navdata);
        Assert.Equal(3, AirportFeatureCatalog.Build("X", "v", new[] { h1, h2, fuel }).Features.Count);
    }

    [Fact]
    public void Named_beats_unnamed_within_a_source_and_unnamed_Other_is_dropped()
    {
        var unnamed = F(FeatureKind.Hangar, "", 47.27, -122.57, FeatureSource.Osm);
        var named = F(FeatureKind.Hangar, "ATP Hangar", 47.2701, -122.57, FeatureSource.Scenery);
        var junk = F(FeatureKind.Other, "", 47.28, -122.58, FeatureSource.Osm);
        var cat = AirportFeatureCatalog.Build("X", "v", new[] { unnamed, named, junk });
        Assert.Equal("ATP Hangar", Assert.Single(cat.Features).Name);
    }

    [Theory]
    [InlineData(FeatureSource.Osm, true, 40)]
    [InlineData(FeatureSource.Scenery, true, 30)]
    [InlineData(FeatureSource.Gsx, true, 20)]
    [InlineData(FeatureSource.Navdata, true, 10)]
    [InlineData(FeatureSource.Osm, false, 0)]
    public void Rank_prefers_named_then_source_order(FeatureSource src, bool named, int expected)
        => Assert.Equal(expected, AirportFeatureCatalog.Rank(F(FeatureKind.Hangar, named ? "N" : "", 0, 0, src)));

    [Fact]
    public void Features_are_sorted_by_kind_then_name_and_the_version_is_kept()
    {
        var cat = AirportFeatureCatalog.Build("X", "tok", new[] {
            F(FeatureKind.Hangar, "B Hangar", 1, 1, FeatureSource.Osm), F(FeatureKind.Concourse, "Concourse A", 2, 2, FeatureSource.Osm), F(FeatureKind.Hangar, "A Hangar", 3, 3, FeatureSource.Osm) });
        Assert.Equal("tok", cat.Version);
        Assert.Equal(new[] { "Concourse A", "A Hangar", "B Hangar" }, cat.Features.Select(f => f.Name));
    }
}
