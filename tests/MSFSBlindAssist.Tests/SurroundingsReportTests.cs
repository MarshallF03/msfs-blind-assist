using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsReportTests
{
    // Own ship at the origin of a flat local frame: 0.0009° lat ≈ 100 m, 0.0009° lon ≈ 100 m at lat 0.
    private const double Lat = 0.0, Lon = 0.0;
    private static string Metres(double m) => $"{Math.Round(m / 10) * 10} metres";

    private static AirportFeature F(FeatureKind k, string name, double dLatMetres, double dLonMetres, FeatureSource src = FeatureSource.Osm, IReadOnlyList<LatLon>? fp = null)
        => new() { Kind = k, Name = name, Lat = Lat + dLatMetres / 111_320.0, Lon = Lon + dLonMetres / 111_320.0, Source = src, Footprint = fp };

    private static AirportFeatureCatalog Cat(params AirportFeature[] fs) => AirportFeatureCatalog.Build("KTIW", "v", fs);

    [Fact]
    public void Compose_leads_with_where_am_i_then_nearest_features_with_direction_and_distance()
    {
        var cat = Cat(
            F(FeatureKind.Tower, "Control Tower", 200, 0),          // north, heading north → ahead
            F(FeatureKind.Fbo, "Narrows Aviation", 0, 100),         // east → to the right
            F(FeatureKind.Fuel, "Fuel", -150, -150));               // south-west → behind and to the left
        string s = SurroundingsReport.Compose("Taxiway A at KTIW.", "KTIW", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("Taxiway A at KTIW. Narrows Aviation, to the right, 100 metres. Control Tower, ahead, 200 metres. Fuel, behind and to the left, 210 metres.", s);
    }

    [Fact]
    public void Compose_names_the_zone_and_excludes_it_from_the_list()
    {
        var square = new[] { new LatLon(-0.0005, -0.0005), new LatLon(-0.0005, 0.0005), new LatLon(0.0005, 0.0005), new LatLon(0.0005, -0.0005) };
        var cat = Cat(F(FeatureKind.Apron, "Commercial Ramp", 0, 0, fp: square), F(FeatureKind.Terminal, "General Aviation Terminal", 0, 80));
        string s = SurroundingsReport.Compose("Not on a known taxiway or ramp at KJAC.", "KJAC", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("Not on a known taxiway or ramp at KJAC. On the Commercial Ramp. General Aviation Terminal, to the right, 80 metres.", s);
    }

    [Fact]
    public void Zone_falls_back_to_a_concourse_within_120m()
    {
        var cat = Cat(F(FeatureKind.Concourse, "Concourse B", 100, 0));
        Assert.Equal("Concourse B", SurroundingsReport.Zone(cat, Lat, Lon)?.Name);
        Assert.Null(SurroundingsReport.Zone(Cat(F(FeatureKind.Concourse, "Concourse B", 130, 0)), Lat, Lon));
        string s = SurroundingsReport.Compose("Gate B2 at KATL.", "KATL", cat, Lat, Lon, 0.0, Metres);
        Assert.StartsWith("Gate B2 at KATL. At Concourse B.", s);
    }

    [Fact]
    public void Compose_caps_at_four_and_one_per_kind_except_hangars_and_fbos()
    {
        var cat = Cat(
            F(FeatureKind.Fuel, "Fuel", 50, 0), F(FeatureKind.Fuel, "Fuel", -60, 0),          // 110 m apart: survives the merge, dropped by one-per-kind
            F(FeatureKind.Hangar, "Hangar 1", 70, 0), F(FeatureKind.Hangar, "Hangar 2", 0, 80), // 106 m apart: both survive
            F(FeatureKind.Tower, "Control Tower", 90, 0), F(FeatureKind.Cargo, "Cargo ramp", 95, 0));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. Fuel, ahead, 50 metres. Hangar 1, ahead, 70 metres. Hangar 2, to the right, 80 metres. Control Tower, ahead, 90 metres.", s);
    }

    [Fact]
    public void Two_unnamed_hangars_collapse_to_hangars_at_the_nearer_distance()
    {
        var cat = Cat(F(FeatureKind.Hangar, "", 0, -80), F(FeatureKind.Hangar, "", 0, -140));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. Hangars, to the left, 80 metres.", s);
    }

    [Fact]
    public void Compose_reports_empty_range_and_missing_catalog()
    {
        Assert.Equal("X. Nothing within 600 metres.", SurroundingsReport.Compose("X.", "X", Cat(F(FeatureKind.Tower, "T", 700, 0)), Lat, Lon, 0.0, Metres));
        Assert.Equal("X. No surroundings data for KXYZ.", SurroundingsReport.Compose("X.", "KXYZ", null, Lat, Lon, 0.0, Metres));
        Assert.Equal("X. No surroundings data for KXYZ.", SurroundingsReport.Compose("X.", "KXYZ", AirportFeatureCatalog.Empty("KXYZ"), Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void Sections_carry_facts_first_then_everything_within_1km_nearest_first_with_detail()
    {
        var cat = Cat(
            new AirportFeature { Kind = FeatureKind.Concourse, Name = "Concourse B", Lat = Lat + 300 / 111_320.0, Lon = Lon, Source = FeatureSource.Navdata, Detail = "Delta gates" },
            F(FeatureKind.Tower, "Control Tower", 0, 150), F(FeatureKind.Hangar, "Far Hangar", 1200, 0));
        var sections = SurroundingsReport.BuildSections("KATL", cat, "Avgas. Tower 118.5.", Lat, Lon, 0.0, Metres);
        Assert.Equal("Airport", sections[0].Heading);
        Assert.Equal(new[] { "Avgas. Tower 118.5." }, sections[0].Items);
        Assert.Equal("Nearby, 2 items", sections[1].Heading);
        Assert.Equal(new[] { "Control Tower, to the right, 150 metres", "Concourse B, Delta gates, ahead, 300 metres" }, sections[1].Items);
    }
}
