# Airport Surroundings Awareness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tell a pilot on the ground what is around the aircraft — terminals, concourses, FBOs, hangars, tower, fuel, cargo, aprons — via an `Alt+L` readout, a `Ctrl+Shift+L` list window, and opt-in passing callouts, from navdata, OpenStreetMap and the installed scenery package — and let the pilot taxi TO one of those places by resolving it onto a navdata stand.

**Architecture:** Three pure sources (`NavdataFeatureSource`, `OsmFeatureSource` inside the existing Overpass pipeline, `SceneryFeatureSource` over the package's placement BGLs) produce `AirportFeature` lists; `AirportFeatureCatalog.Build` merges them per airport; a `SurroundingsCatalogCache` on MainForm holds one catalog per ICAO under the same invalidation tokens Where-Am-I uses. Three consumers read the catalog and never write it: `SurroundingsReport` (the `Alt+L` sentence and the window sections), `AirportSurroundingsMonitor` + `PassingCalloutGate` (callouts). Features are readout-only and never touch `TaxiGraph`.

**Tech Stack:** C# 13 / .NET 10 WinForms, Microsoft.Data.Sqlite (navdatareader schema), System.Text.Json (Overpass), xUnit (`tests/MSFSBlindAssist.Tests`).

**Spec:** `docs/superpowers/specs/2026-09-06-airport-surroundings-design.md`

## Global Constraints

- Build with `dotnet build MSFSBlindAssist.sln -c Debug` — NEVER the bare `.csproj` (it silently builds AnyCPU into the wrong folder). Verify `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` timestamp after a build meant to be run.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Filter one class with `--filter "FullyQualifiedName~ClassName"`.
- Branch is `feature/airport-surroundings` (off `upstream/main`). Never commit to `francesco/feat/cows-da40` or `main`. Commit after every task with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Surroundings features are READOUT ONLY: never passed to `TaxiGraph.Build`, never a `TaxiNode`, never a routing/hold-short input. A feature may be a DESTINATION only by resolving onto navdata pavement (`FeatureDestinationResolver`: a stand within 150 m, else a taxi node within 100 m) — the route target is the stand, never the building.
- OSM data stays IN-MEMORY (`TaxiDataCache`); only the scenery index (the user's own local files) is written to disk, under `%APPDATA%\MSFSBlindAssist\scenery-index\`.
- The OSM query is scoped to the `aeroway=aerodrome` AREA; the radius fallback is bbox-filtered. A bare radius admits road gas stations.
- A model name reaches speech ONLY after `SceneryModelNameClassifier` has produced human text.
- Passing callouts: `Announce` (queued) only, never `AnnounceImmediate`; baseline-first; abeam-only; throttled; suppressed under takeoff assist, landing rollout, docking, LiningUp/HoldShort/ProgressiveHold, and `announcer.Suppressed`.
- The scenery scan runs only over the folders named in `airport.scenery_local_path`, never the whole Community tree, never on the UI thread, never on a position update.
- Screen-reader rule: no announcement for opening a window (the reader speaks it); state announcements carry no advice tail.
- Every log write goes through `Utils/Logging/Log` (`Log.Warn("Surroundings", …)`); every log path through `AppLogs.PathFor`.
- Pure logic gets xUnit characterization tests; sim-facing paths get the in-sim test plan in the PR body. Never commit a payware BGL or a full Overpass dump as a fixture — synthetic BGL bytes built in the test, trimmed ODbL-attributed OSM excerpts only.
- Hotkey ids `9219` (`Alt+L`) and `9220` (`Ctrl+Shift+L`) — verified unused on 2026-09-06 (`grep -n '9219\|9220' MSFSBlindAssist/Hotkeys/HotkeyManager.cs` returns nothing). Re-run that grep before Task 6.

---

## File Structure

**New — model and geometry (`MSFSBlindAssist/Navigation/Surroundings/`)**
- `AirportFeature.cs` — `FeatureKind`, `FeatureSource`, `LatLon`, `AirportFeature`, `FeatureKindWords`.
- `SurroundingsGeometry.cs` — distance (footprint-aware), containment, relative bearing, centroid.
- `AirportFeatureCatalog.cs` — merge/dedupe, rank, `Version`.
- `SurroundingsReport.cs` — `NearbyFeature`, ranking, zone, the `Alt+L` sentence, the window sections.
- `PassingCalloutGate.cs` — pure callout state machine.
- `FeatureDestinationResolver.cs` — a place → the navdata stand/node the route actually ends at.

**New — sources**
- `MSFSBlindAssist/Navigation/Surroundings/NavdataFeatureSource.cs` — concourse inference, stand clusters, helipads.
- `MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs` — GSX `TerminalName` grouping.
- `MSFSBlindAssist/Services/TaxiAugment/OsmFeatureClassifier.cs` — OSM element → `AirportFeature`.
- `MSFSBlindAssist/Services/SceneryIndex/BglPlacementReader.cs`, `ModelLibNameReader.cs`, `SceneryModelNameClassifier.cs`, `SceneryPackageLocator.cs`, `SceneryPackageIndexer.cs` — the scenery tier.

**New — services / UI**
- `MSFSBlindAssist/Services/RelativeDirection.cs` — lifted from `GroundTrafficMonitor`.
- `MSFSBlindAssist/Services/SurroundingsCatalogCache.cs` — per-ICAO catalog cache with token invalidation.
- `MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs` — timer-driven passing callouts.
- `MSFSBlindAssist/Database/Models/AirportFacilities.cs` + `MSFSBlindAssist/Database/IAirportFacilitiesProvider.cs` — the new navdata reads, as a SEPARATE interface (not a widening of `IAirportDataProvider`).

**Modified**
- `MSFSBlindAssist/Services/GroundTrafficMonitor.cs` — call `RelativeDirection.Describe`.
- `MSFSBlindAssist/Database/LittleNavMapProvider.cs` — implement `IAirportFacilitiesProvider`.
- `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` — pass-through facilities + `GetOnlineFeatures`.
- `MSFSBlindAssist/Services/TaxiAugment/OsmTaxiSource.cs`, `AirportTaxiData.cs` — widened query, `Features` list.
- `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`, `MainForm.Hotkeys.cs`, `MainForm.Announcers.cs`, `MainForm.cs`, `MainForm.AircraftSwitch.cs` — hotkeys, composer, wiring, resets.
- `MSFSBlindAssist/Forms/SayIntentionsInfoForm.cs` — optional window title.
- `MSFSBlindAssist/Settings/UserSettings.cs`, `Forms/Settings/TaxiGuidancePanel.cs` — two settings.
- `docs/taxi-guidance.md`, `docs/hotkey-system.md`, `CLAUDE.md`, `changelog.d/`.

**Tests (`tests/MSFSBlindAssist.Tests/`)**
- `RelativeDirectionTests.cs`, `SurroundingsGeometryTests.cs`, `AirportFeatureCatalogTests.cs`, `NavdataFeatureSourceTests.cs`, `SurroundingsReportTests.cs`, `OsmFeatureClassifierTests.cs`, `PassingCalloutGateTests.cs`, `BglPlacementReaderTests.cs`, `ModelLibNameReaderTests.cs`, `SceneryModelNameClassifierTests.cs`, `SceneryPackageLocatorTests.cs`, `GsxTerminalFeatureSourceTests.cs`, `FeatureDestinationResolverTests.cs`, and fixture `Fixtures/osm-features-kjac.json`.

---

### Task 1: Lift `RelativeDirection` out of `GroundTrafficMonitor`

**Files:**
- Create: `MSFSBlindAssist/Services/RelativeDirection.cs`
- Modify: `MSFSBlindAssist/Services/GroundTrafficMonitor.cs:310,379,464-475`
- Test: `tests/MSFSBlindAssist.Tests/RelativeDirectionTests.cs`

**Interfaces:**
- Produces: `static string RelativeDirection.Describe(double relBearingDeg)` (0 = ahead, 90 = right, accepts any angle, also negatives); `static string RelativeDirection.Side(double relBearingDeg)` → `"on the left"` / `"on the right"` / `"ahead"` / `"behind"`; `static double RelativeDirection.Normalize360(double deg)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MSFSBlindAssist.Tests/RelativeDirectionTests.cs
// Pins the thresholds GroundTrafficMonitor.DescribeDirection carried (20/70/110/160) so the
// ground-traffic phrasing cannot drift now that the surroundings readout shares it.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RelativeDirectionTests
{
    [Theory]
    [InlineData(0, "ahead")]
    [InlineData(20, "ahead")]
    [InlineData(340, "ahead")]
    [InlineData(21, "ahead and to the right")]
    [InlineData(70, "ahead and to the right")]
    [InlineData(-45, "ahead and to the left")]
    [InlineData(90, "to the right")]
    [InlineData(270, "to the left")]
    [InlineData(111, "behind and to the right")]
    [InlineData(-150, "behind and to the left")]
    [InlineData(180, "behind")]
    [InlineData(165, "behind")]
    public void Describe_uses_the_ground_traffic_thresholds(double rel, string expected)
        => Assert.Equal(expected, RelativeDirection.Describe(rel));

    [Theory]
    [InlineData(45, "on the right")]
    [InlineData(135, "on the right")]
    [InlineData(-90, "on the left")]
    [InlineData(225, "on the left")]
    [InlineData(10, "ahead")]
    [InlineData(-170, "behind")]
    public void Side_reports_left_or_right_with_ahead_and_behind_caps(double rel, string expected)
        => Assert.Equal(expected, RelativeDirection.Side(rel));

    [Fact]
    public void Normalize360_wraps_negatives_and_overflow()
    {
        Assert.Equal(350.0, RelativeDirection.Normalize360(-10));
        Assert.Equal(10.0, RelativeDirection.Normalize360(370));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RelativeDirectionTests"`
Expected: build error `The type or namespace name 'RelativeDirection' could not be found`.

- [ ] **Step 3: Write the implementation**

```csharp
// MSFSBlindAssist/Services/RelativeDirection.cs
namespace MSFSBlindAssist.Services;

/// <summary>
/// The one relative-bearing phrasing in the app. Lifted from GroundTrafficMonitor so the
/// surroundings readout ("Concourse B, ahead and to the right") and the ground-traffic
/// summary ("Speedbird 12, to the left, 200 metres") say the same words for the same
/// angle. Thresholds pinned by RelativeDirectionTests.
/// </summary>
public static class RelativeDirection
{
    public static double Normalize360(double deg) => ((deg % 360.0) + 360.0) % 360.0;

    /// <param name="relBearingDeg">0 = dead ahead, 90 = hard right, 180 = dead behind; any range.</param>
    public static string Describe(double relBearingDeg)
    {
        double rel = Normalize360(relBearingDeg);
        bool right = rel < 180.0;
        double abs = right ? rel : (360.0 - rel);

        if (abs <= 20.0) return "ahead";
        if (abs <= 70.0) return right ? "ahead and to the right" : "ahead and to the left";
        if (abs <= 110.0) return right ? "to the right" : "to the left";
        if (abs <= 160.0) return right ? "behind and to the right" : "behind and to the left";
        return "behind";
    }

    /// <summary>Side only — the passing-callout form ("Passing Concourse B, on the left.").</summary>
    public static string Side(double relBearingDeg)
    {
        double rel = Normalize360(relBearingDeg);
        bool right = rel < 180.0;
        double abs = right ? rel : (360.0 - rel);
        if (abs <= 20.0) return "ahead";
        if (abs >= 160.0) return "behind";
        return right ? "on the right" : "on the left";
    }
}
```

In `GroundTrafficMonitor.cs`: delete the private `DescribeDirection` method (lines 464-475) and change both call sites (`:310` and `:379`) from `DescribeDirection(relBearing)` to `RelativeDirection.Describe(relBearing)`. Leave `NormalizeDeg` in place (other callers).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RelativeDirectionTests"`
Expected: 20 passed. Then `dotnet build MSFSBlindAssist.sln -c Debug` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/RelativeDirection.cs MSFSBlindAssist/Services/GroundTrafficMonitor.cs tests/MSFSBlindAssist.Tests/RelativeDirectionTests.cs
git commit -m "refactor: lift relative-direction phrasing out of GroundTrafficMonitor

One phrasing app-wide ahead of the surroundings readout that will share it.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Feature model and geometry

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/AirportFeature.cs`
- Create: `MSFSBlindAssist/Navigation/Surroundings/SurroundingsGeometry.cs`
- Test: `tests/MSFSBlindAssist.Tests/SurroundingsGeometryTests.cs`

**Interfaces:**
- Produces (namespace `MSFSBlindAssist.Navigation.Surroundings`):
  - `enum FeatureKind { Terminal, Concourse, Fbo, Hangar, Tower, Fuel, Cargo, FireStation, Helipad, Apron, DeicePad, Office, Other }`
  - `enum FeatureSource { Navdata, Gsx, Osm, Scenery }`
  - `readonly record struct LatLon(double Lat, double Lon)`
  - `sealed class AirportFeature { FeatureKind Kind; string Name; double Lat; double Lon; IReadOnlyList<LatLon>? Footprint; FeatureSource Source; string? Detail; bool HasName; string SpokenName; }` (all `init`)
  - `static string FeatureKindWords.Generic(FeatureKind)`
  - `static double SurroundingsGeometry.DistanceMetres(double lat, double lon, AirportFeature f)`
  - `static bool SurroundingsGeometry.Contains(IReadOnlyList<LatLon> polygon, double lat, double lon)`
  - `static double SurroundingsGeometry.RelativeBearingDeg(double ownLat, double ownLon, double ownHeadingTrue, double lat, double lon)` → -180..180
  - `static LatLon SurroundingsGeometry.Centroid(IReadOnlyList<LatLon> pts)`
- Consumes: `TaxiGeo.HaversineMeters`, `TaxiGeo.BearingDeg`, `TaxiGeo.PointToSegmentMeters` (`MSFSBlindAssist.Services.TaxiAugment.TaxiGeo`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/SurroundingsGeometryTests.cs
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
        Assert.InRange(rel, expected - 0.5, expected + 0.5);
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SurroundingsGeometryTests"`
Expected: build error, namespace `MSFSBlindAssist.Navigation.Surroundings` not found.

- [ ] **Step 3: Write the model**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/AirportFeature.cs
namespace MSFSBlindAssist.Navigation.Surroundings;

public enum FeatureKind { Terminal, Concourse, Fbo, Hangar, Tower, Fuel, Cargo, FireStation, Helipad, Apron, DeicePad, Office, Other }

/// <summary>Where a feature came from. Also the merge tie-break order in AirportFeatureCatalog.</summary>
public enum FeatureSource { Navdata, Gsx, Osm, Scenery }

public readonly record struct LatLon(double Lat, double Lon);

/// <summary>
/// One thing on the airport a pilot might want to know is beside them. READOUT ONLY: never a
/// graph node, never a routing input, never a hold-short input (spec invariant). Name is what
/// is SPOKEN, verbatim — a source must hand over human text, never a raw model or tag string.
/// </summary>
public sealed class AirportFeature
{
    public required FeatureKind Kind { get; init; }
    public string Name { get; init; } = "";
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    /// <summary>Closed polygon (OSM apron/terminal way) or null for a point feature.</summary>
    public IReadOnlyList<LatLon>? Footprint { get; init; }
    public required FeatureSource Source { get; init; }
    /// <summary>Short qualifier spoken after the name in the window: "Delta gates", "operator Jackson Hole Aviation".</summary>
    public string? Detail { get; init; }

    public bool HasName => !string.IsNullOrWhiteSpace(Name);
    public string SpokenName => HasName ? Name.Trim() : FeatureKindWords.Generic(Kind);
}

public static class FeatureKindWords
{
    /// <summary>What an UNNAMED feature of this kind is called. Other → "" (an unnamed Other is dropped upstream).</summary>
    public static string Generic(FeatureKind kind) => kind switch
    {
        FeatureKind.Terminal => "Terminal",
        FeatureKind.Concourse => "Concourse",
        FeatureKind.Fbo => "FBO",
        FeatureKind.Hangar => "Hangar",
        FeatureKind.Tower => "Control tower",
        FeatureKind.Fuel => "Fuel",
        FeatureKind.Cargo => "Cargo ramp",
        FeatureKind.FireStation => "Fire station",
        FeatureKind.Helipad => "Helipad",
        FeatureKind.Apron => "Apron",
        FeatureKind.DeicePad => "De-ice pad",
        FeatureKind.Office => "Airport office",
        _ => "",
    };
}
```

```csharp
// MSFSBlindAssist/Navigation/Surroundings/SurroundingsGeometry.cs
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public static class SurroundingsGeometry
{
    /// <summary>Metres from (lat,lon) to the feature: 0 inside a footprint, else the nearest
    /// polygon edge; a point feature is plain haversine to its representative point.</summary>
    public static double DistanceMetres(double lat, double lon, AirportFeature f)
    {
        var fp = f.Footprint;
        if (fp == null || fp.Count < 3)
            return TaxiGeo.HaversineMeters(lat, lon, f.Lat, f.Lon);
        if (Contains(fp, lat, lon)) return 0.0;
        double best = double.MaxValue;
        for (int i = 0; i < fp.Count; i++)
        {
            var a = fp[i];
            var b = fp[(i + 1) % fp.Count];
            double d = TaxiGeo.PointToSegmentMeters(lat, lon, a.Lat, a.Lon, b.Lat, b.Lon);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Ray-casting point-in-polygon on raw degrees (fine at airport scale; no antimeridian airports).</summary>
    public static bool Contains(IReadOnlyList<LatLon> polygon, double lat, double lon)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            double yi = polygon[i].Lat, xi = polygon[i].Lon;
            double yj = polygon[j].Lat, xj = polygon[j].Lon;
            bool crosses = (yi > lat) != (yj > lat);
            if (!crosses) continue;
            double xAt = xj + (lat - yj) * (xi - xj) / (yi - yj);
            if (lon < xAt) inside = !inside;
        }
        return inside;
    }

    /// <summary>Signed relative bearing, -180..180: negative = left of the nose.</summary>
    public static double RelativeBearingDeg(double ownLat, double ownLon, double ownHeadingTrue, double lat, double lon)
    {
        double brg = TaxiGeo.BearingDeg(ownLat, ownLon, lat, lon);
        double rel = ((brg - ownHeadingTrue) % 360.0 + 540.0) % 360.0 - 180.0;
        return rel;
    }

    public static LatLon Centroid(IReadOnlyList<LatLon> pts)
    {
        if (pts == null || pts.Count == 0) return new LatLon(0, 0);
        double lat = 0, lon = 0;
        foreach (var p in pts) { lat += p.Lat; lon += p.Lon; }
        return new LatLon(lat / pts.Count, lon / pts.Count);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SurroundingsGeometryTests"`
Expected: all pass. If `Distance_to_a_footprint_feature…` is off by more than 5 m, check `TaxiGeo.PointToSegmentMeters` argument order (`pLat,pLon,aLat,aLon,bLat,bLon`).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings tests/MSFSBlindAssist.Tests/SurroundingsGeometryTests.cs
git commit -m "feat(surroundings): airport feature model and geometry

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Navdata facilities read and `NavdataFeatureSource`

**Files:**
- Create: `MSFSBlindAssist/Database/Models/AirportFacilities.cs`
- Create: `MSFSBlindAssist/Database/IAirportFacilitiesProvider.cs`
- Modify: `MSFSBlindAssist/Database/LittleNavMapProvider.cs` (class declaration + new method after `GetParkingSpots`, ~line 500)
- Modify: `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs:112-121` (pass-through)
- Create: `MSFSBlindAssist/Navigation/Surroundings/NavdataFeatureSource.cs`
- Test: `tests/MSFSBlindAssist.Tests/NavdataFeatureSourceTests.cs`

**Interfaces:**
- Produces:
  - `sealed class AirportFacilities { string Icao; bool HasAvgas; bool HasJetFuel; List<LatLon> Helipads; List<ComFrequency> Coms; double LeftLon, RightLon, TopLat, BottomLat; string SceneryLocalPath; }` with `readonly record struct ComFrequency(string Type, int FrequencyHz, string Name)` and `string AirportFacilities.DescribeFacts()`.
  - `interface IAirportFacilitiesProvider { AirportFacilities? GetAirportFacilities(string icao); }` — implemented by `LittleNavMapProvider` and passed through by `AugmentingAirportDataProvider`.
  - `static List<AirportFeature> NavdataFeatureSource.Read(IReadOnlyList<ParkingSpot> spots, AirportFacilities? facilities)`.
- Consumes: `ParkingSpot` (`Name` already mapped by `MapParkingName`: single letter for a concourse, "North"/"South"… for directional ramps; `Type` int; `AirlineCodes` comma-separated), Task 2 types.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/NavdataFeatureSourceTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class NavdataFeatureSourceTests
{
    private static ParkingSpot Spot(string name, int number, int type, double lat, double lon, string airlines = "")
        => new() { Name = name, Number = number, Type = type, Latitude = lat, Longitude = lon, AirlineCodes = airlines };

    [Fact]
    public void Two_or_more_lettered_gates_become_a_concourse_at_their_centroid()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("B", 1, 10, 33.640, -84.430, "DAL"),
            Spot("B", 2, 10, 33.642, -84.430, "DAL"),
            Spot("B", 3, 11, 33.644, -84.430, "AAL"),
            Spot("C", 9, 10, 33.650, -84.420),           // alone: no concourse
        };
        var features = NavdataFeatureSource.Read(spots, null);
        var b = Assert.Single(features, f => f.Kind == FeatureKind.Concourse);
        Assert.Equal("Concourse B", b.Name);
        Assert.InRange(b.Lat, 33.6419, 33.6421);
        Assert.Equal("Delta gates", b.Detail);          // 2 of 3 coded gates = 67 % ≥ 60 %
        Assert.Equal(FeatureSource.Navdata, b.Source);
    }

    [Fact]
    public void Airline_detail_is_omitted_below_the_majority_threshold()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("A", 1, 10, 33.640, -84.430, "DAL"),
            Spot("A", 2, 10, 33.641, -84.430, "AAL"),
        };
        var a = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Null(a.Detail);
    }

    [Fact]
    public void Directional_ramps_become_named_aprons()
    {
        var spots = new List<ParkingSpot> { Spot("North", 1, 4, 47.27, -122.57), Spot("North", 2, 4, 47.271, -122.57) };
        var apron = Assert.Single(NavdataFeatureSource.Read(spots, null));
        Assert.Equal(FeatureKind.Apron, apron.Kind);
        Assert.Equal("North ramp", apron.Name);
    }

    [Fact]
    public void Fuel_stands_cluster_into_one_fuel_feature_with_the_fuel_types()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 16, 47.2700, -122.5700),
            Spot("Parking", 2, 16, 47.2701, -122.5700),   // ~11 m away → same cluster
            Spot("Parking", 3, 16, 47.2750, -122.5700),   // 550 m away → second cluster
        };
        var fac = new AirportFacilities { Icao = "KTIW", HasAvgas = true, HasJetFuel = true };
        var fuel = NavdataFeatureSource.Read(spots, fac).Where(f => f.Kind == FeatureKind.Fuel).ToList();
        Assert.Equal(2, fuel.Count);
        Assert.All(fuel, f => Assert.Equal("Fuel", f.Name));
        Assert.All(fuel, f => Assert.Equal("avgas and jet fuel", f.Detail));
    }

    [Fact]
    public void Cargo_and_ga_ramps_cluster_and_vehicles_are_ignored()
    {
        var spots = new List<ParkingSpot>
        {
            Spot("Parking", 1, 6, 33.62, -84.44), Spot("Parking", 2, 6, 33.6201, -84.44),
            Spot("Parking", 3, 3, 47.27, -122.58), Spot("Parking", 4, 3, 47.2701, -122.58), Spot("Parking", 5, 4, 47.2702, -122.58),
            Spot("Parking", 6, 17, 47.27, -122.58),
        };
        var features = NavdataFeatureSource.Read(spots, null);
        Assert.Single(features, f => f.Kind == FeatureKind.Cargo && f.Name == "Cargo ramp");
        Assert.Single(features, f => f.Kind == FeatureKind.Apron && f.Name == "GA ramp");
        Assert.DoesNotContain(features, f => f.Kind == FeatureKind.Other);
    }

    [Fact]
    public void Helipads_come_from_facilities_and_are_numbered_only_when_several()
    {
        var one = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1) } };
        Assert.Equal("Helipad", Assert.Single(NavdataFeatureSource.Read(new List<ParkingSpot>(), one)).Name);
        var two = new AirportFacilities { Icao = "X", Helipads = { new LatLon(1, 1), new LatLon(1.001, 1) } };
        var names = NavdataFeatureSource.Read(new List<ParkingSpot>(), two).Select(f => f.Name).ToList();
        Assert.Equal(new[] { "Helipad 1", "Helipad 2" }, names);
    }

    [Fact]
    public void Facts_line_lists_fuel_and_the_common_frequencies_in_mhz()
    {
        var fac = new AirportFacilities
        {
            Icao = "KTIW", HasAvgas = true, HasJetFuel = false,
            Coms = { new ComFrequency("T", 118500000, "TACOMA"), new ComFrequency("G", 121800000, "TACOMA"),
                     new ComFrequency("ATIS", 124050000, "KTIW"), new ComFrequency("UC", 122950000, "TACOMA"),
                     new ComFrequency("D", 120100000, "SEATTLE") }
        };
        Assert.Equal("Avgas. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95.", fac.DescribeFacts());
        Assert.Equal("", new AirportFacilities { Icao = "X" }.DescribeFacts());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NavdataFeatureSourceTests"`
Expected: build error (`AirportFacilities`, `NavdataFeatureSource` not found).

- [ ] **Step 3: Write the model, interface and provider read**

```csharp
// MSFSBlindAssist/Database/Models/AirportFacilities.cs
using System.Globalization;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Database.Models;

public readonly record struct ComFrequency(string Type, int FrequencyHz, string Name);

/// <summary>
/// The airport-level navdata columns the surroundings feature reads and nothing else did:
/// fuel flags, helipads, COM frequencies, the airport bounding box (the OSM radius-fallback
/// filter) and scenery_local_path (which packages the scenery scan may open).
/// airport.tower_lonx/laty is deliberately NOT here: NULL on every row of the current
/// navdatareader build (41,866 of 41,866, measured 2026-09-06).
/// </summary>
public sealed class AirportFacilities
{
    public string Icao { get; init; } = "";
    public bool HasAvgas { get; init; }
    public bool HasJetFuel { get; init; }
    public List<LatLon> Helipads { get; } = new();
    public List<ComFrequency> Coms { get; } = new();
    public double LeftLon { get; init; }
    public double RightLon { get; init; }
    public double TopLat { get; init; }
    public double BottomLat { get; init; }
    /// <summary>navdatareader's comma-separated package list, e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows".</summary>
    public string SceneryLocalPath { get; init; } = "";

    public bool ContainsPoint(double lat, double lon)
        => lat <= TopLat && lat >= BottomLat && lon >= LeftLon && lon <= RightLon;

    /// <summary>"Avgas and jet fuel. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95." or "".</summary>
    public string DescribeFacts()
    {
        var parts = new List<string>();
        string fuel = (HasAvgas, HasJetFuel) switch
        {
            (true, true) => "Avgas and jet fuel",
            (true, false) => "Avgas",
            (false, true) => "Jet fuel",
            _ => "",
        };
        if (fuel.Length > 0) parts.Add(fuel + ".");

        var freqs = new List<string>();
        foreach (var (type, label) in new[] { ("T", "Tower"), ("G", "Ground"), ("ATIS", "ATIS"), ("CTAF", "CTAF"), ("UC", "UNICOM"), ("AWOS", "AWOS"), ("ASOS", "ASOS") })
        {
            var com = Coms.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase));
            if (com.FrequencyHz > 0)
                freqs.Add($"{label} {FormatMhz(com.FrequencyHz)}");
        }
        if (freqs.Count > 0) parts.Add(string.Join(", ", freqs) + ".");
        return string.Join(" ", parts);
    }

    /// <summary>118500000 → "118.5"; 124050000 → "124.05"; 122950000 → "122.95" (trailing zeros trimmed, at least one decimal).</summary>
    public static string FormatMhz(int hz)
    {
        double mhz = hz / 1_000_000.0;
        string s = mhz.ToString("0.000", CultureInfo.InvariantCulture).TrimEnd('0');
        if (s.EndsWith('.')) s += "0";
        return s;
    }
}
```

```csharp
// MSFSBlindAssist/Database/IAirportFacilitiesProvider.cs
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Deliberately SEPARATE from IAirportDataProvider: the surroundings feature is the only
/// consumer, and widening the main interface would force every provider and test double to
/// grow a method. Callers probe with `provider as IAirportFacilitiesProvider`.
/// </summary>
public interface IAirportFacilitiesProvider
{
    AirportFacilities? GetAirportFacilities(string icao);
}
```

In `LittleNavMapProvider.cs`, add `IAirportFacilitiesProvider` to the class declaration (`public class LittleNavMapProvider : IAirportDataProvider, IAirportFacilitiesProvider`) and add this method after `GetParkingSpots`:

```csharp
    public AirportFacilities? GetAirportFacilities(string icao)
    {
        if (!DatabaseExists) return null;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        long airportId; bool avgas, jet; double left, right, top, bottom; string sceneryPath;
        using (var cmd = new SqliteCommand(@"
            SELECT airport_id, has_avgas, has_jetfuel, left_lonx, right_lonx, top_laty, bottom_laty, scenery_local_path
            FROM airport WHERE UPPER(icao) = UPPER(@ICAO) OR UPPER(ident) = UPPER(@ICAO) LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@ICAO", icao);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            airportId = Convert.ToInt64(r["airport_id"]);
            avgas = Convert.ToInt32(r["has_avgas"] ?? 0) == 1;
            jet = Convert.ToInt32(r["has_jetfuel"] ?? 0) == 1;
            left = Convert.ToDouble(r["left_lonx"] ?? 0.0);
            right = Convert.ToDouble(r["right_lonx"] ?? 0.0);
            top = Convert.ToDouble(r["top_laty"] ?? 0.0);
            bottom = Convert.ToDouble(r["bottom_laty"] ?? 0.0);
            sceneryPath = r["scenery_local_path"]?.ToString() ?? "";
        }

        var fac = new AirportFacilities
        {
            Icao = icao.ToUpperInvariant(), HasAvgas = avgas, HasJetFuel = jet,
            LeftLon = left, RightLon = right, TopLat = top, BottomLat = bottom, SceneryLocalPath = sceneryPath,
        };

        using (var cmd = new SqliteCommand("SELECT laty, lonx FROM helipad WHERE airport_id = @Id AND is_closed = 0", connection))
        {
            cmd.Parameters.AddWithValue("@Id", airportId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                fac.Helipads.Add(new Navigation.Surroundings.LatLon(Convert.ToDouble(r["laty"]), Convert.ToDouble(r["lonx"])));
        }

        using (var cmd = new SqliteCommand("SELECT type, frequency, name FROM com WHERE airport_id = @Id ORDER BY com_id", connection))
        {
            cmd.Parameters.AddWithValue("@Id", airportId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                fac.Coms.Add(new ComFrequency(r["type"]?.ToString() ?? "", Convert.ToInt32(r["frequency"] ?? 0), r["name"]?.ToString() ?? ""));
        }
        return fac;
    }
```

(`com.frequency` is in Hz — `118500000` at KTIW; `airport.tower_frequency` is a different unit and is NOT used.)

In `AugmentingAirportDataProvider.cs`, add `IAirportFacilitiesProvider` to the class declaration and one pass-through beside the others (line ~121):

```csharp
    public AirportFacilities? GetAirportFacilities(string icao)
        => (_base as IAirportFacilitiesProvider)?.GetAirportFacilities(icao);
```

- [ ] **Step 4: Write the source**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/NavdataFeatureSource.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Tier 1: features derivable from navdata alone, offline, at every airport. Concourses are
/// INFERRED from the gate letters (navdata has no building table); fuel/cargo/GA stands
/// cluster into one feature per group; helipads come from the helipad table. Pure.
/// </summary>
public static class NavdataFeatureSource
{
    public const double ClusterRadiusMetres = 60.0;
    public const double AirlineMajority = 0.60;
    private static readonly string[] Directional = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };

    private static readonly Dictionary<string, string> AirlineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DAL"] = "Delta", ["AAL"] = "American", ["UAL"] = "United", ["SWA"] = "Southwest", ["JBU"] = "JetBlue",
        ["ASA"] = "Alaska", ["FFT"] = "Frontier", ["NKS"] = "Spirit", ["BAW"] = "British Airways", ["DLH"] = "Lufthansa",
        ["AFR"] = "Air France", ["KLM"] = "KLM", ["RYR"] = "Ryanair", ["EZY"] = "easyJet", ["UAE"] = "Emirates",
        ["QTR"] = "Qatar", ["ACA"] = "Air Canada", ["QFA"] = "Qantas", ["FDX"] = "FedEx", ["UPS"] = "UPS",
    };

    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> spots, AirportFacilities? facilities)
    {
        var result = new List<AirportFeature>();
        spots ??= Array.Empty<ParkingSpot>();

        // Concourses: gate-type stands sharing a single-letter Name.
        foreach (var group in spots.Where(s => IsGateType(s.Type) && IsConcourseLetter(s.Name)).GroupBy(s => s.Name.ToUpperInvariant()))
        {
            var members = group.ToList();
            if (members.Count < 2) continue;
            var c = SurroundingsGeometry.Centroid(members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            result.Add(new AirportFeature
            {
                Kind = FeatureKind.Concourse, Name = $"Concourse {group.Key}", Lat = c.Lat, Lon = c.Lon,
                Source = FeatureSource.Navdata, Detail = MajorityAirline(members),
            });
        }

        // Directional ramps ("North" from NP etc.) → one apron per direction.
        foreach (var group in spots.Where(s => Directional.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var c = SurroundingsGeometry.Centroid(group.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            result.Add(new AirportFeature { Kind = FeatureKind.Apron, Name = $"{Capitalize(group.Key)} ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });
        }

        string? fuelDetail = facilities == null ? null : (facilities.HasAvgas, facilities.HasJetFuel) switch
        {
            (true, true) => "avgas and jet fuel", (true, false) => "avgas", (false, true) => "jet fuel", _ => null,
        };
        foreach (var c in Clusters(spots.Where(s => s.Type == 16), 1))
            result.Add(new AirportFeature { Kind = FeatureKind.Fuel, Name = "Fuel", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata, Detail = fuelDetail });
        foreach (var c in Clusters(spots.Where(s => s.Type == 6), 1))
            result.Add(new AirportFeature { Kind = FeatureKind.Cargo, Name = "Cargo ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });
        foreach (var c in Clusters(spots.Where(s => s.Type is 2 or 3 or 4 or 5 or 15 && !IsDirectionalName(s.Name)), 3))
            result.Add(new AirportFeature { Kind = FeatureKind.Apron, Name = "GA ramp", Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Navdata });

        if (facilities != null)
        {
            for (int i = 0; i < facilities.Helipads.Count; i++)
            {
                var h = facilities.Helipads[i];
                string name = facilities.Helipads.Count == 1 ? "Helipad" : $"Helipad {i + 1}";
                result.Add(new AirportFeature { Kind = FeatureKind.Helipad, Name = name, Lat = h.Lat, Lon = h.Lon, Source = FeatureSource.Navdata });
            }
        }
        return result;
    }

    internal static bool IsGateType(int type) => type is 9 or 10 or 11 or 13 or 14;
    internal static bool IsConcourseLetter(string? name) => name != null && name.Length == 1 && char.IsLetter(name[0]);
    private static bool IsDirectionalName(string? name) => name != null && Directional.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>"Delta gates" when ≥ 60 % of the airline-coded gates share one code; else null.</summary>
    internal static string? MajorityAirline(IReadOnlyList<ParkingSpot> gates)
    {
        var codes = gates.SelectMany(g => (g.AirlineCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                         .Select(c => c.ToUpperInvariant()).ToList();
        if (codes.Count == 0) return null;
        var top = codes.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
        if (top.Count() < codes.Count * AirlineMajority) return null;
        string name = AirlineNames.TryGetValue(top.Key, out var n) ? n : top.Key;
        return $"{name} gates";
    }

    /// <summary>Greedy clustering: a spot joins the first cluster whose centroid is within ClusterRadiusMetres.</summary>
    internal static List<LatLon> Clusters(IEnumerable<ParkingSpot> spots, int minSize)
    {
        var clusters = new List<List<LatLon>>();
        foreach (var s in spots)
        {
            var p = new LatLon(s.Latitude, s.Longitude);
            var home = clusters.FirstOrDefault(c =>
            {
                var cen = SurroundingsGeometry.Centroid(c);
                return TaxiGeo.HaversineMeters(cen.Lat, cen.Lon, p.Lat, p.Lon) <= ClusterRadiusMetres;
            });
            if (home == null) clusters.Add(new List<LatLon> { p }); else home.Add(p);
        }
        return clusters.Where(c => c.Count >= minSize).Select(SurroundingsGeometry.Centroid).ToList();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NavdataFeatureSourceTests"`
Expected: 7 passed. Then `dotnet build MSFSBlindAssist.sln -c Debug` — 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Database MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs MSFSBlindAssist/Navigation/Surroundings/NavdataFeatureSource.cs tests/MSFSBlindAssist.Tests/NavdataFeatureSourceTests.cs
git commit -m "feat(surroundings): navdata tier — concourse inference, stand clusters, helipads, facilities read

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 4: `AirportFeatureCatalog` merge

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/AirportFeatureCatalog.cs`
- Test: `tests/MSFSBlindAssist.Tests/AirportFeatureCatalogTests.cs`

**Interfaces:**
- Produces: `sealed class AirportFeatureCatalog { string Icao; string Version; IReadOnlyList<AirportFeature> Features; static AirportFeatureCatalog Build(string icao, string version, IEnumerable<AirportFeature> features); static AirportFeatureCatalog Empty(string icao); static int Rank(AirportFeature f); static double MergeRadiusMetres(FeatureKind k); static bool SameFeature(AirportFeature a, AirportFeature b); }`
- Consumes: Task 2 types.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/AirportFeatureCatalogTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AirportFeatureCatalogTests"`
Expected: build error, `AirportFeatureCatalog` not found.

- [ ] **Step 3: Write the catalog**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/AirportFeatureCatalog.cs
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// One airport's merged, deduplicated feature list. Immutable. Built off the UI thread by
/// SurroundingsCatalogCache; consumers only read. Version is the invalidation token the cache
/// stamped it with (gate-list token + online fetch generation + scenery index stamp).
/// </summary>
public sealed class AirportFeatureCatalog
{
    public string Icao { get; }
    public string Version { get; }
    public IReadOnlyList<AirportFeature> Features { get; }

    private AirportFeatureCatalog(string icao, string version, List<AirportFeature> features)
    {
        Icao = icao; Version = version; Features = features;
    }

    public static AirportFeatureCatalog Empty(string icao) => new(icao, "", new List<AirportFeature>());

    /// <summary>Named beats unnamed; among named, OSM > Scenery > GSX > Navdata.</summary>
    public static int Rank(AirportFeature f)
    {
        if (!f.HasName) return 0;
        return f.Source switch
        {
            FeatureSource.Osm => 40,
            FeatureSource.Scenery => 30,
            FeatureSource.Gsx => 20,
            _ => 10,
        };
    }

    public static double MergeRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Tower => 100.0,
        FeatureKind.Terminal or FeatureKind.Concourse => 150.0,
        FeatureKind.Hangar => 40.0,
        FeatureKind.Fuel => 60.0,
        _ => 50.0,
    };

    /// <summary>Same kind and (concourse letter match, or within the kind's merge radius).</summary>
    public static bool SameFeature(AirportFeature a, AirportFeature b)
    {
        if (a.Kind != b.Kind) return false;
        if (a.Kind == FeatureKind.Concourse && a.HasName && b.HasName
            && string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        return TaxiGeo.HaversineMeters(a.Lat, a.Lon, b.Lat, b.Lon) <= MergeRadiusMetres(a.Kind);
    }

    public static AirportFeatureCatalog Build(string icao, string version, IEnumerable<AirportFeature> features)
    {
        var kept = new List<AirportFeature>();
        // Highest rank first so the first feature standing in a cluster is the winner.
        foreach (var f in features.Where(f => f != null && (f.HasName || f.Kind != FeatureKind.Other)).OrderByDescending(Rank))
        {
            int i = kept.FindIndex(k => SameFeature(k, f));
            if (i < 0) { kept.Add(f); continue; }
            var winner = kept[i];
            if (winner.Footprint == null && f.Footprint != null || winner.Detail == null && f.Detail != null)
            {
                kept[i] = new AirportFeature
                {
                    Kind = winner.Kind, Name = winner.Name, Lat = winner.Lat, Lon = winner.Lon, Source = winner.Source,
                    Footprint = winner.Footprint ?? f.Footprint, Detail = winner.Detail ?? f.Detail,
                };
            }
        }
        var sorted = kept.OrderBy(f => (int)f.Kind).ThenBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase).ToList();
        return new AirportFeatureCatalog(icao, version, sorted);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AirportFeatureCatalogTests"`
Expected: 11 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings/AirportFeatureCatalog.cs tests/MSFSBlindAssist.Tests/AirportFeatureCatalogTests.cs
git commit -m "feat(surroundings): per-airport feature catalog with rank-based merge

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: `SurroundingsReport` — the `Alt+L` sentence and window sections

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/SurroundingsReport.cs`
- Test: `tests/MSFSBlindAssist.Tests/SurroundingsReportTests.cs`

**Interfaces:**
- Produces:
  - `sealed record NearbyFeature(AirportFeature Feature, double DistanceMetres, double RelativeBearingDeg)`
  - `static List<NearbyFeature> SurroundingsReport.Rank(AirportFeatureCatalog cat, double lat, double lon, double hdgTrue, double maxMetres)` — nearest first.
  - `static AirportFeature? SurroundingsReport.Zone(AirportFeatureCatalog cat, double lat, double lon)` — containing Apron/DeicePad, else nearest Concourse/Terminal within `ZoneNearMetres` (120).
  - `static string SurroundingsReport.Compose(string whereAmILine, string icao, AirportFeatureCatalog? cat, double lat, double lon, double hdgTrue, Func<double,string> formatDistance)`
  - `static IReadOnlyList<InfoSection> SurroundingsReport.BuildSections(string icao, AirportFeatureCatalog cat, string facts, double lat, double lon, double hdgTrue, Func<double,string> formatDistance)`
  - Constants `SpeakRadiusMetres = 600`, `MaxSpoken = 4`, `ZoneNearMetres = 120`, `WindowRadiusMetres = 1000`.
- Consumes: `RelativeDirection.Describe` (Task 1), `SurroundingsGeometry` (Task 2), `InfoSection` (`MSFSBlindAssist.Services.SayIntentions`, existing record `InfoSection(string Heading, IReadOnlyList<string> Items)`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/SurroundingsReportTests.cs
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
            F(FeatureKind.Fuel, "Fuel", 50, 0), F(FeatureKind.Fuel, "Fuel", 60, 0),
            F(FeatureKind.Hangar, "Hangar 1", 70, 0), F(FeatureKind.Hangar, "Hangar 2", 80, 0),
            F(FeatureKind.Tower, "Control Tower", 90, 0), F(FeatureKind.Cargo, "Cargo ramp", 95, 0));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. Fuel, ahead, 50 metres. Hangar 1, ahead, 70 metres. Hangar 2, ahead, 80 metres. Control Tower, ahead, 90 metres.", s);
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SurroundingsReportTests"`
Expected: build error, `SurroundingsReport` not found.

- [ ] **Step 3: Write the report composer**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/SurroundingsReport.cs
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Surroundings;

public sealed record NearbyFeature(AirportFeature Feature, double DistanceMetres, double RelativeBearingDeg);

/// <summary>
/// Pure composer for the two readout surfaces. Compose() is ONE utterance: the Where-Am-I
/// line the caller already has, the zone, then the nearest features. Distances go through
/// the caller's formatter (DistanceFormatter on GroundDistanceUnit in production).
/// </summary>
public static class SurroundingsReport
{
    public const double SpeakRadiusMetres = 600.0;
    public const int MaxSpoken = 4;
    public const double ZoneNearMetres = 120.0;
    public const double WindowRadiusMetres = 1000.0;

    public static List<NearbyFeature> Rank(AirportFeatureCatalog cat, double lat, double lon, double hdgTrue, double maxMetres)
    {
        var list = new List<NearbyFeature>();
        foreach (var f in cat.Features)
        {
            double d = SurroundingsGeometry.DistanceMetres(lat, lon, f);
            if (d > maxMetres) continue;
            double rel = SurroundingsGeometry.RelativeBearingDeg(lat, lon, hdgTrue, f.Lat, f.Lon);
            list.Add(new NearbyFeature(f, d, rel));
        }
        return list.OrderBy(n => n.DistanceMetres).ToList();
    }

    public static AirportFeature? Zone(AirportFeatureCatalog cat, double lat, double lon)
    {
        foreach (var f in cat.Features)
            if ((f.Kind == FeatureKind.Apron || f.Kind == FeatureKind.DeicePad) && f.Footprint != null
                && SurroundingsGeometry.Contains(f.Footprint, lat, lon))
                return f;
        AirportFeature? best = null; double bestD = ZoneNearMetres;
        foreach (var f in cat.Features)
        {
            if (f.Kind != FeatureKind.Concourse && f.Kind != FeatureKind.Terminal) continue;
            double d = SurroundingsGeometry.DistanceMetres(lat, lon, f);
            if (d <= bestD) { bestD = d; best = f; }
        }
        return best;
    }

    public static string Compose(string whereAmILine, string icao, AirportFeatureCatalog? cat, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var parts = new List<string> { whereAmILine.Trim() };
        if (cat == null || cat.Features.Count == 0)
        {
            parts.Add($"No surroundings data for {icao}.");
            return string.Join(" ", parts);
        }

        var zone = Zone(cat, lat, lon);
        if (zone != null)
            parts.Add(zone.Kind is FeatureKind.Apron or FeatureKind.DeicePad ? $"On the {zone.SpokenName}." : $"At {zone.SpokenName}.");

        var ranked = Rank(cat, lat, lon, hdgTrue, SpeakRadiusMetres).Where(n => !ReferenceEquals(n.Feature, zone)).ToList();
        if (ranked.Count == 0)
        {
            parts.Add($"Nothing within {formatDistance(SpeakRadiusMetres)}.");
            return string.Join(" ", parts);
        }

        var spoken = new List<NearbyFeature>();
        var kindsUsed = new HashSet<FeatureKind>();
        NearbyFeature? firstUnnamedHangar = null; int unnamedHangars = 0;
        foreach (var n in ranked)
        {
            if (spoken.Count >= MaxSpoken) break;
            var f = n.Feature;
            if (f.Kind == FeatureKind.Hangar && !f.HasName)
            {
                unnamedHangars++;
                if (firstUnnamedHangar == null) { firstUnnamedHangar = n; spoken.Add(n); }
                continue;
            }
            bool repeatable = f.Kind is FeatureKind.Hangar or FeatureKind.Fbo;
            if (!repeatable && !kindsUsed.Add(f.Kind)) continue;
            spoken.Add(n);
        }

        foreach (var n in spoken)
        {
            string name = ReferenceEquals(n, firstUnnamedHangar) && unnamedHangars > 1 ? "Hangars" : n.Feature.SpokenName;
            parts.Add($"{name}, {RelativeDirection.Describe(n.RelativeBearingDeg)}, {formatDistance(n.DistanceMetres)}.");
        }
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<InfoSection> BuildSections(string icao, AirportFeatureCatalog cat, string facts, double lat, double lon, double hdgTrue, Func<double, string> formatDistance)
    {
        var sections = new List<InfoSection>();
        if (!string.IsNullOrWhiteSpace(facts))
            sections.Add(new InfoSection("Airport", new[] { facts.Trim() }));

        var ranked = Rank(cat, lat, lon, hdgTrue, WindowRadiusMetres);
        var items = ranked.Select(n =>
        {
            string detail = string.IsNullOrWhiteSpace(n.Feature.Detail) ? "" : $", {n.Feature.Detail}";
            return $"{n.Feature.SpokenName}{detail}, {RelativeDirection.Describe(n.RelativeBearingDeg)}, {formatDistance(n.DistanceMetres)}";
        }).ToList();
        sections.Add(new InfoSection(items.Count == 0 ? "Nearby, nothing within 1 kilometre" : $"Nearby, {items.Count} items", items));
        return sections;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SurroundingsReportTests"`
Expected: 7 passed. The `Fuel` 210 m case: √(150²+150²) = 212 → rounds to 210 with the test's formatter.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings/SurroundingsReport.cs tests/MSFSBlindAssist.Tests/SurroundingsReportTests.cs
git commit -m "feat(surroundings): readout composer for the look-around sentence and the window

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Catalog cache, `Alt+L` hotkey, MainForm wiring (navdata tier live)

**Files:**
- Create: `MSFSBlindAssist/Services/SurroundingsCatalogCache.cs`
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` (consts near line 164; output-mode switch near line 511; register near line 792; unregister near line 896; enum near line 1367)
- Modify: `MSFSBlindAssist/MainForm.Hotkeys.cs` (switch near line 400)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (new method after `AnnounceWhereAmI`, ~line 1798)
- Modify: `MSFSBlindAssist/MainForm.cs` (field + construction beside `taxiGuidanceManager.ParkingSpotVersionSupplier`, ~line 679; invalidation in the `AirportDataUpdated` handler, ~line 772)
- Modify: `MSFSBlindAssist/MainForm.AircraftSwitch.cs` (no change needed — the cache is per-ICAO, not per-aircraft)
- Test: none new (the cache is a thin lock + dictionary; its policy is `GateDataSource.ShouldRebuildGateList`, already pinned).

**Interfaces:**
- Produces:
  - `sealed class SurroundingsCatalogCache { Func<string, IReadOnlyList<AirportFeature>> FeatureSupplier; Func<string,string>? VersionSupplier; AirportFeatureCatalog? Get(string icao); void Invalidate(string icao); void Clear(); }`
  - `HotkeyAction.LookAround`, `HotkeyAction.ShowSurroundings`; consts `HOTKEY_LOOK_AROUND = 9219`, `HOTKEY_SHOW_SURROUNDINGS = 9220`.
  - `MainForm.AnnounceLookAround()`; `MainForm.BuildSurroundingsFeatures(string icao)` (private; later tasks append tiers here).
- Consumes: Tasks 3–5; `GateDataSource.ShouldRebuildGateList(string?, string)`; `ParkingSpotSource.GetNamedSpots(provider, gateDataSource, icao)`; `BuildGateDataSource()` (existing MainForm helper); `DistanceFormatter.FromMetres`.

- [ ] **Step 1: Verify the hotkey ids are free**

Run: `grep -n '9219\|9220' MSFSBlindAssist/Hotkeys/HotkeyManager.cs`
Expected: no output. If either id is taken, pick the next free pair and use it consistently below.

- [ ] **Step 2: Write the cache**

```csharp
// MSFSBlindAssist/Services/SurroundingsCatalogCache.cs
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
```

- [ ] **Step 3: Register the hotkeys**

In `HotkeyManager.cs`:

1. After line 164 (`HOTKEY_TAXI_WHERE_AM_I = 9205;`) add:
```csharp
        private const int HOTKEY_LOOK_AROUND = 9219;        // Output mode: Alt+L (surroundings readout)
        private const int HOTKEY_SHOW_SURROUNDINGS = 9220;  // Output mode: Ctrl+Shift+L (surroundings window)
```
2. In the output-mode switch (after the `HOTKEY_TAXI_WHERE_AM_I` case at line ~511) add:
```csharp
                        case HOTKEY_LOOK_AROUND:
                            TriggerHotkey(HotkeyAction.LookAround);
                            break;
                        case HOTKEY_SHOW_SURROUNDINGS:
                            TriggerHotkey(HotkeyAction.ShowSurroundings);
                            break;
```
3. In `ActivateOutputHotkeyMode` beside line 792 (`RegisterHotKey(windowHandle, HOTKEY_TAXI_WHERE_AM_I, MOD_ALT, 0x59);`) add:
```csharp
            RegisterHotKey(windowHandle, HOTKEY_LOOK_AROUND, MOD_ALT, 0x4C);                    // Alt+L (Look around)
            RegisterHotKey(windowHandle, HOTKEY_SHOW_SURROUNDINGS, MOD_CONTROL | MOD_SHIFT, 0x4C); // Ctrl+Shift+L (Surroundings window)
```
4. In `DeactivateOutputHotkeyMode` beside line 896 add:
```csharp
            UnregisterHotKey(windowHandle, HOTKEY_LOOK_AROUND);
            UnregisterHotKey(windowHandle, HOTKEY_SHOW_SURROUNDINGS);
```
5. In `enum HotkeyAction` after `TaxiWhereAmI,` (line ~1367) add:
```csharp
        LookAround,
        ShowSurroundings,
```

- [ ] **Step 4: Dispatch and compose in MainForm**

In `MainForm.Hotkeys.cs`, after the `case HotkeyAction.TaxiWhereAmI:` block:
```csharp
            case HotkeyAction.LookAround:
                AnnounceLookAround();
                break;
            case HotkeyAction.ShowSurroundings:
                ShowSurroundingsWindow();   // implemented in Task 9; until then add a one-line stub that calls AnnounceLookAround()
                break;
```

In `MainForm.cs`, add a field beside `taxiGuidanceManager` declarations:
```csharp
    private readonly MSFSBlindAssist.Services.SurroundingsCatalogCache surroundingsCache = new();
```
and right after `taxiGuidanceManager.ParkingSpotVersionSupplier = …;` (line ~680):
```csharp
        // Surroundings catalog: same token as the Where-Am-I graph so a GSX publish re-letters
        // the inferred concourses too. Built on demand from the hotkey handler, never per frame.
        surroundingsCache.VersionSupplier = icao => BuildGateDataSource()?.GetGateListVersion(icao) ?? "none";
        surroundingsCache.FeatureSupplier = BuildSurroundingsFeatures;
```
In the `AirportDataUpdated` handler (line ~772, beside `taxiGuidanceManager?.OnAirportDataUpdated(icao);`):
```csharp
                surroundingsCache.Invalidate(icao);
```

In `MainForm.Announcers.cs`, after `AnnounceWhereAmI()`:

```csharp
    /// <summary>
    /// Every surroundings tier for one airport, merged into one list. Tiers are appended here
    /// as they land: navdata (this task), GSX terminals, OSM, scenery. Runs inside
    /// SurroundingsCatalogCache.Get, i.e. on the hotkey/background thread that asked — a fresh
    /// GateDataSource per call for the same reason ParkingSpotSupplier builds one.
    /// </summary>
    private IReadOnlyList<MSFSBlindAssist.Navigation.Surroundings.AirportFeature> BuildSurroundingsFeatures(string icao)
    {
        var provider = airportDataProvider;
        if (provider == null) return Array.Empty<MSFSBlindAssist.Navigation.Surroundings.AirportFeature>();
        var features = new List<MSFSBlindAssist.Navigation.Surroundings.AirportFeature>();

        var facilities = (provider as MSFSBlindAssist.Database.IAirportFacilitiesProvider)?.GetAirportFacilities(icao);
        var named = MSFSBlindAssist.Services.ParkingSpotSource.GetNamedSpots(provider, BuildGateDataSource(), icao);
        features.AddRange(MSFSBlindAssist.Navigation.Surroundings.NavdataFeatureSource.Read(named, facilities));
        return features;
    }

    /// <summary>
    /// Alt+L (output mode): "Look around." One utterance — the Where-Am-I line, the zone, the
    /// nearest features with direction and distance. Ground-only like Where Am I.
    /// </summary>
    private void AnnounceLookAround()
    {
        if (airportDataProvider == null) { announcer.AnnounceImmediate("Airport database not available."); return; }
        if (!_lastOnGround) { announcer.AnnounceImmediate("In flight."); return; }

        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            string announcement;
            try
            {
                var nearby = airportDataProvider.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                    .Where(c => c != null && c.Length == 4).ToList();
                if (nearby.Count == 0)
                {
                    announcement = "No airport nearby.";
                }
                else
                {
                    string icao = nearby[0];
                    string whereAmI = taxiGuidanceManager.DescribeCurrentLocation(airportDataProvider, icao, position.Latitude, position.Longitude);
                    // AircraftPosition carries degrees (GroundTrafficMonitor adds these two the same way).
                    double hdgTrue = MSFSBlindAssist.Services.RelativeDirection.Normalize360(position.HeadingMagnetic + position.MagneticVariation);
                    var catalog = surroundingsCache.Get(icao);
                    announcement = MSFSBlindAssist.Navigation.Surroundings.SurroundingsReport.Compose(
                        whereAmI, icao, catalog, position.Latitude, position.Longitude, hdgTrue,
                        m => MSFSBlindAssist.Services.DistanceFormatter.FromMetres(m));
                }
            }
            catch (Exception ex)
            {
                announcement = $"Surroundings lookup failed. {ex.Message}";
            }

            if (this.InvokeRequired) this.Invoke(() => announcer.AnnounceImmediate(announcement));
            else announcer.AnnounceImmediate(announcement);
        });
    }
```

- [ ] **Step 5: Build, run the suite, smoke test in the sim**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors; `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all green.
In-sim (owner): at any default airport with lettered gates, press `]` then `Alt+L` — expect the Where-Am-I line followed by "Concourse X, direction, distance" entries; at a GA field expect "Fuel"/"GA ramp"/"Helipad" entries or "Nothing within 600 metres."

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/SurroundingsCatalogCache.cs MSFSBlindAssist/Hotkeys/HotkeyManager.cs MSFSBlindAssist/MainForm.Hotkeys.cs MSFSBlindAssist/MainForm.Announcers.cs MSFSBlindAssist/MainForm.cs
git commit -m "feat(surroundings): Alt+L look-around readout on the navdata tier

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 7: OSM tier — widened query, `OsmFeatureClassifier`, `Features` on `AirportTaxiData`

**Files:**
- Create: `MSFSBlindAssist/Services/TaxiAugment/OsmFeatureClassifier.cs`
- Modify: `MSFSBlindAssist/Services/TaxiAugment/AirportTaxiData.cs` (add `Features` list)
- Modify: `MSFSBlindAssist/Services/TaxiAugment/OsmTaxiSource.cs` (`BuildQuery` signature gains `icao`; new `BuildFeatureFallbackQuery`; `FetchAsync` second round-trip; `Parse` delegates feature elements)
- Create: `tests/MSFSBlindAssist.Tests/Fixtures/osm-features-kjac.json`
- Test: `tests/MSFSBlindAssist.Tests/OsmFeatureClassifierTests.cs`
- Modify (test): any existing test calling `OsmTaxiSource.BuildQuery(lat, lon)` — grep `BuildQuery(` under `tests/` and add the new `icao` argument (`"KTIW"`) so the culture pin keeps compiling.

**Interfaces:**
- Produces:
  - `AirportTaxiData.Features : List<AirportFeature>` (OSM-sourced, `Source = FeatureSource.Osm`).
  - `static AirportFeature? OsmFeatureClassifier.Classify(JsonElement element)` — null when the element is not a feature (taxiway, parking, unnamed Other…).
  - `internal static string OsmTaxiSource.BuildQuery(double lat, double lon, string icao)`; `internal static string OsmTaxiSource.BuildFeatureFallbackQuery(double lat, double lon)`.
- Consumes: `OsmTaxiSource.TryRepresentativePoint(JsonElement, out lat, out lon)` (existing, `internal static`); Task 2 types.

- [ ] **Step 1: Write the fixture and the failing tests**

Fixture (an ODbL-attributed excerpt; keep it this small — never commit a full dump):

```json
{
  "_attribution": "Excerpt of OpenStreetMap data at KJAC and KTIW, © OpenStreetMap contributors, ODbL 1.0. Trimmed to the tags the classifier reads.",
  "version": 0.6,
  "elements": [
    { "type": "way", "id": 1, "center": { "lat": 43.6060, "lon": -110.7378 }, "tags": { "aeroway": "terminal", "name": "General Aviation Terminal", "operator": "Jackson Hole Aviation LLC" } },
    { "type": "way", "id": 2, "center": { "lat": 43.6072, "lon": -110.7375 }, "tags": { "aeroway": "terminal", "name": "Baggage Claim" } },
    { "type": "relation", "id": 3, "center": { "lat": 43.6066, "lon": -110.7370 }, "tags": { "aeroway": "apron", "name": "Commercial Ramp", "type": "multipolygon" } },
    { "type": "way", "id": 4, "geometry": [ { "lat": 43.6050, "lon": -110.7390 }, { "lat": 43.6050, "lon": -110.7380 }, { "lat": 43.6056, "lon": -110.7380 }, { "lat": 43.6056, "lon": -110.7390 }, { "lat": 43.6050, "lon": -110.7390 } ], "tags": { "aeroway": "apron", "ref": "De-icing pad" } },
    { "type": "way", "id": 5, "center": { "lat": 47.2695, "lon": -122.5760 }, "tags": { "aeroway": "hangar" } },
    { "type": "way", "id": 6, "center": { "lat": 47.2712, "lon": -122.5731 }, "tags": { "aeroway": "tower", "man_made": "tower", "name": "Control Tower", "tower:type": "aircraft_control" } },
    { "type": "node", "id": 7, "lat": 33.6400, "lon": -84.4400, "tags": { "amenity": "fuel", "name": "Chevron", "brand": "Chevron" } },
    { "type": "way", "id": 8, "center": { "lat": 33.6300, "lon": -84.4300 }, "tags": { "amenity": "fire_station", "name": "Atlanta Fire Rescue Station 35", "building": "yes" } },
    { "type": "way", "id": 9, "center": { "lat": 33.6350, "lon": -84.4200 }, "tags": { "aeroway": "terminal", "name": "Concourse B", "operator": "Hartsfield-Jackson Atlanta International Airport" } },
    { "type": "way", "id": 10, "center": { "lat": 33.6200, "lon": -84.4500 }, "tags": { "aeroway": "terminal", "name": "FedEx" } },
    { "type": "way", "id": 11, "center": { "lat": 33.6210, "lon": -84.4510 }, "tags": { "building": "hangar", "name": "Delta TechOps Hangar 2" } },
    { "type": "way", "id": 12, "center": { "lat": 33.6220, "lon": -84.4520 }, "tags": { "building": "yes", "name": "Signature Flight Support" } },
    { "type": "way", "id": 13, "center": { "lat": 33.6230, "lon": -84.4530 }, "tags": { "building": "yes", "name": "Hapeville City Hall" } },
    { "type": "node", "id": 14, "lat": 43.6080, "lon": -110.7360, "tags": { "aeroway": "helipad" } },
    { "type": "way", "id": 15, "geometry": [ { "lat": 47.2700, "lon": -122.5700 }, { "lat": 47.2710, "lon": -122.5700 } ], "tags": { "aeroway": "taxiway", "ref": "A" } },
    { "type": "way", "id": 16, "center": { "lat": 33.6240, "lon": -84.4540 }, "tags": { "building": "yes" } }
  ]
}
```

Save it as `tests/MSFSBlindAssist.Tests/Fixtures/osm-features-kjac.json` (the csproj already copies `Fixtures\*.json`).

```csharp
// tests/MSFSBlindAssist.Tests/OsmFeatureClassifierTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OsmFeatureClassifierTests"`
Expected: build error (`OsmFeatureClassifier`, `Features`, three-argument `BuildQuery` not found).

- [ ] **Step 3: Write the classifier**

```csharp
// MSFSBlindAssist/Services/TaxiAugment/OsmFeatureClassifier.cs
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// One Overpass element → one AirportFeature, or null when it is not one (taxiway, stand,
/// holding point, nameless generic building). The FBO lexicon is deliberately a name test:
/// OSM has no reliable FBO tag, but FBOs name themselves the same way everywhere.
/// </summary>
public static class OsmFeatureClassifier
{
    private static readonly Regex FboLexicon = new(@"\b(aviation|jet ?cent(er|re)|fbo|air ?cent(er|re)|flight support|signature|atlantic|million air|executive|general aviation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CargoLexicon = new(@"\b(cargo|freight|fedex|ups|dhl)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DeiceLexicon = new(@"de-?ic", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AirportFeature? Classify(JsonElement el)
    {
        if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) return null;
        string aeroway = Tag(tags, "aeroway"), building = Tag(tags, "building"), amenity = Tag(tags, "amenity");
        string manMade = Tag(tags, "man_made"), office = Tag(tags, "office");
        string name = Tag(tags, "name");
        if (name.Length == 0) name = Tag(tags, "ref");
        string op = Tag(tags, "operator");
        string nameAndOp = name + " " + op;

        FeatureKind? kind = null;
        switch (aeroway)
        {
            case "terminal":
                kind = name.StartsWith("Concourse", StringComparison.OrdinalIgnoreCase) ? FeatureKind.Concourse
                     : Tag(tags, "terminal:type") == "general_aviation" || FboLexicon.IsMatch(nameAndOp) ? FeatureKind.Fbo
                     : CargoLexicon.IsMatch(name) ? FeatureKind.Cargo
                     : FeatureKind.Terminal;
                break;
            case "hangar": kind = FeatureKind.Hangar; break;
            case "apron": kind = DeiceLexicon.IsMatch(name) ? FeatureKind.DeicePad : FeatureKind.Apron; break;
            case "tower": case "control_tower": kind = FeatureKind.Tower; break;
            case "fuel": kind = FeatureKind.Fuel; break;
            case "helipad": kind = FeatureKind.Helipad; break;
            case "taxiway": case "parking_position": case "gate": case "holding_position": case "runway": return null;
        }
        if (kind == null)
        {
            if (building == "hangar") kind = FeatureKind.Hangar;
            else if (building == "terminal") kind = FeatureKind.Terminal;
            else if (manMade == "tower" && Tag(tags, "tower:type") == "aircraft_control") kind = FeatureKind.Tower;
            else if (amenity == "fuel") kind = FeatureKind.Fuel;
            else if (amenity == "fire_station") kind = FeatureKind.FireStation;
            else if (name.Length > 0 && (office.Length > 0 || building.Length > 0))
                kind = FboLexicon.IsMatch(nameAndOp) ? FeatureKind.Fbo : CargoLexicon.IsMatch(name) ? FeatureKind.Cargo : FeatureKind.Office;
        }
        if (kind == null) return null;
        if (kind == FeatureKind.Office && name.Length == 0) return null;

        if (!OsmTaxiSource.TryRepresentativePoint(el, out double lat, out double lon)) return null;

        IReadOnlyList<LatLon>? footprint = null;
        if (kind is FeatureKind.Apron or FeatureKind.DeicePad or FeatureKind.Terminal or FeatureKind.Concourse)
            footprint = Footprint(el);
        if (footprint != null && footprint.Count >= 3)
        {
            var c = SurroundingsGeometry.Centroid(footprint);
            if (SurroundingsGeometry.Contains(footprint, c.Lat, c.Lon)) { lat = c.Lat; lon = c.Lon; }
        }

        return new AirportFeature
        {
            Kind = kind.Value, Name = name.Trim(), Lat = lat, Lon = lon, Footprint = footprint,
            Source = FeatureSource.Osm, Detail = op.Length > 0 && kind == FeatureKind.Fbo ? $"operator {op}" : null,
        };
    }

    private static string Tag(JsonElement tags, string key)
        => tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>A closed way's vertices (closing duplicate dropped), or null. Relations (multipolygons) arrive with `center` only.</summary>
    private static IReadOnlyList<LatLon>? Footprint(JsonElement el)
    {
        if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array) return null;
        var pts = new List<LatLon>();
        foreach (var g in geom.EnumerateArray())
            if (g.TryGetProperty("lat", out var la) && g.TryGetProperty("lon", out var lo))
                pts.Add(new LatLon(la.GetDouble(), lo.GetDouble()));
        if (pts.Count >= 2 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }
}
```

- [ ] **Step 4: Widen the source**

In `AirportTaxiData.cs` add, after `HoldingPoints`:
```csharp
    /// <summary>
    /// Airport FEATURES (terminals, concourses, FBOs, hangars, tower, fuel, cargo, fire
    /// station, helipads, named aprons/de-ice pads) classified by OsmFeatureClassifier.
    /// READOUT ONLY — consumed by the surroundings catalog, never by routing (spec invariant).
    /// In-memory like everything else on this object.
    /// </summary>
    public List<MSFSBlindAssist.Navigation.Surroundings.AirportFeature> Features { get; } = new();
```

In `OsmTaxiSource.cs`:

1. Replace `BuildQuery`:
```csharp
    internal static string BuildQuery(double lat, double lon, string icao)
    {
        string around = string.Format(CultureInfo.InvariantCulture, "(around:5000,{0:0.######},{1:0.######});", lat, lon);
        string safeIcao = new string((icao ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        return "[out:json][timeout:50];" +
               $"area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"{safeIcao}\"]->.ad;" +
               "(" +
               $"way[\"aeroway\"=\"taxiway\"]{around}" +
               $"node[\"aeroway\"=\"parking_position\"]{around}" +
               $"way[\"aeroway\"=\"parking_position\"]{around}" +
               $"node[\"aeroway\"=\"gate\"]{around}" +
               $"way[\"aeroway\"=\"gate\"]{around}" +
               $"node[\"aeroway\"=\"holding_position\"]{around}" +
               FeatureClauses("(area.ad)") +
               ");out tags geom center;";
    }

    /// <summary>Feature-only query for an aerodrome OSM has not tagged with an icao= area; the
    /// caller bbox-filters the result against the navdata airport extent (AirportFacilities).</summary>
    internal static string BuildFeatureFallbackQuery(double lat, double lon)
    {
        string around = string.Format(CultureInfo.InvariantCulture, "(around:3000,{0:0.######},{1:0.######})", lat, lon);
        return "[out:json][timeout:30];(" + FeatureClauses(around) + ");out tags geom center;";
    }

    private static string FeatureClauses(string scope) =>
        $"nwr[\"aeroway\"~\"^(terminal|hangar|apron|tower|control_tower|fuel|helipad)$\"]{scope};" +
        $"nwr[\"building\"~\"^(hangar|terminal)$\"]{scope};" +
        $"nwr[\"man_made\"=\"tower\"][\"tower:type\"=\"aircraft_control\"]{scope};" +
        $"nwr[\"amenity\"~\"^(fuel|fire_station)$\"]{scope};" +
        $"nwr[\"office\"][\"name\"]{scope};" +
        $"nwr[\"building\"][\"name\"]{scope};";
```
Note `out tags geom center` — `geom` keeps the taxiway/way vertices the merger needs, `center` adds a representative point for relations (multipolygon aprons).

2. In `FetchAsync`, change `string q = BuildQuery(lat, lon);` to `string q = BuildQuery(lat, lon, icao);` and, after a successful parse, add the fallback round-trip:
```csharp
                var parsed = Parse(await resp.Content.ReadAsStringAsync(attemptCts.Token));
                if (parsed.Features.Count == 0)
                    await TryFallbackFeaturesAsync(url, lat, lon, parsed, ct).ConfigureAwait(false);
                return parsed;
```
with:
```csharp
    /// <summary>One extra request on the SAME mirror when the area-scoped feature clauses returned
    /// nothing (the aerodrome polygon lacks an icao tag, or there is none). Fills parsed.Features
    /// from a 3 km radius; the decorator bbox-filters it. Failure is silent — the taxiway half is
    /// already in hand and must not be lost to a feature-only miss.</summary>
    private async Task TryFallbackFeaturesAsync(string url, double lat, double lon, AirportTaxiData parsed, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PerMirrorTimeout);
            using var resp = await _http.PostAsync(url,
                new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", BuildFeatureFallbackQuery(lat, lon)) }), cts.Token);
            if (!resp.IsSuccessStatusCode) return;
            var extra = Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            parsed.Features.AddRange(extra.Features);
            parsed.FeaturesFromFallback = extra.Features.Count > 0;
        }
        catch { /* feature-only miss; taxiways already parsed */ }
    }
```
Add to `AirportTaxiData`: `public bool FeaturesFromFallback { get; set; }` (the decorator applies the bbox filter only when this is true).

3. In `Parse`, at the top of the `foreach (var el in els.EnumerateArray())` body, before the taxiway branch, add:
```csharp
            var feature = OsmFeatureClassifier.Classify(el);
            if (feature != null) { data.Features.Add(feature); continue; }
```
(`Classify` returns null for taxiway/parking/gate/holding elements, so those still reach their existing branches.)

4. Fix the existing culture test: grep `BuildQuery(` under `tests/` and pass `"KTIW"` as the third argument.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Osm"`
Expected: all Osm* tests pass (new 18 + the existing culture pin).

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiAugment tests/MSFSBlindAssist.Tests/OsmFeatureClassifierTests.cs tests/MSFSBlindAssist.Tests/Fixtures/osm-features-kjac.json tests/MSFSBlindAssist.Tests/*Osm*Tests.cs
git commit -m "feat(surroundings): OSM tier — area-scoped feature query and classifier

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Surface OSM features through the decorator and into the catalog

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` (new accessor beside `GetNamedHoldingPoints`, ~line 197)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`BuildSurroundingsFeatures`)
- Modify: `MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs:417-424` (checkbox text/description)
- Test: `tests/MSFSBlindAssist.Tests/AugmentingProviderFeaturesTests.cs`

**Interfaces:**
- Produces: `List<AirportFeature> AugmentingAirportDataProvider.GetOnlineFeatures(string icao, AirportFacilities? bbox)` — empty when disabled/uncached; bbox-filters only fallback-sourced features.
- Consumes: `TaxiDataCache.TryLoad`, `AirportTaxiData.Features/FeaturesFromFallback` (Task 7), `AirportFacilities.ContainsPoint` (Task 3).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MSFSBlindAssist.Tests/AugmentingProviderFeaturesTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class AugmentingProviderFeaturesTests
{
    [Fact]
    public void Fallback_sourced_features_are_bbox_filtered_and_area_sourced_are_not()
    {
        var data = new AirportTaxiData { Source = "osm", FeaturesFromFallback = true };
        data.Features.Add(new AirportFeature { Kind = FeatureKind.Fuel, Name = "Chevron on the highway", Lat = 47.30, Lon = -122.60, Source = FeatureSource.Osm });
        data.Features.Add(new AirportFeature { Kind = FeatureKind.Tower, Name = "Control Tower", Lat = 47.2712, Lon = -122.5731, Source = FeatureSource.Osm });
        var bbox = new AirportFacilities { Icao = "KTIW", LeftLon = -122.5794, RightLon = -122.5448, TopLat = 47.2750, BottomLat = 47.2608 };

        var filtered = AugmentingAirportDataProvider.FilterFeatures(new[] { data }, bbox);
        Assert.Equal("Control Tower", Assert.Single(filtered).Name);

        data.FeaturesFromFallback = false;
        Assert.Equal(2, AugmentingAirportDataProvider.FilterFeatures(new[] { data }, bbox).Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AugmentingProviderFeaturesTests"`
Expected: build error, `FilterFeatures` not found.

- [ ] **Step 3: Write the accessor**

In `AugmentingAirportDataProvider.cs`, after `GetNamedHoldingPoints`:
```csharp
    /// <summary>
    /// OSM-classified airport features (terminals, hangars, tower, aprons…) from the cached online
    /// sources. Rides the per-ICAO cache GetTaxiPaths populates; never fetches. Empty when disabled
    /// or uncached. READOUT ONLY: the surroundings catalog is the sole consumer. A fallback (radius)
    /// fetch is bbox-filtered against the navdata airport extent so a gas station on the road
    /// outside the field never becomes "Fuel, ahead, 800 metres".
    /// </summary>
    public List<Navigation.Surroundings.AirportFeature> GetOnlineFeatures(string icao, AirportFacilities? bbox)
    {
        if (!Enabled) return new();
        if (!_cache.TryLoad(icao, out var sources) || sources == null) return new();
        return FilterFeatures(sources, bbox);
    }

    internal static List<Navigation.Surroundings.AirportFeature> FilterFeatures(IReadOnlyList<AirportTaxiData> sources, AirportFacilities? bbox)
    {
        var result = new List<Navigation.Surroundings.AirportFeature>();
        foreach (var src in sources)
        {
            bool filter = src.FeaturesFromFallback && bbox != null;
            foreach (var f in src.Features)
                if (!filter || bbox!.ContainsPoint(f.Lat, f.Lon))
                    result.Add(f);
        }
        return result;
    }
```

In `MainForm.Announcers.cs` `BuildSurroundingsFeatures`, after the navdata line:
```csharp
        if (_augmentingProvider != null)
            features.AddRange(_augmentingProvider.GetOnlineFeatures(icao, facilities));
```

In `TaxiGuidancePanel.cs` (line ~419), update the checkbox copy so the consent text matches the wider query:
```csharp
            Text = "Online taxiway, gate and airport building names (OpenStreetMap + X-Plane)",
            AccessibleName = "Online taxiway, gate and airport building names",
            AccessibleDescription = "When enabled, fetches real-world taxiway and gate names, and airport buildings such as terminals, hangars, the tower and fuel, from OpenStreetMap and the X-Plane Scenery Gateway for the departure and destination. Disable to use navdata only with no online requests. Applies immediately.",
```

- [ ] **Step 4: Run tests, build, smoke in sim**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all green; `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors.
In-sim (owner): at KJAC, open the taxi form once (so the augmentation fetch runs), then `]` `Alt+L` → expect "General Aviation Terminal" and "On the Commercial Ramp." when parked on it. At KTIW expect "Control Tower" and "Hangars".

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs MSFSBlindAssist/MainForm.Announcers.cs MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs tests/MSFSBlindAssist.Tests/AugmentingProviderFeaturesTests.cs
git commit -m "feat(surroundings): OSM features reach the look-around catalog

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: `Ctrl+Shift+L` Surroundings window

**Files:**
- Modify: `MSFSBlindAssist/Forms/SayIntentionsInfoForm.cs:61-70` (optional title)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`ShowSurroundingsWindow`, replacing the Task 6 stub in `MainForm.Hotkeys.cs`)
- Modify: `MSFSBlindAssist/MainForm.cs` (field `surroundingsForm`)
- Test: none (WinForms; sections composer already tested in Task 5).

**Interfaces:**
- Produces: `SayIntentionsInfoForm(IReadOnlyList<InfoSection> sections, IntPtr? previousWindow = null, string? title = null)`; `MainForm.ShowSurroundingsWindow()`.
- Consumes: `SurroundingsReport.BuildSections` (Task 5), `AirportFacilities.DescribeFacts` (Task 3).

- [ ] **Step 1: Give the info form a title parameter**

In `SayIntentionsInfoForm.cs`, change the constructor to
```csharp
    public SayIntentionsInfoForm(IReadOnlyList<InfoSection> sections, IntPtr? previousWindow = null, string? title = null)
    {
        _previousWindow = previousWindow ?? GetForegroundWindow();
        InitializeComponent(sections);
        if (!string.IsNullOrWhiteSpace(title)) Text = title;
    }
```
(`InitializeComponent` sets `Text = "SayIntentions Flight Information"` first; the override lands after it.)

- [ ] **Step 2: Write the window opener**

In `MainForm.cs` fields: `private MSFSBlindAssist.Forms.SayIntentionsInfoForm? surroundingsForm;`

In `MainForm.Announcers.cs` (replace the Task 6 stub call in `MainForm.Hotkeys.cs` with a real `ShowSurroundingsWindow()`):
```csharp
    /// <summary>
    /// Ctrl+Shift+L (output mode): everything within 1 km as a browsable list. No spoken summary
    /// on open — the screen reader speaks the window and its first item (CLAUDE.md rule). Reuses
    /// the SayIntentions sectioned list window; a fresh press replaces the previous window.
    /// </summary>
    private void ShowSurroundingsWindow()
    {
        if (airportDataProvider == null) { announcer.AnnounceImmediate("Airport database not available."); return; }
        if (!_lastOnGround) { announcer.AnnounceImmediate("In flight."); return; }

        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            IReadOnlyList<MSFSBlindAssist.Services.SayIntentions.InfoSection>? sections = null;
            string? failure = null;
            string icao = "";
            try
            {
                var nearby = airportDataProvider.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                    .Where(c => c != null && c.Length == 4).ToList();
                if (nearby.Count == 0) failure = "No airport nearby.";
                else
                {
                    icao = nearby[0];
                    var catalog = surroundingsCache.Get(icao);
                    if (catalog == null || catalog.Features.Count == 0) failure = $"No surroundings data for {icao}.";
                    else
                    {
                        var facilities = (airportDataProvider as MSFSBlindAssist.Database.IAirportFacilitiesProvider)?.GetAirportFacilities(icao);
                        double hdgTrue = MSFSBlindAssist.Services.RelativeDirection.Normalize360(position.HeadingMagnetic + position.MagneticVariation);
                        sections = MSFSBlindAssist.Navigation.Surroundings.SurroundingsReport.BuildSections(
                            icao, catalog, facilities?.DescribeFacts() ?? "", position.Latitude, position.Longitude, hdgTrue,
                            m => MSFSBlindAssist.Services.DistanceFormatter.FromMetres(m));
                    }
                }
            }
            catch (Exception ex) { failure = $"Surroundings lookup failed. {ex.Message}"; }

            void Show()
            {
                if (failure != null) { announcer.AnnounceImmediate(failure); return; }
                try { surroundingsForm?.Close(); } catch { }
                surroundingsForm = new MSFSBlindAssist.Forms.SayIntentionsInfoForm(sections!, null, $"Surroundings at {icao}");
                surroundingsForm.FormClosed += (_, _) => surroundingsForm = null;
                surroundingsForm.Show();
            }
            if (this.InvokeRequired) this.BeginInvoke(Show); else Show();
        });
    }
```

- [ ] **Step 3: Build and smoke in sim**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors.
In-sim (owner): `]` then `Ctrl+Shift+L` at KATL — a window titled "Surroundings at KATL"; Tab lands on "Airport" then "Nearby, N items, Concourse T, ahead, 120 metres, 1 of N"; Escape closes.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/SayIntentionsInfoForm.cs MSFSBlindAssist/MainForm.Announcers.cs MSFSBlindAssist/MainForm.Hotkeys.cs MSFSBlindAssist/MainForm.cs
git commit -m "feat(surroundings): Ctrl+Shift+L surroundings window

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Passing callouts — `PassingCalloutGate` and `AirportSurroundingsMonitor`

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/PassingCalloutGate.cs`
- Create: `MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs`
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs` (property after `TaxiAugmentEnabled` ~line 405; clone entry ~line 636)
- Modify: `MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs` (checkbox, load, save, tab index)
- Modify: `MSFSBlindAssist/MainForm.cs` (construct beside `groundTrafficMonitor`, ~line 723), `MainForm.AircraftSwitch.cs:220,670` (reset), `MainForm.Announcers.cs:712` (turnaround reset), settings apply path (grep `ApplyRuntimeSettings` and add the Enabled refresh there)
- Test: `tests/MSFSBlindAssist.Tests/PassingCalloutGateTests.cs`

**Interfaces:**
- Produces:
  - `sealed class PassingCalloutGate { static double PassRadiusMetres(FeatureKind); static bool IsAnnounceable(AirportFeature); void Baseline(IEnumerable<NearbyFeature> inRange, DateTime now); NearbyFeature? Evaluate(IReadOnlyList<NearbyFeature> ranked, double groundSpeedKts, DateTime now); void Reset(); }` with constants `MinSpeedKts = 2`, `MaxSpeedKts = 40`, `AbeamMinDeg = 45`, `AbeamMaxDeg = 135`, `PerFeatureRepeat = 5 min`, `GlobalGap = 10 s`.
  - `sealed class AirportSurroundingsMonitor : IDisposable { bool Enabled; Func<bool>? SuppressCheck; void Reset(); }` — ctor `(ScreenReaderAnnouncer announcer, SimConnectManager sim, IAirportDataProvider? providerGetter…)` — see code.
  - `UserSettings.SurroundingsCalloutsEnabled` (default `false`).
- Consumes: `SurroundingsReport.Rank`, `RelativeDirection.Side`, `SurroundingsCatalogCache.Get`, `SimConnectManager.RequestAircraftPosition()`/`LastKnownPosition`/`LastKnownOnGround`/`IsConnected`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/PassingCalloutGateTests.cs
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~PassingCalloutGateTests"`
Expected: build error, `PassingCalloutGate` not found.

- [ ] **Step 3: Write the gate**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/PassingCalloutGate.cs
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
        foreach (var n in ranked)   // ranked is nearest-first
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
```

- [ ] **Step 4: Run gate tests**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~PassingCalloutGateTests"`
Expected: 9 passed.

- [ ] **Step 5: Write the monitor, setting and wiring**

```csharp
// MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs
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
```

`UserSettings.cs`, after `TaxiAugmentEnabled` (line ~405):
```csharp
        /// <summary>
        /// Opt-in "Passing Concourse B, on the left." callouts while taxiing (AirportSurroundingsMonitor).
        /// Default OFF like every other automatic announcement. Applies immediately.
        /// </summary>
        public bool SurroundingsCalloutsEnabled { get; set; } = false;
```
and in `Clone()` beside `TaxiAugmentEnabled = TaxiAugmentEnabled,`: `SurroundingsCalloutsEnabled = SurroundingsCalloutsEnabled,`.

`TaxiGuidancePanel.cs`: add a field `private CheckBox surroundingsCalloutsCheckBox = null!;`, create it right after `taxiAugmentAttributionLabel` (shift the SayIntentions heading and everything below it down by 30 px):
```csharp
        surroundingsCalloutsCheckBox = new CheckBox
        {
            Text = "Announce airport buildings as you taxi past them",
            Location = new Point(20, 812),
            Size = new Size(450, 25),
            AccessibleName = "Announce airport buildings as you taxi past them",
            AccessibleDescription = "When enabled, says for example Passing Concourse B, on the left, as a terminal, hangar, tower, fuel or cargo area comes abeam while taxiing. Queued behind taxi guidance, never during takeoff, landing rollout or docking. Applies immediately."
        };
```
Add it to `Controls.AddRange` after `taxiAugmentAttributionLabel`, give it the next `TabIndex`, load with `surroundingsCalloutsCheckBox.Checked = settings.SurroundingsCalloutsEnabled;` beside line 661, save with `settings.SurroundingsCalloutsEnabled = surroundingsCalloutsCheckBox.Checked;` beside line 703.

`MainForm.cs` field + construction (after `groundTrafficMonitor = new …` line ~723):
```csharp
        surroundingsMonitor = new MSFSBlindAssist.Services.AirportSurroundingsMonitor(announcer, simConnectManager, () => airportDataProvider, surroundingsCache)
        {
            Enabled = MSFSBlindAssist.Settings.SettingsManager.Current.SurroundingsCalloutsEnabled,
            SuppressCheck = () =>
                takeoffAssistManager.IsActive
                || dockingGuidanceManager.IsActive
                || taxiGuidanceManager.State is TaxiGuidanceState.LandingRollout or TaxiGuidanceState.LiningUp
                    or TaxiGuidanceState.HoldShort or TaxiGuidanceState.ProgressiveHold
                    or TaxiGuidanceState.BacktrackingOnRunway or TaxiGuidanceState.BacktrackDeparture,
        };
```
(`takeoffAssistManager` and `dockingGuidanceManager` are constructed earlier in the same method — place this after both.) Field: `private MSFSBlindAssist.Services.AirportSurroundingsMonitor? surroundingsMonitor;`. Dispose it where `groundTrafficMonitor` is disposed.

Wherever the settings dialog applies runtime settings (grep `activeSkyWeatherMonitor.Enabled =` in MainForm — the `ApplyRuntimeSettings` path), add `if (surroundingsMonitor != null) surroundingsMonitor.Enabled = settings.SurroundingsCalloutsEnabled;`.

Resets: in `MainForm.AircraftSwitch.cs` beside both `_turnaroundDetector.Reset();` lines add `surroundingsMonitor?.Reset();`; in `MainForm.Announcers.cs` inside the `if (_turnaroundDetector.ObserveEdge(...))` block add `surroundingsMonitor?.Reset();`.

- [ ] **Step 6: Build, full suite, smoke in sim**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors; full `dotnet test …` → green.
In-sim (owner): enable the checkbox; taxi KATL from a T gate to 26R — expect "Passing Concourse T, on the right." style callouts, none while parked, none during takeoff assist.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings/PassingCalloutGate.cs MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs MSFSBlindAssist/Settings/UserSettings.cs MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.AircraftSwitch.cs MSFSBlindAssist/MainForm.Announcers.cs tests/MSFSBlindAssist.Tests/PassingCalloutGateTests.cs
git commit -m "feat(surroundings): opt-in passing callouts with a baseline-first throttled gate

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 11: Scenery tier — `BglPlacementReader` and `ModelLibNameReader`

**Files:**
- Create: `MSFSBlindAssist/Services/SceneryIndex/BglPlacementReader.cs`
- Create: `MSFSBlindAssist/Services/SceneryIndex/ModelLibNameReader.cs`
- Test: `tests/MSFSBlindAssist.Tests/BglPlacementReaderTests.cs`, `tests/MSFSBlindAssist.Tests/ModelLibNameReaderTests.cs`

**Interfaces:**
- Produces (namespace `MSFSBlindAssist.Services.SceneryIndex`):
  - `readonly record struct ScenePlacement(double Lat, double Lon, double HeadingDeg, Guid ModelGuid)`
  - `static List<ScenePlacement> BglPlacementReader.Read(ReadOnlySpan<byte> bgl)` — empty for a non-BGL or a BGL with no SceneryObject section; never throws on truncation.
  - `static Dictionary<Guid,string> ModelLibNameReader.Read(ReadOnlySpan<byte> bgl)`.
- Layout (measured 2026-09-06 on Orbx KTIW, imaginesim KATL, Axonos KJAC): magic `0x19920201` at 0; section count `u32` at `0x14`; section table at `0x38`, 20-byte entries `(type, flags, subsectionCount, offset, size)`; SceneryObject section type `0x25`; subsection entry size = `size / subsectionCount` (16 bytes observed), whose LAST two `u32` are `(dataOffset, dataSize)`; records `id u16, size u16` — LibraryObject id `0x0B`, size 64: lon `u32` at +4 → `v * 360 / (3 * 2^28) - 180`, lat `u32` at +8 → `90 - v * 180 / (2 * 2^28)`, heading `u16` at +22 → `v * 360 / 65536`, GUID 16 bytes at +44 in .NET `Guid(byte[])` layout.

- [ ] **Step 1: Write the failing tests (synthetic BGL built in the test)**

```csharp
// tests/MSFSBlindAssist.Tests/BglPlacementReaderTests.cs
// A synthetic BGL: header, one SceneryObject section, one subsection, three LibraryObject records.
// Never a payware file. Layout as measured on three Community packages, 2026-09-06.
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class BglPlacementReaderTests
{
    internal static byte[] BuildBgl(params (double lat, double lon, double hdg, Guid guid)[] objs)
    {
        const int header = 0x38, sectionEntry = 20, subEntry = 16, rec = 64;
        int sectionTable = header, subTable = sectionTable + sectionEntry, data = subTable + subEntry;
        var b = new byte[data + objs.Length * rec];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(b.AsSpan(at, 2), v);

        U32(0x00, 0x19920201); U32(0x14, 1);
        U32(sectionTable + 0, 0x25); U32(sectionTable + 4, 1); U32(sectionTable + 8, 1);
        U32(sectionTable + 12, (uint)subTable); U32(sectionTable + 16, subEntry);
        U32(subTable + 0, 0); U32(subTable + 4, (uint)objs.Length); U32(subTable + 8, (uint)data); U32(subTable + 12, (uint)(objs.Length * rec));

        int p = data;
        foreach (var (lat, lon, hdg, guid) in objs)
        {
            U16(p, 0x0B); U16(p + 2, rec);
            U32(p + 4, (uint)Math.Round((lon + 180.0) * (3.0 * (1 << 28)) / 360.0));
            U32(p + 8, (uint)Math.Round((90.0 - lat) * (2.0 * (1 << 28)) / 180.0));
            U16(p + 22, (ushort)Math.Round(hdg * 65536.0 / 360.0));
            guid.ToByteArray().CopyTo(b, p + 44);
            p += rec;
        }
        return b;
    }

    [Fact]
    public void Reads_position_heading_and_guid_of_each_library_object()
    {
        var g1 = Guid.Parse("416f6b5f-f52e-4744-858a-29067c13cdb0");
        var g2 = Guid.Parse("82cb66da-9f5b-4116-a774-a6ce91702279");
        var bgl = BuildBgl((47.27064, -122.57373, 277.0, g1), (47.26765, -122.57497, 7.0, g2));
        var placed = BglPlacementReader.Read(bgl);
        Assert.Equal(2, placed.Count);
        Assert.InRange(placed[0].Lat, 47.27063, 47.27065);
        Assert.InRange(placed[0].Lon, -122.57374, -122.57372);
        Assert.InRange(placed[0].HeadingDeg, 276.9, 277.1);
        Assert.Equal(g1, placed[0].ModelGuid);
        Assert.Equal(g2, placed[1].ModelGuid);
    }

    [Fact]
    public void Non_bgl_and_truncated_input_yield_empty_or_partial_never_throw()
    {
        Assert.Empty(BglPlacementReader.Read(new byte[] { 1, 2, 3 }));
        Assert.Empty(BglPlacementReader.Read(Array.Empty<byte>()));
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()), (2, 2, 0, Guid.NewGuid()));
        var cut = bgl.AsSpan(0, bgl.Length - 40).ToArray();          // second record truncated
        Assert.Single(BglPlacementReader.Read(cut));
    }

    [Fact]
    public void Records_that_are_not_library_objects_are_skipped()
    {
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()));
        BitConverter.TryWriteBytes(bgl.AsSpan(0x38 + 20 + 16, 2), (ushort)0x0E);   // flip the record id
        Assert.Empty(BglPlacementReader.Read(bgl));
    }
}
```

```csharp
// tests/MSFSBlindAssist.Tests/ModelLibNameReaderTests.cs
using System.Text;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class ModelLibNameReaderTests
{
    [Fact]
    public void Reads_guid_name_pairs_in_either_attribute_order_and_ignores_noise()
    {
        string xml =
            "\0\0<ModelInfo version=\"1.1\" guid=\"{416f6b5f-f52e-4744-858a-29067c13cdb0}\" name=\"KTIW_ATP_Hanger\"><LODS/></ModelInfo>\0\0" +
            "<ModelInfo guid=\"{8DC82808-32C8-4C75-8FC5-BBC825565B12}\" version=\"1.1\" name=\"concourse_a_02\"><LODS/></ModelInfo>" +
            "<ModelInfo name=\"tower_01\" version=\"1.1\" guid=\"{a611c36a-df97-4154-a1f1-65a8fbec9bd0}\"/>" +
            "<ModelInfo version=\"1.1\" name=\"no_guid\"/>";
        var names = ModelLibNameReader.Read(Encoding.Latin1.GetBytes(xml));
        Assert.Equal(3, names.Count);
        Assert.Equal("KTIW_ATP_Hanger", names[Guid.Parse("416f6b5f-f52e-4744-858a-29067c13cdb0")]);
        Assert.Equal("concourse_a_02", names[Guid.Parse("8dc82808-32c8-4c75-8fc5-bbc825565b12")]);
        Assert.Equal("tower_01", names[Guid.Parse("a611c36a-df97-4154-a1f1-65a8fbec9bd0")]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BglPlacementReaderTests|FullyQualifiedName~ModelLibNameReaderTests"`
Expected: build error, namespace `MSFSBlindAssist.Services.SceneryIndex` not found.

- [ ] **Step 3: Write the readers**

```csharp
// MSFSBlindAssist/Services/SceneryIndex/BglPlacementReader.cs
using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.SceneryIndex;

public readonly record struct ScenePlacement(double Lat, double Lon, double HeadingDeg, Guid ModelGuid);

/// <summary>
/// Reads LibraryObject placements out of an MSFS scenery BGL. Layout measured 2026-09-06 on
/// Orbx KTIW, imaginesim KATL and Axonos KJAC (see the plan/spec). Bounds-checked at every
/// step: a truncated or foreign file yields whatever parsed cleanly, never an exception.
/// </summary>
public static class BglPlacementReader
{
    private const uint Magic = 0x19920201;
    private const int HeaderSize = 0x38, SectionEntrySize = 20;
    private const uint SceneryObjectSection = 0x25;
    private const ushort LibraryObjectId = 0x0B;
    private const int LibraryObjectSize = 64;
    private const double LonScale = 360.0 / (3.0 * (1 << 28));
    private const double LatScale = 180.0 / (2.0 * (1 << 28));

    public static List<ScenePlacement> Read(ReadOnlySpan<byte> b)
    {
        var result = new List<ScenePlacement>();
        if (b.Length < HeaderSize || U32(b, 0) != Magic) return result;
        uint sections = U32(b, 0x14);
        for (uint s = 0; s < sections; s++)
        {
            int e = HeaderSize + (int)s * SectionEntrySize;
            if (e + SectionEntrySize > b.Length) break;
            if (U32(b, e) != SceneryObjectSection) continue;
            uint subCount = U32(b, e + 8), subOff = U32(b, e + 12), subSize = U32(b, e + 16);
            if (subCount == 0 || subSize < 16 || subOff + subSize > b.Length) continue;
            int subEntry = (int)(subSize / subCount);
            if (subEntry < 16) continue;
            for (uint i = 0; i < subCount; i++)
            {
                int so = (int)subOff + (int)i * subEntry;
                uint dataOff = U32(b, so + subEntry - 8), dataSize = U32(b, so + subEntry - 4);
                if (dataOff >= b.Length) continue;
                long end = Math.Min((long)dataOff + dataSize, b.Length);
                int p = (int)dataOff;
                while (p + 4 <= end)
                {
                    ushort id = U16(b, p), size = U16(b, p + 2);
                    if (size < 4 || p + size > end) break;
                    if (id == LibraryObjectId && size >= LibraryObjectSize)
                    {
                        double lon = U32(b, p + 4) * LonScale - 180.0;
                        double lat = 90.0 - U32(b, p + 8) * LatScale;
                        double hdg = U16(b, p + 22) * (360.0 / 65536.0);
                        var guid = new Guid(b.Slice(p + 44, 16));
                        result.Add(new ScenePlacement(lat, lon, hdg, guid));
                    }
                    p += size;
                }
            }
        }
        return result;
    }

    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
}
```

```csharp
// MSFSBlindAssist/Services/SceneryIndex/ModelLibNameReader.cs
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Model GUID → author's model name, from the `&lt;ModelInfo … guid="{…}" … name="…"&gt;` XML
/// fragments MSFS embeds beside each model in a modelLib/objects BGL (verified on three packages).
/// Attribute order varies by developer, so each tag is scanned for both attributes independently.
/// </summary>
public static class ModelLibNameReader
{
    private static readonly Regex Tag = new(@"<ModelInfo\b([^>]*)>", RegexOptions.CultureInvariant);
    private static readonly Regex GuidAttr = new(@"guid=""\{?([0-9a-fA-F-]{36})\}?""", RegexOptions.CultureInvariant);
    private static readonly Regex NameAttr = new(@"name=""([^""]+)""", RegexOptions.CultureInvariant);

    public static Dictionary<Guid, string> Read(ReadOnlySpan<byte> bgl)
    {
        var map = new Dictionary<Guid, string>();
        string text = Encoding.Latin1.GetString(bgl);
        foreach (Match m in Tag.Matches(text))
        {
            string attrs = m.Groups[1].Value;
            var g = GuidAttr.Match(attrs); var n = NameAttr.Match(attrs);
            if (!g.Success || !n.Success) continue;
            if (Guid.TryParse(g.Groups[1].Value, out var guid)) map[guid] = n.Groups[1].Value;
        }
        return map;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BglPlacementReaderTests|FullyQualifiedName~ModelLibNameReaderTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/SceneryIndex tests/MSFSBlindAssist.Tests/BglPlacementReaderTests.cs tests/MSFSBlindAssist.Tests/ModelLibNameReaderTests.cs
git commit -m "feat(scenery-index): BGL placement and model-name readers

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: `SceneryModelNameClassifier`

**Files:**
- Create: `MSFSBlindAssist/Services/SceneryIndex/SceneryModelNameClassifier.cs`
- Test: `tests/MSFSBlindAssist.Tests/SceneryModelNameClassifierTests.cs`

**Interfaces:**
- Produces: `sealed record ClassifiedModel(FeatureKind Kind, string Name)`; `static ClassifiedModel? SceneryModelNameClassifier.Classify(string modelName, string icao)` — null = drop. `Name` is the human text; two placements whose `Classify` results are equal (same Kind + Name) are one feature (merged by the indexer, Task 13).

Rules (each pinned by a test row below): stop-list wins over keywords; strip a leading ICAO token with optional digits (`KATL2020_`); split on `_`, `-`, space and lower→Upper camel boundaries; "hanger"→"Hangar"; a trailing 1–2 digit token is a PART number and is dropped unless the name would then be the bare kind word (then it is an identity: `Hangar_1` → "Hangar 1"); a trailing single letter is dropped only when the previous token contains a digit (`Hangar_09_B` → "Hangar 9", but `Outskirt_Hangars_A` keeps A); Concourse/Terminal keep exactly one identity token after the keyword; digit-only tokens lose leading zeros; ALL-CAPS tokens of ≤3 letters stay caps; other tokens are title-cased.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/SceneryModelNameClassifierTests.cs
// Every row is a real model name measured in an installed package on 2026-09-06.
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryModelNameClassifierTests
{
    [Theory]
    [InlineData("KTIW_Cessna_Service_Hanger", "KTIW", FeatureKind.Hangar, "Cessna Service Hangar")]
    [InlineData("KTIW_ATP_Hanger", "KTIW", FeatureKind.Hangar, "ATP Hangar")]
    [InlineData("KTIW_Pavco_Hanger", "KTIW", FeatureKind.Hangar, "Pavco Hangar")]
    [InlineData("KTIW_Narrows_Aviation_Hangar_Large_1", "KTIW", FeatureKind.Hangar, "Narrows Aviation Hangar Large")]
    [InlineData("KTIW_Hangar_09_B", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KTIW_Hangar_09B_1", "KTIW", FeatureKind.Hangar, "Hangar 9B")]
    [InlineData("KTIW_Hangar_09", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KJAC_Hangar_1", "KJAC", FeatureKind.Hangar, "Hangar 1")]
    [InlineData("KJAC_Hangar_2", "KJAC", FeatureKind.Hangar, "Hangar 2")]
    [InlineData("KTIW_Outskirt_Hangars_A", "KTIW", FeatureKind.Hangar, "Outskirt Hangars A")]
    [InlineData("KTIW_hangar_blue_octogon", "KTIW", FeatureKind.Hangar, "Hangar Blue Octogon")]
    [InlineData("Hangar_04", "KTIW", FeatureKind.Hangar, "Hangar 4")]
    [InlineData("Control_Tower_1", "KTIW", FeatureKind.Tower, "Control Tower")]
    [InlineData("KJAC_Tower", "KJAC", FeatureKind.Tower, "Tower")]
    [InlineData("tower_01", "KATL", FeatureKind.Tower, "Tower")]
    [InlineData("Fueltank", "KTIW", FeatureKind.Fuel, "Fuel")]
    [InlineData("Airport_Office_1", "KTIW", FeatureKind.Office, "Airport Office")]
    [InlineData("HubCafe_1", "KTIW", FeatureKind.Office, "Hub Cafe")]
    [InlineData("concourse_a_02", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_a_interface_12m_b36", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_t_canopy_01", "KATL", FeatureKind.Concourse, "Concourse T")]
    [InlineData("northwestern_cargo_01", "KATL", FeatureKind.Cargo, "Northwestern Cargo")]
    [InlineData("southern_hangar_01", "KATL", FeatureKind.Hangar, "Southern Hangar")]
    [InlineData("KATL2020_cargo_loader_01", "KATL", FeatureKind.Cargo, "Cargo Loader")]
    public void Classifies_measured_model_names(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    [Theory]
    [InlineData("KTIW_Fence2")]
    [InlineData("concourse_a_aircon4")]
    [InlineData("terminal_light1")]
    [InlineData("ground_terminal_light_01")]
    [InlineData("concourse_e_rooflight01")]
    [InlineData("concourse_t_carparks_02")]
    [InlineData("concourse_t_pedestrian_crossing01")]
    [InlineData("jetway_base")]
    [InlineData("safegate01")]
    [InlineData("KATL2020_truck_fuel")]
    [InlineData("KATL2020_cargovan")]
    [InlineData("KJAC_Vehicles_Fuel_Truck_JHA")]
    [InlineData("KTIW_Bridge1")]
    [InlineData("KTIW_Silo")]
    [InlineData("KTIW_Pylon")]
    [InlineData("ViewingPlatform1")]
    [InlineData("gates")]
    [InlineData("fence_black")]
    [InlineData("")]
    public void Drops_noise_and_unclassifiable_names(string model)
        => Assert.Null(SceneryModelNameClassifier.Classify(model, "KTIW"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SceneryModelNameClassifierTests"`
Expected: build error, `SceneryModelNameClassifier` not found.

- [ ] **Step 3: Write the classifier**

```csharp
// MSFSBlindAssist/Services/SceneryIndex/SceneryModelNameClassifier.cs
using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.SceneryIndex;

public sealed record ClassifiedModel(FeatureKind Kind, string Name);

/// <summary>
/// Author-chosen model names → spoken feature names. The ONLY path by which a scenery model
/// name may reach speech (spec invariant): everything unrecognised is dropped, and what is
/// kept is rewritten into human text. Every rule here is pinned by a measured name in
/// SceneryModelNameClassifierTests; extend the tables there first.
/// </summary>
public static class SceneryModelNameClassifier
{
    private static readonly Regex StopList = new(
        @"\b(fences?|lights?|rooflights?|poles?|aircon\d*|hvac|vehicles?|cars?|carparks?|trucks?|vans?|cargovan|loader|cones?|signs?|markings?|lines?|jetways?|bridges?|pylons?|silos?|lod\d*|shadows?|decals?|grass|trees?|pedestrian|crossing\d*|tickets?|platform\d*|gates?|safegate\d*|interface|canopy|base|stairs?|railing|barrier|bollards?|hydrant)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Order matters: first match wins.
    private static readonly (Regex Rx, FeatureKind Kind)[] Kinds =
    {
        (new Regex(@"\b(hangars?|hangers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Hangar),
        (new Regex(@"\b(concourse|pier|satellite)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Concourse),
        (new Regex(@"\bterminal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Terminal),
        (new Regex(@"\b(tower|atc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Tower),
        (new Regex(@"\b(fbo|aviation|jet ?cent(er|re))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fbo),
        (new Regex(@"\b(fire|arff|rescue)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.FireStation),
        (new Regex(@"\b(cargo|freight|fedex|ups|dhl)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Cargo),
        (new Regex(@"\b(deice|de ?ice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.DeicePad),
        (new Regex(@"\b(fuel|fueltank|avgas|tank)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fuel),
        (new Regex(@"\b(office|admin|cafe|restaurant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Office),
    };

    public static ClassifiedModel? Classify(string modelName, string icao)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        var tokens = Tokenize(modelName, icao);
        if (tokens.Count == 0) return null;
        string joined = string.Join(" ", tokens);
        if (StopList.IsMatch(joined)) return null;

        FeatureKind? kind = null;
        foreach (var (rx, k) in Kinds) if (rx.IsMatch(joined)) { kind = k; break; }
        if (kind == null) return null;

        // A hangar named by another kind's word ("Cessna Service Hangar" contains no other keyword) is
        // fine; but "Narrows Aviation Hangar" must be a HANGAR, not an FBO — Hangar is checked first.
        var kept = new List<string>(tokens);
        if (kind is FeatureKind.Concourse or FeatureKind.Terminal)
        {
            int kw = kept.FindIndex(t => Regex.IsMatch(t, @"^(concourse|pier|satellite|terminal)$", RegexOptions.IgnoreCase));
            kept = kw >= 0 && kw + 1 < kept.Count ? new List<string> { kept[kw], kept[kw + 1] } : new List<string> { kept[Math.Max(kw, 0)] };
        }
        else
        {
            // Trailing single letter: a part label only when the token before it carries a digit.
            if (kept.Count >= 2 && kept[^1].Length == 1 && char.IsLetter(kept[^1][0]) && kept[^2].Any(char.IsDigit))
                kept.RemoveAt(kept.Count - 1);
            // Trailing 1–2 digit token: a part number, unless removing it leaves just the kind word.
            if (kept.Count >= 2 && Regex.IsMatch(kept[^1], @"^\d{1,2}$"))
            {
                var without = kept.Take(kept.Count - 1).ToList();
                bool bareKind = without.Count == 1 && Kinds.Any(x => x.Rx.IsMatch(without[0]));
                if (!bareKind) kept = without;
            }
        }

        string name = string.Join(" ", kept.Select(Pretty));
        if (kind == FeatureKind.Fuel && kept.Count == 1) name = "Fuel";
        return new ClassifiedModel(kind.Value, name);
    }

    private static List<string> Tokenize(string model, string icao)
    {
        string s = model.Trim();
        if (!string.IsNullOrEmpty(icao))
            s = Regex.Replace(s, $@"^{Regex.Escape(icao)}\d*[_\- ]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");                   // HubCafe → Hub Cafe
        return s.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string Pretty(string t)
    {
        if (Regex.IsMatch(t, @"^\d+$")) return int.Parse(t, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (Regex.IsMatch(t, @"^\d+[A-Za-z]$")) return int.Parse(t[..^1], CultureInfo.InvariantCulture) + t[^1..].ToUpperInvariant();
        if (Regex.IsMatch(t, @"^hangers?$", RegexOptions.IgnoreCase)) return "Hangar";
        if (t.Length <= 3 && t.All(char.IsUpper)) return t;
        return char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SceneryModelNameClassifierTests"`
Expected: 43 passed. Adjust the stop-list/keyword tables — never the test rows — until every measured name lands where the table says.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/SceneryIndex/SceneryModelNameClassifier.cs tests/MSFSBlindAssist.Tests/SceneryModelNameClassifierTests.cs
git commit -m "feat(scenery-index): model-name classifier pinned to measured package names

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: `SceneryPackageLocator`, `SceneryPackageIndexer` (disk cache), setting, wiring

**Files:**
- Create: `MSFSBlindAssist/Services/SceneryIndex/SceneryPackageLocator.cs`
- Create: `MSFSBlindAssist/Services/SceneryIndex/SceneryPackageIndexer.cs`
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs` (`SceneryIndexEnabled`, default true, + clone)
- Modify: `MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs` (checkbox + read-only status `TextBox`)
- Modify: `MSFSBlindAssist/MainForm.cs` (field), `MSFSBlindAssist/MainForm.Announcers.cs` (`BuildSurroundingsFeatures`)
- Test: `tests/MSFSBlindAssist.Tests/SceneryPackageLocatorTests.cs`, `tests/MSFSBlindAssist.Tests/SceneryPackageIndexerTests.cs`

**Interfaces:**
- Produces:
  - `static List<string> SceneryPackageLocator.PackageDirs(string sceneryLocalPath, Func<string,bool> dirExists)` — absolute Community/Official package folders from the navdata column; skips `fs-base-genericairports` (no path), `navigraph-navdata`, and folders that do not exist.
  - `sealed class SceneryPackageIndexer { SceneryPackageIndexer(string cacheDir); IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs); string LastStatus; }` — per-package JSON cache keyed on `layout.json` length + mtime; merges same `(Kind, Name)` placements to their centroid; `Source = FeatureSource.Scenery`.
  - `UserSettings.SceneryIndexEnabled` (default `true`).
- Consumes: Tasks 11–12; `AirportFacilities.SceneryLocalPath` (Task 3).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/SceneryPackageLocatorTests.cs
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageLocatorTests
{
    [Fact]
    public void Keeps_existing_addon_folders_and_drops_base_and_navigraph_entries()
    {
        string col = @"fs-base-genericairports, C:\P\Community\orbx-airport-ktiw-tacoma-narrows, C:\P\Community\navigraph-navdata, C:\P\Official\OneStore\asobo-airport-ksea-seattle-tacoma, C:\P\Community\missing";
        var dirs = SceneryPackageLocator.PackageDirs(col, d => !d.EndsWith("missing"));
        Assert.Equal(new[] { @"C:\P\Community\orbx-airport-ktiw-tacoma-narrows", @"C:\P\Official\OneStore\asobo-airport-ksea-seattle-tacoma" }, dirs);
    }

    [Fact]
    public void Duplicates_collapse_and_blank_input_yields_nothing()
    {
        string col = @"C:\P\Community\pyreegue-airport-egnx-east-midlands, C:\P\Community\pyreegue-airport-egnx-east-midlands";
        Assert.Single(SceneryPackageLocator.PackageDirs(col, _ => true));
        Assert.Empty(SceneryPackageLocator.PackageDirs("", _ => true));
    }
}
```

```csharp
// tests/MSFSBlindAssist.Tests/SceneryPackageIndexerTests.cs
// Builds a fake package on disk: a modelLib BGL holding ModelInfo XML for three GUIDs and an
// objects BGL placing them (two parts of one concourse, one fence). Same synthetic builder as
// BglPlacementReaderTests.
using System.Text;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-scenery-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string MakePackage()
    {
        string pkg = Path.Combine(_root, "pkg", "scenery");
        Directory.CreateDirectory(pkg);
        var gA1 = Guid.NewGuid(); var gA2 = Guid.NewGuid(); var gF = Guid.NewGuid();
        string xml = $"<ModelInfo guid=\"{{{gA1}}}\" name=\"concourse_a_01\"/><ModelInfo guid=\"{{{gA2}}}\" name=\"concourse_a_02\"/><ModelInfo guid=\"{{{gF}}}\" name=\"KTIW_Fence2\"/>";
        var lib = new byte[0x38 + 20].Concat(Encoding.Latin1.GetBytes(xml)).ToArray();
        BitConverter.TryWriteBytes(lib.AsSpan(0, 4), 0x19920201u);            // a BGL with zero sections + XML tail
        File.WriteAllBytes(Path.Combine(pkg, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(pkg, "objects.bgl"), BglPlacementReaderTests.BuildBgl((33.640, -84.430, 0, gA1), (33.642, -84.430, 0, gA2), (33.650, -84.440, 0, gF)));
        File.WriteAllText(Path.Combine(_root, "pkg", "layout.json"), "{}");
        return Path.Combine(_root, "pkg");
    }

    [Fact]
    public void Indexes_a_package_merging_parts_and_dropping_noise_then_serves_from_cache()
    {
        string pkg = MakePackage();
        string cache = Path.Combine(_root, "cache");
        var indexer = new SceneryPackageIndexer(cache);

        var features = indexer.GetFeatures("KATL", new[] { pkg });
        var only = Assert.Single(features);
        Assert.Equal(FeatureKind.Concourse, only.Kind);
        Assert.Equal("Concourse A", only.Name);
        Assert.InRange(only.Lat, 33.6409, 33.6411);
        Assert.Equal(FeatureSource.Scenery, only.Source);
        Assert.Single(Directory.GetFiles(cache, "*.json"));
        Assert.Contains("1 features", indexer.LastStatus);

        // Second call: cache hit (delete the BGLs to prove nothing is re-read).
        File.Delete(Path.Combine(pkg, "scenery", "objects.bgl"));
        Assert.Single(indexer.GetFeatures("KATL", new[] { pkg }));

        // layout.json change → rebuild → now empty because the placements are gone.
        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{ \"changed\": true }");
        Assert.Empty(indexer.GetFeatures("KATL", new[] { pkg }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SceneryPackage"`
Expected: build errors for both new types.

- [ ] **Step 3: Write the locator and indexer**

```csharp
// MSFSBlindAssist/Services/SceneryIndex/SceneryPackageLocator.cs
namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// The package folders navdatareader recorded for an airport (airport.scenery_local_path),
/// e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows". ONLY these
/// are ever opened (spec invariant: never the whole Community tree).
/// </summary>
public static class SceneryPackageLocator
{
    public static List<string> PackageDirs(string sceneryLocalPath, Func<string, bool> dirExists)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sceneryLocalPath)) return result;
        foreach (var raw in sceneryLocalPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.Contains('\\') && !raw.Contains('/')) continue;                       // "fs-base-genericairports": no folder
            if (raw.EndsWith("navigraph-navdata", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Contains(raw, StringComparer.OrdinalIgnoreCase)) continue;
            if (!dirExists(raw)) continue;
            result.Add(raw);
        }
        return result;
    }
}
```

```csharp
// MSFSBlindAssist/Services/SceneryIndex/SceneryPackageIndexer.cs
using System.Text.Json;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Tier 3: what the installed scenery actually models. For each package folder: every *.bgl
/// is read once for ModelInfo names and once for LibraryObject placements; placements whose
/// GUID resolves in-package are classified; same (Kind, Name) placements merge to a centroid.
/// Result cached per package as JSON under cacheDir, keyed on layout.json length + mtime —
/// the user's OWN local files, so a disk cache is fine (unlike OSM data). Call from a
/// background thread or a hotkey handler: the first read of a 100 MB objects.BGL takes a
/// few hundred ms; every later call is a JSON read.
/// </summary>
public sealed class SceneryPackageIndexer
{
    private readonly string _cacheDir;
    private readonly object _lock = new();
    public string LastStatus { get; private set; } = "";

    private sealed class CacheFile
    {
        public string Package { get; set; } = "";
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        public int Placements { get; set; }
        public int Unresolved { get; set; }
        public List<Entry> Features { get; set; } = new();
    }
    private sealed class Entry { public FeatureKind Kind { get; set; } public string Name { get; set; } = ""; public double Lat { get; set; } public double Lon { get; set; } }

    public SceneryPackageIndexer(string cacheDir) { _cacheDir = cacheDir; }

    public IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs)
    {
        var all = new List<AirportFeature>();
        var status = new List<string>();
        foreach (var dir in packageDirs)
        {
            try
            {
                var cf = LoadOrBuild(dir, icao);
                all.AddRange(cf.Features.Select(e => new AirportFeature { Kind = e.Kind, Name = e.Name, Lat = e.Lat, Lon = e.Lon, Source = FeatureSource.Scenery }));
                status.Add($"{cf.Features.Count} features from {Path.GetFileName(dir)} ({cf.Placements} placements, {cf.Unresolved} base-library)");
            }
            catch (Exception ex)
            {
                Log.Warn("SceneryIndex", $"{icao}: {Path.GetFileName(dir)}: {ex.Message}");
                status.Add($"{Path.GetFileName(dir)}: unreadable");
            }
        }
        LastStatus = status.Count == 0 ? $"{icao}: no add-on package" : $"{icao}: " + string.Join("; ", status);
        return all;
    }

    private CacheFile LoadOrBuild(string dir, string icao)
    {
        string layout = Path.Combine(dir, "layout.json");
        var info = new FileInfo(layout);
        long len = info.Exists ? info.Length : 0, ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
        string cachePath = Path.Combine(_cacheDir, Path.GetFileName(dir.TrimEnd('\\', '/')) + ".json");

        lock (_lock)
        {
            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath));
                    if (cached != null && cached.LayoutLength == len && cached.LayoutTicks == ticks) return cached;
                }
                catch { /* rebuild */ }
            }

            var names = new Dictionary<Guid, string>();
            var placements = new List<ScenePlacement>();
            foreach (var bgl in Directory.EnumerateFiles(dir, "*.bgl", SearchOption.AllDirectories))
            {
                byte[] bytes = File.ReadAllBytes(bgl);
                foreach (var kv in ModelLibNameReader.Read(bytes)) names[kv.Key] = kv.Value;
                placements.AddRange(BglPlacementReader.Read(bytes));
            }

            var groups = new Dictionary<(FeatureKind, string), List<LatLon>>();
            int unresolved = 0;
            foreach (var p in placements)
            {
                if (!names.TryGetValue(p.ModelGuid, out var model)) { unresolved++; continue; }
                var c = SceneryModelNameClassifier.Classify(model, icao);
                if (c == null) continue;
                if (!groups.TryGetValue((c.Kind, c.Name), out var pts)) groups[(c.Kind, c.Name)] = pts = new List<LatLon>();
                pts.Add(new LatLon(p.Lat, p.Lon));
            }

            var cf = new CacheFile { Package = dir, LayoutLength = len, LayoutTicks = ticks, Placements = placements.Count, Unresolved = unresolved };
            foreach (var ((kind, name), pts) in groups)
            {
                var cen = SurroundingsGeometry.Centroid(pts);
                cf.Features.Add(new Entry { Kind = kind, Name = name, Lat = cen.Lat, Lon = cen.Lon });
            }
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cf));
            Log.Info("SceneryIndex", $"{icao}: indexed {Path.GetFileName(dir)}: {cf.Features.Count} features, {placements.Count} placements, {unresolved} unresolved");
            return cf;
        }
    }
}
```

- [ ] **Step 4: Run the two test classes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~SceneryPackage"`
Expected: 3 passed.

- [ ] **Step 5: Setting, panel, wiring**

`UserSettings.cs` after `SurroundingsCalloutsEnabled`:
```csharp
        /// <summary>
        /// Read the installed scenery package's placement BGLs for named buildings (hangars,
        /// concourses, tower…) at add-on airports. Offline; cached under %APPDATA%\MSFSBlindAssist\scenery-index.
        /// </summary>
        public bool SceneryIndexEnabled { get; set; } = true;
```
plus the `Clone()` entry.

`TaxiGuidancePanel.cs`: add `sceneryIndexEnabledCheckBox` (Text "Read installed scenery for airport buildings (offline)", AccessibleDescription "When enabled, reads the add-on airport package on disk for named hangars, concourses, the tower and other modeled buildings and includes them in the surroundings readout. No network use. Applies on the next airport.") placed after the callouts checkbox, and a read-only `TextBox` (`ReadOnly = true`, `Multiline = true`, `Height = 44`, `AccessibleName = "Scenery index status"`) below it — a `Label` is not in the tab order (CLAUDE.md VATSIM rule). The panel constructor gains `Func<string>? sceneryIndexStatus` and the TextBox shows its value on load ("KTIW: 36 features from orbx-airport-ktiw-tacoma-narrows (1269 placements, 257 base-library)"). Load/save the checkbox like the others. Shift the SayIntentions block down by 100 px.

`MainForm.cs` field:
```csharp
    private readonly MSFSBlindAssist.Services.SceneryIndex.SceneryPackageIndexer sceneryIndexer =
        new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "scenery-index"));
```
Wherever the settings dialog constructs `TaxiGuidancePanel`, pass `() => sceneryIndexer.LastStatus`.

`MainForm.Announcers.cs` `BuildSurroundingsFeatures`, after the OSM line:
```csharp
        if (MSFSBlindAssist.Settings.SettingsManager.Current.SceneryIndexEnabled && facilities != null)
        {
            var dirs = MSFSBlindAssist.Services.SceneryIndex.SceneryPackageLocator.PackageDirs(facilities.SceneryLocalPath, System.IO.Directory.Exists);
            features.AddRange(sceneryIndexer.GetFeatures(icao, dirs));
        }
```

- [ ] **Step 6: Build, suite, smoke in sim**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors; full test run → green.
In-sim (owner): KTIW, `]` `Alt+L` on the Narrows Aviation ramp — expect "Narrows Aviation Hangar" / "Cessna Service Hangar" / "Control Tower"; `%APPDATA%\MSFSBlindAssist\scenery-index\orbx-airport-ktiw-tacoma-narrows.json` exists; the settings TextBox shows the status line.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/SceneryIndex MSFSBlindAssist/Settings/UserSettings.cs MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.Announcers.cs tests/MSFSBlindAssist.Tests/SceneryPackageLocatorTests.cs tests/MSFSBlindAssist.Tests/SceneryPackageIndexerTests.cs
git commit -m "feat(scenery-index): package indexer with disk cache, setting and look-around wiring

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: Spike — are Asobo base-library model names worth resolving?

**Files:**
- Create (throwaway, NOT committed unless it earns its keep): `tools/SceneryIndexProbe/SceneryIndexProbe.csproj` + `Program.cs`
- Output: a paragraph in `docs/superpowers/specs/2026-09-06-airport-surroundings-design.md` under "Out of scope" or a follow-up task, depending on the answer.

**Question:** ~50 % of KATL/KJAC placements reference GUIDs outside the package (2,626 of 5,324 at KATL). If those resolve in the Official `fs-base*` modelLibs to names the classifier can use ("ASOBO_Hangar_Medium_01"?), a one-time base index adds generic hangars/towers at every airport; if they are unnamed or all vehicles/props, it is not worth the read.

- [ ] **Step 1: Write the probe**

```xml
<!-- tools/SceneryIndexProbe/SceneryIndexProbe.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\MSFSBlindAssist\Services\SceneryIndex\BglPlacementReader.cs" Link="BglPlacementReader.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Services\SceneryIndex\ModelLibNameReader.cs" Link="ModelLibNameReader.cs" />
  </ItemGroup>
</Project>
```

```csharp
// tools/SceneryIndexProbe/Program.cs
// Usage: dotnet run --project tools/SceneryIndexProbe -- <packageDir> <officialRoot>
// Prints how many placement GUIDs resolve in-package, how many in the Official tree, and the
// top 40 resolved base-library names — so a human can judge the classifier's chances.
using MSFSBlindAssist.Services.SceneryIndex;

string pkg = args[0], official = args[1];
var names = new Dictionary<Guid, string>(); var placed = new List<ScenePlacement>();
foreach (var f in Directory.EnumerateFiles(pkg, "*.bgl", SearchOption.AllDirectories))
{ var b = File.ReadAllBytes(f); foreach (var kv in ModelLibNameReader.Read(b)) names[kv.Key] = kv.Value; placed.AddRange(BglPlacementReader.Read(b)); }
var unresolved = placed.Where(p => !names.ContainsKey(p.ModelGuid)).Select(p => p.ModelGuid).ToHashSet();
Console.WriteLine($"placements {placed.Count}, in-package {placed.Count - unresolved.Count}, unresolved distinct {unresolved.Count}");

var baseNames = new Dictionary<Guid, string>();
foreach (var f in Directory.EnumerateFiles(official, "modelLib*.bgl", SearchOption.AllDirectories))
{ try { foreach (var kv in ModelLibNameReader.Read(File.ReadAllBytes(f))) baseNames[kv.Key] = kv.Value; } catch { } }
var hits = unresolved.Where(baseNames.ContainsKey).Select(g => baseNames[g]).ToList();
Console.WriteLine($"base library names indexed {baseNames.Count}; unresolved now resolved {hits.Count}");
foreach (var g in hits.GroupBy(n => n).OrderByDescending(g => g.Count()).Take(40)) Console.WriteLine($"{g.Count()}\t{g.Key}");
```

- [ ] **Step 2: Run it against KATL and KJAC**

Run (Official root is the `OneStore` folder beside `Community`):
```bash
dotnet run --project tools/SceneryIndexProbe -- "C:/Users/marsh/AppData/Local/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache/Packages/Community/imaginesim-airport-katl-jackson-atlanta" "C:/Users/marsh/AppData/Local/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache/Packages/Official/OneStore"
```
Expected: a count line and up to 40 names. Judge: if ≥ 20 % of the resolved names would classify as Hangar/Tower/Terminal/Fuel/Fire, add a follow-up task "base-library index" (index once, cache under `scenery-index/_base.json`, keyed on the count of modelLib files + total size); otherwise record the measured numbers in the spec's "Out of scope" section and delete the probe.

- [ ] **Step 3: Record the verdict**

Append the measured numbers and the decision to the spec (`## Out of scope` or a new `## Follow-ups` section). Commit the spec change; commit the probe only if the follow-up task is taken (then it becomes a kept tool, listed in CLAUDE.md's standalone-tools paragraph).

```bash
git add docs/superpowers/specs/2026-09-06-airport-surroundings-design.md
git commit -m "docs(surroundings): base-library resolution spike verdict

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 15: GSX terminal tier

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs`
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`BuildSurroundingsFeatures`)
- Test: `tests/MSFSBlindAssist.Tests/GsxTerminalFeatureSourceTests.cs`

**Interfaces:**
- Produces: `static List<AirportFeature> GsxTerminalFeatureSource.Read(IReadOnlyList<ParkingSpot> selectableGates)` — groups GSX-sourced spots by `SpeakableTerminalName`, ≥ 2 stands per group, `Source = FeatureSource.Gsx`, `Kind = Concourse` when the name contains "Concourse" else `Terminal`, `Name` = the speakable terminal name.
- Consumes: `ParkingSpot.Source == GateSource.Gsx`, `ParkingSpot.TerminalName`, `ParkingSpot.SpeakableTerminalName(string? terminalName)` (existing STATIC method at ParkingSpot.cs:~310, strips the "=< Medium" / "N/A" tails; if it is private, make it `internal static` — the test project has InternalsVisibleTo).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MSFSBlindAssist.Tests/GsxTerminalFeatureSourceTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class GsxTerminalFeatureSourceTests
{
    private static ParkingSpot Gsx(string terminal, double lat, double lon)
        => new() { Source = GateSource.Gsx, TerminalName = terminal, Latitude = lat, Longitude = lon, Number = 1, Name = "B" };

    [Fact]
    public void Groups_gsx_stands_by_terminal_name_and_types_concourses()
    {
        var spots = new List<ParkingSpot>
        {
            Gsx("Terminal 4 - Concourse B", 40.6440, -73.7820), Gsx("Terminal 4 - Concourse B", 40.6442, -73.7820),
            Gsx("A-Platform =< Medium ", 52.31, 4.76), Gsx("A-Platform =< Medium ", 52.311, 4.76),
            Gsx("Lonely", 1, 1),
            new ParkingSpot { Source = GateSource.Navdata, TerminalName = "Terminal 4 - Concourse B", Latitude = 40.7, Longitude = -73.7 },
        };
        var f = GsxTerminalFeatureSource.Read(spots);
        Assert.Equal(2, f.Count);
        var b = f.Single(x => x.Kind == FeatureKind.Concourse);
        Assert.Equal("Terminal 4 - Concourse B", b.Name);
        Assert.InRange(b.Lat, 40.6440, 40.6442);
        Assert.Equal(FeatureSource.Gsx, b.Source);
        Assert.Equal("A-Platform", f.Single(x => x.Kind == FeatureKind.Terminal).Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GsxTerminalFeatureSourceTests"`
Expected: build error, `GsxTerminalFeatureSource` not found.

- [ ] **Step 3: Write the source and wire it**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// GSX's uiTerminalName per stand, grouped. Read from the SELECTABLE list (GetSelectableGates)
/// — GetNamedSpots deliberately does not carry TerminalName. Outranks navdata's letter
/// inference in the catalog (GSX is right where navdata's letter is wrong, measured KJFK).
/// </summary>
public static class GsxTerminalFeatureSource
{
    public static List<AirportFeature> Read(IReadOnlyList<ParkingSpot> selectableGates)
    {
        var result = new List<AirportFeature>();
        var groups = selectableGates
            .Where(s => s.Source == GateSource.Gsx && !string.IsNullOrWhiteSpace(ParkingSpot.SpeakableTerminalName(s.TerminalName)))
            .GroupBy(s => ParkingSpot.SpeakableTerminalName(s.TerminalName).Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            var c = SurroundingsGeometry.Centroid(members.Select(m => new LatLon(m.Latitude, m.Longitude)).ToList());
            bool concourse = g.Key.Contains("Concourse", StringComparison.OrdinalIgnoreCase);
            result.Add(new AirportFeature { Kind = concourse ? FeatureKind.Concourse : FeatureKind.Terminal, Name = g.Key, Lat = c.Lat, Lon = c.Lon, Source = FeatureSource.Gsx });
        }
        return result;
    }
}
```

In `BuildSurroundingsFeatures` after the navdata line:
```csharp
        var selectable = MSFSBlindAssist.Services.ParkingSpotSource.GetSelectableGates(provider, BuildGateDataSource(), icao);
        features.AddRange(MSFSBlindAssist.Navigation.Surroundings.GsxTerminalFeatureSource.Read(selectable));
```
(Check `GetSelectableGates`' exact parameter list at `Services/ParkingSpotSource.cs:108` and match it.)

Note for the catalog: a GSX "Terminal 4 - Concourse B" and a navdata "Concourse B" are both `Concourse` but their names differ, so `SameFeature` falls back to the 150 m radius — which is the intended behaviour (same building, one entry, GSX's fuller name wins by rank).

- [ ] **Step 4: Run tests, build**

Run: full test suite → green; `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs MSFSBlindAssist/MainForm.Announcers.cs tests/MSFSBlindAssist.Tests/GsxTerminalFeatureSourceTests.cs
git commit -m "feat(surroundings): GSX terminal names as a catalog tier

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 16: `FeatureDestinationResolver` — a place becomes a navdata destination

**Files:**
- Create: `MSFSBlindAssist/Navigation/Surroundings/FeatureDestinationResolver.cs`
- Test: `tests/MSFSBlindAssist.Tests/FeatureDestinationResolverTests.cs`

**Interfaces:**
- Produces:
  - `sealed record PlaceDestination(AirportFeature Feature, ParkingSpot? Spot, int NodeId, double Lat, double Lon, double HeadingDeg, double DistanceMetres)` — `Spot` null means "route ends at the nearest taxi node"; `Lat/Lon/HeadingDeg` are the STAND's (or node's), never the building's.
  - `readonly record struct NearestNode(int NodeId, double Lat, double Lon, double DistanceMetres)`
  - `static bool FeatureDestinationResolver.IsRoutable(FeatureKind kind)` — Fbo, Hangar, Fuel, Terminal, Concourse, Cargo, FireStation, DeicePad, Office; never Tower, Helipad, Apron, Other.
  - `static PlaceDestination? FeatureDestinationResolver.Resolve(AirportFeature feature, IReadOnlyList<ParkingSpot> spots, Func<double, double, NearestNode?> nearestNode)`
  - `static string FeatureDestinationResolver.Label(PlaceDestination d)` — `"Narrows Aviation, FBO, ramp spot 12"` / `"Cessna Service Hangar, hangar, end of taxiway"`.
  - Constants `MaxSpotMetres = 150`, `MaxNodeMetres = 100`.
- Consumes: `ParkingSpot` (`Type`, `Latitude`, `Longitude`, `Heading`, `Name`, `Number`, `Suffix`), Task 2 types. The rule: a feature is NEVER a route target itself — it RESOLVES onto navdata pavement (a stand within 150 m, preferring the stand type that matches the place; else a taxi node within 100 m; else not routable). Same anti-grass rule as OSM aliases and GSX stands.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/FeatureDestinationResolverTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class FeatureDestinationResolverTests
{
    private static AirportFeature F(FeatureKind k, string name, double lat, double lon)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Scenery };
    private static ParkingSpot S(int type, double lat, double lon, string name = "Parking", int number = 1, double hdg = 90)
        => new() { Type = type, Latitude = lat, Longitude = lon, Name = name, Number = number, Heading = hdg };
    private static NearestNode? NoNode(double lat, double lon) => null;
    private static NearestNode? NodeAt(double lat, double lon) => new(42, lat + 0.0002, lon, 22.0);

    // 0.0009° lat ≈ 100 m.
    [Fact]
    public void Fbo_prefers_the_nearest_ga_stand_over_a_closer_gate()
    {
        var fbo = F(FeatureKind.Fbo, "Narrows Aviation", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(10, 47.2703, -122.5700, "A", 5), S(4, 47.2708, -122.5700, "Parking", 12) };
        var d = FeatureDestinationResolver.Resolve(fbo, spots, NoNode);
        Assert.NotNull(d);
        Assert.Equal(12, d!.Spot!.Number);
        Assert.Equal(47.2708, d.Lat);
        Assert.Equal(90.0, d.HeadingDeg);
        Assert.InRange(d.DistanceMetres, 85, 95);
        Assert.Equal("Narrows Aviation, FBO, Parking 12", FeatureDestinationResolver.Label(d));
    }

    [Fact]
    public void Falls_back_to_any_non_vehicle_stand_when_no_preferred_type_is_close()
    {
        var hangar = F(FeatureKind.Hangar, "ATP Hangar", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(10, 47.2703, -122.5700, "A", 5), S(17, 47.2701, -122.5700, "V", 1), S(4, 47.2720, -122.5700) };
        var d = FeatureDestinationResolver.Resolve(hangar, spots, NoNode);
        Assert.Equal(5, d!.Spot!.Number);   // the gate: vehicles excluded, GA stand at 220 m is beyond 150 m
    }

    [Fact]
    public void Fuel_resolves_to_a_fuel_stand()
    {
        var fuel = F(FeatureKind.Fuel, "Fuel", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(4, 47.2701, -122.5700), S(16, 47.2706, -122.5700, "Parking", 3) };
        Assert.Equal(16, FeatureDestinationResolver.Resolve(fuel, spots, NoNode)!.Spot!.Type);
    }

    [Fact]
    public void Node_fallback_within_100m_when_no_stand_is_within_150m()
    {
        var hangar = F(FeatureKind.Hangar, "Cessna Service Hangar", 47.2700, -122.5700);
        var spots = new List<ParkingSpot> { S(4, 47.2720, -122.5700) };
        var d = FeatureDestinationResolver.Resolve(hangar, spots, NodeAt);
        Assert.NotNull(d);
        Assert.Null(d!.Spot);
        Assert.Equal(42, d.NodeId);
        Assert.Equal("Cessna Service Hangar, hangar, end of taxiway", FeatureDestinationResolver.Label(d));
        Assert.Null(FeatureDestinationResolver.Resolve(hangar, spots, NoNode));
        Assert.Null(FeatureDestinationResolver.Resolve(hangar, spots, (la, lo) => new NearestNode(7, la, lo, 130.0)));
    }

    [Theory]
    [InlineData(FeatureKind.Tower)]
    [InlineData(FeatureKind.Helipad)]
    [InlineData(FeatureKind.Apron)]
    [InlineData(FeatureKind.Other)]
    public void Non_routable_kinds_never_resolve(FeatureKind kind)
    {
        Assert.False(FeatureDestinationResolver.IsRoutable(kind));
        Assert.Null(FeatureDestinationResolver.Resolve(F(kind, "X", 47.27, -122.57), new List<ParkingSpot> { S(4, 47.27, -122.57) }, NodeAt));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FeatureDestinationResolverTests"`
Expected: build error, `FeatureDestinationResolver` not found.

- [ ] **Step 3: Write the resolver**

```csharp
// MSFSBlindAssist/Navigation/Surroundings/FeatureDestinationResolver.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public readonly record struct NearestNode(int NodeId, double Lat, double Lon, double DistanceMetres);

/// <summary>
/// Where the route actually ends when the pilot asks for a PLACE. Spot null → the nearest
/// taxi node; Lat/Lon/HeadingDeg are the stand's (or node's), never the building's.
/// </summary>
public sealed record PlaceDestination(AirportFeature Feature, ParkingSpot? Spot, int NodeId, double Lat, double Lon, double HeadingDeg, double DistanceMetres);

/// <summary>
/// A feature is never a route target itself (spec invariant: features are readout-only and
/// never enter TaxiGraph). It RESOLVES onto navdata pavement: the nearest stand within 150 m,
/// preferring the stand type that matches the place (GA ramp for an FBO/hangar, FUEL for fuel,
/// cargo for cargo), else any non-vehicle stand in range, else a taxi node within 100 m, else
/// not routable. Same anti-grass rule OSM aliases and GSX stands already follow.
/// </summary>
public static class FeatureDestinationResolver
{
    public const double MaxSpotMetres = 150.0;
    public const double MaxNodeMetres = 100.0;

    public static bool IsRoutable(FeatureKind kind) => kind is FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Fuel
        or FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Cargo or FeatureKind.FireStation
        or FeatureKind.DeicePad or FeatureKind.Office;

    private static bool IsPreferredStand(FeatureKind kind, int type) => kind switch
    {
        FeatureKind.Fbo or FeatureKind.Hangar or FeatureKind.Office or FeatureKind.FireStation => type is 2 or 3 or 4 or 5 or 12 or 15,
        FeatureKind.Fuel => type == 16,
        FeatureKind.Cargo => type is 6 or 7,
        FeatureKind.Terminal or FeatureKind.Concourse => type is 9 or 10 or 11 or 13 or 14,
        _ => true,
    };

    public static PlaceDestination? Resolve(AirportFeature feature, IReadOnlyList<ParkingSpot> spots, Func<double, double, NearestNode?> nearestNode)
    {
        if (!IsRoutable(feature.Kind)) return null;

        ParkingSpot? best = null; double bestD = double.MaxValue; bool bestPreferred = false;
        foreach (var s in spots)
        {
            if (s.Type == 17) continue;                               // vehicles: never a place to park an aircraft
            double d = TaxiGeo.HaversineMeters(feature.Lat, feature.Lon, s.Latitude, s.Longitude);
            if (d > MaxSpotMetres) continue;
            bool preferred = IsPreferredStand(feature.Kind, s.Type);
            if (best == null || (preferred && !bestPreferred) || (preferred == bestPreferred && d < bestD))
            { best = s; bestD = d; bestPreferred = preferred; }
        }
        if (best != null)
            return new PlaceDestination(feature, best, -1, best.Latitude, best.Longitude, best.Heading, bestD);

        var node = nearestNode(feature.Lat, feature.Lon);
        if (node is NearestNode n && n.DistanceMetres <= MaxNodeMetres)
            return new PlaceDestination(feature, null, n.NodeId, n.Lat, n.Lon, TaxiGeo.BearingDeg(n.Lat, n.Lon, feature.Lat, feature.Lon), n.DistanceMetres);
        return null;
    }

    /// <summary>"Narrows Aviation, FBO, Parking 12" — the place, its kind, and the stand you are actually guided to.</summary>
    public static string Label(PlaceDestination d)
    {
        string kind = FeatureKindWords.Generic(d.Feature.Kind);
        string kindWord = kind == "FBO" ? kind : kind.ToLowerInvariant();
        string where = d.Spot != null ? $"{d.Spot.Name} {d.Spot.Number}{d.Spot.Suffix}".Trim() : "end of taxiway";
        return $"{d.Feature.SpokenName}, {kindWord}, {where}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FeatureDestinationResolverTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/Surroundings/FeatureDestinationResolver.cs tests/MSFSBlindAssist.Tests/FeatureDestinationResolverTests.cs
git commit -m "feat(surroundings): resolve a place onto navdata pavement for routing

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 17: "Place" destination type in the Taxi Assist form

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — `cmbDestType.Items` (line 494), `PopulateDestinations` (line 2191, add an `isPlace` branch modelled on the deice branch at ~2330), `OnDestTypeChanged` (line 2742: hide the gate search/filters for Place; the empty-list announcement at its end), a new `Func<string, AirportFeatureCatalog?>? SurroundingsCatalogSupplier` property
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs` (or wherever `new TaxiAssistForm(` is constructed — grep) to set `SurroundingsCatalogSupplier = icao => surroundingsCache.Get(icao)`
- Test: none new (form code); the resolver carries the logic.

**Interfaces:**
- Produces: destination-type index **4 = "Place"**; labels from `FeatureDestinationResolver.Label`; the same `_destinationNodeMap/_destinationHeadingMap/_destinationHeadingTrueMap/_destinationThresholdMap/_destinationSpotMap` entries the gate and deice branches fill, so `OnCalculateClicked`, `LoadRoute`, `SetDestinationGate` and `ApplyGsxStopOffset` need NO change — a Place with a stand docks like a gate; a Place with only a node clears docking (`_destinationSpotMap` has no entry, so `TryGetValue` yields null → `SetDestinationGate(null)`).
- Consumes: Task 16, `SurroundingsCatalogCache.Get` (Task 6), `TaxiGraph.FindNearestNode(lat, lon)` → `TaxiNode` (`NodeId`, `Latitude`, `Longitude`), `TaxiGraph.CalculateDistanceMeters`, `ParkingSpotSource.GetNamedSpots(_dataProvider, _gateSource, icao)` (the form already calls this at ~line 371 of LandingExitForm and in its own gate branch — match the local call).

- [ ] **Step 1: Add the type and the supplier**

Line 494:
```csharp
        cmbDestType.Items.AddRange(new object[] { "Runway", "Gate / Parking", "Progressive Taxi", "Deice Area", "Place" });
```
Property near `_destinationSpotMap` (line ~305):
```csharp
    /// <summary>
    /// The surroundings catalog for an ICAO (MainForm wires SurroundingsCatalogCache.Get). Null
    /// keeps the "Place" destination list empty, which is announced, never silent.
    /// </summary>
    public Func<string, Navigation.Surroundings.AirportFeatureCatalog?>? SurroundingsCatalogSupplier { get; set; }
```
Where MainForm constructs the form (grep `new TaxiAssistForm(`), add after construction: `taxiAssistForm.SurroundingsCatalogSupplier = icao => surroundingsCache.Get(icao);`.

- [ ] **Step 2: Populate the Place list**

In `PopulateDestinations`, after `bool isDeice = cmbDestType.SelectedIndex == 3;` add `bool isPlace = cmbDestType.SelectedIndex == 4;` and, before the final `else` (the gate branch), a new branch:

```csharp
        else if (isPlace)
        {
            // PLACE path: FBOs, hangars, fuel, terminals, cargo from the surroundings catalog,
            // each RESOLVED onto navdata pavement by FeatureDestinationResolver (a stand within
            // 150 m, else a taxi node within 100 m). Fills the same maps as the gate and deice
            // branches so Calculate, LoadRoute and docking need no Place-specific code. A feature
            // that resolves to nothing is not listed — there is no way to taxi to it.
            var catalog = SurroundingsCatalogSupplier?.Invoke(_currentIcao);
            if (catalog != null)
            {
                var named = Services.ParkingSpotSource.GetNamedSpots(_dataProvider, _gateSource, _currentIcao);
                Navigation.Surroundings.NearestNode? Nearest(double lat, double lon)
                {
                    var n = _graph.FindNearestNode(lat, lon);
                    if (n == null) return null;
                    return new Navigation.Surroundings.NearestNode(n.NodeId, n.Latitude, n.Longitude,
                        TaxiGraph.CalculateDistanceMeters(n.Latitude, n.Longitude, lat, lon));
                }

                foreach (var feature in catalog.Features.Where(f => Navigation.Surroundings.FeatureDestinationResolver.IsRoutable(f.Kind))
                                                        .OrderBy(f => f.SpokenName, StringComparer.OrdinalIgnoreCase))
                {
                    var dest = Navigation.Surroundings.FeatureDestinationResolver.Resolve(feature, named, Nearest);
                    if (dest == null) continue;

                    int nodeId = dest.NodeId;
                    if (dest.Spot != null)
                    {
                        var nearNode = _graph.FindNearestNode(dest.Lat, dest.Lon);
                        if (nearNode == null) continue;
                        if (TaxiGraph.CalculateDistanceMeters(nearNode.Latitude, nearNode.Longitude, dest.Lat, dest.Lon) > 100.0) continue;
                        nodeId = nearNode.NodeId;
                    }

                    string label = Navigation.Surroundings.FeatureDestinationResolver.Label(dest);
                    if (_destinationNodeMap.ContainsKey(label)) continue;
                    _destinationNodeMap[label] = nodeId;
                    _destinationHeadingMap[label] = dest.HeadingDeg;
                    _destinationHeadingTrueMap[label] = dest.HeadingDeg;
                    _destinationThresholdMap[label] = (dest.Lat, dest.Lon);
                    if (dest.Spot != null) _destinationSpotMap[label] = dest.Spot;
                    cmbDestination.Items.Add(label);
                }
            }
        }
```

(Headings: `ParkingSpot.Heading` is already the convention the gate branch stores for both maps — copy exactly what the gate branch does for `_destinationHeadingTrueMap` if it converts; read the gate branch's two heading lines and mirror them.)

- [ ] **Step 3: Type switching and the empty announcement**

In `OnDestTypeChanged`, nothing else changes for Place (`isGate` is index 1, so the search box and filters hide themselves). At its end, beside the deice `"no deicing areas"` announcement, add:
```csharp
        if (cmbDestType.SelectedIndex == 4 && cmbDestination.Items.Count == 0)
            _announcer?.Announce($"No places to route to at {_currentIcao}. Open Alt+L to hear what is around you.");
```
(Match the deice announcement's exact announcer call and field name.)

- [ ] **Step 4: Build and smoke in sim**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → 0 errors; full test suite → green.
In-sim (owner): KTIW after landing on 17 — Taxi form, destination type Place, pick "Narrows Aviation, FBO, Parking 12", Calculate → route summary names it, guidance ends at the stand, docking engages if navdata heading is usable. Then pick "Fuel, fuel, Parking 3" → route to the fuel island.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs MSFSBlindAssist/MainForm*.cs
git commit -m "feat(taxi): Place destination type — taxi to an FBO, hangar, fuel or terminal

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

**Deferred (own task later, against a live clearance):** SayIntentions "taxi to the FBO" / "taxi to Signature" → a `Place` candidate tried after the gate name and before any runway, through the same resolver. Not in this plan: no capture yet of SI phrasing a place instead of a stand.

---

### Task 18: Docs, invariants, changelog fragments, PR

**Files:**
- Modify: `docs/taxi-guidance.md` (hotkey tables at lines ~489 and ~593; a new "Airport surroundings" section after the Where-Am-I implementation section, ~line 628; the stale "per-ICAO JSON, 30-day TTL" line ~1922 corrected to "in-memory")
- Modify: `docs/hotkey-system.md` (output-mode table: `Alt+L`, `Ctrl+Shift+L`)
- Modify: `CLAUDE.md` (Taxi guidance invariants group: six bullets from the spec's "Invariants this design adds")
- Create after the PR exists: `changelog.d/<pr>-airport-surroundings.feature.md`, `changelog.d/<pr>-surroundings-callouts.feature.md`, `changelog.d/<pr>-scenery-index.feature.md`

- [ ] **Step 1: Write the docs**

`docs/taxi-guidance.md` — add to the user hotkey table (line ~489):
```
| Look around | Output > `Alt+L` | `Taxiway A at KTIW. Narrows Aviation Hangar, to the right, 80 metres. Control Tower, ahead, 200 metres. Fuel, behind and to the left, 210 metres.` Where you are, the apron or concourse you are in, then the nearest features. Ground-only. |
| Surroundings window | Output > `Ctrl+Shift+L` | Read-only list of everything within 1 km, nearest first, with the airport's fuel and frequencies on the first row. |
| Taxi to a place | Taxi form, destination type **Place** | Lists every FBO, hangar, fuel island, terminal, cargo area the catalog knows that resolves onto a stand (or a taxi node) — "Narrows Aviation, FBO, Parking 12" — and routes there like a gate. |
```
and a new section "## Airport surroundings (Alt+L, Ctrl+Shift+L, passing callouts)" carrying: the three tiers with their measured coverage (copy the spec's Motivation bullets), the merge order, the callout rules (radii, abeam window, throttles, suppression list, baseline-first), the settings, the scenery-index cache location, and the six invariants. Fix the augmentation diagram line that still says "per-ICAO JSON, 30-day TTL" to "in-memory, per session".

`docs/hotkey-system.md` — add both chords to the output-mode table beside `Alt+Y`.

`CLAUDE.md` — under "### Taxi guidance" invariants, add:
```
- Airport surroundings features (`Navigation/Surroundings`) are READOUT ONLY — never handed to `TaxiGraph.Build`, never a node, never a routing/hold-short input. The ONE way a place becomes a destination is `FeatureDestinationResolver`, which resolves it onto a navdata stand within 150 m (else a taxi node within 100 m) and routes to THAT — never to the building's coordinate. → [taxi-guidance.md](docs/taxi-guidance.md)
- OSM feature data stays IN-MEMORY like every other OSM datum; only the scenery index (the user's own local package files) is disk-cached, under `%APPDATA%\MSFSBlindAssist\scenery-index`. → [taxi-guidance.md](docs/taxi-guidance.md)
- The OSM feature query is scoped to the `aeroway=aerodrome` AREA, and the radius fallback is bbox-filtered against the navdata airport extent — a bare radius admitted a Chevron on the road outside KTIW. → [taxi-guidance.md](docs/taxi-guidance.md)
- A scenery model name reaches speech ONLY through `SceneryModelNameClassifier`; raw `KTIW_*` / `concourse_a_02` strings never do. Its rules are pinned to measured package names — extend the test table first. → [taxi-guidance.md](docs/taxi-guidance.md)
- Passing callouts are queued (`Announce`), baseline-first, abeam-only (45°–135°), throttled (5 min per feature, 10 s global, 2–40 kt) and suppressed under takeoff assist, landing rollout, docking, LiningUp/HoldShort/ProgressiveHold and `announcer.Suppressed`. → [taxi-guidance.md](docs/taxi-guidance.md)
- The scenery scan opens only the folders `airport.scenery_local_path` names, never the whole Community tree, never on the UI thread, never on a position update; `airport.tower_lonx/laty` is NULL on every row of the current navdatareader build, so the tower never comes from navdata. → [taxi-guidance.md](docs/taxi-guidance.md)
```

- [ ] **Step 2: Commit docs, push, open the PR**

```bash
git add docs/taxi-guidance.md docs/hotkey-system.md CLAUDE.md
git commit -m "docs: airport surroundings awareness

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
git push -u origin feature/airport-surroundings
gh pr create --repo oasis1701/msfs-blind-assist --base main --title "Airport surroundings awareness: Alt+L look-around, surroundings window, passing callouts, scenery index" --body-file <(cat <<'EOF'
## Summary
- `Alt+L` (output mode): where you are, the apron/concourse you are in, the nearest terminals, hangars, FBOs, tower, fuel and cargo with direction and distance.
- Taxi form destination type **Place**: taxi to an FBO, hangar, fuel island, terminal or cargo area — resolved onto the navdata stand in front of it.
- `Ctrl+Shift+L`: a read-only list of everything within 1 km plus the airport's fuel and frequencies.
- Opt-in passing callouts ("Passing Concourse B, on the left.") while taxiing, default off.
- Three sources: unused navdata columns (concourse inference from gate letters, fuel/cargo/GA clusters, helipads, COM), a widened area-scoped OSM query, and the installed scenery package's placement BGL (offline, disk-cached).

## In-sim test plan
1. KTIW (Orbx): park on the Narrows Aviation ramp; `]` `Alt+L` → expect named hangars, Control Tower, Fuel. `Ctrl+Shift+L` → Airport row shows Avgas and jet fuel + Tower 118.5.
2. KJAC (Axonos): on the commercial ramp → "On the Commercial Ramp. General Aviation Terminal, …".
3. KATL (imaginesim): at a T gate → "At Concourse T." then Concourse A/B and cargo; enable callouts, taxi to 26R → "Passing Concourse …" callouts, none during takeoff assist.
4. A default Asobo airport with lettered gates (no OSM/scenery): concourse readout from navdata alone.
5. Airborne: `Alt+L` says "In flight."
6. KTIW after landing: Taxi form → Place → "Narrows Aviation, FBO, Parking 12" → Calculate → guidance ends at the stand.

Spec: docs/superpowers/specs/2026-09-06-airport-surroundings-design.md

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)
```

- [ ] **Step 3: Add the changelog fragments with the REAL PR number**

Read the number from the URL `gh pr create` printed (never guess it), then:

```bash
# replace NNN with the number printed by gh pr create
cat > changelog.d/NNN-airport-surroundings.feature.md <<'EOF'
Press Alt+L on the ground to hear what is around you: the taxiway or stand you are on, the apron or concourse you are in, and the nearest terminals, hangars, FBOs, tower, fuel and cargo areas with their direction and distance. Ctrl+Shift+L opens the full list within one kilometre, with the airport's fuel and frequencies on the first row.
EOF
cat > changelog.d/NNN-surroundings-callouts.feature.md <<'EOF'
Optional passing callouts while taxiing ("Passing Concourse B, on the left") — off by default, switched on under Taxi Guidance settings, and silent during takeoff, landing rollout and docking.
EOF
cat > changelog.d/NNN-taxi-to-place.feature.md <<'EOF'
The taxi form has a new destination type, Place: pick an FBO, hangar, fuel island, terminal or cargo area by name and guidance takes you to the stand in front of it, docking included where the scenery gives a stop.
EOF
cat > changelog.d/NNN-scenery-index.feature.md <<'EOF'
At add-on airports the surroundings readout also knows what the installed scenery actually models — named hangars, concourses, cargo buildings and the tower are read from the package on disk, so a hangar OpenStreetMap leaves unnamed can still be called by name.
EOF
git add changelog.d
git commit -m "changelog: airport surroundings fragments

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
git push
```

- [ ] **Step 4: Verify CI**

Run: `gh pr checks --watch` — the test job and the changelog check must both be green.

---

## Self-review notes (done at writing time)

- Spec coverage: §1 model → T2; §2a navdata → T3 (tower removed per the measured NULL column); §2b OSM → T7–T8; §2c scenery → T11–T14; §2d GSX → T15; §3 merge → T4; §4 geometry → T1–T2; §5 `Alt+L` → T5–T6; §6 window → T9 (reuses `SayIntentionsInfoForm`, no F5 — spec amended); §7 callouts → T10; §8 settings → T10, T13; §9 hotkeys → T6; §10 error handling → each reader/source swallows to empty + `Log.Warn`; §11 tests → one class per pure unit; §12 phases → task order; §7b taxi-to-place → T16–T17 (SayIntentions mapping deferred).
- Type consistency: `NearbyFeature(Feature, DistanceMetres, RelativeBearingDeg)` used identically in T5 and T10; `AirportFacilities` fields in T3 match the reads in T8/T9/T13; `SurroundingsCatalogCache.Get(icao)` used by T6, T9, T10; `InfoSection` is the existing SayIntentions record.
- Deviation from spec recorded: the window has no `F5`; reopen the chord instead.
