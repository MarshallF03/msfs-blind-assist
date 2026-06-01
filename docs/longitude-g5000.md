# Cessna Citation Longitude (G5000) — Avionics Discovery & Build Reference

Live-probed 2026-06-01 against the stock Asobo/Working Title Longitude on a
runway, powered, with an EGNT→EKCH flight plan loaded. This is the authoritative
map for building the accessibility layer. **Everything here was confirmed live.**

## Aircraft identity / detection

| Field | Value |
|---|---|
| ICAO type | `C700` |
| Title contains | `Cessna Longitude` |
| Package | `asobo-aircraft-longitude` (SimObjects/Airplanes/Asobo_Longitude) |
| Avionics | Working Title **G3000/G5000** (`WTG3000`) |

Detect in BA by ICAO `C700` or title substring "Longitude".

## Coherent pages (port 19999 `/pagelist.json`)

| Page title | Role |
|---|---|
| `VCockpit01 - WTG3000_PFD_1` | PFD |
| `VCockpit03 - WTG3000_MFD` | MFD (hosts the FMS) |
| `VCockpit04 - WTG3000_GTC_1` | Touchscreen 1 (defaults to PFD control mode) |
| `VCockpit05 - WTG3000_GTC_2` | Touchscreen 2 (MFD control mode) |
| `VCockpit06 - WTG3000_GTC_3` | Touchscreen 3 (MFD control mode) |
| `VCockpit08 - AS1000_AttitudeSpeedBackup` | Standby instrument |

Page IDs change every session — resolve by title substring (e.g. `WTG3000_MFD`).

## FMS object

```js
document.querySelector('wtg3000-mfd').fsInstrument.fms
```

The MFD element is `wtg3000-mfd`; `.fsInstrument` is the live instrument. Compared
to the G1000 NXi (`document.querySelector('wtg1000-mfd').fms`), the FMS is reached
through `.fsInstrument`. `fsInstrument` also exposes: `bus`, `facLoader`, `facRepo`,
`flightPlanner`, `flightPlanStore`, `vnavDataProvider`, `fmsSpeedManager`,
`vnavAuralManager`, `navSources`, `navIndicators`, plus many SimVar publishers.

`facLoader` API is **identical to the G1000** (`getFacility`, `searchByIdent`,
`findNearestFacilitiesByIdent` — the latter wants lat/lon in **degrees**).

### Fms methods (133 total) — key ones, signatures confirmed live

```
createDirectToExisting(segmentIndex, segmentLegIndex, course, deletePriorConstraints=false)
createDirectToRandom(target, course)
cancelDirectTo()  canDirectTo(...)  getDirectToState()
activateLeg(segmentIndex, segmentLegIndex, planIndex=PRIMARY, inhibitImmediateSequence=false)
activateVtf()  canActivateVtf()  activateApproach()  canActivateApproach()
activateMissedApproach()  canMissedApproachActivate()  activateNearestLeg()
insertApproach(facility, approachIndex, approachTransitionIndex, visRwyNum, visRwyDes, skipCourseReversal=false, activate=false)
insertDeparture(facility, departureIndex, departureRunwayIndex, enrouteTransitionIndex, oneWayRunway)
insertArrival(facility, arrivalIndex, arrivalRunwayTransitionIndex, enrouteTransitionIndex, arrivalRunway)
insertWaypoint(segmentIndex, facility, legIndex)
insertHold(...)  editHold(...)
insertAirwaySegment(...)  buildAirwayLegs(...)  removeAirway(...)
setOrigin(airport, runway)  setDestination(airport, runway)
removeWaypoint(segmentIndex, segmentLegIndex)  removeApproach/removeArrival/removeDeparture(...)
invertFlightplan()  emptyPrimaryFlightPlan()
getPrimaryFlightPlan()  getFlightPlan(idx)  hasPrimaryFlightPlan()

— VNAV (NOT present on G1000) —
setUserConstraint(segmentIndex, segmentLegIndex, altitudeFeet, displayAsFlightLevel=false)
setUserConstraintAdvanced(...)  revertAltitudeConstraint(...)
setUserSpeedConstraint(planIndex, segmentIndex, segmentLegIndex, speed, speedUnit, speedDesc)
revertSpeedConstraint(...)  setLegVerticalData(...)  setUserFpa(...)
activateVerticalDirect(...)  cancelVerticalDirectTo()  getVerticalDirectFpa(...)
isAdvancedVnav (property)
```

**Most G1000FmsClient JS ports verbatim** — same positional signatures. The only
swaps: FMS ref (`.fsInstrument.fms`) and MFD page title (`WTG3000_MFD`).

## Flight plan data model (confirmed live)

`fp = fms.getPrimaryFlightPlan()` →
- `fp.length` (leg count), `fp.segmentCount`
- `fp.getSegment(s)` → `{ segmentType, airway, legs[], offset }`
  - segmentType enum seen: Departure / Enroute / Arrival / Approach / Destination
  - `offset` = flat index where the segment's legs begin
