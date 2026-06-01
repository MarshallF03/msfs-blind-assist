using System.Text.Json;
using Timer = System.Threading.Timer;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Direct Coherent GT client for the Working Title G3000/G5000 FMS, as fitted to
/// the Cessna Citation Longitude (ICAO C700). No bridge, no Community package.
/// Requires MSFS Developer Mode ON (Options → General → Developers).
///
/// Connects to the MFD page (title "WTG3000_MFD") over the dev-mode debugger and
/// runs all JS against:
///     document.querySelector('wtg3000-mfd').fsInstrument.fms
/// (confirmed live 2026-06-01 — see docs/longitude-g5000.md).
///
/// The G5000 FMS shares almost all of the G1000 NXi's method signatures, so this
/// client mirrors G1000FmsClient closely, but adds the G5000's richer VNAV layer
/// (per-leg altitude AND speed constraints) and reads constraint data from each
/// leg's verticalData (altitudes in metres, speeds in knots).
///
/// The transport (CoherentGTClient) is reused unchanged — it is aircraft-agnostic.
/// </summary>
public sealed class G5000FmsClient : IDisposable
{
    private const string MfdTitleFilter = "WTG3000_MFD";
    private const string FmsRef = "document.querySelector('wtg3000-mfd').fsInstrument.fms";
    private const string MfdEl  = "document.querySelector('wtg3000-mfd')";

    private readonly CoherentGTClient _cgt = new();
    private Timer? _pollTimer;
    private bool _disposed;

    public bool IsConnected  => _cgt.IsConnected;
    public string? PageTitle => _cgt.ConnectedTargetTitle;

    public event EventHandler<G5000FplState>? FplUpdated;
    public event EventHandler<G5000NavState>? NavUpdated;
    public event EventHandler?                Connected;
    public event EventHandler?                Disconnected;

    // ── Connection ────────────────────────────────────────────────────────────

    public async Task<bool> TryConnectAsync(CancellationToken ct = default)
    {
        if (_cgt.IsConnected) return true;
        bool ok = await _cgt.TryConnectAsync(MfdTitleFilter, ct);
        if (ok) { Connected?.Invoke(this, EventArgs.Empty); StartPolling(); }
        return ok;
    }

