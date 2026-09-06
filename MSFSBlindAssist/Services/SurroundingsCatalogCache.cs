using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One AirportFeatureCatalog per ICAO. Same invalidation shape as TaxiGuidanceManager's
/// Where-Am-I graph cache: a version token compared through GateDataSource.ShouldRebuildGateList
/// (rebuild on upgrade/refresh, never on a transient GSX downgrade) plus explicit Invalidate()
/// from the augmentation fetch. Get() may build, so call it from a hotkey handler or a
/// background thread — never from a per-frame position update.
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

    public void Invalidate(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        lock (_lock) _byIcao.Remove(icao);
    }

    public void Clear() { lock (_lock) _byIcao.Clear(); }
}
