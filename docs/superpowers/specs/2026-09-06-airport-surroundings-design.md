# Airport Surroundings Awareness — Design

**Date:** 2026-09-06
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance / ground awareness (`Services/TaxiGuidanceManager.cs`,
`Navigation/TaxiGraph.cs`, `Services/TaxiAugment/*`, `Database/LittleNavMapProvider.cs`,
`MainForm.Announcers.cs`, `Hotkeys/HotkeyManager.cs`)
**Branch:** `feature/airport-surroundings` (off `upstream/main`). Nothing in this
work touches `francesco/feat/cows-da40`.

## Summary

Give a pilot on the ground a picture of what is AROUND the aircraft, not just
what is under it. Today Where Am I (`Alt+Y`) answers "Taxiway B at KTIW" and
nothing more. This design adds a per-airport **feature catalog** — terminals,
concourses, FBOs, hangars, the tower, fuel, cargo, fire station, helipads,
named aprons, de-ice pads — assembled from three sources, and three consumers
of it:

1. **`Alt+L` (output mode) — "Look around."** One utterance: where you are, the
   zone you are in, then the nearest features with direction and distance.
2. **`Ctrl+Shift+L` (output mode) — Surroundings window.** A read-only list of
   every feature within 1 km, nearest first, for browsing with the arrow keys.
3. **Passing callouts (opt-in, default off).** "Passing Concourse B, on the
   left." as a notable feature comes abeam while taxiing.

The three sources, in the order they are delivered:

| Tier | Source | What it uniquely contributes | Availability |
|---|---|---|---|
| 1 | Navdata columns already in the user's DB and never read | Concourse grouping from gate letters, airline codes per gate, fuel/vehicle/cargo/GA stand types, tower position, helipads, avgas/jet availability, COM frequencies | Every airport, offline |
| 2 | OpenStreetMap via the existing Overpass pipeline, query widened | Named terminals/concourses, FBOs (with operator), named aprons and de-ice pads, hangars, fire station, tower, cargo | Wherever OSM is mapped; needs the existing online opt-in |
| 3 | The installed scenery package for the airport (placement BGL + model names) | What THIS scenery actually models: "Cessna Service Hangar", "ATP Hangar", "Concourse A", cargo buildings — including hangars OSM leaves unnamed | Add-on airports only; offline; disk-cached per package |

Features are **readout only**. They never enter `TaxiGraph`, never affect
routing, hold-shorts or the steering tone.

## Motivation

The app now supports GA (COWS DA40) and the user flies payware airports
(inibuilds, Orbx, imaginesim, Aerosoft, MK Studios, FlyTampa — about 55 add-on
airports in the current navdata). A sighted GA pilot taxiing at a field like
KTIW sees the FBO, the fuel island, which hangar row is Narrows Aviation, the
tower. A blind pilot gets "Taxiway A". At a hub, "Concourse B is off your
right" is the difference between orientation and being lost on a ramp.

Measured on 2026-09-06 against the user's `fs2020.sqlite` and Community folder:

- The navdata schema has **no building table at all** — terminal/hangar/FBO
  cannot come from navdatareader. It DOES carry `parking.type` (16 = fuel,
  17 = vehicles, cargo, GA small/medium/large), `parking.airline_codes` (set on
  about 1,700 gates), the concourse letter on every gate, `airport.tower_lonx/laty`
  (+ `has_tower_object`), `has_avgas`/`has_jetfuel`, `helipad`, `apron`
  (unnamed polygons) and `com`. None of these is read anywhere in the app.
- OSM at KATL names all seven concourses, North/South/Domestic terminals,
  FedEx and UPS cargo, 15 named aprons, the fire station, the tower, and 201
  gates. At KJAC it names the General Aviation Terminal with its FBO
  operator, the Teton Interagency Helibase, the Commercial Ramp and a
  "De-icing pad" apron. At KTIW it names the control tower and **none** of
  its 20 hangars. A radius query also catches a Chevron gas station on the
  road outside KTIW — the query must be scoped to the aerodrome polygon.