    public void Disconnect()
    {
        StopPolling();
        _cgt.Disconnect();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public static Task<List<(string title, string url)>> ListPagesAsync()
        => CoherentGTClient.ListTargetsAsync(19999);

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

    // ── Polling (every 2s) with auto-reconnect + operation suspension ──────────

    private volatile int _pollSuspended;
    private bool _wasConnected;

    private async Task PollAsync()
    {
        if (_pollSuspended > 0) return;

        if (!_cgt.IsConnected)
        {
            if (_wasConnected) { _wasConnected = false; Disconnected?.Invoke(this, EventArgs.Empty); }
            bool reconnected = await _cgt.TryConnectAsync(MfdTitleFilter);
            if (!reconnected) return;
            _wasConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        else _wasConnected = true;

        string? fpl = await _cgt.EvaluateAsync(JsFpl, 4000);
        if (_pollSuspended > 0) return;
        string? nav = await _cgt.EvaluateAsync(JsNav, 3000);

        if (fpl != null) ParseFpl(fpl);
        if (nav != null) ParseNav(nav);
    }

    private async Task<T> RunOperationAsync<T>(Func<Task<T>> op)
    {
        System.Threading.Interlocked.Increment(ref _pollSuspended);
        try { return await op(); }
        finally { await Task.Delay(150); System.Threading.Interlocked.Decrement(ref _pollSuspended); }
    }

    // ── FMS operations ─────────────────────────────────────────────────────────
    // Signatures verified live 2026-06-01 (identical to G1000 NXi unless noted):
    //   createDirectToExisting(segmentIndex, segmentLegIndex, course?, deletePriorConstraints?)
    //   activateLeg(segmentIndex, segmentLegIndex, planIndex?, inhibitImmediateSequence?)
    //   removeWaypoint(segmentIndex, segmentLegIndex)
    //   setUserConstraint(segmentIndex, segmentLegIndex, altitudeFeet, displayAsFlightLevel?)
    //   setUserSpeedConstraint(planIndex, segmentIndex, segmentLegIndex, speed, speedUnit, speedDesc)   [G5000 VNAV]
    //   insertWaypoint(segmentIndex, facility, legIndex?)
    //   setOrigin(airport, runway?)   setDestination(airport, runway?)
    //   insertApproach(facility, approachIndex, approachTransitionIndex, visRwyNum?, visRwyDes?, skipCourseReversal?, activate?)
    //   insertDeparture(facility, departureIndex, departureRunwayIndex, enrouteTransitionIndex, oneWayRunway?)
    //   insertArrival(facility, arrivalIndex, arrivalRunwayTransitionIndex, enrouteTransitionIndex, arrivalRunway?)

    /// <summary>Direct-to an existing FPL leg by flat leg index (works fwd and back).</summary>
    public Task<bool> DirectToLegIndexAsync(int flatIndex) => SegOpAsync(flatIndex,
        "fms.createDirectToExisting(seg, segLeg);");

    /// <summary>Activate an existing leg by flat index (resume FPL nav to that leg).</summary>
    public Task<bool> ActivateLegIndexAsync(int flatIndex) => SegOpAsync(flatIndex,
        "fms.activateLeg(seg, segLeg);");

    /// <summary>Remove a waypoint by flat leg index.</summary>
    public Task<bool> RemoveWaypointAsync(int flatIndex) => SegOpAsync(flatIndex,
        "fms.removeWaypoint(seg, segLeg);");

    /// <summary>Set/clear an altitude constraint (feet). altFeet &lt;= 0 reverts.</summary>
    public Task<bool> SetAltitudeConstraintAsync(int flatIndex, int altFeet) => SegOpAsync(flatIndex,
        altFeet <= 0 ? "fms.revertAltitudeConstraint(seg, segLeg);"
                     : $"fms.setUserConstraint(seg, segLeg, {altFeet}, false);");

    /// <summary>
    /// Set/clear a SPEED constraint (knots) on a leg — a G5000 VNAV capability the
    /// G1000 lacks. speedKt &lt;= 0 reverts. speedUnit 0 = knots, speedDesc 1 = "at".
    /// </summary>
    public Task<bool> SetSpeedConstraintAsync(int flatIndex, int speedKt) => SegOpAsync(flatIndex,
        speedKt <= 0 ? "fms.revertSpeedConstraint(seg, segLeg);"
                     : $"fms.setUserSpeedConstraint(0, seg, segLeg, {speedKt}, 0, 1);");

    /// <summary>Shared helper: resolve flat index to (seg, segLeg) then run the op JS.</summary>
    private async Task<bool> SegOpAsync(int flatIndex, string opJs)
    {
        string js = $@"(function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var seg=fp.getSegmentIndex({flatIndex}), segLeg=fp.getSegmentLegIndex({flatIndex});
  if(seg<0||segLeg<0) return 'ERR:bad index';
  {opJs}
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 3000) == "ok");
    }

    /// <summary>Direct-to any waypoint by ident (off-route fix allowed).</summary>
    public async Task<bool> DirectToAsync(string ident)
    {
        ident = ident.ToUpperInvariant().Replace("'", "").Trim();
        string js = $"(function(){{try{{{FmsRef}.createDirectToRandom('{ident}');return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js) == "ok");
    }

    public async Task<bool> CancelDirectToAsync()
        => await _cgt.EvaluateAsync($"(function(){{try{{{FmsRef}.cancelDirectTo();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()") == "ok";

    public async Task<bool> ActivateVtfAsync()
        => await _cgt.EvaluateJobAsync($"(async function(){{try{{await {FmsRef}.activateVtf();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()", 6000) == "ok";

    public async Task<bool> ActivateApproachAsync()
        => await _cgt.EvaluateJobAsync($"(async function(){{try{{await {FmsRef}.activateApproach();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()", 6000) == "ok";

    public async Task<bool> ActivateMissedApproachAsync()
        => await _cgt.EvaluateAsync($"(function(){{try{{{FmsRef}.activateMissedApproach();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()") == "ok";

    public async Task<bool> InvertPlanAsync()
        => await _cgt.EvaluateAsync($"(function(){{try{{{FmsRef}.invertFlightplan();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()", 4000) == "ok";

