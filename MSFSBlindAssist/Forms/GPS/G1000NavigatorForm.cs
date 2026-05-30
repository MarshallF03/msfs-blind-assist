using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Full-featured accessible GPS/FMS window for the WT G1000 NXi.
/// Connects via MSFS Developer Mode's Coherent GT debugger (port 9999 / 19999).
/// Requires dev mode ON in MSFS Options → General → Developers.
///
/// Tabs:
///   1. Flight Plan  — live FPL readout, origin/destination, active leg
///   2. Procedures   — load airport, pick SID/STAR/approach from lists, activate
///   3. Route        — set origin/dest, clear FPL, direct-to
///
/// Hotkey to open: Input mode [ then Shift+G
/// Inside the form: F5 refreshes, Escape closes.
/// </summary>
public sealed class G1000NavigatorForm : Form
{
    private const string G1000MfdFilter = "WTG1000";

    private readonly CoherentGTClient _client;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _simbriefUsername;

    // Procedure state: once an airport's facility is loaded into JS window._msfsba_*,
    // we keep the procedure lists here so dropdowns stay populated between operations.
    private List<ProcedureItem> _departures = new();
    private List<ProcedureItem> _arrivals   = new();
    private List<ProcedureItem> _approaches = new();

    // ─────────────────────────────────────────────────────────────────────
    // JS building blocks (evaluated inside the G1000 MFD page)
    // ─────────────────────────────────────────────────────────────────────

    private const string JsActiveLeg = @"
(function(){try{
  var fp=fms.getPrimaryFlightPlan();
  var ali=fp.activeLegIndex;
  if(ali<0||ali>=fp.length)return JSON.stringify({ok:true,active:false});
  var l=fp.getLeg(ali);
  var id=l&&l.leg?(l.leg.fixIcao||'').trim().replace(/\x00/g,''):'';
  if(!id&&l&&l.leg)id=l.leg.type||'?';
  return JSON.stringify({ok:true,active:true,ident:id,name:(l&&l.name)?l.name:id,index:ali,total:fp.length});
}catch(e){return JSON.stringify({ok:false,err:e.message});}})()";

    private const string JsFlightPlan = @"
(function(){try{
  var fp=fms.getPrimaryFlightPlan();
  var legs=[];
  for(var i=0;i<fp.length;i++){
    try{
      var l=fp.getLeg(i);
      var id=(l&&l.leg?(l.leg.fixIcao||'').trim().replace(/\x00/g,''):'')||'?';
      legs.push({ident:id,name:(l&&l.name)?l.name:id,active:i===fp.activeLegIndex});
    }catch(le){legs.push({ident:'ERR',name:'ERR',active:false});}
  }
  var proc={};
  try{var pd=fp.procedureDetails;if(pd){proc.depIdx=pd.departureIndex;proc.depRwy=pd.departureRunwayIndex;proc.arrIdx=pd.arrivalIndex;proc.apprIdx=pd.approachIndex;proc.apprType=pd.approachType;}}catch(pe){}
  return JSON.stringify({ok:true,origin:fp.originAirport||'',dest:fp.destinationAirport||'',activeLeg:fp.activeLegIndex,legs:legs,proc:proc});
}catch(e){return JSON.stringify({ok:false,err:e.message});}})()";

    // Load airport into window._msfsba_depFac (for departure) or _arrFac (for arrival/approach)
    // Returns the lists of procedures available.
    private static string JsLoadAirport(string icao, string slot) => $@"
(async function(){{try{{
  var fac=await fms.facLoader.getFacility('A','{Esc(icao)}');
  window['_msfsba_{slot}']=fac;
  var deps=(fac.departures||[]).map(function(d,i){{
    var rwys=(d.runwayTransitions||[]).map(function(r){{return r.name||('Rwy '+r.runwayNumber);}});
    var trans=(d.enRouteTransitions||[]).map(function(t,j){{return {{i:j,name:t.name||('Trans '+j)}}}});
    return {{i:i,name:d.name,runways:rwys,transitions:trans}};
  }});
  var arrs=(fac.arrivals||[]).map(function(a,i){{
    var rwys=(a.runwayTransitions||[]).map(function(r){{return r.name||('Rwy '+r.runwayNumber);}});
    var trans=(a.enRouteTransitions||[]).map(function(t,j){{return {{i:j,name:t.name||('Trans '+j)}}}});
    return {{i:i,name:a.name,runways:rwys,transitions:trans}};
  }});
  var apprs=(fac.approaches||[]).map(function(ap,i){{
    var trans=(ap.transitions||[]).map(function(t,j){{return {{i:j,name:t.name||('Trans '+j)}}}});
    return {{i:i,name:ap.name,type:ap.approachType,runway:ap.runway,transitions:trans}};
  }});
  return JSON.stringify({{ok:true,icao:fac.icao,name:fac.name,departures:deps,arrivals:arrs,approaches:apprs}});
}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}
}})()";