- The Orbx KTIW package's placement BGL parses: 1,269 placements, 1,012
  resolving to in-package model names — 36 distinct named objects including
  `KTIW_Narrows_Aviation_Hangar_Large`, `KTIW_Cessna_Service_Hanger`,
  `KTIW_ATP_Hanger`, `KTIW_Pavco_Hanger`, `Control_Tower_1`, `Fueltank`,
  `Airport_Office`, `HubCafe_1` — each with lat/lon/heading. imaginesim KATL:
  5,324 placements, 2,698 in-package (concourses A–F as multi-part models,
  `northwestern_cargo_01`, `tower_01`); Axonos KJAC: `KJAC_Hangar_1/2`,
  `KJAC_Tower`. Roughly half of KATL/KJAC placements reference Asobo's base
  library by GUID — resolving those needs a one-time index of the Official
  `fs-base*` modelLibs (a spike inside the plan).
- The existing OSM query (`OsmTaxiSource.BuildQuery`) asks only for taxiway,
  parking_position, gate and holding_position.

## Background (current behaviour and what is reused)

- **Where Am I** — `MainForm.Announcers.cs` `AnnounceWhereAmI()` → ground gate
  (`_lastOnGround`) → position → `GetNearbyAirportICAOs(…, 5.0)` filtered to
  4-char at the call site → `TaxiGuidanceManager.DescribeCurrentLocation` →
  `TaxiGraph.DescribeLocation(lat, lon)` (parking within 40 m, runway centreline,
  taxiway edge, near-node fallback). No heading, no distances, nothing about
  surroundings. Its graph cache `_whereAmICachedGraph` is invalidated by
  `GateDataSource.GetGateListVersion` via `ShouldRebuildGateList` and by
  `OnAirportDataUpdated(icao)` from the augmentation fetch.
- **Augmentation** — `AugmentingAirportDataProvider` (decorator, gated on
  `UserSettings.TaxiAugmentEnabled`, default ON), sources `OsmTaxiSource` +
  `XplaneAptDatSource`, `TaxiDataCache` **in-memory only** (user ruling: no
  disk cache for OSM data), one merge pass, `AirportDataUpdated` event.
  Non-interface accessors (`GetHoldingPoints`, `GetNamedHoldingPoints`) are the
  precedent for exposing new online data without widening
  `IAirportDataProvider`.