    public async Task<bool> ClearPlanAsync()
        => await _cgt.EvaluateJobAsync($"(async function(){{try{{await {FmsRef}.emptyPrimaryFlightPlan();return 'ok';}}catch(e){{return 'ERR:'+e.message;}}}})()", 6000) == "ok";

    public async Task<bool> SetOriginAsync(string icao) => await SetEndpointAsync(icao, "setOrigin");
    public async Task<bool> SetDestinationAsync(string icao) => await SetEndpointAsync(icao, "setDestination");

    private async Task<bool> SetEndpointAsync(string icao, string method)
    {
        icao = icao.ToUpperInvariant().Trim();
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var FT=msfssdk.FacilityType.Airport; var f=null;
  try{{f=await fms.facLoader.getFacility(FT,'A      {icao} ');}}catch(e){{}}
  if(!f) f=await fms.facLoader.getFacility(FT,'{icao}');
  if(!f) return 'ERR:not found';
  fms.{method}(f);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateJobAsync(js, 12000) == "ok");
    }

    /// <summary>Insert an enroute waypoint by ident before the given flat index (-1 = append).</summary>
    public async Task<bool> InsertWaypointAsync(string ident, int flatIndex)
    {
        ident = ident.ToUpperInvariant().Replace("'", "").Trim();
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var fp=fms.getPrimaryFlightPlan();
  var lat=SimVar.GetSimVarValue('PLANE LATITUDE','degrees');
  var lon=SimVar.GetSimVarValue('PLANE LONGITUDE','degrees');
  var icao=null;
  try{{var near=await fms.facLoader.findNearestFacilitiesByIdent(msfssdk.FacilitySearchType.AllExceptVisual,'{ident}',lat,lon,20);
    if(near&&near.length>0) icao=(typeof near[0]==='string')?near[0]:near[0].icao;}}catch(e){{}}
  if(!icao){{var res=await fms.facLoader.searchByIdent(msfssdk.FacilitySearchType.All,'{ident}',20);
    if(res&&res.length>0) icao=(typeof res[0]==='string')?res[0]:res[0].icao;}}
  if(!icao) return 'ERR:not found';
  var typeFor=function(ic){{var c=ic.charAt(0);return c==='A'?msfssdk.FacilityType.Airport:c==='V'?msfssdk.FacilityType.VOR:c==='N'?msfssdk.FacilityType.NDB:msfssdk.FacilityType.Intersection;}};
  var fac=await fms.facLoader.getFacility(typeFor(icao),icao);
  if(!fac) return 'ERR:load failed';
  var seg, legIdx;
  if({flatIndex}<0){{ seg=(fms.findLastEnrouteSegmentIndex)?fms.findLastEnrouteSegmentIndex(fp):-1; if(seg<0) seg=fp.getSegmentIndex(Math.max(0,fp.length-1)); legIdx=undefined; }}
  else {{ seg=fp.getSegmentIndex({flatIndex}); legIdx=fp.getSegmentLegIndex({flatIndex}); }}
  if(seg<0) return 'ERR:no segment';
  fms.insertWaypoint(seg, fac, legIdx);
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => (await _cgt.EvaluateJobAsync(js, 12000))?.StartsWith("ok") == true);
    }

    // ── Procedures ───────────────────────────────────────────────────────────

    /// <summary>Load an airport's procedures; caches facility in window._msfsba_fac_{slot}.</summary>
    public async Task<G5000FacilityData?> LoadAirportAsync(string icao, string slot)
    {
        icao = icao.ToUpperInvariant().Trim();
        string js = $@"(async function(){{try{{
  var fms={FmsRef}; var ft=msfssdk.FacilityType.Airport; var fac=null;
  try{{fac=await fms.facLoader.getFacility(ft,'A      {icao} ');}}catch(e){{}}
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
}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";
        return await RunOperationAsync<G5000FacilityData?>(async () =>
        {
            string? r = await _cgt.EvaluateJobAsync(js, 15000);
            if (r == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(r);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;
                return G5000FacilityData.Parse(root);
            }
            catch { return null; }
        });
    }

    public async Task<bool> InsertApproachAsync(string slot, int approachIdx, int transIdx, bool activate = false)
    {
        string js = $@"(async function(){{try{{
  var fac=window['_msfsba_fac_{slot}']; if(!fac) return 'no_fac';
  await {FmsRef}.insertApproach(fac, {approachIdx}, {transIdx}, undefined, undefined, false, {(activate ? "true" : "false")});
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateJobAsync(js, 12000) == "ok");
    }

    public async Task<bool> InsertDepartureAsync(string slot, int depIdx, int rwyIdx, int transIdx)
    {
        string js = $@"(function(){{try{{
  var fac=window['_msfsba_fac_{slot}']; if(!fac) return 'no_fac';
  {FmsRef}.insertDeparture(fac, {depIdx}, {rwyIdx}, {transIdx});
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 8000) == "ok");
    }

    public async Task<bool> InsertArrivalAsync(string slot, int arrIdx, int rwyTransIdx, int transIdx)
    {
        string js = $@"(function(){{try{{
  var fac=window['_msfsba_fac_{slot}']; if(!fac) return 'no_fac';
  {FmsRef}.insertArrival(fac, {arrIdx}, {rwyTransIdx}, {transIdx});
  return 'ok';
}}catch(e){{return 'ERR:'+e.message;}}}})()";
        return await RunOperationAsync(async () => await _cgt.EvaluateAsync(js, 8000) == "ok");
    }

    // ── SimBrief route loader (ported; resolves each fix by its own lat/lon) ───

    public async Task<string> LoadSimBriefRouteAsync(
        string originIcao, string destIcao,
        System.Collections.Generic.IReadOnlyList<(string Ident, double Lat, double Lon)> enroute)
    {
        originIcao = (originIcao ?? "").ToUpperInvariant().Replace("'", "").Trim();
        destIcao   = (destIcao   ?? "").ToUpperInvariant().Replace("'", "").Trim();

        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < enroute.Count; i++)
        {
            var w = enroute[i];
            string id = (w.Ident ?? "").ToUpperInvariant().Replace("'", "").Replace("\"", "").Trim();
            if (id.Length == 0) continue;
            if (sb.Length > 1) sb.Append(',');
            sb.Append("{id:'").Append(id).Append("',lat:")
              .Append(w.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",lon:")
              .Append(w.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        }
        sb.Append(']');
        string wpArray = sb.ToString();

        string js = $@"(async function(){{
  var fms={FmsRef}; var FT=msfssdk.FacilityType; var FST=msfssdk.FacilitySearchType;
  try{{
    var getApt=async function(icao){{var f=null;
      try{{f=await fms.facLoader.getFacility(FT.Airport,'A      '+icao+' ');}}catch(e){{}}
      if(!f) try{{f=await fms.facLoader.getFacility(FT.Airport,icao);}}catch(e){{}}
      return f;}};
    await fms.emptyPrimaryFlightPlan();
    var orig=await getApt('{originIcao}');
    if(!orig) return JSON.stringify({{ok:false,err:'origin {originIcao} not found'}});
    fms.setOrigin(orig);
    var typeFor=function(ic){{var c=ic.charAt(0);return c==='A'?FT.Airport:c==='V'?FT.VOR:c==='N'?FT.NDB:FT.Intersection;}};
    var wps={wpArray}; var added=0; var skipped=[];
    for(var i=0;i<wps.length;i++){{
      var w=wps[i]; var icao=null;
      try{{var near=await fms.facLoader.findNearestFacilitiesByIdent(FST.AllExceptVisual,w.id,w.lat,w.lon,5);
        if(near&&near.length>0) icao=(typeof near[0]==='string')?near[0]:near[0].icao;}}catch(e){{}}
      if(!icao){{ skipped.push(w.id); continue; }}
      var fac=null; try{{fac=await fms.facLoader.getFacility(typeFor(icao),icao);}}catch(e){{}}
      if(!fac){{ skipped.push(w.id); continue; }}
      try{{var fp=fms.getPrimaryFlightPlan();
        var seg=(fms.findLastEnrouteSegmentIndex)?fms.findLastEnrouteSegmentIndex(fp):-1;
        if(seg<0) seg=fp.getSegmentIndex(Math.max(0,fp.length-1));
        fms.insertWaypoint(seg, fac); added++;
      }}catch(e){{ skipped.push(w.id); }}
    }}
    var dest=await getApt('{destIcao}');
    if(dest) fms.setDestination(dest);
    return JSON.stringify({{ok:true,added:added,total:wps.length,skipped:skipped,dest:dest?true:false}});
  }}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}
}})()";

        return await RunOperationAsync(async () =>
        {
            string? r = await _cgt.EvaluateJobAsync(js, 30000);
            if (r == null) return "SimBrief load failed — no response from G5000.";
            try
            {
                using var doc = JsonDocument.Parse(r);
                var root = doc.RootElement;
                if (!root.GetProperty("ok").GetBoolean())
                    return "SimBrief load failed: " + (root.TryGetProperty("err", out var er) ? er.GetString() : "unknown");
                int added = root.GetProperty("added").GetInt32();
                int total = root.GetProperty("total").GetInt32();
                bool dest = root.TryGetProperty("dest", out var d) && d.GetBoolean();
                var skip = new System.Collections.Generic.List<string>();
                if (root.TryGetProperty("skipped", out var sk) && sk.ValueKind == JsonValueKind.Array)
                    foreach (var s in sk.EnumerateArray()) skip.Add(s.GetString() ?? "");
                string msg = $"Route loaded. {originIcao} to {destIcao}, {added} of {total} enroute waypoints" +
                             (dest ? ", destination set." : ", destination not found.");
                if (skip.Count > 0) msg += " Skipped " + string.Join(", ", skip) + ". Add these manually.";
                return msg;
            }
            catch (Exception ex) { return "SimBrief load: could not parse result — " + ex.Message; }
        });
    }

    // ── JS expressions ──────────────────────────────────────────────────────
    // FPL reader verified live 2026-06-01 against EGNT→EKCH (33 legs, VNAV
    // constraints reading correctly). Altitudes in verticalData are METRES;
    // distances in calculated are METRES; speeds are knots (-1 = none).

    private const string JsFpl = @"
