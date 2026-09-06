using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One AirportFeatureCatalog per ICAO. Same invalidation shape as TaxiGuidanceManager's
/// Where-Am-I graph cache: a version token compared through GateDataSource.ShouldRebuildGateList
/// (rebuild on upgrade/refresh, never on a transient GSX downgrade) plus explicit Invalidate()
/// from the augmentation fetch. Get() may build — including a first-time scenery scan/DB read
/// under a lock — so it must run on a thread-pool thread (a hotkey handler's own Task.Run, or
/// a background build kicked off from a timer tick), NEVER on the UI thread and NEVER from a
/// per-frame position update. TryGetCached() is the non-building counterpart for a UI-thread
/// timer that must not itself trigger that build.
///
/// Two explicit Clear() sites exist beside the token-based invalidations above: MainForm's
/// RefreshDatabaseProvider (a database switch changes which airport data every catalog was
/// built from) and ApplyRuntimeSettings (toggling SceneryIndexEnabled/TaxiAugmentEnabled must
/// take effect on the next Alt+L rather than serving a catalog built under the old setting).
/// </summary>
public sealed class SurroundingsCatalogCache
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AirportFeatureCatalog> _byIcao = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All tiers for an ICAO, already merged by the caller into one list. Required.</summary>
    public Func<string, IReadOnlyList<AirportFeature>> FeatureSupplier { get; set; } = _ => Array.Empty<AirportFeature>();
    /// <summary>Gate-list token (GateDataSource.GetGateListVersion) plus anything else that should force a rebuild; null → "none".</summary>
    public Func<string, string>? VersionSupplier { get; set; }

    public AirportFeatureCatalog? Get(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return null;
        string token;
        try { token = VersionSupplier?.Invoke(icao) ?? "none"; } catch { token = "none"; }

        lock (_lock)
        {
            if (_byIcao.TryGetValue(icao, out var cached) && !GateDataSource.ShouldRebuildGateList(cached.Version, token))
                return cached;
        }

        AirportFeatureCatalog built;
        try
        {
            built = AirportFeatureCatalog.Build(icao, token, FeatureSupplier(icao));
        }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"catalog build failed for {icao}: {ex.Message}");
            lock (_lock) return _byIcao.TryGetValue(icao, out var previous) ? previous : null;
        }
        lock (_lock) _byIcao[icao] = built;
        Log.Debug("Surroundings", $"catalog {icao}: {built.Features.Count} features, token={token}");
        return built;
    }

    /// <summary>
    /// A non-building read: true and <paramref name="catalog"/> set only when a catalog for
    /// <paramref name="icao"/> is already cached AND not stale under the same
    /// GateDataSource.ShouldRebuildGateList staleness check <see cref="Get"/> applies — never
    /// calls FeatureSupplier. For a caller (AirportSurroundingsMonitor's UI-thread timer tick)
    /// that must never trigger the possibly-slow first-time scenery scan/DB read itself; it
    /// kicks off that build on a thread-pool thread instead and revisits this on a later tick.
    /// </summary>
    public bool TryGetCached(string icao, out AirportFeatureCatalog? catalog)
    {
        catalog = null;
        if (string.IsNullOrWhiteSpace(icao)) return false;
        string token;
        try { token = VersionSupplier?.Invoke(icao) ?? "none"; } catch { token = "none"; }

        lock (_lock)
        {
            if (_byIcao.TryGetValue(icao, out var cached) && !GateDataSource.ShouldRebuildGateList(cached.Version, token))
            {
                catalog = cached;
                return true;
            }
        }
        return false;
    }

    public void Invalidate(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        lock (_lock) _byIcao.Remove(icao);
    }

    public void Clear() { lock (_lock) _byIcao.Clear(); }
}