- **Direction phrasing** — `GroundTrafficMonitor.DescribeDirection(relBearing)`
  ("ahead", "ahead and to the right", "to the right", "behind and to the
  right", "behind"), private today. `TcasTraffic.ClockPosition` is the only
  clock-position helper and lives on the TCAS model.
- **Distances** — `DistanceFormatter` on `UserSettings.GroundDistanceUnit`
  (display-only; rounds 5/10/50 m, 25/50 ft). `GroundTrafficUseMetres` is a
  separate, independent toggle — not used here.
- **ParkingSpot** already carries `AirlineCodes` (never consumed), `Type`
  (incl. Fuel/Vehicles), and GSX `TerminalName` (kept as data even when not
  spoken; NOT copied onto `GetNamedSpots`, so a terminal readout reads the
  selectable list).
- **Announcement rules** (CLAUDE.md): background changes announce; a
  periodic/automatic callout is queued (`Announce`), never `AnnounceImmediate`;
  baseline-first monitors; no explanatory tails ("do not baby the pilot");
  no feet-quantities for cross-track guidance (landmark distances are not
  cross-track guidance and are fine).

## Design

### 1. Model — `Navigation/Surroundings/AirportFeature.cs`

```csharp
enum FeatureKind { Terminal, Concourse, Fbo, Hangar, Tower, Fuel, Cargo,
                   FireStation, Helipad, Apron, DeicePad, Office, Other }
enum FeatureSource { Navdata, Osm, Scenery, Gsx }

sealed class AirportFeature {
    FeatureKind Kind; string Name; double Lat, Lon;      // representative point
    IReadOnlyList<GeoPoint>? Footprint;                   // OSM way / merged parts; null for points
    FeatureSource Source; int Priority;                   // higher wins in merge
    string? Detail;                                       // "Delta gates", "operator: Jackson Hole Aviation", "avgas"
}
```

`Name` is what is SPOKEN, verbatim — so it must already be human text
("Cessna Service Hangar", never `KTIW_Cessna_Service_Hanger`). A feature with
no usable name is still kept when its kind is self-describing (a hangar is
"a hangar"); an `Other` with no name is dropped.

### 2. Sources

Each source is a pure function `IReadOnlyList<AirportFeature> Read(icao, …)`
with no UI or SimConnect dependency, so each is unit-testable and each can be
absent.

**2a. `NavdataFeatureSource`** (new SQL in `LittleNavMapProvider`, surfaced via a
new accessor on the augmenting decorator — NOT a widening of
`IAirportDataProvider`):

- **Concourses from gates.** Group `parking` rows whose mapped `Name` is a
  concourse letter (via the existing `MapParkingName`, i.e. `GA` becomes `A`)
  and whose type is a gate type. Each group with 2 or more gates becomes a
  `Concourse` named "Concourse {letter}" at the gate centroid, `Detail` from
  the majority `airline_codes` ("Delta gates") when at least 60 % of the coded
  gates share one airline. Directional names (`NP` = "North") become an
  `Apron` named "North ramp". This is what gives a default Asobo airport a
  concourse readout with no network at all.
- **Stand clusters.** `parking.type` FUEL → one `Fuel` feature per cluster
  (rows within 60 m merge; name "Fuel" + `Detail` from `has_avgas`/`has_jetfuel`
  → "avgas and jet"); cargo types → `Cargo` ("Cargo ramp"); GA ramp types → an
  `Apron` "GA ramp" cluster; vehicle stands are ignored.
- **Tower: NOT from navdata.** `airport.tower_lonx/laty` is NULL on all 41,866 rows of
  the current build (measured 2026-09-06) even where `has_tower_object = 1`; the tower
  comes from OSM and the scenery scan only.
- **Helipads** from `helipad` (name "Helipad" or "Helipad {n}" when several).
- **Airport facts** (not features; one line on the window only): avgas/jet
  fuel flags, tower/ground/ATIS/UNICOM from `com`, rendered as "Tower 118.5"
  and so on. This is the GA "who do I call" line. Out of the `Alt+L` utterance.
- `apron` polygons (`vertices` blob, atools binary) are NOT decoded in v1 —
  they carry no name, so containment could only say "on an apron", which
  Where Am I already covers with "ramp". Recorded as a possible later spike.

**2b. `OsmFeatureSource`** — widens `OsmTaxiSource.BuildQuery` (one query, one
round trip; the mirrors, cooldown and User-Agent are unchanged):

```
area["aeroway"="aerodrome"]["icao"="{ICAO}"]->.ad;        // scope to the field
(
  way["aeroway"="taxiway"](area.ad); …existing four…        // unchanged
  nwr["aeroway"~"^(terminal|hangar|apron|tower|control_tower|fuel|helipad)$"](area.ad);
  nwr["building"~"^(hangar|terminal)$"](area.ad);
  nwr["man_made"="tower"]["tower:type"="aircraft_control"](area.ad);
  nwr["amenity"~"^(fuel|fire_station)$"](area.ad);
  nwr["office"]["name"](area.ad);                          // FBO candidates
  nwr["building"]["name"](area.ad);                        // named buildings → Office/Other
);
out tags center;   // plus geom for apron ways (footprint)
```

Fallback: when the aerodrome area has no `icao` tag (or the area query returns
nothing), re-run with the existing `around:5000` form plus a post-filter that
keeps only features inside the airport bounding box from the `airport` row
(`left_lonx/right_lonx/top_laty/bottom_laty`). This is what keeps the Chevron
on the highway out.

Classification (`OsmFeatureClassifier`, pure): `aeroway=terminal` → `Terminal`,
or `Concourse` when the name starts "Concourse", or `Fbo` when
`terminal:type=general_aviation` or the name/operator matches the FBO lexicon
(Aviation, Jet Center, FBO, Air Center, Flight Support, Signature, Atlantic,
Million Air…); `aeroway|building=hangar` → `Hangar`; `aeroway=apron` →
`Apron` (footprint kept), or `DeicePad` when the name matches /de-?ic/i;
`aeroway=tower|control_tower` and `man_made=tower` + `aircraft_control` →
`Tower`; `aeroway=fuel|amenity=fuel` → `Fuel`; `amenity=fire_station` →
`FireStation`; `aeroway=helipad` → `Helipad`; named `office`/`building` →
`Fbo` when the name matches the FBO lexicon, `Cargo` when it contains
Cargo/Freight, else `Office`. Ways get their representative point from the
existing `TryRepresentativePoint`; `out center` supplies it for relations.

The parsed features ride the existing `AirportTaxiData` (new `Features` list)
through `TaxiDataCache` — **in-memory only**, same as everything OSM, so the
ODbL "produced work" position in `docs/taxi-guidance.md` is unchanged.

**2c. `SceneryFeatureSource`** (phase 5):

- Package discovery: `airport.scenery_local_path` in the navdata row lists the
  package folders that contribute to the airport (navdatareader already
  records it: `fs-base-genericairports, Community\orbx-airport-ktiw-…`). Only
  those folders are scanned; never the whole Community tree.
- `BglPlacementReader` (pure, byte-level): BGL magic `0x19920201`, section
  table at 0x38 (20-byte entries), section type `0x25` SceneryObject,
  subsection `(qmid, count, offset, size)`, records `id 0x0B, size ≥ 64`:
  lon = `u32 * 360 / (3 * 2^28) - 180`, lat = `90 - u32 * 180 / (2 * 2^28)`,
  heading = `u16 * 360 / 65536` at +22, GUID (mixed-endian) at +44. Verified
  on three packages.
- `ModelLibNameReader`: regex over the BGL bytes for
  `<ModelInfo … guid="{…}" … name="…">` (either attribute order). Also
  verified. Unresolved GUIDs are counted and, in the spike, looked up in an
  index of the Official `fs-base*` modelLibs to see whether base-library names
  (generic hangars, towers) are worth having.
- `SceneryModelNameClassifier` (pure, the heart of it): tokenise on `_`, `-`,
  case change and digits; strip a leading ICAO token; classify on keywords
  (`hangar|hanger` → Hangar; `concourse|pier|satellite` → Concourse;
  `terminal` → Terminal; `tower|atc` → Tower; `fbo|aviation|jet.?cent` → Fbo;
  `fuel|avgas|tank` → Fuel; `cargo|freight|fedex|ups|dhl` → Cargo;
  `fire|arff|rescue` → FireStation; `deice|de_ice` → DeicePad;
  `office|admin|cafe|restaurant` → Office); DROP on a stop-list
  (`fence|light|pole|aircon|hvac|vehicle|car|truck|van|cone|sign|marking|line|jetway|bridge|pylon|silo|lod\d|shadow|decal|grass|tree`)
  and drop anything unclassified. Display name: remaining tokens title-cased,
  "Hanger" normalised to "Hangar", trailing part numbers (`_02`, `_1`, `_B`)
  folded — multi-part models with the same stem (`concourse_a_01..03`) MERGE
  into one feature at the centroid of their placements. Names that reduce to
  the bare kind ("Hangar", "Hangar 9B") stay as-is; the kind word is never
  spoken twice.
- Disk cache: `%APPDATA%\MSFSBlindAssist\scenery-index\<package>.json` keyed on
  `layout.json` size + mtime; rebuilt when either changes. The user's own
  local files — no licensing question. The scan runs in the background at
  airport load (`Task.Run`), never on a position update; measured cost is
  milliseconds per package after the first read.
- Setting `SceneryIndexEnabled`, default ON, on the Taxi Guidance settings
  panel with a read-only status TextBox ("KTIW: 36 features from
  orbx-airport-ktiw-tacoma-narrows").

**2d. GSX terminal names** (a tier already in the app): when GSX's selectable
list carries `TerminalName`, gates grouped by it yield `Terminal`/`Concourse`
features with priority above navdata's letter-grouping (GSX is right where
navdata's letter is wrong — measured at KJFK). Read from
`ParkingSpotSource.GetSelectableGates`, never from the graph.

### 3. Merge — `AirportFeatureCatalog`

`Build(navdata, gsx, osm, scenery)` → deduplicated list:

- Two features of the SAME kind within a kind-specific radius (Tower 100 m,
  Terminal/Concourse 150 m, Hangar 40 m, Fuel 60 m, others 50 m) collapse to
  one. Winner by `Priority`: OSM named > Scenery named > GSX > Navdata >
  unnamed. The loser's `Footprint`/`Detail` are kept if the winner lacks one.
- Concourse name reconciliation: "Concourse B" (navdata) and "Concourse B"
  (OSM/scenery) match on the letter regardless of distance between the gate
  centroid and the building.
- The result is immutable, sorted by kind then name, with a `Version` token
  composed of the gate-list token + an OSM-fetch counter + the scenery-index
  stamp, so consumers can cache per airport exactly the way
  `_whereAmICachedGraph` does.
- Lives on `TaxiGuidanceManager` beside the Where-Am-I cache (`_surroundings`,
  `_surroundingsIcao`, `_surroundingsToken`), built off the UI thread,
  invalidated by the same two events (`ShouldRebuildGateList` upgrade,
  `OnAirportDataUpdated`).

### 4. Geometry — `SurroundingsGeometry` (pure)

- `RelativeBearing(ownLat, ownLon, ownHeadingTrue, feature)` → -180..180.
- `RelativeDirection.Describe(relBearing)` — `GroundTrafficMonitor.DescribeDirection`
  LIFTED into `Services/RelativeDirection.cs` and the monitor rewired to call
  it (one phrasing app-wide; its existing thresholds 20/70/110/160 unchanged;
  pinned by a test).
- Distance: haversine to the representative point; for a feature with a
  `Footprint` the distance is to the nearest polygon edge (so "Concourse B,
  50 metres" means the wall, not the roof centroid) and `Contains(point)`
  answers the zone question.
- Heading comes from the position request (radians, multiply by 57.2958, per
  the gsx.md gotcha), true; the direction words are relative, so mag/true does
  not matter as long as the bearing uses the same convention.

### 5. Consumer 1 — `Alt+L` "Look around" (`MainForm.Announcers.cs`)

Ground-only (same `_lastOnGround` gate and "In flight." answer as Where Am I).
One queued utterance composed by `SurroundingsReport.Compose(...)` (pure):

```
{WhereAmI line}. {Zone}. {Feature 1}, {dir}, {dist}. {Feature 2}, … (up to 4)
```

- `{WhereAmI line}` is exactly `DescribeCurrentLocation`'s text (reuse, not
  copy).
- `{Zone}`: the innermost containing Apron/DeicePad footprint ("On the
  Commercial Ramp.") or, failing that, the nearest Concourse/Terminal within
  120 m ("At Concourse B."). Omitted when neither applies.
- Features: nearest-first within 600 m, at most one per kind unless the kind
  is Hangar/Fbo (GA fields are all hangars), max 4, the zone feature excluded.
  Hangars with no name read "Hangar"; two unnamed hangars in the top 4 are
  collapsed to "Hangars, to the left, 80 metres".
- "Nothing within 600 metres." when the catalog is empty in range; "No
  surroundings data for {icao}." when the catalog itself is empty.
- Distances via `DistanceFormatter.FromMetres(...)` (rounded, long form).

### 6. Consumer 2 — `Ctrl+Shift+L` Surroundings window (`Forms/SurroundingsForm.cs`)

One `ListBox` (the SayIntentions flight-info pattern: list items braille as
units and announce "3 of 17"), `AccessibleName` "Surroundings at KTIW, 17
items", item 0 pre-selected, no spoken summary on open (the reader speaks the
list). Rows: `"{Name} — {dir}, {dist}"` plus a first "facts" row ("Avgas and
jet fuel. Tower 118.5, Ground 121.9, UNICOM 122.95.") when tier-1 facts exist.
Everything within 1 km, nearest first.
`Escape` closes; reopen the chord for a fresh snapshot. Not live-updating (no
reselect-under-the-caret problem). Reuses `SayIntentionsInfoForm` (sectioned
list window) with a title parameter rather than a new form.

### 7. Consumer 3 — passing callouts (`Services/AirportSurroundingsMonitor.cs`)

- Setting `SurroundingsCalloutsEnabled` (default **OFF**), Taxi Guidance panel.
- Fed from the same ground position stream MainForm already drives taxi
  guidance/docking from; the monitor holds the catalog reference and does no
  I/O.
- `PassingCalloutGate` (pure state machine, tested): a feature fires when it is
  announceable (Terminal, Concourse, Fbo, Tower, Fuel, Cargo, FireStation,
  and Hangar only when NAMED), its distance is within `PassRadius(kind)`
  (Concourse/Terminal 150 m, Tower 200 m, others 100 m) AND its relative
  bearing is in the abeam window (45° to 135° either side), it has not fired
  in the last 5 minutes, no callout has fired in the last 10 s, ground speed
  is 2 to 40 kt, and it was NOT already inside its radius when the monitor
  (re)started (baseline-first — a start-up at the gate must not recite the
  terminal).
- Suppressed entirely while: takeoff assist active, landing rollout, docking
  `IsActive`, taxi guidance in `LiningUp` or a hold-short stop, or
  `announcer.Suppressed`.
- Phrase: "Passing {Name}, {left|right}." — side only, no distance, no advice.
  Always `Announce` (queued).
- Reset on aircraft switch, sim reconnect, airport change, and the
  turnaround-liftoff detector (the same trio every other baseline-first
  monitor observes).

### 8. Settings

| Setting | Default | Panel |
|---|---|---|
| `SurroundingsCalloutsEnabled` | false | Taxi Guidance |
| `SceneryIndexEnabled` | true | Taxi Guidance (with status TextBox) |
| OSM feature tags | ride `TaxiAugmentEnabled` (already ON) | — |

The widened OSM query is one materially larger request per airport. It still
rides the single existing opt-in because splitting the switch would let a
pilot have taxiway names without terminals for no reason they could name; the
settings text for `TaxiAugmentEnabled` is updated to say it also fetches
airport buildings and aprons.

### 9. Hotkeys

- `Alt+L` output mode → `HotkeyAction.LookAround` (free; adjacent to `Alt+Y`).
- `Ctrl+Shift+L` output mode → `HotkeyAction.ShowSurroundings` (free).
- Both need a position, so both need SimConnect; they follow Where Am I's
  connection handling (not in the `offlineActions` set).
- `docs/hotkey-system.md` and `docs/taxi-guidance.md` hotkey tables gain both.

### 10. Error handling

- Any source failing (OSM down, package unreadable, a BGL that is not a BGL)
  yields an empty list for that source and one `Log.Warn`; the catalog is
  built from whatever answered. `Alt+L` never throws — worst case it speaks
  the Where-Am-I line alone.
- The BGL reader bounds-checks every offset against the file length and stops
  at the first malformed record (a truncated file yields partial, never a
  crash).
- The scenery scan never runs on the UI thread and never on a position update.
- A catalog build failure leaves the previous catalog in place (never nulled
  out on error).

### 11. Testing

Pure logic, xUnit (`tests/MSFSBlindAssist.Tests`):

- `SceneryModelNameClassifierTests` — the measured names: KTIW's 36, KATL's
  concourse parts, the stop-list, "Hanger" to "Hangar", ICAO prefix strip,
  part-number folding, bare-kind naming.
- `BglPlacementReaderTests` — a synthetic BGL BUILT IN THE TEST (header,
  section table, one 0x25 subsection, three 0x0B records with known
  lat/lon/heading/GUID) plus truncation cases. Never a payware file.
- `ModelLibNameReaderTests` — both attribute orders, GUID case.
- `OsmFeatureClassifierTests` — fixture JSON from the KATL/KJAC/KTIW probes
  (excerpts, ODbL-attributed in the file header).
- `NavdataConcourseInferenceTests` — letter grouping, airline majority,
  directional ramps, fuel/cargo clusters.
- `AirportFeatureCatalogTests` — dedupe radii, priority, concourse letter
  reconciliation, footprint/detail inheritance.
- `SurroundingsReportTests` — ordering, per-kind cap, hangar collapse,
  empty/no-data phrases, distance unit.
- `PassingCalloutGateTests` — abeam window, radii, 5-min per-feature and 10-s
  global throttles, speed band, baseline-first, suppression flags.
- `RelativeDirectionTests` — pins the lifted thresholds so the ground-traffic
  phrasing cannot drift.

Sim-facing (in-sim test plan for the PR): KTIW (Orbx, GA: hangar names,
tower, fuel), KJAC (Axonos, FBO terminal + de-ice apron), KATL (imaginesim,
concourses A–F + cargo), and one default Asobo airport with lettered gates
(navdata-only concourse inference). Each: `Alt+L` at the stand, mid-taxi,
holding short; `Ctrl+Shift+L` list; callouts ON for one taxi to the runway.

### 12. Phases (for the implementation plan)

1. Model, geometry, `RelativeDirection` lift, navdata source, catalog, `Alt+L`.
2. OSM source (query, classifier, cache plumbing), zone containment.
3. Surroundings window (`Ctrl+Shift+L`).
4. Passing-callout monitor + setting.
5. Scenery index (readers, classifier, cache, setting, base-library spike).
6. GSX terminal tier, docs (`taxi-guidance.md` new section + hotkey tables,
   `hotkey-system.md`), changelog fragments, CLAUDE.md invariant lines.

Each phase is a PR-able increment that leaves the app fully working.

## Invariants this design adds (for CLAUDE.md / taxi-guidance.md)

- Surroundings features are READOUT ONLY: never handed to `TaxiGraph.Build`,
  never a node, never a routing/hold-short input.
- OSM feature data stays IN-MEMORY like every other OSM datum; only the
  scenery index (the user's own local files) is disk-cached.
- The OSM query is scoped to the aerodrome AREA, with a bbox post-filter on
  the radius fallback — a bare radius admits road gas stations and hotels.
- A model name is SPOKEN only after the classifier has produced human text;
  raw `KTIW_*` / `concourse_a_02` strings never reach speech.
- Passing callouts are queued, baseline-first, abeam-only, throttled, and
  suppressed under every guidance phase that already speaks.
- The scenery scan runs only over the packages `scenery_local_path` names,
  never the whole Community tree, and never on the UI thread or a position
  update.

## Out of scope (recorded, not forgotten)

- Taxiway SIGN text (the airport BGL carries `TaxiwaySign` records — "[A2]",
  "35-17" are visible in KTIW's Objects.bgl). A natural later addition ("sign
  ahead reads A2"), separate spec.
- Decoding navdata `apron.vertices` blobs.
- Jet-bridge/gate-door positions from OSM `aeroway=gate` / `jet_bridge`.
- An airport BRIEFING readout (runways, frequencies, pattern altitude) as its
  own key — the facts row on the window is the toe-hold.
- Airborne use; the feature is ground-only like Where Am I.
