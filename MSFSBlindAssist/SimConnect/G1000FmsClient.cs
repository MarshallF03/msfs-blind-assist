using System.Text.Json;
using Timer = System.Threading.Timer;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Direct Coherent GT client for the WT G1000 NXi FMS.
/// No bridge, no Community package, no sim restart.
/// Requires MSFS Developer Mode ON (Options → General → Developers).
///
/// Connects to the G1000 MFD page via ws://127.0.0.1:19999/devtools/page/N,
/// finds the page by title "AS1000_MFD" (never hardcodes IDs — they change
/// every session). Polls FPL + nav state every 2 seconds. Persists connection.
///
/// All JS runs against document.querySelector('wtg1000-mfd').fms —
/// confirmed working path in live session 2026-05-31.
/// </summary>
public sealed class G1000FmsClient : IDisposable
{
    private const string MfdTitleFilter = "AS1000_MFD";

    private readonly CoherentGTClient _cgt = new();
    private Timer? _pollTimer;
    private bool _disposed;

    public bool IsConnected  => _cgt.IsConnected;
    public string? PageTitle => _cgt.ConnectedTargetTitle;

    // Events fired on thread-pool — subscribers must marshal to UI thread themselves
    public event EventHandler<G1000FplState>?  FplUpdated;
    public event EventHandler<G1000NavState>?  NavUpdated;
    public event EventHandler?                 Connected;
    public event EventHandler?                 Disconnected;

    // ─────────────────────────────────────────────────────────────────────────
    // Connection management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Try to connect to the G1000 MFD. Returns true if successful.</summary>
    public async Task<bool> TryConnectAsync(CancellationToken ct = default)
    {
        if (_cgt.IsConnected) return true;
        bool ok = await _cgt.TryConnectAsync(MfdTitleFilter, ct);
        if (ok)
        {
            Connected?.Invoke(this, EventArgs.Empty);
            StartPolling();
        }
        return ok;
    }

    public void Disconnect()
    {
        StopPolling();
        _cgt.Disconnect();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void StartPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = new Timer(_ => _ = PollAsync(), null, 500, 2000);
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Polling — called every 2 seconds
    // ─────────────────────────────────────────────────────────────────────────

    // When a user operation runs, polling pauses so the background fpl/nav evals
    // don't race the operation's response on the shared socket. Set via the
    // RunOperationAsync wrapper below.
    private volatile int _pollSuspended;

    private bool _wasConnected;

    private async Task PollAsync()
    {
        if (_pollSuspended > 0) return;   // an operation is in flight — skip this tick

        // Auto-reconnect (pattern from the A380 CoherentDebuggerClient): if the
        // socket dropped or the G1000 page cycled (flight reload changes the page
        // id), re-resolve by title and reconnect. The timer keeps running so it
        // retries every tick until the page is back — no manual reopen needed.
        if (!_cgt.IsConnected)
        {
            if (_wasConnected)
            {
                _wasConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            bool reconnected = await _cgt.TryConnectAsync(MfdTitleFilter);
            if (!reconnected) return;     // keep the timer alive; try again next tick
            _wasConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _wasConnected = true;
        }

        // Run sequentially (not WhenAll) so the two evals never overlap their sends
        string? fpl = await _cgt.EvaluateAsync(JsFpl, 4000);
        if (_pollSuspended > 0) return;
        string? nav = await _cgt.EvaluateAsync(JsNav, 3000);

        if (fpl != null) ParseFpl(fpl);
        if (nav != null) ParseNav(nav);
    }

    /// <summary>
    /// Run a user FMS operation with polling suspended, so the background
    /// fpl/nav poll can't race the operation's response on the shared socket.
    /// </summary>
    private async Task<T> RunOperationAsync<T>(Func<Task<T>> op)
    {
        System.Threading.Interlocked.Increment(ref _pollSuspended);
        try { return await op(); }
        finally
        {
            // brief settle so an in-flight poll fully drains before we resume
            await Task.Delay(150);
            System.Threading.Interlocked.Decrement(ref _pollSuspended);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FMS operations
    // ─────────────────────────────────────────────────────────────────────────

    // NOTE on signatures (verified live 2026-05-31 against the installed NXi):
    //   activateLeg(segmentIndex, segmentLegIndex, planIndex?, inhibitImmediateSequence?)
    //   createDirectToExisting(segmentIndex, segmentLegIndex, course?, deletePriorConstraints?)
    //   createDirectToRandom(target, course?)
    //   insertApproach(facility, approachIndex, approachTransitionIndex, visRwyNum?, visRwyDes?, skipCourseReversal?, activate?)
    //   insertDeparture(facility, departureIndex, departureRunwayIndex, enrouteTransitionIndex, oneWayRunway?)
    //   insertArrival(facility, arrivalIndex, arrivalRunwayTransitionIndex, enrouteTransitionIndex, arrivalRunway?)
    //   setUserConstraint(segmentIndex, segmentLegIndex, altitudeFeet, displayAsFlightLevel?)
    //   removeWaypoint(segmentIndex, segmentLegIndex)
    // Flat leg index -> segment addressing via fp.getSegmentIndex(i) / fp.getSegmentLegIndex(i).

    private const string FmsRef = "document.querySelector('wtg1000-mfd').fms";

    /// <summary>
    /// Direct-to an existing FPL leg by flat leg index. Converts to segment
    /// addressing inside JS via getSegmentIndex/getSegmentLegIndex.
    /// Works backwards AND forwards. THIS is the reliable direct-to.
    /// </summary>
    public async Task<bool> DirectToLegIndexAsync(int flatIndex)
    {
        string js = $@"(function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var seg=fp.getSegmentIndex({flatIndex}), segLeg=fp.getSegmentLegIndex({flatIndex});
  if(seg<0||segLeg<0) return 'ERR:bad index';
  fms.createDirectToExisting(seg, segLeg);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 3000) == "ok");
    }

    /// <summary>Activate an existing leg by flat index (resume FPL nav to that leg).</summary>
    public async Task<bool> ActivateLegIndexAsync(int flatIndex)
    {
        string js = $@"(function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var seg=fp.getSegmentIndex({flatIndex}), segLeg=fp.getSegmentLegIndex({flatIndex});
  if(seg<0||segLeg<0) return 'ERR:bad index';
  fms.activateLeg(seg, segLeg);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 3000) == "ok");
    }