- `fp.getLeg(i)` (flat index) →
  - `.name` / `.ident`
  - `.leg.type` (Garmin leg-type enum number, e.g. 15=runway, 4=…, 18=TF)
  - `.leg.fixIcao` (12-char ICAO string)
  - `.calculated.endLat` / `.endLon` (degrees), `.calculated.distance` (**metres**)
  - `.verticalData`:
    - `.altDesc` (AltitudeRestrictionType: 0=none,1=At,2=AtOrAbove,3=AtOrBelow,4=Between)
    - `.altitude1` (**metres**), `.altitude2`
    - `.speed` (knots; **-1** = none), `.speedDesc`
    - `.fpa` (flight path angle degrees)

Segment/leg addressing (same as G1000): use `fp.getSegmentIndex(flat)` /
`fp.getSegmentLegIndex(flat)` to convert a flat index to (segment, legInSegment).

## GTC architecture — for the systems/settings PANELS

GTC root element: `wtg3000-gtc`; `.fsInstrument.gtcService` is the view engine.
`.fsInstrument.systems` (Array) holds aircraft system logic.

**Control modes** (a GTC can be switched between):
`0 = PfdHome`, `1 = MfdHome`, `2 = NavComHome`. GTC_1 defaults to PFD;
GTC_2/3 default to MFD.

### GtcService navigation API (73 methods) — key ones

```
changePageTo(key)         // navigate the active stack to a registered page
goToHomePage()  goBack()  goBackTo(...)  goBackToHomePage()
changeControlModeTo(mode) // switch PFD / MFD / NavCom
openPopup(...)  openView(key)  closeView(...)
getActiveViewItem()  getCurrentMainViewStackKey()
registeredViews           // Map<controlModeStackKey, Map<viewKey, entry>>
activeView / currentPage / activeControlMode (Subjects — call .get())
```

To drive a panel: `changePageTo('<ViewKey>')`, then scrape the rendered DOM
(leaf elements with short textContent that have an offsetParent are the visible
labels/buttons). To "press", dispatch the touch/mouse event on the target element.

### Complete registered view inventory (MFD control mode)

**Flight planning / FMS**
`MfdHome, Initialization, DirectTo, FlightPlan, FlightPlanOptions,
FlightPlanDataFields, FlightPlanOriginOptions, FlightPlanDepartureOptions,
FlightPlanEnrouteOptions, FlightPlanAirwayOptions, FlightPlanArrivalOptions,
FlightPlanDestinationOptions, FlightPlanApproachOptions, FlightPlanWaypointOptions,
FlightPlanFpaSpeedMenu, FlightPlanVnavConstraint, AdvancedVnavProfile, Hold,
Procedures, Departure, Arrival, Approach, ApproachMinimums`

**Performance (Longitude-specific — TOLD)**
`Perf, TakeoffData, ToldMetar, RunwayGradientDialog, TakeoffConfigDefaults,
LandingData, LandingConfigDefaults, FlapSpeeds, WeightAndFuel`

**Aircraft systems panels** ← the "all the panels" request
`AircraftSystems` (parent), `Temp` (ECS/temperature), `Propulsion`,
`ExteriorLights`, `CabinPressure`, `LandingElevationDialog`

**Waypoint / info / nearest**
`WaypointInfo, AirportInfo, AirportInfoOptions, IntersectionInfo, VorInfo, NdbInfo,
UserWaypointInfo, Nearest, NearestAirport, NearestIntersection, NearestVor,
NearestNdb, NearestUserWaypoint, NearestWeather`

**Avionics / setup / utilities**
`AvionicsSettings, Setup, Utilities, Timer, SpeedBugs, MapSettings, TerrainSettings,
TrafficSettings, WeatherSelection, WeatherRadarSettings, ConnextWeatherSettings,
MapPointerControl`

**Radios** (MFD_OVERLAY / PFD_OVERLAY stack)
`AudioRadios, Transponder, TransponderMode, DmeMode, FrequencyDialog`

**External services**
`Auth, DatabaseStatus, SimBrief, NavigraphSettings, Charts, ChartOptions,
ChartPointerControl`

**Input dialogs (reusable)**
`KeyboardDialog, WaypointDialog, FindWaypointDialog, DuplicateWaypointDialog,
LoadFrequencyDialog, AltitudeDialog1, SpeedDialog1, DistanceDialog1,
DurationDialog1, TemperatureDialog1, WeightDialog1, CourseDialog, BaroPressureDialog1,
LatLonDialog, VnavAltitudeDialog, VnavFlightPathAngleDialog, SpeedConstraintDialog,
AirwaySelectionDialog, RunwayLengthDialog, FmsSpeedDialog, MomentArmDialog, ...`

## Build approach (hybrid)

1. **FMS / nav / VNAV** → talk to the `fms` object directly (structured, reliable).
   Port `G1000FmsClient` → `G5000FmsClient`: swap FMS ref + page title, add VNAV
   speed/altitude constraint + vertical-direct methods.
2. **Performance (TOLD), Weight & Fuel** → read `vnavDataProvider`,
   `fmsSpeedManager`, `weightFuelPublisher`, `fuelTotalizerPublisher`, plus SimVars.
3. **Systems panels (Temp/Propulsion/ExteriorLights/CabinPressure) & settings** →
   GTC view navigation (`changePageTo`) + DOM scrape + event dispatch, the
   francescotissera1211 display-agent pattern.

Transport (`CoherentGTClient`) is reused unchanged — it is aircraft-agnostic.
