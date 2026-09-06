using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// TryGetCached is the non-building counterpart to Get() — AirportSurroundingsMonitor's
/// UI-thread timer tick must never trigger a possibly-slow first-time scenery scan/DB read
/// itself, so it reads through TryGetCached and only kicks off Get() on a background thread.
/// </summary>
public class SurroundingsCatalogCacheTests
{
    private static AirportFeature F(string name)
        => new() { Kind = FeatureKind.Hangar, Name = name, Lat = 0, Lon = 0, Source = FeatureSource.Navdata };

    [Fact]
    public void TryGetCached_misses_before_Get_has_ever_built_the_icao()
    {
        var cache = new SurroundingsCatalogCache
        {
            FeatureSupplier = _ => new[] { F("Hangar 1") },
            VersionSupplier = _ => "navdata",
        };

        bool hit = cache.TryGetCached("KTIW", out var catalog);

        Assert.False(hit);
        Assert.Null(catalog);
    }

    [Fact]
    public void TryGetCached_hits_the_same_instance_Get_built()
    {
        var cache = new SurroundingsCatalogCache
        {
            FeatureSupplier = _ => new[] { F("Hangar 1") },
            VersionSupplier = _ => "navdata",
        };

        var built = cache.Get("KTIW");
        bool hit = cache.TryGetCached("KTIW", out var cached);

        Assert.NotNull(built);
        Assert.True(hit);
        Assert.Same(built, cached);
    }

    [Fact]
    public void TryGetCached_reports_a_miss_once_the_version_supplier_moves_to_a_higher_tier()
    {
        // "navdata" (tier 0) -> "api:1" (tier 2) is an upgrade under GateDataSource.TokenTier,
        // so the cached navdata-tier catalog is stale and must never be handed back silently.
        string token = "navdata";
        var cache = new SurroundingsCatalogCache
        {
            FeatureSupplier = _ => new[] { F("Hangar 1") },
            VersionSupplier = _ => token,
        };

        cache.Get("KTIW");
        Assert.True(cache.TryGetCached("KTIW", out _));

        token = "api:1";
        bool hit = cache.TryGetCached("KTIW", out var stale);

        Assert.False(hit);
        Assert.Null(stale);
    }
}
