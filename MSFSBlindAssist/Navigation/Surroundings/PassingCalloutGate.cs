namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Pure decision state for "Passing X, on the left." Fires for one feature per tick at most,
/// nearest first, only when abeam and inside the kind's radius, inside the taxi speed band,
/// at most once per feature per five minutes and once globally per ten seconds. Baseline():
/// anything already in range when the monitor starts is marked as seen — a start-up at the
/// gate must not recite the terminal. No clock inside: the caller passes `now`.
/// </summary>
public sealed class PassingCalloutGate
{
    public const double MinSpeedKts = 2.0, MaxSpeedKts = 40.0;
    public const double AbeamMinDeg = 45.0, AbeamMaxDeg = 135.0;
    public static readonly TimeSpan PerFeatureRepeat = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GlobalGap = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DateTime> _lastByFeature = new(StringComparer.Ordinal);
    private DateTime? _lastAny;

    public static double PassRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Concourse or FeatureKind.Terminal => 150.0,
        FeatureKind.Tower => 200.0,
        _ => 100.0,
    };

    public static bool IsAnnounceable(AirportFeature f) => f.Kind switch
    {
        FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Fbo or FeatureKind.Tower
            or FeatureKind.Fuel or FeatureKind.Cargo or FeatureKind.FireStation => true,
        FeatureKind.Hangar => f.HasName,
        _ => false,
    };

    private static string Key(AirportFeature f) => $"{f.Kind}|{f.SpokenName}|{f.Lat:F4}|{f.Lon:F4}";

    private static bool InRange(NearbyFeature n)
    {
        double abs = Math.Abs(n.RelativeBearingDeg);
        return n.DistanceMetres <= PassRadiusMetres(n.Feature.Kind) && abs >= AbeamMinDeg && abs <= AbeamMaxDeg;
    }

    public void Baseline(IEnumerable<NearbyFeature> inRange, DateTime now)
    {
        foreach (var n in inRange)
            if (IsAnnounceable(n.Feature) && n.DistanceMetres <= PassRadiusMetres(n.Feature.Kind))
                _lastByFeature[Key(n.Feature)] = now;
    }

    public NearbyFeature? Evaluate(IReadOnlyList<NearbyFeature> ranked, double groundSpeedKts, DateTime now)
    {
        if (groundSpeedKts < MinSpeedKts || groundSpeedKts > MaxSpeedKts) return null;
        if (_lastAny is DateTime last && now - last < GlobalGap) return null;
        foreach (var n in ranked.OrderBy(x => x.DistanceMetres))   // nearest first, whatever order the caller used
        {
            if (!IsAnnounceable(n.Feature) || !InRange(n)) continue;
            string key = Key(n.Feature);
            if (_lastByFeature.TryGetValue(key, out var seen) && now - seen < PerFeatureRepeat) continue;
            _lastByFeature[key] = now;
            _lastAny = now;
            return n;
        }
        return null;
    }

    public void Reset() { _lastByFeature.Clear(); _lastAny = null; }
}