    // Load a SID (synchronous after airport loaded)
    private static string JsLoadDeparture(string slot, int depIdx, int rwyIdx, int transIdx) =>
        $"(function(){{try{{" +
        $"if(!window['_msfsba_{slot}'])return JSON.stringify({{ok:false,err:'airport not loaded'}});" +
        $"fms.loadDeparture(window['_msfsba_{slot}'],{depIdx},{rwyIdx},{transIdx});" +
        $"return JSON.stringify({{ok:true}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Load a STAR (synchronous after airport loaded)
    private static string JsLoadArrival(string slot, int arrIdx, int rwyIdx, int transIdx) =>
        $"(function(){{try{{" +
        $"if(!window['_msfsba_{slot}'])return JSON.stringify({{ok:false,err:'airport not loaded'}});" +
        $"fms.loadArrival(window['_msfsba_{slot}'],{arrIdx},{transIdx},{rwyIdx});" +
        $"return JSON.stringify({{ok:true}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Arm an approach (async — returns Promise<bool>)
    private static string JsInsertApproach(string slot, int apprIdx, int transIdx) =>
        $"(async function(){{try{{" +
        $"if(!window['_msfsba_{slot}'])return JSON.stringify({{ok:false,err:'airport not loaded'}});" +
        $"var r=await fms.insertApproach({{facility:window['_msfsba_{slot}'],approachIndex:{apprIdx},approachTransitionIndex:{transIdx}}});" +
        $"return JSON.stringify({{ok:true,result:r}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Direct-to random (can take plain string ident)
    private static string JsDirectTo(string ident) =>
        $"(function(){{try{{" +
        $"if(typeof fms.createDirectToRandom==='function'){{fms.createDirectToRandom('{Esc(ident)}');return JSON.stringify({{ok:true,method:'createDirectToRandom'}})}}" +
        $"if(typeof fms.createDirectTo==='function'){{fms.createDirectTo('{Esc(ident)}');return JSON.stringify({{ok:true,method:'createDirectTo'}})}}" +
        $"return JSON.stringify({{ok:false,err:'no directTo API'}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Set origin airport
    private static string JsSetOrigin(string slot) =>
        $"(function(){{try{{" +
        $"if(!window['_msfsba_{slot}'])return JSON.stringify({{ok:false,err:'airport not loaded'}});" +
        $"if(typeof fms.setOrigin==='function'){{fms.setOrigin(window['_msfsba_{slot}']);return JSON.stringify({{ok:true}})}}" +
        $"return JSON.stringify({{ok:false,err:'fms.setOrigin not available'}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Set destination airport
    private static string JsSetDest(string slot) =>
        $"(function(){{try{{" +
        $"if(!window['_msfsba_{slot}'])return JSON.stringify({{ok:false,err:'airport not loaded'}});" +
        $"if(typeof fms.setDestination==='function'){{fms.setDestination(window['_msfsba_{slot}']);return JSON.stringify({{ok:true}})}}" +
        $"return JSON.stringify({{ok:false,err:'fms.setDestination not available'}});}}catch(e){{return JSON.stringify({{ok:false,err:e.message}});}}}})()";

    // Clear and reinitialise the primary flight plan
    private const string JsClearFpl =
        "(async function(){try{await fms.emptyPrimaryFlightPlan();return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false,err:e.message});}})()";

    // Activate the approach
    private const string JsActivateApproach =
        "(function(){try{fms.activateApproach();return JSON.stringify({ok:true});}catch(e){return JSON.stringify({ok:false,err:e.message});}})()";

    private static string Esc(string s) => s.Replace("'", "").Replace("\\", "").ToUpperInvariant();

    // ─────────────────────────────────────────────────────────────────────
    // Procedure data model
    // ─────────────────────────────────────────────────────────────────────

    private record ProcedureItem(int Index, string Name, List<string> Runways, List<TransitionItem> Transitions);
    private record TransitionItem(int Index, string Name);

    // ─────────────────────────────────────────────────────────────────────
    // UI controls
    // ─────────────────────────────────────────────────────────────────────

    private TextBox  _statusBox       = null!;
    private TabControl _tabs          = null!;

    // Tab 1 — Flight Plan
    private TextBox  _waypointBox     = null!;
    private TextBox  _fplBox          = null!;

    // Tab 2 — Procedures
    private TextBox  _depAirportBox   = null!;
    private ComboBox _sidCombo        = null!;
    private ComboBox _sidRwyCombo     = null!;
    private ComboBox _sidTransCombo   = null!;
    private TextBox  _arrAirportBox   = null!;
    private ComboBox _starCombo       = null!;
    private ComboBox _starRwyCombo    = null!;
    private ComboBox _starTransCombo  = null!;
    private ComboBox _apprCombo       = null!;
    private ComboBox _apprTransCombo  = null!;

    // Tab 3 — Route
    private TextBox  _originBox       = null!;
    private TextBox  _destBox         = null!;
    private TextBox  _directToBox     = null!;

    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    public G1000NavigatorForm(CoherentGTClient client, ScreenReaderAnnouncer announcer,
        string simbriefUsername = "")
    {
        _client           = client   ?? throw new ArgumentNullException(nameof(client));
        _announcer        = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _simbriefUsername = simbriefUsername;

        BuildUi();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)  { _ = RefreshAsync();     e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Hide();              e.Handled = e.SuppressKeyPress = true; }
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI layout
    // ─────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "G1000 FMS Navigator";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(680, 640);
        MinimumSize = new Size(520, 480);
        KeyPreview = true;
        ShowInTaskbar = true;

        _statusBox = new TextBox
        {
            Dock = DockStyle.Top, Height = 26, ReadOnly = true,
            Text = "Not connected — enable MSFS dev mode and press [ Shift+G",
            AccessibleName = "G1000 status"
        };