    /// <summary>Remove a waypoint by flat leg index.</summary>
    public async Task<bool> RemoveWaypointAsync(int flatIndex)
    {
        string js = $@"(function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var seg=fp.getSegmentIndex({flatIndex}), segLeg=fp.getSegmentLegIndex({flatIndex});
  if(seg<0||segLeg<0) return 'ERR:bad index';
  fms.removeWaypoint(seg, segLeg);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 3000) == "ok");
    }

    /// <summary>
    /// Set (or change) an altitude constraint on a leg, in feet.
    /// Pass altFeet = 0 to revert the constraint instead.
    /// </summary>
    public async Task<bool> SetAltitudeConstraintAsync(int flatIndex, int altFeet)
    {
        string js = $@"(function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var seg=fp.getSegmentIndex({flatIndex}), segLeg=fp.getSegmentLegIndex({flatIndex});
  if(seg<0||segLeg<0) return 'ERR:bad index';
  {(altFeet <= 0
      ? "fms.revertAltitudeConstraint(seg, segLeg);"
      : $"fms.setUserConstraint(seg, segLeg, {altFeet}, false);")}
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 3000) == "ok");
    }

    /// <summary>Direct-to any waypoint by ident (manual entry; off-route fix allowed).</summary>
    public async Task<bool> DirectToAsync(string ident)
    {
        ident = ident.ToUpperInvariant().Replace("'", "").Trim();
        string js = $"(function(){{try{{{FmsRef}.createDirectToRandom('{ident}');return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js) == "ok");
    }

    /// <summary>Vectors to final — activate the approach in VTF mode.</summary>
    public async Task<bool> ActivateVtfAsync()
    {
        string js = $"(async function(){{try{{await {FmsRef}.activateVtf();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateJobAsync(js, 6000);
        return r == "ok";
    }

    /// <summary>Activate the missed approach procedure.</summary>
    public async Task<bool> ActivateMissedApproachAsync()
    {
        string js = $"(function(){{try{{{FmsRef}.activateMissedApproach();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>Invert the flight plan (reverse origin/destination and all legs).</summary>
    public async Task<bool> InvertPlanAsync()
    {
        string js = $"(function(){{try{{{FmsRef}.invertFlightplan();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateAsync(js, 4000);
        return r == "ok";
    }

    /// <summary>Empty the entire primary flight plan.</summary>
    public async Task<bool> ClearPlanAsync()
    {
        string js = $"(async function(){{try{{await {FmsRef}.emptyPrimaryFlightPlan();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateJobAsync(js, 6000);
        return r == "ok";
    }

    /// <summary>Set origin airport by ICAO (builds/extends the plan).</summary>
    public async Task<bool> SetOriginAsync(string icao)
    {
        icao = icao.ToUpperInvariant().Trim();
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var FT=msfssdk.FacilityType.Airport;
  var f=null;
  try{{f=await fms.facLoader.getFacility(FT,'A      {icao} ');}}catch(e){{}}
  if(!f) f=await fms.facLoader.getFacility(FT,'{icao}');
  if(!f) return 'ERR:not found';
  fms.setOrigin(f);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateJobAsync(js, 12000);
        return r == "ok";
    }

    /// <summary>Set destination airport by ICAO.</summary>
    public async Task<bool> SetDestinationAsync(string icao)
    {
        icao = icao.ToUpperInvariant().Trim();
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var FT=msfssdk.FacilityType.Airport;
  var f=null;
  try{{f=await fms.facLoader.getFacility(FT,'A      {icao} ');}}catch(e){{}}
  if(!f) f=await fms.facLoader.getFacility(FT,'{icao}');
  if(!f) return 'ERR:not found';
  fms.setDestination(f);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateJobAsync(js, 12000);
        return r == "ok";
    }

    /// <summary>
    /// Insert an enroute waypoint by ICAO/ident before the given flat leg index.
    /// Loads the facility (intersection/VOR/NDB/airport) then inserts into the
    /// enroute segment. Pass flatIndex = -1 to append at the end of enroute.
    /// </summary>
    public async Task<bool> InsertWaypointAsync(string ident, int flatIndex)
    {
        ident = ident.ToUpperInvariant().Replace("'", "").Trim();
        // Use findNearestFacilitiesByIdent(filter, ident, lat, lon, maxItems) so an
        // ambiguous ident (e.g. LAM exists in several countries) resolves to the
        // one NEAREST the aircraft — not a random global match 1000nm away.
        // lat/lon in radians. Falls back to plain searchByIdent if nearest fails.
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  // findNearestFacilitiesByIdent wants lat/lon in DEGREES (verified live:
  // radians returned an Italian LAM, degrees returned London LAMBOURNE).
  var lat=SimVar.GetSimVarValue('PLANE LATITUDE','degrees');
  var lon=SimVar.GetSimVarValue('PLANE LONGITUDE','degrees');
  // findNearestFacilitiesByIdent returns FACILITY OBJECTS (with .icao/.name),
  // sorted nearest-first. searchByIdent returns ICAO STRINGS. Normalise to an
  // icao string either way, then load.
  var icao=null;
  try{{
    var near=await fms.facLoader.findNearestFacilitiesByIdent(msfssdk.FacilitySearchType.AllExceptVisual,'{ident}',lat,lon,20);
    if(near&&near.length>0) icao=(typeof near[0]==='string')?near[0]:near[0].icao;
  }}catch(e){{}}
  if(!icao){{
    var res=await fms.facLoader.searchByIdent(msfssdk.FacilitySearchType.All,'{ident}',20);
    if(res&&res.length>0) icao=(typeof res[0]==='string')?res[0]:res[0].icao;
  }}
  if(!icao) return 'ERR:not found';
  var typeFor=function(ic){{var c=ic.charAt(0);
    return c==='A'?msfssdk.FacilityType.Airport:c==='V'?msfssdk.FacilityType.VOR:
           c==='N'?msfssdk.FacilityType.NDB:msfssdk.FacilityType.Intersection;}};
  var fac=await fms.facLoader.getFacility(typeFor(icao),icao);
  if(!fac) return 'ERR:load failed';
  var seg, legIdx;
  if({flatIndex}<0){{
    seg=(fms.findLastEnrouteSegmentIndex)?fms.findLastEnrouteSegmentIndex(fp):-1;
    if(seg<0) seg=fp.getSegmentIndex(Math.max(0,fp.length-1));
    legIdx=undefined;
  }}else{{
    seg=fp.getSegmentIndex({flatIndex});
    legIdx=fp.getSegmentLegIndex({flatIndex});
  }}
  if(seg<0) return 'ERR:no segment';
  fms.insertWaypoint(seg, fac, legIdx);
  return 'ok:'+(fac.name||pick);
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () =>
        {
            string? r = await _cgt.EvaluateJobAsync(js, 12000);
            return r != null && r.StartsWith("ok");
        });
    }

    /// <summary>Toggle GPS DRIVES NAV1. Returns new state (true=on).</summary>
    public async Task<bool> ToggleGpsDrivesNavAsync()
    {
        const string js = "(function(){SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1','number',0);return SimVar.GetSimVarValue('GPS DRIVES NAV1','bool')>0?'on':'off';})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "on";
    }

    /// <summary>
    /// GPS STEERING (GPSS emulation) — the reliable way to make the autopilot
    /// follow the GPS, proven live 2026-05-31.
    ///
    /// WHY: the C172 KAP140's NAV mode engages (AUTOPILOT NAV1 LOCK=1) but
    /// commands ZERO bank when GPS drives NAV1 (it gates on NAV HAS NAV which is
    /// false with an untuned VOR). HDG mode works perfectly. So we do what real
    /// aftermarket GPSS converters do: keep HDG mode engaged and continuously
    /// drive the heading bug to the GPS bearing-to-waypoint. The aircraft flies
    /// directly to the active waypoint and sequences through the plan.
    ///
    /// The steering loop runs INSIDE the G1000 page (setInterval) so it keeps
    /// working between C# polls and survives navigator-form restarts.
    /// </summary>
    public async Task<bool> StartGpsSteeringAsync()
    {
        const string js = @"(function(){try{
  if(!SimVar.GetSimVarValue('AUTOPILOT HEADING LOCK','bool'))
    SimVar.SetSimVarValue('K:AP_PANEL_HEADING_HOLD','number',0);
  if(window.__msfsba_gpss) clearInterval(window.__msfsba_gpss);
  window.__msfsba_gpss=setInterval(function(){
    try{
      var wp=SimVar.GetSimVarValue('GPS WP NEXT ID','string');
      var brg=SimVar.GetSimVarValue('GPS WP BEARING','degrees');
      if(wp && typeof brg==='number' && !isNaN(brg)){
        if(!SimVar.GetSimVarValue('AUTOPILOT HEADING LOCK','bool'))
          SimVar.SetSimVarValue('K:AP_PANEL_HEADING_HOLD','number',0);
        SimVar.SetSimVarValue('K:HEADING_BUG_SET','number',Math.round(brg));
      }
    }catch(e){}
  },1000);
  return 'ok';
}catch(e){return 'ERR:'+e.message;}})()";
        string? r = await _cgt.EvaluateAsync(js, 4000);
        return r == "ok";
    }

    /// <summary>Stop GPS steering (clears the in-page loop). Leaves HDG mode as-is.</summary>
    public async Task<bool> StopGpsSteeringAsync()
    {
        const string js = "(function(){if(window.__msfsba_gpss){clearInterval(window.__msfsba_gpss);window.__msfsba_gpss=null;return 'ok';}return 'notrunning';})()";
        string? r = await _cgt.EvaluateAsync(js, 3000);
        return r == "ok" || r == "notrunning";
    }

    /// <summary>Is GPS steering currently running in the page?</summary>
    public async Task<bool> IsGpsSteeringActiveAsync()
    {
        string? r = await _cgt.EvaluateAsync("window.__msfsba_gpss?'1':'0'", 2000);
        return r == "1";
    }

    /// <summary>Is the autopilot master engaged?</summary>
    public async Task<bool> IsAutopilotOnAsync()
    {
        string? r = await _cgt.EvaluateAsync("SimVar.GetSimVarValue('AUTOPILOT MASTER','bool')>0?'1':'0'", 2000);
        return r == "1";
    }

    /// <summary>Toggle the autopilot master on/off. Returns new state.</summary>
    public async Task<bool> ToggleAutopilotAsync()
    {
        const string js = "(function(){SimVar.SetSimVarValue('K:AP_MASTER','number',0);return SimVar.GetSimVarValue('AUTOPILOT MASTER','bool')>0?'on':'off';})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "on";
    }

    /// <summary>
    /// One-button "Follow GPS flight plan" — the key IFR action.
    /// Ensures GPS drives NAV1, turns off HDG mode, engages NAV mode.
    /// Smart: reads current state first, only changes what needs changing.
    /// </summary>
    public async Task<string> FollowGpsPlanAsync()
    {
        const string readJs = @"(function(){var sv=SimVar.GetSimVarValue;
return JSON.stringify({gps:sv('GPS DRIVES NAV1','bool')>0,nav:sv('AUTOPILOT NAV1 LOCK','bool')>0,
hdg:sv('AUTOPILOT HEADING LOCK','bool')>0,ap:sv('AUTOPILOT MASTER','bool')>0});})()";
        string? stateJson = await _cgt.EvaluateAsync(readJs, 3000);
        if (stateJson == null) return "Could not read autopilot state";

        bool gps, nav, hdg, ap;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(stateJson);
            var r = doc.RootElement;
            gps = r.TryGetProperty("gps", out var g) && g.GetBoolean();
            nav = r.TryGetProperty("nav", out var n) && n.GetBoolean();
            hdg = r.TryGetProperty("hdg", out var h) && h.GetBoolean();
            ap  = r.TryGetProperty("ap",  out var a) && a.GetBoolean();
        }
        catch { return "Could not parse autopilot state"; }

        if (!ap) return "Autopilot master is OFF — engage it first.";

        var actions = new System.Collections.Generic.List<string>();
        if (!gps)
        {
            await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1','number',0);'ok'", 2000);
            await Task.Delay(300);
            actions.Add("GPS to NAV1 on");
        }
        if (hdg)
        {
            await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:AP_PANEL_HEADING_HOLD','number',0);'ok'", 2000);
            await Task.Delay(500);
            actions.Add("HDG off");
        }
        if (!nav)
        {
            bool navEngaged = false;
            for (int attempt = 0; attempt < 3 && !navEngaged; attempt++)
            {
                await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:AP_NAV1_HOLD','number',0);'ok'", 2000);
                await Task.Delay(500 + attempt * 300);
                string? check = await _cgt.EvaluateAsync("SimVar.GetSimVarValue('AUTOPILOT NAV1 LOCK','bool')>0?'1':'0'", 1500);
                navEngaged = check == "1";
            }
            actions.Add(navEngaged ? "NAV mode on" : "NAV mode sent — check panel");
        }

        string? finalJson = await _cgt.EvaluateAsync(@"(function(){var sv=SimVar.GetSimVarValue;
return JSON.stringify({gps:sv('GPS DRIVES NAV1','bool')>0,nav:sv('AUTOPILOT NAV1 LOCK','bool')>0});})()");
        bool fGps = false, fNav = false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(finalJson ?? "{}");
            fGps = doc.RootElement.TryGetProperty("gps", out var fg) && fg.GetBoolean();
            fNav = doc.RootElement.TryGetProperty("nav", out var fn) && fn.GetBoolean();
        }
        catch { }

        if (fGps && fNav)
            return actions.Count > 0 ? $"Following GPS plan. {string.Join(", ", actions)}." : "Already following GPS plan.";
        var missing = new System.Collections.Generic.List<string>();
        if (!fGps) missing.Add("GPS to NAV1 still off");
        if (!fNav) missing.Add("NAV mode not engaged — press NAV in autopilot panel");
        return string.Join(". ", missing) + ".";
    }

    /// <summary>
    /// Load an airport's procedures (SIDs/STARs/approaches) from the FMS.
    /// Caches the facility in window._msfsba_fac_{slot} for subsequent insert calls.
    /// </summary>
    public async Task<G1000FacilityData?> LoadAirportAsync(string icao, string slot)
    {
        icao = icao.ToUpperInvariant().Trim();
        string js = $@"(async function(){{
  try {{
    var fms={FmsRef};
    var ft=msfssdk.FacilityType.Airport;
    var fac=null;
    try{{ fac=await fms.facLoader.getFacility(ft,'A      {icao} '); }}catch(e){{}}
    if(!fac) fac=await fms.facLoader.getFacility(ft,'{icao}');
    if(!fac) return JSON.stringify({{ok:false,err:'not found'}});
    window['_msfsba_fac_{slot}']=fac;
    var strip=function(s){{return (s||'').trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim().substring(0,4);}};
    var mapProc=function(arr){{return (arr||[]).map(function(p,i){{
      var trans=(p.enRouteTransitions||p.transitions||[]).map(function(t,j){{return {{i:j,name:t.name||('Trans '+j)}}}});
      var rwys=(p.runwayTransitions||[]).map(function(r,j){{return {{i:j,name:r.runwayDesignation||r.name||('Rwy '+j)}}}});
      return {{i:i,name:p.name||('#'+i),transitions:trans,runways:rwys}};
    }});}};
    return JSON.stringify({{ok:true,ident:strip(fac.icao),name:fac.name||'{icao}',slot:'{slot}',
      departures:mapProc(fac.departures),arrivals:mapProc(fac.arrivals),approaches:mapProc(fac.approaches)}});
  }}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}
}})()";
        return await RunOperationAsync<G1000FacilityData?>(async () =>
        {
            string? r = await _cgt.EvaluateJobAsync(js, 15000);
            if (r == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(r);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;
                return G1000FacilityData.Parse(root);
            }
            catch { return null; }
        });
    }

    /// <summary>Insert + optionally activate an approach. Correct positional signature.</summary>
    public async Task<bool> InsertApproachAsync(string slot, int approachIdx, int transIdx, bool activate = false)
    {
        // insertApproach(facility, approachIndex, approachTransitionIndex, visRwyNum, visRwyDes, skipCourseReversal, activate)
        string js = $@"(async function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    await {FmsRef}.insertApproach(fac, {approachIdx}, {transIdx}, undefined, undefined, false, {(activate ? "true" : "false")});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        return await RunOperationAsync(async () =>
        {
            string? r = await _cgt.EvaluateJobAsync(js, 12000);
            return r == "ok";
        });
    }

    /// <summary>Insert a departure SID. Correct positional signature.</summary>
    public async Task<bool> InsertDepartureAsync(string slot, int depIdx, int rwyIdx, int transIdx)
    {
        // insertDeparture(facility, departureIndex, departureRunwayIndex, enrouteTransitionIndex, oneWayRunway)
        string js = $@"(function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    {FmsRef}.insertDeparture(fac, {depIdx}, {rwyIdx}, {transIdx});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        return await RunOperationAsync(async () =>
        {
            string? r = await _cgt.EvaluateAsync(js, 8000);
            return r == "ok";
        });
    }

    /// <summary>Insert an arrival STAR. Correct positional signature.</summary>
    public async Task<bool> InsertArrivalAsync(string slot, int arrIdx, int rwyTransIdx, int transIdx)
    {
        // insertArrival(facility, arrivalIndex, arrivalRunwayTransitionIndex, enrouteTransitionIndex, arrivalRunway)
        string js = $@"(function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    {FmsRef}.insertArrival(fac, {arrIdx}, {rwyTransIdx}, {transIdx});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        return await RunOperationAsync(async () =>
        {
            string? r = await _cgt.EvaluateAsync(js, 8000);
            return r == "ok";
        });
    }

    /// <summary>Activate the loaded approach (begin flying it).</summary>
    public async Task<bool> ActivateApproachAsync()
    {
        string js = $"(function(){{try{{{FmsRef}.activateApproach();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>Cancel direct-to.</summary>
    public async Task<bool> CancelDirectToAsync()
    {
        string js = $"(function(){{try{{{FmsRef}.cancelDirectTo();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>List all Coherent GT pages — useful for diagnostics.</summary>
    public static Task<List<(string title, string url)>> ListPagesAsync()
        => CoherentGTClient.ListTargetsAsync(19999);


    // ─────────────────────────────────────────────────────────────────────────
    // JS expressions
    // ─────────────────────────────────────────────────────────────────────────

    private const string JsFpl = @"
(function(){try{
  var el=document.querySelector('wtg1000-mfd');
  if(!el||!el.fms)return null;
  var fp=el.fms.getPrimaryFlightPlan();
  var al=fp.activeLateralLeg!==undefined?fp.activeLateralLeg:fp.activeLegIndex;
  var stripIcao=function(s){return(s||'').trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim().substring(0,6);};
  var legs=[];
  for(var i=0;i<fp.length;i++){try{
    var leg=fp.tryGetLeg?fp.tryGetLeg(i):fp.getLeg(i);
    if(!leg)continue;
    var ident=leg.name||(leg.leg?stripIcao(leg.leg.fixIcao):'')||'?';
    var dist=leg.calculated?(leg.calculated.distanceWithTransitions||0)/1852:0;
    var dtk=leg.calculated?(leg.calculated.initialDtk||0):0;
    var alt='';
    try{if(leg.leg&&leg.leg.altDesc&&leg.leg.altDesc!==0&&leg.leg.altitude1!=null){
      var ft=Math.round(leg.leg.altitude1);
      if(leg.leg.altDesc===1)alt=' AT '+ft;
      else if(leg.leg.altDesc===2)alt=' +'+ft;
      else if(leg.leg.altDesc===3)alt=' -'+ft;
    }}catch(e){}
    // Segment addressing comes from the FlightPlan API, NOT from the leg object
    // (leg.segmentIndex is undefined). getSegmentIndex/getSegmentLegIndex map a
    // flat global leg index to (segment, segmentLeg) — required by createDirectToExisting.
    var sIdx=-1, sLeg=-1;
    try{ sIdx=fp.getSegmentIndex(i); sLeg=fp.getSegmentLegIndex(i); }catch(e){}
    legs.push({ident:ident,dist:parseFloat(dist.toFixed(2)),dtk:Math.round(dtk),alt:alt,index:i,active:i===al,
               segIdx:sIdx,segLeg:sLeg});
  }catch(e){}}
  var proc={dep:-1,arr:-1,appr:-1};
  try{var pd=fp.procedureDetails;if(pd){proc.dep=pd.departureIndex;proc.arr=pd.arrivalIndex;proc.appr=pd.approachIndex;}}catch(e){}
  return JSON.stringify({ok:true,origin:stripIcao(fp.originAirport||''),dest:stripIcao(fp.destinationAirport||''),activeLeg:al,legs:legs,proc:proc});
}catch(e){return JSON.stringify({ok:false,err:e.message});}})()";

    private const string JsNav = @"
(function(){try{var sv=SimVar.GetSimVarValue;
return JSON.stringify({
  dist:parseFloat(sv('GPS WP DISTANCE','nautical miles').toFixed(2)),
  brg:Math.round(sv('GPS WP BEARING','degrees')),
  ete:Math.round(sv('GPS WP ETE','seconds')),
  dtk:Math.round(sv('GPS WP DESIRED TRACK','degrees')),
  xtk:parseFloat(sv('GPS WP CROSS TRK','nautical miles').toFixed(3)),
  gs:Math.round(sv('GPS GROUND SPEED','knots')),
  gpsDrivesNav:sv('GPS DRIVES NAV1','bool')>0,
  apprMode:sv('GPS APPROACH MODE','number'),
  apprLoaded:sv('GPS IS APPROACH LOADED','bool')>0,
  apprActive:sv('GPS IS APPROACH ACTIVE','bool')>0,
  isDto:sv('GPS IS DIRECTTO FLIGHTPLAN','bool')>0,
  apMaster:sv('AUTOPILOT MASTER','bool')>0,
  gpss:(typeof window.__msfsba_gpss!=='undefined' && window.__msfsba_gpss!==null),
  // Approach/glideslope coupling state — for capture callouts so the pilot
  // hears whether the autopilot actually grabbed the LOC and glide.
  apprHold:sv('AUTOPILOT APPROACH HOLD','bool')>0,
  navHold:sv('AUTOPILOT NAV1 LOCK','bool')>0,
  gsArm:sv('AUTOPILOT GLIDESLOPE ARM','bool')>0,
  gsActive:sv('AUTOPILOT GLIDESLOPE ACTIVE','bool')>0,
  navHasNav:sv('NAV HAS NAV:1','bool')>0,
  navHasGs:sv('NAV HAS GLIDE SLOPE:1','bool')>0,
  gsDev:parseFloat((sv('NAV GLIDE SLOPE ERROR:1','degrees')||0).toFixed(2)),
  altMode:sv('AUTOPILOT ALTITUDE LOCK','bool')>0
});
}catch(e){return null;}})()";

    // ─────────────────────────────────────────────────────────────────────────
    // Parsers
    // ─────────────────────────────────────────────────────────────────────────

    private void ParseFpl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return;
            FplUpdated?.Invoke(this, G1000FplState.Parse(r));
        }
        catch { }
    }

    private void ParseNav(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            NavUpdated?.Invoke(this, G1000NavState.Parse(doc.RootElement));
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        _cgt.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Data models
// ─────────────────────────────────────────────────────────────────────────────

public record G1000FplLeg(string Ident, double Dist, int Dtk, string AltText, int Index, bool Active,
    int SegIdx = -1, int SegLeg = -1)
{
    public bool HasSegmentInfo => SegIdx >= 0 && SegLeg >= 0;
    public string DisplayText => Ident + AltText +
        (Dist > 0.05 ? $"  {Dist:F1} nm" : "") +
        (Dtk > 0 ? $"  {Dtk}°" : "");
}

public class G1000FplState : EventArgs
{
    public string            Origin    { get; init; } = "";
    public string            Dest      { get; init; } = "";
    public int               ActiveLeg { get; init; } = -1;
    public List<G1000FplLeg> Legs      { get; init; } = new();
    public int               DepIdx    { get; init; } = -1;
    public int               ArrIdx    { get; init; } = -1;
    public int               ApprIdx   { get; init; } = -1;

    public static G1000FplState Parse(JsonElement r)
    {
        var legs = new List<G1000FplLeg>();
        if (r.TryGetProperty("legs", out var la) && la.ValueKind == JsonValueKind.Array)
            foreach (var l in la.EnumerateArray())
                legs.Add(new G1000FplLeg(
                    l.TryGetProperty("ident",  out var id) ? id.GetString() ?? "?" : "?",
                    l.TryGetProperty("dist",   out var di) ? di.GetDouble()     : 0,
                    l.TryGetProperty("dtk",    out var dk) ? dk.GetInt32()      : 0,
                    l.TryGetProperty("alt",    out var al) ? al.GetString() ?? "" : "",
                    l.TryGetProperty("index",  out var ix) ? ix.GetInt32()      : 0,
                    l.TryGetProperty("active", out var ac) && ac.GetBoolean(),
                    l.TryGetProperty("segIdx", out var si) ? si.GetInt32()      : -1,
                    l.TryGetProperty("segLeg", out var sl) ? sl.GetInt32()      : -1));

        int P(string k) => r.TryGetProperty("proc", out var p) && p.TryGetProperty(k, out var v) ? v.GetInt32() : -1;
        return new G1000FplState
        {
            Origin    = r.TryGetProperty("origin",    out var og) ? og.GetString() ?? "" : "",
            Dest      = r.TryGetProperty("dest",      out var dt) ? dt.GetString() ?? "" : "",
            ActiveLeg = r.TryGetProperty("activeLeg", out var alv) ? alv.GetInt32()        : -1,
            Legs      = legs,
            DepIdx    = P("dep"),
            ArrIdx    = P("arr"),
            ApprIdx   = P("appr")
        };
    }
}

public class G1000NavState : EventArgs
{
    public double Dist          { get; init; }
    public int    Brg           { get; init; }
    public int    Ete           { get; init; }
    public int    Dtk           { get; init; }
    public double Xtk           { get; init; }
    public int    Gs            { get; init; }
    public bool   GpsDrivesNav  { get; init; }
    public int    ApproachMode  { get; init; }
    public bool   ApprLoaded    { get; init; }
    public bool   ApprActive    { get; init; }
    public bool   IsDirectTo    { get; init; }
    public bool   ApMaster      { get; init; }
    public bool   Gpss          { get; init; }
    public bool   ApprHold      { get; init; }   // APPR mode (LOC/approach) armed or active
    public bool   NavHold       { get; init; }
    public bool   GsArm         { get; init; }   // glideslope armed
    public bool   GsActive      { get; init; }   // glideslope captured
    public bool   NavHasNav     { get; init; }   // NAV1 has a valid signal
    public bool   NavHasGs      { get; init; }   // NAV1 has a glideslope signal
    public double GsDev         { get; init; }   // glideslope deviation, degrees
    public bool   AltMode       { get; init; }

    public string ApproachModeText => ApproachMode switch { 1 => "ARMED", 2 => "ACTIVE", _ => "none" };
    public string EteFormatted => Ete > 0 ? $"{Ete/60:D2}:{Ete%60:D2}" : "--:--";

    public static G1000NavState Parse(JsonElement r)
    {
        double G(string k) => r.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
        int    I(string k) => r.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0) : 0;
        bool   B(string k) => r.TryGetProperty(k, out var v) && v.GetBoolean();
        return new G1000NavState
        {
            Dist = G("dist"), Brg = I("brg"), Ete = I("ete"), Dtk = I("dtk"),
            Xtk  = G("xtk"),  Gs  = I("gs"),
            GpsDrivesNav = B("gpsDrivesNav"), ApproachMode = I("apprMode"),
            ApprLoaded = B("apprLoaded"), ApprActive = B("apprActive"), IsDirectTo = B("isDto"),
            ApMaster = B("apMaster"), Gpss = B("gpss"),
            ApprHold = B("apprHold"), NavHold = B("navHold"),
            GsArm = B("gsArm"), GsActive = B("gsActive"),
            NavHasNav = B("navHasNav"), NavHasGs = B("navHasGs"),
            GsDev = G("gsDev"), AltMode = B("altMode")
        };
    }
}

