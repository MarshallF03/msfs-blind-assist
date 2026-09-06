using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Opt-in "Passing Concourse B, on the left." Polls own position every 2 s on the UI-thread
/// timer (same shape as GroundTrafficMonitor — the taxi position stream is taxi-scoped and
/// is OFF when no route is loaded, so this cannot ride it). Resolves the airport at most every
/// 30 s, reads the catalog from the cache (a build there is bounded and off the hot path:
/// once per airport), and hands the ranked list to the pure gate. Queued speech only.
/// </summary>
public sealed class AirportSurroundingsMonitor : IDisposable
{
    private const int PollMs = 2000;
    private static readonly TimeSpan IcaoRefresh = TimeSpan.FromSeconds(30);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Func<IAirportDataProvider?> _provider;
    private readonly SurroundingsCatalogCache _cache;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PassingCalloutGate _gate = new();

    private string _icao = "";
    private DateTime _icaoAt = DateTime.MinValue;
    private bool _baselined;

    public bool Enabled { get; set; }
    /// <summary>True while callouts must stay silent (takeoff assist, rollout, docking, lineup/hold, announcer suppressed).</summary>
    public Func<bool>? SuppressCheck { get; set; }

    public AirportSurroundingsMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim, Func<IAirportDataProvider?> provider, SurroundingsCatalogCache cache)
    {
        _announcer = announcer; _sim = sim; _provider = provider; _cache = cache;
        _timer = new System.Windows.Forms.Timer { Interval = PollMs };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Aircraft switch, reconnect, turnaround liftoff: forget what was seen and re-baseline.</summary>
    public void Reset() { _gate.Reset(); _baselined = false; _icao = ""; _icaoAt = DateTime.MinValue; }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!Enabled || !_sim.IsConnected) return;
        if (_sim.LastKnownOnGround != true) { _baselined = false; return; }
        _sim.RequestAircraftPosition();
        var pos = _sim.LastKnownPosition;
        if (pos == null) return;
        var p = pos.Value;
        var provider = _provider();
        if (provider == null) return;

        try
        {
            var now = DateTime.UtcNow;
            if (now - _icaoAt > IcaoRefresh)
            {
                _icaoAt = now;
                var nearby = provider.GetNearbyAirportICAOs(p.Latitude, p.Longitude, 5.0).Where(c => c != null && c.Length == 4).ToList();
                string next = nearby.Count > 0 ? nearby[0] : "";
                if (!string.Equals(next, _icao, StringComparison.OrdinalIgnoreCase)) { _icao = next; _gate.Reset(); _baselined = false; }
            }
            if (_icao.Length == 0) return;
            var catalog = _cache.Get(_icao);
            if (catalog == null || catalog.Features.Count == 0) return;

            double hdgTrue = RelativeDirection.Normalize360(p.HeadingMagnetic + p.MagneticVariation);
            var ranked = SurroundingsReport.Rank(catalog, p.Latitude, p.Longitude, hdgTrue, 250.0);
            if (!_baselined) { _gate.Baseline(ranked, now); _baselined = true; return; }
            if (SuppressCheck?.Invoke() == true || _announcer.Suppressed) return;

            var hit = _gate.Evaluate(ranked, p.GroundSpeedKnots, now);
            if (hit == null) return;
            string phrase = $"Passing {hit.Feature.SpokenName}, {RelativeDirection.Side(hit.RelativeBearingDeg)}.";
            _announcer.Announce(phrase);
            Log.Debug("Surroundings", $"callout {_icao}: {phrase} dist={hit.DistanceMetres:F0} rel={hit.RelativeBearingDeg:F0}");
        }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"monitor tick failed: {ex.Message}");
        }
    }

    public void Dispose() { _timer.Stop(); _timer.Dispose(); }
}