        _tabs = new TabControl { Dock = DockStyle.Fill };

        _tabs.TabPages.Add(BuildFlightPlanTab());
        _tabs.TabPages.Add(BuildProceduresTab());
        _tabs.TabPages.Add(BuildRouteTab());

        Controls.Add(_tabs);
        Controls.Add(_statusBox);
    }

    private TabPage BuildFlightPlanTab()
    {
        var page = new TabPage("Flight Plan (F5)") { AccessibleName = "Flight plan tab" };

        _waypointBox = new TextBox
        {
            Dock = DockStyle.Top, Height = 48, Multiline = true, ReadOnly = true,
            AccessibleName = "Active waypoint"
        };
        var wpLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "&Active waypoint:", Padding = new Padding(4,4,0,0) };

        _fplBox = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Flight plan legs"
        };
        var fplLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "&Flight plan:", Padding = new Padding(4,4,0,0) };

        var refreshBtn = new Button { Dock = DockStyle.Top, Height = 28, Text = "&Refresh (F5)" };
        refreshBtn.Click += async (_, _) => await RefreshAsync();

        var diagnoseBtn = new Button { Dock = DockStyle.Top, Height = 28, Text = "&Diagnose connection" };
        diagnoseBtn.Click += async (_, _) => await ShowDiagnosticsAsync();

        page.Controls.Add(_fplBox);
        page.Controls.Add(fplLabel);
        page.Controls.Add(_waypointBox);
        page.Controls.Add(wpLabel);
        page.Controls.Add(refreshBtn);
        page.Controls.Add(diagnoseBtn);
        return page;
    }

    private TabPage BuildProceduresTab()
    {
        var page = new TabPage("Procedures") { AccessibleName = "Procedures tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 8;

        // ── Departure ──
        AddSectionLabel(panel, "DEPARTURE (SID)", ref y);
        _depAirportBox = AddLabeledTextBox(panel, "&Departure airport (ICAO):", ref y, "depAirport", "e.g. EGNX");
        var loadDepBtn = AddButton(panel, "&Load SIDs", ref y);
        loadDepBtn.Click += async (_, _) => await LoadProceduresAsync("dep", _depAirportBox.Text.Trim(), "departure");

        _sidCombo     = AddLabeledCombo(panel, "&SID:",          ref y, "sid");
        _sidRwyCombo  = AddLabeledCombo(panel, "SID &runway:",   ref y, "sidRwy");
        _sidTransCombo = AddLabeledCombo(panel, "SID &transition:", ref y, "sidTrans");
        _sidCombo.SelectedIndexChanged += (_, _) => PopulateSidRunwaysAndTransitions();
        var activateSidBtn = AddButton(panel, "&Activate SID", ref y);
        activateSidBtn.Click += async (_, _) => await ActivateSidAsync();

        y += 8;

        // ── Arrival ──
        AddSectionLabel(panel, "ARRIVAL (STAR)", ref y);
        _arrAirportBox = AddLabeledTextBox(panel, "&Arrival airport (ICAO):", ref y, "arrAirport", "e.g. EGLL");
        var loadArrBtn = AddButton(panel, "L&oad STARs + Approaches", ref y);
        loadArrBtn.Click += async (_, _) => await LoadProceduresAsync("arr", _arrAirportBox.Text.Trim(), "arrival");

        _starCombo     = AddLabeledCombo(panel, "S&TAR:",            ref y, "star");
        _starRwyCombo  = AddLabeledCombo(panel, "STAR r&unway:",     ref y, "starRwy");
        _starTransCombo = AddLabeledCombo(panel, "STAR trans&ition:", ref y, "starTrans");
        _starCombo.SelectedIndexChanged += (_, _) => PopulateStarRunwaysAndTransitions();
        var activateStarBtn = AddButton(panel, "Activate S&TAR", ref y);
        activateStarBtn.Click += async (_, _) => await ActivateStarAsync();

        y += 8;

        // ── Approach ──
        AddSectionLabel(panel, "APPROACH", ref y);
        _apprCombo      = AddLabeledCombo(panel, "A&pproach:",          ref y, "appr");
        _apprTransCombo = AddLabeledCombo(panel, "Appr trans&ition:", ref y, "apprTrans");
        _apprCombo.SelectedIndexChanged += (_, _) => PopulateApproachTransitions();
        var armApprBtn     = AddButton(panel, "Ar&m Approach", ref y);
        var activateApprBtn = AddButton(panel, "Acti&vate Approach", ref y);
        armApprBtn.Click      += async (_, _) => await ArmApproachAsync();
        activateApprBtn.Click += async (_, _) => await ActivateApproachAsync();

        panel.Controls.Add(new Label
        {
            Text = "Note: load the arrival airport first to populate STARs and approaches.",
            Location = new Point(8, y), Size = new Size(560, 36),
            ForeColor = SystemColors.GrayText
        });

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildRouteTab()
    {
        var page = new TabPage("Route / Direct-To") { AccessibleName = "Route and direct-to tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 8;

        AddSectionLabel(panel, "ORIGIN / DESTINATION", ref y);
        _originBox = AddLabeledTextBox(panel, "&Origin ICAO:", ref y, "origin", "e.g. EGNX");
        var setOriginBtn = AddButton(panel, "Set &Origin", ref y);
        setOriginBtn.Click += async (_, _) => await SetAirportAsync("dep", _originBox.Text.Trim(), isOrigin: true);

        _destBox = AddLabeledTextBox(panel, "&Destination ICAO:", ref y, "dest", "e.g. EGLL");
        var setDestBtn = AddButton(panel, "Set &Destination", ref y);
        setDestBtn.Click += async (_, _) => await SetAirportAsync("arr", _destBox.Text.Trim(), isOrigin: false);

        y += 12;
        AddSectionLabel(panel, "DIRECT-TO", ref y);
        _directToBox = AddLabeledTextBox(panel, "&Waypoint ICAO:", ref y, "directTo", "e.g. NUGRA");
        var directToBtn = AddButton(panel, "&Direct-To", ref y);
        directToBtn.Click += async (_, _) => await ExecuteDirectToAsync();
        _directToBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return) { _ = ExecuteDirectToAsync(); e.Handled = e.SuppressKeyPress = true; }
        };

        y += 12;
        AddSectionLabel(panel, "SIMBRIEF", ref y);
        var sbNote = new Label
        {
            Text = "Loads your latest SimBrief OFP: sets origin/dest, then tries to match the\n" +
                   "filed SID, STAR and approach by name in the G1000's procedure lists.\n" +
                   "Configure your SimBrief username in File → SimBrief Settings.",
            Location = new Point(8, y), Size = new Size(560, 52)
        };
        panel.Controls.Add(sbNote);
        y += 56;
        var loadSbBtn = AddButton(panel, "Load from &SimBrief", ref y);
        loadSbBtn.Click += async (_, _) => await LoadFromSimbriefAsync();

        y += 12;
        AddSectionLabel(panel, "FLIGHT PLAN", ref y);
        var clearFplBtn = AddButton(panel, "&Clear Flight Plan", ref y);
        clearFplBtn.Click += async (_, _) =>
        {
            if (MessageBox.Show("Clear the entire flight plan?", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                await ClearFplAsync();
        };

        page.Controls.Add(panel);
        return page;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Connection & refresh
    // ─────────────────────────────────────────────────────────────────────

    public async Task ConnectAndRefreshAsync()
    {
        if (!_client.IsConnected)
        {
            SetStatus("Connecting to G1000 MFD via dev mode…");
            bool ok = await _client.TryConnectAsync(G1000MfdFilter);
            if (!ok)
            {
                var t9  = await CoherentGTClient.ListTargetsAsync(9999);
                var t19 = await CoherentGTClient.ListTargetsAsync(19999);
                var all = t9.Concat(t19).ToList();
                if (all.Count == 0)
                    SetStatus("Dev mode not running — enable it in MSFS Options → General → Developers");
                else
                    SetStatus($"G1000 MFD not found. Found: {string.Join(", ", all.Select(t => t.title).Take(4))}");
                _announcer.AnnounceImmediate("G1000 not connected — check dev mode");
                return;
            }
        }
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!_client.IsConnected) { await ConnectAndRefreshAsync(); return; }

        SetStatus("Refreshing…");
        var legTask = _client.EvaluateAsync(JsActiveLeg, 4000);
        var fplTask = _client.EvaluateAsync(JsFlightPlan, 6000);
        await Task.WhenAll(legTask, fplTask);

        string legText = ParseActiveLeg(legTask.Result);
        string fplText = ParseFlightPlan(fplTask.Result, out string origin, out string dest);

        InvokeUI(() =>
        {
            _waypointBox.Text = legText;
            _fplBox.Text      = fplText;
            // Pre-fill procedure airport boxes if FPL has origin/dest
            if (!string.IsNullOrWhiteSpace(origin))
            {
                _depAirportBox.Text = origin;
                _originBox.Text     = origin;
            }
            if (!string.IsNullOrWhiteSpace(dest))
            {
                _arrAirportBox.Text = dest;
                _destBox.Text       = dest;
            }
        });

        SetStatus($"Connected — {_client.ConnectedTargetTitle}  •  F5 to refresh");
        _announcer.AnnounceImmediate(legText);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Diagnostics
    // ─────────────────────────────────────────────────────────────────────

    private async Task ShowDiagnosticsAsync()
    {
        SetStatus("Running connection diagnostics — probing ports 9999, 19999, 9222…");
        _announcer.AnnounceImmediate("Running connection diagnostics, please wait");

        string report = await CoherentGTClient.DiagnoseAsync();

        // Show in a simple scrollable text window
        var diagForm = new Form
        {
            Text = "G1000 Connection Diagnostics",
            Size = new Size(700, 500),
            StartPosition = FormStartPosition.CenterParent
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Courier New", 9),
            Text = report + "\r\n\r\nHINTS:\r\n" +
                   "• Port responding with HTML/JSON  → Coherent GT server is running. Check target filter.\r\n" +
                   "• Connection refused on all ports → Dev mode may need a flight restart, OR MSFS 2024\r\n" +
                   "  requires the SDK Debugger.exe to create the bridge (see below).\r\n" +
                   "• MSFS 2024 SDK path: SDK\\Tools\\CoherentGT\\Debugger.exe\r\n" +
                   "  Run it while in a flight to bridge Coherent GT to port 9999/19999.\r\n" +
                   "• Make sure the G1000 avionics are powered on and the MFD is initialised.\r\n"
        };
        diagForm.Controls.Add(box);
        diagForm.ShowDialog(this);

        SetStatus($"Diagnostics shown — {_client.ConnectedTargetTitle ?? "not connected"}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Procedure loading
    // ─────────────────────────────────────────────────────────────────────

    private async Task LoadProceduresAsync(string slot, string icao, string kind)
    {
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter an airport ICAO first"); return; }
        if (!EnsureConnected()) return;

        SetStatus($"Loading {kind} procedures for {icao}…");
        _announcer.AnnounceImmediate($"Loading {kind} procedures for {icao}, please wait");

        string? json = await _client.EvaluatePromiseAsync(JsLoadAirport(icao, slot), 15000);
        if (json == null) { SetStatus("Timeout loading airport — check connection"); _announcer.AnnounceImmediate("Airport load timed out"); return; }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                string err = r.TryGetProperty("err", out var e) ? e.GetString() ?? "?" : "?";
                SetStatus($"Airport load failed: {err}");
                _announcer.AnnounceImmediate($"Could not load {icao}: {err}");
                return;
            }

            string name = r.TryGetProperty("name", out var nm) ? nm.GetString() ?? icao : icao;

            if (slot == "dep")
            {
                _departures = ParseProcList(r, "departures");
                InvokeUI(() => PopulateSidCombo());
                _announcer.AnnounceImmediate($"{name}: {_departures.Count} SIDs loaded. Use SID dropdown to select.");
            }
            else
            {
                _arrivals   = ParseProcList(r, "arrivals");
                _approaches = ParseProcList(r, "approaches");
                InvokeUI(() => { PopulateStarCombo(); PopulateApproachCombo(); });
                _announcer.AnnounceImmediate($"{name}: {_arrivals.Count} STARs and {_approaches.Count} approaches loaded.");
            }

            SetStatus($"Connected — {_client.ConnectedTargetTitle}  •  F5 to refresh");
        }
        catch (Exception ex) { SetStatus($"Parse error: {ex.Message}"); }
    }

    private async Task ActivateSidAsync()
    {
        if (!EnsureConnected()) return;
        var sid = SelectedProcedure(_sidCombo, _departures);
        if (sid == null) { _announcer.AnnounceImmediate("Select a SID first"); return; }

        int rwyIdx   = SelectedSubIndex(_sidRwyCombo);
        int transIdx = SelectedSubIndex(_sidTransCombo, defaultToNegOne: true);

        string? result = await _client.EvaluateAsync(JsLoadDeparture("dep", sid.Index, rwyIdx, transIdx));
        HandleSimpleResult(result, $"SID {sid.Name} activated", $"SID activation failed");
        if (WasOk(result)) await RefreshAsync();
    }

    private async Task ActivateStarAsync()
    {
        if (!EnsureConnected()) return;
        var star = SelectedProcedure(_starCombo, _arrivals);
        if (star == null) { _announcer.AnnounceImmediate("Select a STAR first"); return; }

        int rwyIdx   = SelectedSubIndex(_starRwyCombo);
        int transIdx = SelectedSubIndex(_starTransCombo, defaultToNegOne: true);

        string? result = await _client.EvaluateAsync(JsLoadArrival("arr", star.Index, rwyIdx, transIdx));
        HandleSimpleResult(result, $"STAR {star.Name} activated", "STAR activation failed");
        if (WasOk(result)) await RefreshAsync();
    }

    private async Task ArmApproachAsync()
    {
        if (!EnsureConnected()) return;
        var appr = SelectedProcedure(_apprCombo, _approaches);
        if (appr == null) { _announcer.AnnounceImmediate("Select an approach first"); return; }

        int transIdx = SelectedSubIndex(_apprTransCombo, defaultToNegOne: true);
        string? result = await _client.EvaluatePromiseAsync(JsInsertApproach("arr", appr.Index, transIdx), 10000);
        HandleSimpleResult(result, $"Approach {appr.Name} armed", "Approach arm failed");
        if (WasOk(result)) await RefreshAsync();
    }

    private async Task ActivateApproachAsync()
    {
        if (!EnsureConnected()) return;
        string? result = await _client.EvaluateAsync(JsActivateApproach);
        HandleSimpleResult(result, "Approach activated", "Approach activate failed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Route / Direct-To
    // ─────────────────────────────────────────────────────────────────────

    private async Task SetAirportAsync(string slot, string icao, bool isOrigin)
    {
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter an ICAO first"); return; }
        if (!EnsureConnected()) return;

        string role = isOrigin ? "origin" : "destination";
        _announcer.AnnounceImmediate($"Setting {role} to {icao}");

        // Load the facility into the slot first, then set origin/dest
        string? loadResult = await _client.EvaluatePromiseAsync(JsLoadAirport(icao, slot), 12000);
        if (!WasOk(loadResult)) { _announcer.AnnounceImmediate($"Could not find airport {icao}"); return; }

        string jsSet = isOrigin ? JsSetOrigin(slot) : JsSetDest(slot);
        string? setResult = await _client.EvaluateAsync(jsSet);
        HandleSimpleResult(setResult, $"{role.Capitalize()} set to {icao}", $"Could not set {role}");
        if (WasOk(setResult)) await RefreshAsync();
    }

    private async Task ExecuteDirectToAsync()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident)) { _announcer.AnnounceImmediate("Enter a waypoint ICAO"); return; }
        if (!EnsureConnected()) return;

        _announcer.AnnounceImmediate($"Requesting direct to {ident}");
        string? result = await _client.EvaluateAsync(JsDirectTo(ident));
        HandleSimpleResult(result, $"Direct to {ident} set", "Direct-to failed");
        if (WasOk(result)) { InvokeUI(() => _directToBox.Clear()); await Task.Delay(600); await RefreshAsync(); }
    }

    private async Task ClearFplAsync()
    {
        if (!EnsureConnected()) return;
        string? result = await _client.EvaluatePromiseAsync(JsClearFpl, 8000);
        HandleSimpleResult(result, "Flight plan cleared", "Clear FPL failed");
        if (WasOk(result)) await RefreshAsync();
    }

    private async Task LoadFromSimbriefAsync()
    {
        if (string.IsNullOrWhiteSpace(_simbriefUsername))
        {
            _announcer.AnnounceImmediate("No SimBrief username set — go to File, SimBrief Settings");
            return;
        }
        if (!EnsureConnected()) return;

        SetStatus("Fetching SimBrief OFP…");
        _announcer.AnnounceImmediate("Fetching SimBrief flight plan, please wait");

        Models.SimBriefOFP ofp;
        try
        {
            var svc = new SimBriefService();
            ofp = await svc.FetchFullOFPAsync(_simbriefUsername);
        }
        catch (Exception ex)
        {
            SetStatus($"SimBrief fetch failed: {ex.Message}");
            _announcer.AnnounceImmediate($"SimBrief error: {ex.Message}");
            return;
        }

        string orig = ofp.OriginIcao.Trim().ToUpperInvariant();
        string dest = ofp.DestIcao.Trim().ToUpperInvariant();
        string sid  = ofp.OriginSid.Trim().ToUpperInvariant();
        string star = ofp.DestStar.Trim().ToUpperInvariant();
        string appr = ofp.DestApproach.Trim().ToUpperInvariant();

        _announcer.AnnounceImmediate(
            $"SimBrief plan: {orig} to {dest}" +
            (string.IsNullOrWhiteSpace(sid) ? "" : $", SID {sid}") +
            (string.IsNullOrWhiteSpace(star) ? "" : $", STAR {star}") +
            (string.IsNullOrWhiteSpace(appr) ? "" : $", approach {appr}"));

        // 1. Load departure airport + set origin
        InvokeUI(() => { _depAirportBox.Text = orig; _originBox.Text = orig; });
        await SetAirportAsync("dep", orig, isOrigin: true);

        // 2. Load arrival airport + set destination
        InvokeUI(() => { _arrAirportBox.Text = dest; _destBox.Text = dest; });
        await SetAirportAsync("arr", dest, isOrigin: false);

        // 3. Load procedures for both airports
        await LoadProceduresAsync("dep", orig, "departure");
        await LoadProceduresAsync("arr", dest, "arrival");

        // 4. Match and select SID by name
        if (!string.IsNullOrWhiteSpace(sid))
        {
            int sidIdx = _departures.FindIndex(d => d.Name.Contains(sid, StringComparison.OrdinalIgnoreCase));
            if (sidIdx >= 0)
            {
                InvokeUI(() => { _sidCombo.SelectedIndex = sidIdx; PopulateSidRunwaysAndTransitions(); });
                await ActivateSidAsync();
                _announcer.AnnounceImmediate($"SID {_departures[sidIdx].Name} selected");
            }
            else
                _announcer.AnnounceImmediate($"SID {sid} not found in G1000 procedure list — select manually");
        }

        // 5. Match and select STAR by name
        if (!string.IsNullOrWhiteSpace(star))
        {
            int starIdx = _arrivals.FindIndex(a => a.Name.Contains(star, StringComparison.OrdinalIgnoreCase));
            if (starIdx >= 0)
            {
                InvokeUI(() => { _starCombo.SelectedIndex = starIdx; PopulateStarRunwaysAndTransitions(); });
                await ActivateStarAsync();
                _announcer.AnnounceImmediate($"STAR {_arrivals[starIdx].Name} selected");
            }
            else
                _announcer.AnnounceImmediate($"STAR {star} not found in G1000 procedure list — select manually");
        }

        // 6. Match and arm approach by name
        if (!string.IsNullOrWhiteSpace(appr))
        {
            int apprIdx = _approaches.FindIndex(a => a.Name.Contains(appr, StringComparison.OrdinalIgnoreCase));
            if (apprIdx >= 0)
            {
                InvokeUI(() => { _apprCombo.SelectedIndex = apprIdx; PopulateApproachTransitions(); });
                await ArmApproachAsync();
                _announcer.AnnounceImmediate($"Approach {_approaches[apprIdx].Name} armed");
            }
            else
                _announcer.AnnounceImmediate($"Approach {appr} not found — select manually");
        }

        await RefreshAsync();
        SetStatus($"SimBrief plan loaded: {orig}→{dest}  •  F5 to refresh");
        _announcer.AnnounceImmediate("SimBrief plan loaded into G1000. Check Flight Plan tab to confirm.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Combo population helpers
    // ─────────────────────────────────────────────────────────────────────

    private void PopulateSidCombo()
    {
        _sidCombo.Items.Clear();
        foreach (var d in _departures) _sidCombo.Items.Add(d.Name);
        _sidRwyCombo.Items.Clear(); _sidTransCombo.Items.Clear();
        if (_sidCombo.Items.Count > 0) _sidCombo.SelectedIndex = 0;
    }

    private void PopulateStarCombo()
    {
        _starCombo.Items.Clear();
        foreach (var a in _arrivals) _starCombo.Items.Add(a.Name);
        _starRwyCombo.Items.Clear(); _starTransCombo.Items.Clear();
        if (_starCombo.Items.Count > 0) _starCombo.SelectedIndex = 0;
    }

    private void PopulateApproachCombo()
    {
        _apprCombo.Items.Clear();
        foreach (var a in _approaches) _apprCombo.Items.Add(a.Name);
        _apprTransCombo.Items.Clear();
        if (_apprCombo.Items.Count > 0) _apprCombo.SelectedIndex = 0;
    }

    private void PopulateSidRunwaysAndTransitions()
    {
        var proc = SelectedProcedure(_sidCombo, _departures);
        if (proc == null) return;
        _sidRwyCombo.Items.Clear();
        _sidRwyCombo.Items.Add("Any runway");
        foreach (var r in proc.Runways) _sidRwyCombo.Items.Add(r);
        if (_sidRwyCombo.Items.Count > 0) _sidRwyCombo.SelectedIndex = 0;

        _sidTransCombo.Items.Clear();
        _sidTransCombo.Items.Add("No transition");
        foreach (var t in proc.Transitions) _sidTransCombo.Items.Add(t.Name);
        if (_sidTransCombo.Items.Count > 0) _sidTransCombo.SelectedIndex = 0;
    }

    private void PopulateStarRunwaysAndTransitions()
    {
        var proc = SelectedProcedure(_starCombo, _arrivals);
        if (proc == null) return;
        _starRwyCombo.Items.Clear();
        _starRwyCombo.Items.Add("Any runway");
        foreach (var r in proc.Runways) _starRwyCombo.Items.Add(r);
        if (_starRwyCombo.Items.Count > 0) _starRwyCombo.SelectedIndex = 0;

        _starTransCombo.Items.Clear();
        _starTransCombo.Items.Add("No transition");
        foreach (var t in proc.Transitions) _starTransCombo.Items.Add(t.Name);
        if (_starTransCombo.Items.Count > 0) _starTransCombo.SelectedIndex = 0;
    }

    private void PopulateApproachTransitions()
    {
        var proc = SelectedProcedure(_apprCombo, _approaches);
        if (proc == null) return;
        _apprTransCombo.Items.Clear();
        _apprTransCombo.Items.Add("No transition (vectors)");
        foreach (var t in proc.Transitions) _apprTransCombo.Items.Add(t.Name);
        if (_apprTransCombo.Items.Count > 0) _apprTransCombo.SelectedIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON parsers
    // ─────────────────────────────────────────────────────────────────────

    private static string ParseActiveLeg(string? json)
    {
        if (json == null) return "No data";
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                return FmsError(r);
            if (r.TryGetProperty("active", out var act) && !act.GetBoolean())
                return "No active leg — flight plan empty or inactive";
            string ident = r.TryGetProperty("ident", out var id) ? id.GetString() ?? "?" : "?";
            string name  = r.TryGetProperty("name",  out var nm) ? nm.GetString() ?? ident : ident;
            int idx      = r.TryGetProperty("index", out var ix) ? ix.GetInt32() : -1;
            int tot      = r.TryGetProperty("total", out var tt) ? tt.GetInt32() : 0;
            string disp  = name != ident && !string.IsNullOrWhiteSpace(name) ? $"{ident} ({name})" : ident;
            return $"Active: {disp}  [{idx + 1}/{tot}]";
        }
        catch { return "Parse error"; }
    }

    private static string ParseFlightPlan(string? json, out string origin, out string dest)
    {
        origin = dest = "";
        if (json == null) return "No data";
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                return FmsError(r);

            origin = r.TryGetProperty("origin", out var og) ? og.GetString() ?? "" : "";
            dest   = r.TryGetProperty("dest",   out var dt) ? dt.GetString() ?? "" : "";
            int active = r.TryGetProperty("activeLeg", out var al) ? al.GetInt32() : -1;

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(origin) || !string.IsNullOrWhiteSpace(dest))
            {
                sb.AppendLine($"{(string.IsNullOrWhiteSpace(origin) ? "----" : origin)} → {(string.IsNullOrWhiteSpace(dest) ? "----" : dest)}");
                sb.AppendLine(new string('─', 32));
            }
            if (r.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var leg in legs.EnumerateArray())
                {
                    string ident = leg.TryGetProperty("ident", out var li) ? li.GetString() ?? "?" : "?";
                    string name  = leg.TryGetProperty("name",  out var ln) ? ln.GetString() ?? ident : ident;
                    string disp  = name != ident && !string.IsNullOrWhiteSpace(name) ? $"{ident}  {name}" : ident;
                    sb.AppendLine($"{(i == active ? "▶ " : "  ")}{disp}");
                    i++;
                }
            }
            if (sb.Length == 0) sb.AppendLine("Flight plan is empty");
            AppendProcedures(sb, r);
            return sb.ToString().TrimEnd();
        }
        catch { return "Parse error"; }
    }

    private static void AppendProcedures(StringBuilder sb, JsonElement r)
    {
        if (!r.TryGetProperty("proc", out var proc) || proc.ValueKind != JsonValueKind.Object) return;
        var lines = new List<string>();
        if (proc.TryGetProperty("depIdx",  out var dep) && dep.GetInt32() >= 0) lines.Add($"SID idx {dep.GetInt32()}");
        if (proc.TryGetProperty("arrIdx",  out var arr) && arr.GetInt32() >= 0) lines.Add($"STAR idx {arr.GetInt32()}");
        if (proc.TryGetProperty("apprIdx", out var apr) && apr.GetInt32() >= 0) lines.Add($"Appr idx {apr.GetInt32()}");
        if (lines.Count > 0) { sb.AppendLine(new string('─', 32)); sb.AppendLine("Procedures: " + string.Join(", ", lines)); }
    }

    private static List<ProcedureItem> ParseProcList(JsonElement root, string key)
    {
        var list = new List<ProcedureItem>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            int idx    = item.TryGetProperty("i",    out var iv) ? iv.GetInt32() : 0;
            string nm  = item.TryGetProperty("name", out var nv) ? nv.GetString() ?? $"#{idx}" : $"#{idx}";
            var rwys   = new List<string>();
            var trans  = new List<TransitionItem>();
            if (item.TryGetProperty("runways", out var rv) && rv.ValueKind == JsonValueKind.Array)
                foreach (var r in rv.EnumerateArray()) rwys.Add(r.GetString() ?? "");
            if (item.TryGetProperty("transitions", out var tv) && tv.ValueKind == JsonValueKind.Array)
                foreach (var t in tv.EnumerateArray())
                {
                    int ti  = t.TryGetProperty("i",    out var tiv) ? tiv.GetInt32() : 0;
                    string tn = t.TryGetProperty("name", out var tnv) ? tnv.GetString() ?? $"#{ti}" : $"#{ti}";
                    trans.Add(new TransitionItem(ti, tn));
                }
            list.Add(new ProcedureItem(idx, nm, rwys, trans));
        }
        return list;
    }

    private static string FmsError(JsonElement r)
    {
        string err = r.TryGetProperty("err", out var e) ? e.GetString() ?? "" : "?";
        if (err.Contains("fms") || err.Contains("undefined")) return "FMS not available — is the G1000 powered on?";
        return $"FMS error: {err}";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Operation helpers
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSimpleResult(string? json, string successMsg, string failMsg)
    {
        if (WasOk(json))
        {
            SetStatus($"Connected — {_client.ConnectedTargetTitle}");
            _announcer.AnnounceImmediate(successMsg);
        }
        else
        {
            string err = "";
            try { if (json != null) { using var d = JsonDocument.Parse(json); err = d.RootElement.TryGetProperty("err", out var e) ? e.GetString() ?? "" : ""; } } catch { }
            _announcer.AnnounceImmediate($"{failMsg}{(string.IsNullOrWhiteSpace(err) ? "" : ": " + err)}");
        }
    }

    private static bool WasOk(string? json)
    {
        if (json == null) return false;
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean(); }
        catch { return false; }
    }

    private bool EnsureConnected()
    {
        if (_client.IsConnected) return true;
        _announcer.AnnounceImmediate("G1000 not connected — press F5 to reconnect");
        return false;
    }

    private static ProcedureItem? SelectedProcedure(ComboBox combo, List<ProcedureItem> list)
    {
        int i = combo.SelectedIndex;
        return i >= 0 && i < list.Count ? list[i] : null;
    }

    private static int SelectedSubIndex(ComboBox combo, bool defaultToNegOne = false)
    {
        int sel = combo.SelectedIndex;
        if (sel <= 0) return defaultToNegOne ? -1 : 0;
        return sel - 1; // offset by 1 because index 0 is "Any/No transition"
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI builder helpers
    // ─────────────────────────────────────────────────────────────────────

    private static void AddSectionLabel(Panel p, string text, ref int y)
    {
        p.Controls.Add(new Label
        {
            Text = text, Location = new Point(8, y), Size = new Size(560, 20),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        });
        y += 24;
    }

    private static TextBox AddLabeledTextBox(Panel p, string label, ref int y, string name, string placeholder = "")
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(8, y), Size = new Size(200, 20) });
        var box = new TextBox
        {
            Location = new Point(215, y - 2), Size = new Size(180, 24),
            AccessibleName = label.Replace("&",""), PlaceholderText = placeholder,
            Name = name
        };
        p.Controls.Add(box);
        y += 30;
        return box;
    }

    private static ComboBox AddLabeledCombo(Panel p, string label, ref int y, string name)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(8, y), Size = new Size(200, 20) });
        var combo = new ComboBox
        {
            Location = new Point(215, y - 2), Size = new Size(320, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = label.Replace("&",""), Name = name
        };
        p.Controls.Add(combo);
        y += 32;
        return combo;
    }

    private static Button AddButton(Panel p, string text, ref int y)
    {
        var btn = new Button { Text = text, Location = new Point(215, y), Size = new Size(180, 28) };
        p.Controls.Add(btn);
        y += 34;
        return btn;
    }

    private void SetStatus(string text) => InvokeUI(() => _statusBox.Text = text);

    private void InvokeUI(Action a)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(a);
        else if (IsHandleCreated) a();
    }

    protected override void Dispose(bool disposing) { if (disposing) _client.Dispose(); base.Dispose(disposing); }
}

internal static class StringExtensions
{
    internal static string Capitalize(this string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