public class G1000ProcItem
{
    public int                   Index       { get; init; }
    public string                Name        { get; init; } = "";
    public List<(int i, string n)> Transitions { get; init; } = new();
    public List<(int i, string n)> Runways     { get; init; } = new();
}

public class G1000FacilityData
{
    public string              Ident      { get; init; } = "";
    public string              Name       { get; init; } = "";
    public string              Slot       { get; init; } = "";
    public List<G1000ProcItem> Departures { get; init; } = new();
    public List<G1000ProcItem> Arrivals   { get; init; } = new();
    public List<G1000ProcItem> Approaches { get; init; } = new();

    public static G1000FacilityData Parse(JsonElement r)
    {
        static List<G1000ProcItem> PL(JsonElement root, string key)
        {
            var list = new List<G1000ProcItem>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in arr.EnumerateArray())
            {
                var trans = new List<(int, string)>();
                var rwys  = new List<(int, string)>();
                if (item.TryGetProperty("transitions", out var ta) && ta.ValueKind == JsonValueKind.Array)
                    foreach (var t in ta.EnumerateArray())
                        trans.Add((t.TryGetProperty("i", out var ti) ? ti.GetInt32() : 0,
                                   t.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : ""));
                if (item.TryGetProperty("runways", out var ra) && ra.ValueKind == JsonValueKind.Array)
                    foreach (var rv in ra.EnumerateArray())
                        rwys.Add((rv.TryGetProperty("i", out var ri) ? ri.GetInt32() : 0,
                                  rv.TryGetProperty("name", out var rn) ? rn.GetString() ?? "" : ""));
                list.Add(new G1000ProcItem
                {
                    Index       = item.TryGetProperty("i",    out var ix) ? ix.GetInt32()      : 0,
                    Name        = item.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    Transitions = trans,
                    Runways     = rwys
                });
            }
            return list;
        }
        return new G1000FacilityData
        {
            Ident      = r.TryGetProperty("ident", out var id) ? id.GetString() ?? "" : "",
            Name       = r.TryGetProperty("name",  out var nm) ? nm.GetString() ?? "" : "",
            Slot       = r.TryGetProperty("slot",  out var sl) ? sl.GetString() ?? "" : "",
            Departures = PL(r, "departures"),
            Arrivals   = PL(r, "arrivals"),
            Approaches = PL(r, "approaches")
        };
    }
}
