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

    private async Task PollAsync()
    {
        if (!_cgt.IsConnected)
        {
            StopPolling();
            Disconnected?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Run both queries in parallel
        var fplTask = _cgt.EvaluateAsync(JsFpl, 4000);
        var navTask = _cgt.EvaluateAsync(JsNav, 3000);
        await Task.WhenAll(fplTask, navTask);

        if (fplTask.Result != null) ParseFpl(fplTask.Result);
        if (navTask.Result != null) ParseNav(navTask.Result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FMS operations
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Activate a specific leg index (go direct in FMS).</summary>
    public async Task<bool> ActivateLegAsync(int index)
    {
        string js = $"(function(){{document.querySelector('wtg1000-mfd').fms.activateLeg(0,{index});return 'ok';}})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>Direct-to any waypoint by ident (no visual dialog).</summary>
    public async Task<bool> DirectToAsync(string ident)
    {
        ident = ident.ToUpperInvariant().Replace("'", "").Trim();
        string js = $"(function(){{document.querySelector('wtg1000-mfd').fms.createDirectToRandom('{ident}');return 'ok';}})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>Toggle GPS DRIVES NAV1. Returns new state (true=on).</summary>
    public async Task<bool> ToggleGpsDrivesNavAsync()
    {
        const string js = "(function(){SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1','number',0);return SimVar.GetSimVarValue('GPS DRIVES NAV1','bool')>0?'on':'off';})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "on";
    }

    /// <summary>
    /// One-button "Follow GPS flight plan" — the key IFR action.
    /// Ensures GPS drives NAV1, turns off HDG mode, engages NAV mode.
    /// Smart: reads current state first, only changes what needs changing.
    /// Returns a status string for announcement.
    /// </summary>
    public async Task<string> FollowGpsPlanAsync()
    {
        // Read current state
        const string readJs = @"(function(){var sv=SimVar.GetSimVarValue;
return JSON.stringify({
  gps: sv('GPS DRIVES NAV1','bool')>0,
  nav: sv('AUTOPILOT NAV1 LOCK','bool')>0,
  hdg: sv('AUTOPILOT HEADING LOCK','bool')>0,
  ap:  sv('AUTOPILOT MASTER','bool')>0
});})()";
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

        if (!ap) return "Autopilot master is OFF — engage it first (AP button on KAP140 panel)";

        var actions = new System.Collections.Generic.List<string>();

        // Step 1: GPS drives NAV1 must be ON — toggle only if currently off
        if (!gps)
        {
            await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:TOGGLE_GPS_DRIVES_NAV1','number',0);'ok'", 2000);
            await Task.Delay(300);
            actions.Add("GPS → NAV1 ON");
        }

        // Step 2: Turn off HDG mode if on (KAP140 won't enter NAV while HDG is active)
        if (hdg)
        {
            await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:AP_PANEL_HEADING_HOLD','number',0);'ok'", 2000);
            await Task.Delay(300);
            actions.Add("HDG mode OFF");
        }

        // Step 3: Engage NAV mode if not already on
        if (!nav)
        {
            await _cgt.EvaluateAsync("SimVar.SetSimVarValue('K:AP_NAV1_HOLD','number',0);'ok'", 2000);
            await Task.Delay(400);
            actions.Add("NAV mode ON");
        }

        // Verify final state
        string? finalJson = await _cgt.EvaluateAsync(@"(function(){var sv=SimVar.GetSimVarValue;
return JSON.stringify({gps:sv('GPS DRIVES NAV1','bool')>0,nav:sv('AUTOPILOT NAV1 LOCK','bool')>0});})()");
        bool finalGps = false, finalNav = false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(finalJson ?? "{}");
            finalGps = doc.RootElement.TryGetProperty("gps", out var fg) && fg.GetBoolean();
            finalNav = doc.RootElement.TryGetProperty("nav", out var fn) && fn.GetBoolean();
        }
        catch { }

        if (finalGps && finalNav)
            return actions.Count > 0
                ? $"Following GPS plan. Changed: {string.Join(", ", actions)}"
                : "Already following GPS plan (GPS→NAV1 ON, NAV mode ON)";
        else
            return $"Partial result — GPS drives NAV1: {(finalGps ? "ON" : "OFF")}, NAV mode: {(finalNav ? "ON" : "OFF")}. Try again.";
    }

    /// <summary>
    /// Load an airport's procedures (SIDs/STARs/approaches) from the FMS.
    /// Returns null if the airport wasn't found or on timeout.
    /// The facility object is cached in the G1000 page under window._msfsba_fac_dep
    /// or _arr for subsequent insert calls.
    /// </summary>
    public async Task<G1000FacilityData?> LoadAirportAsync(string icao, string slot)
    {
        icao = icao.ToUpperInvariant().Trim();
        string paddedIcao = $"A      {icao} ";

        string js = $@"(async function(){{
  try {{
    var el=document.querySelector('wtg1000-mfd');
    var ft=msfssdk.FacilityType.Airport;
    var fac=null;
    try{{ fac=await el.fms.facLoader.getFacility(ft,'{paddedIcao}'); }}catch(e){{}}
    if(!fac) fac=await el.fms.facLoader.getFacility(ft,'{icao}');
    if(!fac) return JSON.stringify({{ok:false,err:'not found'}});
    window['_msfsba_fac_{slot}']=fac;
    var stripIcao=function(s){{return (s||'').trim().replace(/\x00/g,'').replace(/^[AVWNRU]\s+/,'').trim().substring(0,4);}};
    var mapProc=function(arr){{return (arr||[]).map(function(p,i){{
      var trans=(p.enRouteTransitions||p.transitions||[]).map(function(t,j){{return {{i:j,name:t.name||('Trans '+j)}}}});
      var rwys=(p.runwayTransitions||[]).map(function(r,j){{return {{i:j,name:r.runwayDesignation||r.name||('Rwy '+j)}}}});
      return {{i:i,name:p.name||('#'+i),transitions:trans,runways:rwys}};
    }});}};
    return JSON.stringify({{ok:true,ident:stripIcao(fac.icao),name:fac.name||'{icao}',slot:'{slot}',
      departures:mapProc(fac.departures),arrivals:mapProc(fac.arrivals),approaches:mapProc(fac.approaches)}});
  }}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}
}})()";

        string? r = await _cgt.EvaluatePromiseAsync(js, 15000);
        if (r == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(r);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;
            return G1000FacilityData.Parse(root);
        }
        catch { return null; }
    }

    /// <summary>Insert an approach (async FMS operation).</summary>
    public async Task<bool> InsertApproachAsync(string slot, int approachIdx, int transIdx)
    {
        string js = $@"(async function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    var r=await document.querySelector('wtg1000-mfd').fms.insertApproach(
      {{facility:fac,approachIndex:{approachIdx},approachTransitionIndex:{transIdx}}});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        string? r = await _cgt.EvaluatePromiseAsync(js, 12000);
        return r == "ok";
    }

    /// <summary>Insert a departure SID.</summary>
    public async Task<bool> InsertDepartureAsync(string slot, int depIdx, int rwyIdx, int transIdx)
    {
        string js = $@"(async function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    await document.querySelector('wtg1000-mfd').fms.insertDeparture(
      {{facility:fac,departureIndex:{depIdx},departureRunwayIndex:{rwyIdx},enrouteTransitionIndex:{transIdx}}});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        string? r = await _cgt.EvaluatePromiseAsync(js, 10000);
        return r == "ok";
    }

    /// <summary>Insert an arrival STAR.</summary>
    public async Task<bool> InsertArrivalAsync(string slot, int arrIdx, int rwyIdx, int transIdx)
    {
        string js = $@"(async function(){{
  try{{
    var fac=window['_msfsba_fac_{slot}'];
    if(!fac) return 'no_fac';
    await document.querySelector('wtg1000-mfd').fms.insertArrival(
      {{facility:fac,arrivalIndex:{arrIdx},enrouteTransitionIndex:{transIdx},arrivalRunwayIndex:{rwyIdx}}});
    return 'ok';
  }}catch(e){{return 'ERR:'+e.message;}}
}})()";
        string? r = await _cgt.EvaluatePromiseAsync(js, 10000);
        return r == "ok";
    }

    /// <summary>Activate approach (begin flying it).</summary>
    public async Task<bool> ActivateApproachAsync()
    {
        const string js = "(function(){document.querySelector('wtg1000-mfd').fms.activateApproach();return 'ok';})()";
        string? r = await _cgt.EvaluateAsync(js);
        return r == "ok";
    }

    /// <summary>Cancel direct-to.</summary>
    public async Task<bool> CancelDirectToAsync()
    {
        const string js = "(function(){document.querySelector('wtg1000-mfd').fms.cancelDirectTo();return 'ok';})()";
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
    legs.push({ident:ident,dist:parseFloat(dist.toFixed(2)),dtk:Math.round(dtk),alt:alt,index:i,active:i===al});
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
  isDto:sv('GPS IS DIRECTTO FLIGHTPLAN','bool')>0
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

public record G1000FplLeg(string Ident, double Dist, int Dtk, string AltText, int Index, bool Active)
{
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
                    l.TryGetProperty("ident", out var id) ? id.GetString() ?? "?" : "?",
                    l.TryGetProperty("dist",  out var di) ? di.GetDouble()     : 0,
                    l.TryGetProperty("dtk",   out var dk) ? dk.GetInt32()      : 0,
                    l.TryGetProperty("alt",   out var al) ? al.GetString() ?? "" : "",
                    l.TryGetProperty("index", out var ix) ? ix.GetInt32()      : 0,
                    l.TryGetProperty("active",out var ac) && ac.GetBoolean()));

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
            ApprLoaded = B("apprLoaded"), ApprActive = B("apprActive"), IsDirectTo = B("isDto")
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
