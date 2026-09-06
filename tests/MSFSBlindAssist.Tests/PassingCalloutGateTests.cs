using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class PassingCalloutGateTests
{
    private static readonly DateTime T0 = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static AirportFeature F(FeatureKind k, string name = "X") => new() { Kind = k, Name = name, Lat = 1, Lon = 1, Source = FeatureSource.Osm };
    private static NearbyFeature N(AirportFeature f, double dist, double rel) => new(f, dist, rel);

    [Fact]
    public void Fires_for_an_announceable_feature_abeam_inside_its_radius()
    {
        var gate = new PassingCalloutGate();
        var hit = gate.Evaluate(new[] { N(F(FeatureKind.Concourse, "Concourse B"), 120, 90) }, 10, T0);
        Assert.Equal("Concourse B", hit?.Feature.Name);
    }

    [Theory]
    [InlineData(30.0)]   // ahead, not abeam
    [InlineData(150.0)]  // behind
    public void Does_not_fire_outside_the_abeam_window(double rel)
    {
        var gate = new PassingCalloutGate();
        Assert.Null(gate.Evaluate(new[] { N(F(FeatureKind.Concourse), 100, rel) }, 10, T0));
    }

    [Fact]
    public void Does_not_fire_beyond_the_kind_radius_or_for_unannounceable_kinds()
    {
        var gate = new PassingCalloutGate();
        Assert.Null(gate.Evaluate(new[] { N(F(FeatureKind.Concourse), 160, 90) }, 10, T0));   // Concourse radius 150
        Assert.Null(gate.Evaluate(new[] { N(F(FeatureKind.Hangar, ""), 50, 90) }, 10, T0));    // unnamed hangar
        Assert.Null(gate.Evaluate(new[] { N(F(FeatureKind.Apron, "Ramp"), 50, 90) }, 10, T0));  // aprons never
        Assert.NotNull(gate.Evaluate(new[] { N(F(FeatureKind.Hangar, "ATP Hangar"), 50, -90) }, 10, T0));
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(45.0)]
    public void Silent_outside_the_speed_band(double gs)
    {
        var gate = new PassingCalloutGate();
        Assert.Null(gate.Evaluate(new[] { N(F(FeatureKind.Tower), 100, 90) }, gs, T0));
    }

    [Fact]
    public void One_callout_per_ten_seconds_and_one_per_feature_per_five_minutes()
    {
        var gate = new PassingCalloutGate();
        var tower = F(FeatureKind.Tower, "Control Tower");
        var fuel = F(FeatureKind.Fuel, "Fuel");
        Assert.NotNull(gate.Evaluate(new[] { N(tower, 100, 90), N(fuel, 50, -90) }, 10, T0));                    // nearest first: fuel
        Assert.Null(gate.Evaluate(new[] { N(tower, 100, 90) }, 10, T0.AddSeconds(5)));                          // global gap
        Assert.Equal("Control Tower", gate.Evaluate(new[] { N(tower, 100, 90) }, 10, T0.AddSeconds(11))?.Feature.Name);
        Assert.Null(gate.Evaluate(new[] { N(fuel, 50, -90) }, 10, T0.AddMinutes(4)));                            // fuel repeat blocked
        Assert.NotNull(gate.Evaluate(new[] { N(fuel, 50, -90) }, 10, T0.AddMinutes(6)));
    }

    [Fact]
    public void Baseline_marks_features_already_in_range_so_startup_at_the_gate_is_silent()
    {
        var gate = new PassingCalloutGate();
        var conc = F(FeatureKind.Concourse, "Concourse B");
        gate.Baseline(new[] { N(conc, 60, 90) }, T0);
        Assert.Null(gate.Evaluate(new[] { N(conc, 60, 90) }, 10, T0.AddSeconds(30)));
        Assert.NotNull(gate.Evaluate(new[] { N(conc, 60, 90) }, 10, T0.AddMinutes(6)));
        gate.Reset();
        Assert.NotNull(gate.Evaluate(new[] { N(conc, 60, 90) }, 10, T0.AddMinutes(7)));
    }

    [Fact]
    public void Baseline_leaves_an_ahead_feature_unmarked_so_it_speaks_when_it_comes_abeam()
    {
        var gate = new PassingCalloutGate();
        var conc = F(FeatureKind.Concourse, "Concourse B");
        gate.Baseline(new[] { N(conc, 60, 10) }, T0);                  // dead ahead at pushback: not "seen"
        Assert.Null(gate.Evaluate(new[] { N(conc, 60, 10) }, 10, T0.AddSeconds(30)));      // still ahead: nothing
        Assert.Equal("Concourse B", gate.Evaluate(new[] { N(conc, 60, 90) }, 10, T0.AddSeconds(60))?.Feature.Name); // now abeam: speaks
    }
}
