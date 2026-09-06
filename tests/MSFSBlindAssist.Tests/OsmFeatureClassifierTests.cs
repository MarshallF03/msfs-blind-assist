using System.Text.Json;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class OsmFeatureClassifierTests
{
    private static readonly Lazy<List<(long Id, AirportFeature? Feature)>> Parsed = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-kjac.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("elements").EnumerateArray()
            .Select(e => (e.GetProperty("id").GetInt64(), OsmFeatureClassifier.Classify(e))).ToList();
    });

    private static AirportFeature? Get(long id) => Parsed.Value.Single(p => p.Id == id).Feature;

    [Theory]
    [InlineData(1, FeatureKind.Fbo, "General Aviation Terminal")]
    [InlineData(2, FeatureKind.Terminal, "Baggage Claim")]
    [InlineData(3, FeatureKind.Apron, "Commercial Ramp")]
    [InlineData(4, FeatureKind.DeicePad, "De-icing pad")]
    [InlineData(5, FeatureKind.Hangar, "")]
    [InlineData(6, FeatureKind.Tower, "Control Tower")]
    [InlineData(7, FeatureKind.Fuel, "Chevron")]
    [InlineData(8, FeatureKind.FireStation, "Atlanta Fire Rescue Station 35")]
    [InlineData(9, FeatureKind.Concourse, "Concourse B")]
    [InlineData(10, FeatureKind.Cargo, "FedEx")]
    [InlineData(11, FeatureKind.Hangar, "Delta TechOps Hangar 2")]
    [InlineData(12, FeatureKind.Fbo, "Signature Flight Support")]
    [InlineData(13, FeatureKind.Office, "Hapeville City Hall")]
    [InlineData(14, FeatureKind.Helipad, "")]
    public void Classifies_kind_and_name(long id, FeatureKind kind, string name)
    {
        var f = Get(id);
        Assert.NotNull(f);
        Assert.Equal(kind, f!.Kind);
        Assert.Equal(name, f.Name);
        Assert.Equal(FeatureSource.Osm, f.Source);
    }

    [Fact]
    public void Taxiways_and_nameless_buildings_are_not_features()
    {
        Assert.Null(Get(15));
        Assert.Null(Get(16));
    }

    [Fact]
    public void Operator_becomes_detail_and_apron_ways_keep_their_footprint()
    {
        Assert.Equal("operator Jackson Hole Aviation LLC", Get(1)!.Detail);
        var deice = Get(4)!;
        Assert.NotNull(deice.Footprint);
        Assert.Equal(4, deice.Footprint!.Count);          // closing duplicate vertex dropped
        Assert.InRange(deice.Lat, 43.6050, 43.6056);       // representative point inside
    }

    [Fact]
    public void Query_scopes_new_tags_to_the_aerodrome_area_and_keeps_the_old_four()
    {
        string q = OsmTaxiSource.BuildQuery(47.27, -122.56, "KTIW");
        Assert.Contains("area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"KTIW\"]->.ad;", q);
        Assert.Contains("way[\"aeroway\"=\"taxiway\"](around:5000,47.27,-122.56);", q);
        Assert.Contains("nwr[\"aeroway\"~\"^(terminal|hangar|apron|tower|control_tower|fuel|helipad)$\"](area.ad);", q);
        Assert.Contains("nwr[\"amenity\"~\"^(fuel|fire_station)$\"](area.ad);", q);
        Assert.EndsWith("out tags geom center;", q);
        string fb = OsmTaxiSource.BuildFeatureFallbackQuery(47.27, -122.56);
        Assert.Contains("(around:3000,47.27,-122.56)", fb);
        Assert.DoesNotContain("taxiway", fb);
    }

    [Fact]
    public void Parse_fills_features_and_still_fills_taxiways()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-kjac.json");
        var data = OsmTaxiSource.Parse(File.ReadAllText(path));
        Assert.Equal(14, data.Features.Count);
        Assert.Single(data.Taxiways);
    }
}