(function(){try{
  var el=" + MfdEl + @";
  if(!el||!el.fsInstrument||!el.fsInstrument.fms) return null;
  var fms=el.fsInstrument.fms; var fp=fms.getPrimaryFlightPlan();
  var al=fp.activeLateralLeg!==undefined?fp.activeLateralLeg:fp.activeLegIndex;
  var strip=function(s){return(s||'').trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim().substring(0,6);};
  var M2F=3.28084;
  var legs=[];
  for(var i=0;i<fp.length;i++){try{
    var leg=fp.tryGetLeg?fp.tryGetLeg(i):fp.getLeg(i); if(!leg)continue;
    var ident=leg.name||(leg.leg?strip(leg.leg.fixIcao):'')||'?';
    var dist=leg.calculated?((leg.calculated.distance||leg.calculated.distanceWithTransitions||0)/1852):0;
    var dtk=leg.calculated?(leg.calculated.initialDtk||0):0;
    var vd=leg.verticalData, alt='', spd=-1;
    if(vd){
      if(vd.altDesc&&vd.altDesc!==0&&vd.altitude1!=null){var ft=Math.round(vd.altitude1*M2F);
        alt=vd.altDesc===1?(' AT '+ft):vd.altDesc===2?(' +'+ft):vd.altDesc===3?(' -'+ft):(' '+ft);}
      if(typeof vd.speed==='number'&&vd.speed>0) spd=Math.round(vd.speed);
    }
    var sIdx=-1,sLeg=-1; try{sIdx=fp.getSegmentIndex(i);sLeg=fp.getSegmentLegIndex(i);}catch(e){}
    legs.push({ident:ident,dist:parseFloat(dist.toFixed(2)),dtk:Math.round(dtk),alt:alt,spd:spd,index:i,active:i===al,segIdx:sIdx,segLeg:sLeg});
  }catch(e){}}
  var proc={dep:-1,arr:-1,appr:-1};
  try{var pd=fp.procedureDetails;if(pd){proc.dep=pd.departureIndex;proc.arr=pd.arrivalIndex;proc.appr=pd.approachIndex;}}catch(e){}
  return JSON.stringify({ok:true,origin:strip(fp.originAirport||''),dest:strip(fp.destinationAirport||''),activeLeg:al,legs:legs,proc:proc});
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
  apprActive:sv('GPS IS APPROACH ACTIVE','bool')>0,
  apprLoaded:sv('GPS IS APPROACH LOADED','bool')>0,
  isDto:sv('GPS IS DIRECTTO FLIGHTPLAN','bool')>0,
  apMaster:sv('AUTOPILOT MASTER','bool')>0,
  navHold:sv('AUTOPILOT NAV1 LOCK','bool')>0,
  hdgHold:sv('AUTOPILOT HEADING LOCK','bool')>0,
  altHold:sv('AUTOPILOT ALTITUDE LOCK','bool')>0,
  vnavActive:sv('AUTOPILOT VNAV ACTIVE','bool')>0,
  apprHold:sv('AUTOPILOT APPROACH HOLD','bool')>0,
  gsArm:sv('AUTOPILOT GLIDESLOPE ARM','bool')>0,
  gsActive:sv('AUTOPILOT GLIDESLOPE ACTIVE','bool')>0,
  navHasNav:sv('NAV HAS NAV:1','bool')>0,
  navHasGs:sv('NAV HAS GLIDE SLOPE:1','bool')>0,
  selAlt:Math.round(sv('AUTOPILOT ALTITUDE LOCK VAR','feet'))
});
}catch(e){return null;}})()";

    private void ParseFpl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return;
            FplUpdated?.Invoke(this, G5000FplState.Parse(r));
        }
        catch { }
    }

    private void ParseNav(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            NavUpdated?.Invoke(this, G5000NavState.Parse(doc.RootElement));
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

// ── Data models ─────────────────────────────────────────────────────────────

public record G5000FplLeg(string Ident, double Dist, int Dtk, string AltText, int Spd,
    int Index, bool Active, int SegIdx = -1, int SegLeg = -1)
{
    public bool HasSegmentInfo => SegIdx >= 0 && SegLeg >= 0;
    public string DisplayText => Ident + AltText +
        (Spd > 0 ? $"  {Spd}kt" : "") +
        (Dist > 0.05 ? $"  {Dist:F1} nm" : "") +
        (Dtk > 0 ? $"  {Dtk}°" : "");
}

public class G5000FplState : EventArgs
{
    public string            Origin    { get; init; } = "";
    public string            Dest      { get; init; } = "";
    public int               ActiveLeg { get; init; } = -1;
    public List<G5000FplLeg> Legs      { get; init; } = new();
    public int               DepIdx    { get; init; } = -1;
    public int               ArrIdx    { get; init; } = -1;
    public int               ApprIdx   { get; init; } = -1;

    public static G5000FplState Parse(JsonElement r)
    {
        var legs = new List<G5000FplLeg>();
        if (r.TryGetProperty("legs", out var la) && la.ValueKind == JsonValueKind.Array)
            foreach (var l in la.EnumerateArray())
                legs.Add(new G5000FplLeg(
                    l.TryGetProperty("ident",  out var id) ? id.GetString() ?? "?" : "?",
                    l.TryGetProperty("dist",   out var di) ? di.GetDouble()      : 0,
                    l.TryGetProperty("dtk",    out var dk) ? dk.GetInt32()       : 0,
                    l.TryGetProperty("alt",    out var al) ? al.GetString() ?? "" : "",
                    l.TryGetProperty("spd",    out var sp) ? sp.GetInt32()       : -1,
                    l.TryGetProperty("index",  out var ix) ? ix.GetInt32()       : 0,
                    l.TryGetProperty("active", out var ac) && ac.GetBoolean(),
                    l.TryGetProperty("segIdx", out var si) ? si.GetInt32()       : -1,
                    l.TryGetProperty("segLeg", out var sl) ? sl.GetInt32()       : -1));

        int P(string k) => r.TryGetProperty("proc", out var p) && p.TryGetProperty(k, out var v) ? v.GetInt32() : -1;
        return new G5000FplState
        {
            Origin    = r.TryGetProperty("origin",    out var og) ? og.GetString() ?? "" : "",
            Dest      = r.TryGetProperty("dest",      out var dt) ? dt.GetString() ?? "" : "",
            ActiveLeg = r.TryGetProperty("activeLeg", out var alv) ? alv.GetInt32() : -1,
            Legs      = legs,
            DepIdx    = P("dep"),
            ArrIdx    = P("arr"),
            ApprIdx   = P("appr")
        };
    }
}

public class G5000NavState : EventArgs
{
    public double Dist         { get; init; }
    public int    Brg          { get; init; }
    public int    Ete          { get; init; }
    public int    Dtk          { get; init; }
    public double Xtk          { get; init; }
    public int    Gs           { get; init; }
    public bool   GpsDrivesNav { get; init; }
    public bool   ApprActive   { get; init; }
    public bool   ApprLoaded   { get; init; }
    public bool   IsDto        { get; init; }
    public bool   ApMaster     { get; init; }
    public bool   NavHold      { get; init; }
    public bool   HdgHold      { get; init; }
    public bool   AltHold      { get; init; }
    public bool   VnavActive   { get; init; }
    public bool   ApprHold     { get; init; }
    public bool   GsArm        { get; init; }
    public bool   GsActive     { get; init; }
    public bool   NavHasNav    { get; init; }
    public bool   NavHasGs     { get; init; }
    public int    SelAlt       { get; init; }

    public static G5000NavState Parse(JsonElement r)
    {
        double D(string k) => r.TryGetProperty(k, out var v) ? v.GetDouble() : 0;
        int    I(string k) => r.TryGetProperty(k, out var v) ? v.GetInt32()  : 0;
        bool   B(string k) => r.TryGetProperty(k, out var v) && v.GetBoolean();
        return new G5000NavState
        {
            Dist = D("dist"), Brg = I("brg"), Ete = I("ete"), Dtk = I("dtk"),
            Xtk = D("xtk"), Gs = I("gs"), GpsDrivesNav = B("gpsDrivesNav"),
            ApprActive = B("apprActive"), ApprLoaded = B("apprLoaded"), IsDto = B("isDto"),
            ApMaster = B("apMaster"), NavHold = B("navHold"), HdgHold = B("hdgHold"),
            AltHold = B("altHold"), VnavActive = B("vnavActive"), ApprHold = B("apprHold"),
            GsArm = B("gsArm"), GsActive = B("gsActive"), NavHasNav = B("navHasNav"),
            NavHasGs = B("navHasGs"), SelAlt = I("selAlt")
        };
    }
}

public record G5000ProcItem(int Index, string Name, List<(int i, string name)> Transitions, List<(int i, string name)> Runways);

public class G5000FacilityData
{
    public string Ident { get; init; } = "";
    public string Name  { get; init; } = "";
    public string Slot  { get; init; } = "";
    public List<G5000ProcItem> Departures { get; init; } = new();
    public List<G5000ProcItem> Arrivals   { get; init; } = new();
    public List<G5000ProcItem> Approaches { get; init; } = new();

    public static G5000FacilityData Parse(JsonElement r)
    {
        static List<G5000ProcItem> Procs(JsonElement e, string key)
        {
            var list = new List<G5000ProcItem>();
            if (e.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                {
                    var trans = new List<(int, string)>();
                    if (p.TryGetProperty("transitions", out var ta) && ta.ValueKind == JsonValueKind.Array)
                        foreach (var t in ta.EnumerateArray())
                            trans.Add((t.GetProperty("i").GetInt32(), t.GetProperty("name").GetString() ?? ""));
                    var rwys = new List<(int, string)>();
                    if (p.TryGetProperty("runways", out var ra) && ra.ValueKind == JsonValueKind.Array)
                        foreach (var rw in ra.EnumerateArray())
                            rwys.Add((rw.GetProperty("i").GetInt32(), rw.GetProperty("name").GetString() ?? ""));
                    list.Add(new G5000ProcItem(p.GetProperty("i").GetInt32(),
                        p.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "", trans, rwys));
                }
            return list;
        }
        return new G5000FacilityData
        {
            Ident = r.TryGetProperty("ident", out var id) ? id.GetString() ?? "" : "",
            Name  = r.TryGetProperty("name",  out var nm) ? nm.GetString() ?? "" : "",
            Slot  = r.TryGetProperty("slot",  out var sl) ? sl.GetString() ?? "" : "",
            Departures = Procs(r, "departures"),
            Arrivals   = Procs(r, "arrivals"),
            Approaches = Procs(r, "approaches"),
        };
    }
}
