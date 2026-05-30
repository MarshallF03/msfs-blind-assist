using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible FMS window for the WT G1000 NXi.
/// Works via a Community mod package (zzz-g1000-accessibility) that injects
/// g1000-accessibility-bridge.js into WTG1000_MFD.html. The JS bridge
/// communicates with this form via HTTP on localhost:19779.
///
/// Hotkey: Input mode [ then Shift+M  (same as 777/737 CDU, 787 FMC)
/// F5 = refresh  |  Escape = close
/// </summary>
public sealed class G1000NavigatorForm : Form
{
    private readonly G1000BridgeServer _bridge;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _simbriefUsername;

    // Latest FMS state from the bridge
    private string _origin  = "";
    private string _dest    = "";
    private int    _activeLeg = -1;
    private List<LegItem>       _legs      = new();
    private List<ProcedureItem> _departures = new();
    private List<ProcedureItem> _arrivals   = new();
    private List<ProcedureItem> _approaches = new();

    private record LegItem(string Ident, string Name, bool Active);
    private record ProcedureItem(int Index, string Name, List<string> Runways, List<(int i, string name)> Transitions);

    // ─────────────────────────────────────────────────────────────────────────
    // UI controls
    // ─────────────────────────────────────────────────────────────────────────

    private TextBox  _statusBox    = null!;
    private TabControl _tabs       = null!;
    private TextBox  _waypointBox  = null!;
    private TextBox  _fplBox       = null!;
    private TextBox  _depAirportBox = null!;
    private ComboBox _sidCombo      = null!;
    private ComboBox _sidRwyCombo   = null!;
    private ComboBox _sidTransCombo = null!;
    private TextBox  _arrAirportBox = null!;
    private ComboBox _starCombo     = null!;
    private ComboBox _starRwyCombo  = null!;
    private ComboBox _starTransCombo = null!;
    private ComboBox _apprCombo      = null!;
    private ComboBox _apprTransCombo = null!;
    private TextBox  _originBox     = null!;
    private TextBox  _destBox       = null!;
    private TextBox  _directToBox   = null!;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public G1000NavigatorForm(G1000BridgeServer bridge, ScreenReaderAnnouncer announcer,
        string simbriefUsername = "")
    {
        _bridge           = bridge;
        _announcer        = announcer;
        _simbriefUsername = simbriefUsername;

        _bridge.StateReceived += OnBridgeState;

        BuildUi();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)     { UpdateDisplay(); _announcer.AnnounceImmediate("Refreshed"); e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bridge state handler — runs on thread-pool, must InvokeUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnBridgeState(object? sender, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            switch (type)
            {
                case "fms_state":
                    if (root.TryGetProperty("data", out var d)) ParseFmsState(d);
                    break;

                case "command_result":
                    if (root.TryGetProperty("data", out var dr)) HandleCommandResult(dr);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[G1000Nav] state parse error: {ex.Message}");
        }
    }

    private void ParseFmsState(JsonElement d)
    {
        _origin    = d.TryGetProperty("origin",    out var og) ? og.GetString() ?? "" : "";
        _dest      = d.TryGetProperty("dest",      out var dt) ? dt.GetString() ?? "" : "";
        _activeLeg = d.TryGetProperty("activeLeg", out var al) ? al.GetInt32() : -1;

        _legs.Clear();
        if (d.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array)
            foreach (var l in legs.EnumerateArray())
            {
                string id  = l.TryGetProperty("ident",  out var li) ? li.GetString() ?? "?" : "?";
                string nm  = l.TryGetProperty("name",   out var ln) ? ln.GetString() ?? id  : id;
                bool   act = l.TryGetProperty("active", out var la) && la.GetBoolean();
                _legs.Add(new LegItem(id, nm, act));
            }

        InvokeUI(() =>
        {
            // Auto-fill airport boxes from FPL
            if (!string.IsNullOrWhiteSpace(_origin))
            { _depAirportBox.Text = _origin; _originBox.Text = _origin; }
            if (!string.IsNullOrWhiteSpace(_dest))
            { _arrAirportBox.Text = _dest; _destBox.Text = _dest; }

            SetStatus($"Connected — {_origin}→{_dest}  •  F5 to refresh");
            UpdateDisplay();
        });
    }

    private void HandleCommandResult(JsonElement d)
    {
        string cmd = d.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
        bool   ok  = false;
        if (d.TryGetProperty("result", out var r))
            ok = r.TryGetProperty("ok", out var okp) && okp.GetBoolean();

        switch (cmd)
        {
            case "load_airport":
                if (ok && d.TryGetProperty("result", out var res))
                    ParseLoadedAirport(res);
                break;
        }
    }

    private void ParseLoadedAirport(JsonElement res)
    {
        // Determine which slot this was for by checking what the form last requested
        // We use the 'slot' field the JS bridge echoes back (if available)
        string slot = res.TryGetProperty("slot", out var s) ? s.GetString() ?? "" : "";

        var deps  = ParseProcList(res, "departures");
        var arrs  = ParseProcList(res, "arrivals");
        var apprs = ParseProcList(res, "approaches");
        string name = res.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

        InvokeUI(() =>
        {
            if (deps.Count > 0 || slot == "dep")
            {
                _departures = deps;
                PopulateSidCombo();
                if (!string.IsNullOrWhiteSpace(name))
                    _announcer.AnnounceImmediate($"{name}: {deps.Count} SIDs loaded");
            }
            if (arrs.Count > 0 || apprs.Count > 0 || slot == "arr")
            {
                _arrivals   = arrs;
                _approaches = apprs;
                PopulateStarCombo();
                PopulateApprCombo();
                if (!string.IsNullOrWhiteSpace(name))
                    _announcer.AnnounceImmediate($"{name}: {arrs.Count} STARs, {apprs.Count} approaches loaded");
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Display update
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateDisplay()
    {
        // Active waypoint
        var active = _legs.FirstOrDefault(l => l.Active);
        if (active != null)
        {
            string disp = active.Name != active.Ident ? $"{active.Ident} ({active.Name})" : active.Ident;
            int idx = _legs.IndexOf(active);
            _waypointBox.Text = $"Active: {disp}  [{idx + 1}/{_legs.Count}]";
        }
        else
            _waypointBox.Text = _legs.Count == 0 ? "No flight plan loaded" : "No active leg";

        // Flight plan
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_origin) || !string.IsNullOrWhiteSpace(_dest))
        {
            sb.AppendLine($"{(_origin.Length > 0 ? _origin : "----")} → {(_dest.Length > 0 ? _dest : "----")}");
            sb.AppendLine(new string('─', 32));
        }
        foreach (var l in _legs)
        {
            string disp = l.Name != l.Ident && !string.IsNullOrWhiteSpace(l.Name)
                ? $"{l.Ident}  {l.Name}" : l.Ident;
            sb.AppendLine($"{(l.Active ? "▶ " : "  ")}{disp}");
        }
        if (_legs.Count == 0) sb.AppendLine("Flight plan is empty");
        _fplBox.Text = sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Commands sent to the bridge
    // ─────────────────────────────────────────────────────────────────────────

    private void LoadAirport(string icao, string slot)
    {
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter an airport ICAO"); return; }
        _announcer.AnnounceImmediate($"Loading {icao} procedures, please wait");
        _bridge.SendCommand("load_airport", new { icao = icao.Trim().ToUpperInvariant(), slot });
    }

    private void ActivateSid()
    {
        var sid = SelectedProc(_sidCombo, _departures);
        if (sid == null) { _announcer.AnnounceImmediate("Select a SID first"); return; }
        _bridge.SendCommand("load_dep", new
        {
            depIdx  = sid.Index,
            rwyIdx  = SubIdx(_sidRwyCombo),
            transIdx = SubIdx(_sidTransCombo, neg: true)
        });
        _announcer.AnnounceImmediate($"SID {sid.Name} activating");
    }

    private void ActivateStar()
    {
        var star = SelectedProc(_starCombo, _arrivals);
        if (star == null) { _announcer.AnnounceImmediate("Select a STAR first"); return; }
        _bridge.SendCommand("load_arr", new
        {
            arrIdx   = star.Index,
            rwyIdx   = SubIdx(_starRwyCombo),
            transIdx = SubIdx(_starTransCombo, neg: true)
        });
        _announcer.AnnounceImmediate($"STAR {star.Name} activating");
    }

    private void ArmApproach()
    {
        var appr = SelectedProc(_apprCombo, _approaches);
        if (appr == null) { _announcer.AnnounceImmediate("Select an approach first"); return; }
        _bridge.SendCommand("arm_approach", new
        {
            apprIdx  = appr.Index,
            transIdx = SubIdx(_apprTransCombo, neg: true)
        });
        _announcer.AnnounceImmediate($"Arming approach {appr.Name}");
    }

    private void ActivateApproach()
    {
        _bridge.SendCommand("activate_approach");
        _announcer.AnnounceImmediate("Activating approach");
    }

    private void SetOrigin()
    {
        if (string.IsNullOrWhiteSpace(_originBox.Text)) { _announcer.AnnounceImmediate("Enter origin ICAO"); return; }
        string icao = _originBox.Text.Trim().ToUpperInvariant();
        // Load airport first so the JS has the facility, then set origin
        _bridge.SendCommand("load_airport", new { icao, slot = "dep" });
        _bridge.SendCommand("set_origin");
        _announcer.AnnounceImmediate($"Setting origin {icao}");
    }

    private void SetDest()
    {
        if (string.IsNullOrWhiteSpace(_destBox.Text)) { _announcer.AnnounceImmediate("Enter destination ICAO"); return; }
        string icao = _destBox.Text.Trim().ToUpperInvariant();
        _bridge.SendCommand("load_airport", new { icao, slot = "arr" });
        _bridge.SendCommand("set_dest");
        _announcer.AnnounceImmediate($"Setting destination {icao}");
    }

    private void DirectTo()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident)) { _announcer.AnnounceImmediate("Enter a waypoint ICAO"); return; }
        _bridge.SendCommand("direct_to", new { ident });
        InvokeUI(() => _directToBox.Clear());
        _announcer.AnnounceImmediate($"Direct to {ident}");
    }

    private void ClearFpl()
    {
        if (MessageBox.Show("Clear the entire flight plan?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _bridge.SendCommand("clear_fpl");
        _announcer.AnnounceImmediate("Clearing flight plan");
    }

    private async Task LoadFromSimbriefAsync()
    {
        if (string.IsNullOrWhiteSpace(_simbriefUsername))
        { _announcer.AnnounceImmediate("No SimBrief username — go to File, SimBrief Settings"); return; }

        _announcer.AnnounceImmediate("Fetching SimBrief flight plan, please wait");

        Models.SimBriefOFP ofp;
        try { var svc = new SimBriefService(); ofp = await svc.FetchFullOFPAsync(_simbriefUsername); }
        catch (Exception ex) { _announcer.AnnounceImmediate($"SimBrief error: {ex.Message}"); return; }

        string orig = ofp.OriginIcao.Trim().ToUpperInvariant();
        string dest = ofp.DestIcao.Trim().ToUpperInvariant();
        string sid  = ofp.OriginSid.Trim().ToUpperInvariant();
        string star = ofp.DestStar.Trim().ToUpperInvariant();
        string appr = ofp.DestApproach.Trim().ToUpperInvariant();

        InvokeUI(() => { _originBox.Text = orig; _destBox.Text = dest;
                         _depAirportBox.Text = orig; _arrAirportBox.Text = dest; });

        _announcer.AnnounceImmediate(
            $"SimBrief: {orig}→{dest}" +
            (string.IsNullOrWhiteSpace(sid) ? "" : $" SID {sid}") +
            (string.IsNullOrWhiteSpace(star) ? "" : $" STAR {star}") +
            (string.IsNullOrWhiteSpace(appr) ? "" : $" Appr {appr}"));

        // Queue all commands — JS processes them one by one
        _bridge.SendCommand("load_airport", new { icao = orig, slot = "dep" });
        _bridge.SendCommand("set_origin");
        _bridge.SendCommand("load_airport", new { icao = dest, slot = "arr" });
        _bridge.SendCommand("set_dest");

        // Store procedure names so we can match after airport loads
        if (!string.IsNullOrWhiteSpace(sid))  _pendingSid  = sid;
        if (!string.IsNullOrWhiteSpace(star)) _pendingStar = star;
        if (!string.IsNullOrWhiteSpace(appr)) _pendingAppr = appr;

        SetStatus("SimBrief plan queued — procedures will load automatically");
    }

    // Pending procedure names set by SimBrief; matched in ParseLoadedAirport
    private string _pendingSid  = "";
    private string _pendingStar = "";
    private string _pendingAppr = "";

    // Override ParseLoadedAirport to also auto-select pending procedures
    // (called in HandleCommandResult → ParseLoadedAirport)
    private void AutoSelectPendingProcedures(string slot)
    {
        if (slot == "dep" && !string.IsNullOrWhiteSpace(_pendingSid))
        {
            int i = _departures.FindIndex(d => d.Name.Contains(_pendingSid, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { InvokeUI(() => { _sidCombo.SelectedIndex = i; ActivateSid(); }); }
            else _announcer.AnnounceImmediate($"SID {_pendingSid} not found — select manually");
            _pendingSid = "";
        }
        if (slot == "arr")
        {
            if (!string.IsNullOrWhiteSpace(_pendingStar))
            {
                int i = _arrivals.FindIndex(a => a.Name.Contains(_pendingStar, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) { InvokeUI(() => { _starCombo.SelectedIndex = i; ActivateStar(); }); }
                else _announcer.AnnounceImmediate($"STAR {_pendingStar} not found — select manually");
                _pendingStar = "";
            }
            if (!string.IsNullOrWhiteSpace(_pendingAppr))
            {
                int i = _approaches.FindIndex(a => a.Name.Contains(_pendingAppr, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) { InvokeUI(() => { _apprCombo.SelectedIndex = i; ArmApproach(); }); }
                else _announcer.AnnounceImmediate($"Approach {_pendingAppr} not found — select manually");
                _pendingAppr = "";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI construction
    // ─────────────────────────────────────────────────────────────────────────

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
            Text = "Waiting for G1000 bridge — is the mod package installed?",
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

        _waypointBox = new TextBox { Dock = DockStyle.Top, Height = 48, Multiline = true, ReadOnly = true, AccessibleName = "Active waypoint" };
        var wpLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "&Active waypoint:", Padding = new Padding(4,4,0,0) };

        _fplBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Flight plan" };
        var fplLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "&Flight plan:", Padding = new Padding(4,4,0,0) };

        var refreshBtn = new Button { Dock = DockStyle.Top, Height = 28, Text = "&Refresh (F5)" };
        refreshBtn.Click += (_, _) => { UpdateDisplay(); _announcer.AnnounceImmediate("Refreshed"); };

        page.Controls.Add(_fplBox);
        page.Controls.Add(fplLabel);
        page.Controls.Add(_waypointBox);
        page.Controls.Add(wpLabel);
        page.Controls.Add(refreshBtn);
        return page;
    }

    private TabPage BuildProceduresTab()
    {
        var page  = new TabPage("Procedures") { AccessibleName = "Procedures tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        AddSect(panel, "DEPARTURE (SID)", ref y);
        _depAirportBox = AddTxt(panel, "&Departure airport:", ref y, "dep", "e.g. EGNX");
        var loadDep = AddBtn(panel, "&Load SIDs", ref y);
        loadDep.Click += (_, _) => LoadAirport(_depAirportBox.Text, "dep");
        _sidCombo  = AddCombo(panel, "&SID:",          ref y, "sid");
        _sidRwyCombo = AddCombo(panel, "SID &runway:",  ref y, "sidRwy");
        _sidTransCombo = AddCombo(panel, "SID trans&ition:", ref y, "sidTrans");
        _sidCombo.SelectedIndexChanged += (_, _) => PopulateSidSubs();
        var actSid = AddBtn(panel, "&Activate SID", ref y);
        actSid.Click += (_, _) => ActivateSid();

        y += 8;
        AddSect(panel, "ARRIVAL (STAR)", ref y);
        _arrAirportBox = AddTxt(panel, "&Arrival airport:", ref y, "arr", "e.g. EGLL");
        var loadArr = AddBtn(panel, "Load &STARs + Approaches", ref y);
        loadArr.Click += (_, _) => LoadAirport(_arrAirportBox.Text, "arr");
        _starCombo  = AddCombo(panel, "S&TAR:",         ref y, "star");
        _starRwyCombo = AddCombo(panel, "STAR r&unway:", ref y, "starRwy");
        _starTransCombo = AddCombo(panel, "STAR trans&ition:", ref y, "starTrans");
        _starCombo.SelectedIndexChanged += (_, _) => PopulateStarSubs();
        var actStar = AddBtn(panel, "Activate ST&AR", ref y);
        actStar.Click += (_, _) => ActivateStar();

        y += 8;
        AddSect(panel, "APPROACH", ref y);
        _apprCombo  = AddCombo(panel, "A&pproach:",    ref y, "appr");
        _apprTransCombo = AddCombo(panel, "Appr trans&ition:", ref y, "apprTrans");
        _apprCombo.SelectedIndexChanged += (_, _) => PopulateApprTrans();
        var armAppr = AddBtn(panel, "Ar&m Approach", ref y);
        var actAppr = AddBtn(panel, "Acti&vate Approach", ref y);
        armAppr.Click += (_, _) => ArmApproach();
        actAppr.Click += (_, _) => ActivateApproach();

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildRouteTab()
    {
        var page  = new TabPage("Route / Direct-To") { AccessibleName = "Route tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        AddSect(panel, "ORIGIN / DESTINATION", ref y);
        _originBox = AddTxt(panel, "&Origin ICAO:", ref y, "origin", "e.g. EGNX");
        var setOrig = AddBtn(panel, "Set &Origin", ref y);
        setOrig.Click += (_, _) => SetOrigin();
        _destBox = AddTxt(panel, "&Destination ICAO:", ref y, "dest", "e.g. EGLL");
        var setDest = AddBtn(panel, "Set &Destination", ref y);
        setDest.Click += (_, _) => SetDest();

        y += 12;
        AddSect(panel, "DIRECT-TO", ref y);
        _directToBox = AddTxt(panel, "&Waypoint ICAO:", ref y, "dto", "e.g. NUGRA");
        var dtoBtn = AddBtn(panel, "&Direct-To", ref y);
        dtoBtn.Click += (_, _) => DirectTo();
        _directToBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Return) { DirectTo(); e.Handled = e.SuppressKeyPress = true; } };

        y += 12;
        AddSect(panel, "SIMBRIEF", ref y);
        panel.Controls.Add(new Label
        {
            Text = "Loads your latest SimBrief plan, sets origin/dest,\nmatches SID/STAR/approach by name automatically.",
            Location = new Point(8, y), Size = new Size(560, 36)
        });
        y += 40;
        var sbBtn = AddBtn(panel, "Load from &SimBrief", ref y);
        sbBtn.Click += async (_, _) => await LoadFromSimbriefAsync();

        y += 12;
        AddSect(panel, "FLIGHT PLAN", ref y);
        var clearBtn = AddBtn(panel, "&Clear Flight Plan", ref y);
        clearBtn.Click += (_, _) => ClearFpl();

        page.Controls.Add(panel);
        return page;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Combo population
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateSidCombo()
    {
        _sidCombo.Items.Clear();
        foreach (var d in _departures) _sidCombo.Items.Add(d.Name);
        _sidRwyCombo.Items.Clear(); _sidTransCombo.Items.Clear();
        if (_sidCombo.Items.Count > 0) _sidCombo.SelectedIndex = 0;
        AutoSelectPendingProcedures("dep");
    }

    private void PopulateStarCombo()
    {
        _starCombo.Items.Clear();
        foreach (var a in _arrivals) _starCombo.Items.Add(a.Name);
        _starRwyCombo.Items.Clear(); _starTransCombo.Items.Clear();
        if (_starCombo.Items.Count > 0) _starCombo.SelectedIndex = 0;
    }

    private void PopulateApprCombo()
    {
        _apprCombo.Items.Clear();
        foreach (var a in _approaches) _apprCombo.Items.Add(a.Name);
        _apprTransCombo.Items.Clear();
        if (_apprCombo.Items.Count > 0) _apprCombo.SelectedIndex = 0;
        AutoSelectPendingProcedures("arr");
    }

    private void PopulateSidSubs()
    {
        var p = SelectedProc(_sidCombo, _departures); if (p == null) return;
        _sidRwyCombo.Items.Clear(); _sidRwyCombo.Items.Add("Any runway");
        foreach (var r in p.Runways) _sidRwyCombo.Items.Add(r);
        if (_sidRwyCombo.Items.Count > 0) _sidRwyCombo.SelectedIndex = 0;
        _sidTransCombo.Items.Clear(); _sidTransCombo.Items.Add("No transition");
        foreach (var t in p.Transitions) _sidTransCombo.Items.Add(t.name);
        if (_sidTransCombo.Items.Count > 0) _sidTransCombo.SelectedIndex = 0;
    }

    private void PopulateStarSubs()
    {
        var p = SelectedProc(_starCombo, _arrivals); if (p == null) return;
        _starRwyCombo.Items.Clear(); _starRwyCombo.Items.Add("Any runway");
        foreach (var r in p.Runways) _starRwyCombo.Items.Add(r);
        if (_starRwyCombo.Items.Count > 0) _starRwyCombo.SelectedIndex = 0;
        _starTransCombo.Items.Clear(); _starTransCombo.Items.Add("No transition");
        foreach (var t in p.Transitions) _starTransCombo.Items.Add(t.name);
        if (_starTransCombo.Items.Count > 0) _starTransCombo.SelectedIndex = 0;
    }

    private void PopulateApprTrans()
    {
        var p = SelectedProc(_apprCombo, _approaches); if (p == null) return;
        _apprTransCombo.Items.Clear(); _apprTransCombo.Items.Add("No transition (vectors)");
        foreach (var t in p.Transitions) _apprTransCombo.Items.Add(t.name);
        if (_apprTransCombo.Items.Count > 0) _apprTransCombo.SelectedIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<ProcedureItem> ParseProcList(JsonElement root, string key)
    {
        var list = new List<ProcedureItem>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in arr.EnumerateArray())
        {
            int    idx   = item.TryGetProperty("i",    out var iv) ? iv.GetInt32()  : 0;
            string nm    = item.TryGetProperty("name", out var nv) ? nv.GetString() ?? $"#{idx}" : $"#{idx}";
            var    rwys  = new List<string>();
            var    trans = new List<(int i, string name)>();
            if (item.TryGetProperty("runways", out var rv) && rv.ValueKind == JsonValueKind.Array)
                foreach (var r in rv.EnumerateArray()) rwys.Add(r.GetString() ?? "");
            if (item.TryGetProperty("transitions", out var tv) && tv.ValueKind == JsonValueKind.Array)
                foreach (var t in tv.EnumerateArray())
                {
                    int    ti = t.TryGetProperty("i",    out var tiv) ? tiv.GetInt32()  : 0;
                    string tn = t.TryGetProperty("name", out var tnv) ? tnv.GetString() ?? $"#{ti}" : $"#{ti}";
                    trans.Add((ti, tn));
                }
            list.Add(new ProcedureItem(idx, nm, rwys, trans));
        }
        return list;
    }

    private static ProcedureItem? SelectedProc(ComboBox cb, List<ProcedureItem> list)
        => cb.SelectedIndex >= 0 && cb.SelectedIndex < list.Count ? list[cb.SelectedIndex] : null;

    private static int SubIdx(ComboBox cb, bool neg = false)
    {
        int i = cb.SelectedIndex;
        return i <= 0 ? (neg ? -1 : 0) : i - 1; // offset 1 for "Any/No" sentinel
    }

    private void SetStatus(string text) => InvokeUI(() => _statusBox.Text = text);

    private void InvokeUI(Action a)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(a);
        else if (IsHandleCreated) a();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI builder helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void AddSect(Panel p, string t, ref int y)
    {
        p.Controls.Add(new Label { Text = t, Location = new Point(8, y), Size = new Size(560, 20),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });
        y += 24;
    }

    private static TextBox AddTxt(Panel p, string lbl, ref int y, string name, string placeholder = "")
    {
        p.Controls.Add(new Label { Text = lbl, Location = new Point(8, y), Size = new Size(200, 20) });
        var box = new TextBox { Location = new Point(215, y-2), Size = new Size(180, 24),
            AccessibleName = lbl.Replace("&",""), PlaceholderText = placeholder, Name = name };
        p.Controls.Add(box); y += 30;
        return box;
    }

    private static ComboBox AddCombo(Panel p, string lbl, ref int y, string name)
    {
        p.Controls.Add(new Label { Text = lbl, Location = new Point(8, y), Size = new Size(200, 20) });
        var cb = new ComboBox { Location = new Point(215, y-2), Size = new Size(320, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = lbl.Replace("&",""), Name = name };
        p.Controls.Add(cb); y += 32;
        return cb;
    }

    private static Button AddBtn(Panel p, string text, ref int y)
    {
        var btn = new Button { Text = text, Location = new Point(215, y), Size = new Size(200, 28) };
        p.Controls.Add(btn); y += 34;
        return btn;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bridge.StateReceived -= OnBridgeState;
        base.Dispose(disposing);
    }
}
