using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using System.Text;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible GPS/FMS window for the WT G1000 NXi.
/// Receives live data from the bridge JS via G1000BridgeServer (port 19778).
/// Hotkey: Input mode [ then Shift+M  (same as 777/737 CDU / 787 FMC)
/// F5 = request fresh state   |   Escape = close
/// </summary>
public sealed class G1000NavigatorForm : Form
{
    private readonly G1000BridgeServer _bridge;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _simbriefUsername;

    // Cached state
    private G1000FplStateArgs?     _lastFpl;
    private G1000NavStateArgs?     _lastNav;
    private G1000FacilityDataArgs? _lastFacility;
    private string _lastActiveIdent = "";

    // ── UI controls ───────────────────────────────────────────────────────────
    private TextBox  _statusBox     = null!;
    private TabControl _tabs        = null!;

    // Tab 1 — Active nav
    private TextBox  _navBox        = null!;
    private Button   _gpsNavBtn     = null!;   // GPS → NAV1 toggle
    private bool     _gpsDrivesNav  = false;

    // Tab 2 — Flight plan
    private ListBox  _fplList       = null!;
    private TextBox  _fplInfoBox    = null!;

    // Tab 3 — Procedures
    private TextBox   _procAirportBox = null!;
    private ComboBox  _procTypeCombo  = null!;
    private ListBox   _procList       = null!;
    private ComboBox  _rwyCombo       = null!;
    private ComboBox  _transCombo     = null!;

    // Tab 4 — Direct-to / SimBrief
    private TextBox  _directToBox   = null!;
    private TextBox  _sbInfoBox     = null!;

    // ── Constructor ───────────────────────────────────────────────────────────
    public G1000NavigatorForm(G1000BridgeServer bridge, ScreenReaderAnnouncer announcer,
        string simbriefUsername = "")
    {
        _bridge           = bridge;
        _announcer        = announcer;
        _simbriefUsername = simbriefUsername;

        _bridge.FplStateReceived      += OnFplState;
        _bridge.NavStateReceived      += OnNavState;
        _bridge.FacilityDataReceived  += OnFacilityData;
        _bridge.CommandAck            += OnCommandAck;
        _bridge.CommandError          += OnCommandError;
        _bridge.BridgeConnected       += OnBridgeConnected;

        BuildUi();

        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        KeyDown     += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)     { _bridge.SendCommand("request_state"); e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
        };
    }

    // ── Bridge event handlers ─────────────────────────────────────────────────

    private void OnBridgeConnected(object? sender, EventArgs e)
    {
        SetStatus("G1000 bridge connected — receiving live data");
        _announcer.AnnounceImmediate("G1000 navigator connected. Flight plan loading.");
        _bridge.SendCommand("request_state");
    }

    /// <summary>Called from MainForm when the form is shown — ensures the bridge
    /// server is running and announces current connection state.</summary>
    public void EnsureVisible()
    {
        if (!_bridge.IsRunning)
        {
            _bridge.Start();
            SetStatus("Bridge server starting on port 19778…");
        }
        else if (_lastFpl == null)
        {
            // Connected but no state yet — request it
            _bridge.SendCommand("request_state");
        }
    }

    private void OnFplState(object? sender, G1000FplStateArgs args)
    {
        _lastFpl = args;

        // Announce active leg change
        var active = args.Items.FirstOrDefault(l => l.IsActive);
        if (active != null && active.Ident != _lastActiveIdent)
        {
            _lastActiveIdent = active.Ident;
            string dist = active.Distance > 0.05 ? $"  {active.Distance:F1} nm" : "";
            _announcer.AnnounceImmediate($"Active waypoint: {active.Ident}{dist}");
        }

        InvokeUI(() =>
        {
            // Update flight plan list
            int sel = _fplList.SelectedIndex;
            _fplList.Items.Clear();
            foreach (var leg in args.Items)
                _fplList.Items.Add((leg.IsActive ? "▶ " : "  ") + leg.Text);
            if (sel >= 0 && sel < _fplList.Items.Count) _fplList.SelectedIndex = sel;
            else if (args.Selected >= 0 && args.Selected < _fplList.Items.Count)
                _fplList.SelectedIndex = args.Selected;

            string route = "";
            if (!string.IsNullOrWhiteSpace(args.Origin) || !string.IsNullOrWhiteSpace(args.Dest))
                route = $"{(string.IsNullOrWhiteSpace(args.Origin) ? "----" : args.Origin)} → " +
                        $"{(string.IsNullOrWhiteSpace(args.Dest)   ? "----" : args.Dest)}  " +
                        $"({args.Items.Count} legs)";
            _fplInfoBox.Text = route;

            // Pre-fill procedure airport box from FPL dest
            if (!string.IsNullOrWhiteSpace(args.Dest) && string.IsNullOrWhiteSpace(_procAirportBox.Text))
                _procAirportBox.Text = args.Dest;

            if (!_bridge.IsRunning)
                SetStatus("Bridge not running — switch to Cessna 172 G1000 to start it");
        });
    }

    private void OnNavState(object? sender, G1000NavStateArgs args)
    {
        _lastNav = args;
        bool gpsChanged = _gpsDrivesNav != args.GpsDrivesNav;
        _gpsDrivesNav = args.GpsDrivesNav;
        if (gpsChanged)
            _announcer.AnnounceImmediate(args.GpsDrivesNav ? "GPS drives NAV 1: ON" : "GPS drives NAV 1: OFF");

        InvokeUI(() =>
        {
            // Update GPS→NAV1 button label so it reads current state
            _gpsNavBtn.Text = args.GpsDrivesNav
                ? "GPS → NAV1: ON  (click to turn OFF)"
                : "GPS → NAV1: OFF  (click to turn ON)";
            _gpsNavBtn.BackColor = args.GpsDrivesNav
                ? System.Drawing.Color.DarkGreen
                : System.Drawing.Color.DarkRed;
            _gpsNavBtn.ForeColor = System.Drawing.Color.White;

            var active = _lastFpl?.Items.FirstOrDefault(l => l.IsActive);
            string ident = active?.Ident ?? "---";

            var sb = new StringBuilder();
            sb.AppendLine($"Waypoint : {ident}");
            if (args.NextWpDist > 0.01) sb.AppendLine($"Distance : {args.NextWpDist:F2} nm");
            if (args.NextWpBearing > 0) sb.AppendLine($"Bearing  : {args.NextWpBearing:F0}°");
            if (args.NextWpEte    > 0)
            {
                int mins = (int)(args.NextWpEte / 60);
                int secs = (int)(args.NextWpEte % 60);
                sb.AppendLine($"ETE      : {mins:D2}:{secs:D2}");
            }
            if (args.Dtk > 0) sb.AppendLine($"Track    : {args.Dtk:F0}°");
            if (Math.Abs(args.Xtk) > 0.01) sb.AppendLine($"XTK      : {args.Xtk:F2} nm {(args.Xtk < 0 ? "L" : "R")}");
            if (args.GroundSpeed > 1) sb.AppendLine($"GS       : {args.GroundSpeed:F0} kts");
            sb.AppendLine();
            sb.AppendLine($"GPS → NAV1  : {(args.GpsDrivesNav ? "YES" : "NO")}");
            sb.AppendLine($"Direct-To   : {(args.IsDirectTo ? "YES" : "no")}");
            sb.AppendLine($"Approach    : {(args.IsApproachActive ? "ACTIVE" : args.IsApproachLoaded ? "LOADED" : args.ApproachMode == 1 ? "ARMED" : "none")}");

            _navBox.Text = sb.ToString().TrimEnd();
        });
    }

    private void OnFacilityData(object? sender, G1000FacilityDataArgs args)
    {
        _lastFacility = args;
        InvokeUI(() =>
        {
            PopulateProcList();
            _announcer.AnnounceImmediate(
                $"{args.Name}: {args.Departures.Count} SIDs, {args.Arrivals.Count} STARs, {args.Approaches.Count} approaches loaded");
        });
    }

    private void OnCommandAck(object? sender, string cmd)
    {
        System.Diagnostics.Debug.WriteLine($"[G1000Nav] ack: {cmd}");
    }

    private void OnCommandError(object? sender, string err)
    {
        _announcer.AnnounceImmediate($"G1000 error: {err}");
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "G1000 FMS Navigator";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(680, 600);
        MinimumSize = new Size(520, 460);
        KeyPreview = true;
        ShowInTaskbar = true;

        _statusBox = new TextBox
        {
            Dock = DockStyle.Top, Height = 26, ReadOnly = true,
            Text = "Waiting for G1000 bridge connection…",
            AccessibleName = "G1000 status"
        };

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildNavTab());
        _tabs.TabPages.Add(BuildFplTab());
        _tabs.TabPages.Add(BuildProcTab());
        _tabs.TabPages.Add(BuildDirectTab());

        Controls.Add(_tabs);
        Controls.Add(_statusBox);
    }

    private TabPage BuildNavTab()
    {
        var page = new TabPage("Nav (active)") { AccessibleName = "Active navigation tab" };

        _navBox = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Active waypoint navigation info",
            Text = "Waiting for data…"
        };

        // GPS → NAV1 toggle — most important button, shown at the top
        _gpsNavBtn = new Button
        {
            Dock = DockStyle.Top, Height = 34,
            Text = "GPS → NAV1: OFF  (click to turn ON)",
            AccessibleName = "Toggle GPS drives NAV 1",
            AccessibleDescription = "When ON the autopilot follows the GPS flight plan. Must be ON for activate leg to cause a turn.",
            BackColor = System.Drawing.Color.DarkRed,
            ForeColor = System.Drawing.Color.White
        };
        _gpsNavBtn.Click += (_, _) =>
        {
            _bridge.SendCommand("gps_drives_nav");
            _announcer.AnnounceImmediate("Toggling GPS drives NAV 1");
        };

        var refreshBtn = new Button { Dock = DockStyle.Top, Height = 28, Text = "&Refresh (F5)" };
        refreshBtn.Click += (_, _) => _bridge.SendCommand("request_state");

        // Add bottom-up (last added = topmost with DockStyle.Top)
        page.Controls.Add(_navBox);
        page.Controls.Add(_gpsNavBtn);
        page.Controls.Add(refreshBtn);
        return page;
    }

    private TabPage BuildFplTab()
    {
        var page = new TabPage("Flight Plan") { AccessibleName = "Flight plan tab" };

        _fplInfoBox = new TextBox
        {
            Dock = DockStyle.Top, Height = 26, ReadOnly = true,
            AccessibleName = "Route summary"
        };
        _fplList = new ListBox
        {
            Dock = DockStyle.Fill, AccessibleName = "Flight plan legs",
            AccessibleDescription = "Press Enter on a leg to go direct to it"
        };
        _fplList.KeyDown += FplList_KeyDown;

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 20, TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Text = "Press Enter to activate leg (go direct)"
        };

        page.Controls.Add(_fplList);
        page.Controls.Add(hint);
        page.Controls.Add(_fplInfoBox);
        return page;
    }

    private TabPage BuildProcTab()
    {
        var page  = new TabPage("Procedures") { AccessibleName = "Procedures tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        AddLabel(panel, "&Airport ICAO:", 8, y, 130);
        _procAirportBox = new TextBox { Location = new Point(145, y-2), Size = new Size(120, 24),
            AccessibleName = "Airport ICAO", PlaceholderText = "e.g. EGBB" };
        panel.Controls.Add(_procAirportBox);
        y += 30;

        AddLabel(panel, "Procedure &type:", 8, y, 130);
        _procTypeCombo = new ComboBox { Location = new Point(145, y-2), Size = new Size(160, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Procedure type" };
        _procTypeCombo.Items.AddRange(new[] { "Approaches", "STARs", "SIDs" });
        _procTypeCombo.SelectedIndex = 0;
        _procTypeCombo.SelectedIndexChanged += (_, _) => PopulateProcList();
        panel.Controls.Add(_procTypeCombo);
        y += 30;

        var loadBtn = new Button { Location = new Point(145, y), Size = new Size(130, 28), Text = "&Load procedures" };
        loadBtn.Click += LoadProcedures_Click;
        panel.Controls.Add(loadBtn);
        y += 36;

        _procList = new ListBox { Location = new Point(8, y), Size = new Size(600, 140),
            AccessibleName = "Procedure list",
            AccessibleDescription = "Select a procedure then choose runway/transition below" };
        _procList.SelectedIndexChanged += ProcList_SelectedIndexChanged;
        panel.Controls.Add(_procList);
        y += 148;

        AddLabel(panel, "&Runway:", 8, y, 80);
        _rwyCombo = new ComboBox { Location = new Point(92, y-2), Size = new Size(180, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Runway" };
        panel.Controls.Add(_rwyCombo);
        y += 30;

        AddLabel(panel, "Trans&ition:", 8, y, 80);
        _transCombo = new ComboBox { Location = new Point(92, y-2), Size = new Size(250, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Transition" };
        panel.Controls.Add(_transCombo);
        y += 36;

        var activateBtn = new Button { Location = new Point(92, y), Size = new Size(160, 28), Text = "&Activate selected" };
        activateBtn.Click += ActivateProc_Click;
        panel.Controls.Add(activateBtn);

        panel.Controls.Add(new Label { Location = new Point(8, y+36), Size = new Size(580, 32),
            Text = "Load procedures loads SIDs/STARs/approaches for the airport directly from the FMS." });

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildDirectTab()
    {
        var page  = new TabPage("Direct-To / SimBrief") { AccessibleName = "Direct-to and SimBrief tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        panel.Controls.Add(new Label { Location = new Point(8, y), Size = new Size(560, 20),
            Text = "DIRECT-TO", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;

        AddLabel(panel, "&Waypoint ICAO:", 8, y, 120);
        _directToBox = new TextBox { Location = new Point(130, y-2), Size = new Size(120, 24),
            AccessibleName = "Direct-to waypoint ICAO", PlaceholderText = "e.g. NUGRA",
            MaxLength = 7 };
        _directToBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Return) { ExecuteDirectTo(); e.Handled = e.SuppressKeyPress = true; } };
        panel.Controls.Add(_directToBox);
        y += 30;

        var dtoBtn = new Button { Location = new Point(130, y), Size = new Size(120, 28), Text = "&Direct-To" };
        dtoBtn.Click += (_, _) => ExecuteDirectTo();
        panel.Controls.Add(dtoBtn);
        y += 36;

        var cancelBtn = new Button { Location = new Point(130, y), Size = new Size(140, 28), Text = "&Cancel Direct-To" };
        cancelBtn.Click += (_, _) => { _bridge.SendCommand("cancel_direct_to"); _announcer.AnnounceImmediate("Direct-to cancelled"); };
        panel.Controls.Add(cancelBtn);
        y += 48;

        panel.Controls.Add(new Label { Location = new Point(8, y), Size = new Size(560, 20),
            Text = "SIMBRIEF", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;

        _sbInfoBox = new TextBox { Location = new Point(8, y), Size = new Size(600, 60),
            Multiline = true, ReadOnly = true, AccessibleName = "SimBrief info" };
        panel.Controls.Add(_sbInfoBox);
        y += 68;

        panel.Controls.Add(new Label { Location = new Point(8, y), Size = new Size(560, 34),
            Text = "Reads your latest SimBrief OFP and announces the route. The G1000's own SimBrief button\nloads the route into the FMS (use that after this to set origin/dest)." });
        y += 42;

        var sbBtn = new Button { Location = new Point(8, y), Size = new Size(160, 28), Text = "Load &SimBrief info" };
        sbBtn.Click += async (_, _) => await LoadSimbriefAsync();
        panel.Controls.Add(sbBtn);

        page.Controls.Add(panel);
        return page;
    }

    // ── Flight plan list ──────────────────────────────────────────────────────

    private void FplList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return) return;
        int i = _fplList.SelectedIndex;
        if (i < 0 || _lastFpl == null || i >= _lastFpl.Items.Count) return;
        var leg = _lastFpl.Items[i];
        _bridge.SendCommand("activate_leg", new { index = leg.Index });

        // Tell the user what happened and whether the autopilot will actually follow
        string msg = $"Direct to {leg.Ident} activated in FMS.";
        if (!_gpsDrivesNav)
            msg += " Warning: GPS is NOT driving NAV 1 — enable it on the Nav tab so the autopilot follows.";
        else if (_lastNav?.GpsDrivesNav == true)
            msg += " GPS drives NAV 1 is ON — autopilot will follow if NAV mode is engaged.";
        _announcer.AnnounceImmediate(msg);
        e.Handled = e.SuppressKeyPress = true;
    }

    // ── Procedures ────────────────────────────────────────────────────────────

    private void LoadProcedures_Click(object? sender, EventArgs e)
    {
        string icao = _procAirportBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter an airport ICAO"); return; }
        string slot = _procTypeCombo.SelectedIndex == 2 ? "dep" : "arr";
        _announcer.AnnounceImmediate($"Loading procedures for {icao}, please wait");
        _bridge.SendCommand("load_procedures_list", new { ident = icao, slot });
    }

    private void PopulateProcList()
    {
        _procList.Items.Clear();
        if (_lastFacility == null) return;
        int type = _procTypeCombo.SelectedIndex; // 0=approach 1=star 2=sid
        var list = type == 0 ? _lastFacility.Approaches
                 : type == 1 ? _lastFacility.Arrivals
                 :              _lastFacility.Departures;
        foreach (var p in list)
            _procList.Items.Add($"[{p.Index}] {p.Name}{(string.IsNullOrWhiteSpace(p.Runway) ? "" : "  rwy " + p.Runway)}");
        if (_procList.Items.Count > 0) _procList.SelectedIndex = 0;
    }

    private void ProcList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_lastFacility == null || _procList.SelectedIndex < 0) return;
        int type = _procTypeCombo.SelectedIndex;
        var list = type == 0 ? _lastFacility.Approaches
                 : type == 1 ? _lastFacility.Arrivals
                 :              _lastFacility.Departures;
        int i = _procList.SelectedIndex;
        if (i >= list.Count) return;
        var proc = list[i];

        _rwyCombo.Items.Clear();
        _rwyCombo.Items.Add("Any runway");
        foreach (var (_, nm) in proc.Runways) _rwyCombo.Items.Add(nm);
        if (_rwyCombo.Items.Count > 0) _rwyCombo.SelectedIndex = 0;

        _transCombo.Items.Clear();
        _transCombo.Items.Add("No transition (vectors)");
        foreach (var (_, nm) in proc.Transitions) _transCombo.Items.Add(nm);
        if (_transCombo.Items.Count > 0) _transCombo.SelectedIndex = 0;
    }

    private void ActivateProc_Click(object? sender, EventArgs e)
    {
        if (_lastFacility == null || _procList.SelectedIndex < 0)
        { _announcer.AnnounceImmediate("Load procedures first"); return; }

        int type = _procTypeCombo.SelectedIndex;
        var list = type == 0 ? _lastFacility.Approaches
                 : type == 1 ? _lastFacility.Arrivals
                 :              _lastFacility.Departures;
        int pi = _procList.SelectedIndex;
        if (pi >= list.Count) return;
        var proc = list[pi];

        int rwyIdx   = _rwyCombo.SelectedIndex <= 0 ? -1 : _rwyCombo.SelectedIndex - 1;
        int transIdx = _transCombo.SelectedIndex <= 0 ? -1 : _transCombo.SelectedIndex - 1;
        string slot  = _lastFacility.Slot;

        string cmd = type == 0 ? "insert_approach" : type == 1 ? "insert_arrival" : "insert_departure";
        _bridge.SendCommand(cmd, new
        {
            slot,
            approachIndex   = proc.Index,
            arrivalIndex    = proc.Index,
            departureIndex  = proc.Index,
            transitionIndex = transIdx,
            runwayIndex     = rwyIdx
        });
        _announcer.AnnounceImmediate($"Activating {proc.Name}");
    }

    // ── Direct-to ─────────────────────────────────────────────────────────────

    private void ExecuteDirectTo()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident)) { _announcer.AnnounceImmediate("Enter a waypoint ICAO"); return; }
        _bridge.SendCommand("create_direct_to", new { ident });
        _announcer.AnnounceImmediate($"Direct to {ident}");
        InvokeUI(() => _directToBox.Clear());
    }

    // ── SimBrief ──────────────────────────────────────────────────────────────

    private async Task LoadSimbriefAsync()
    {
        if (string.IsNullOrWhiteSpace(_simbriefUsername))
        { _announcer.AnnounceImmediate("No SimBrief username — go to File, SimBrief Settings"); return; }

        _announcer.AnnounceImmediate("Fetching SimBrief plan, please wait");
        try
        {
            var svc = new SimBriefService();
            var ofp = await svc.FetchFullOFPAsync(_simbriefUsername);
            string info =
                $"Route: {ofp.OriginIcao} → {ofp.DestIcao}\n" +
                $"Callsign: {ofp.Callsign}\n" +
                $"Cruise: FL{ofp.InitialAltitude}  {ofp.CruiseMach} M\n" +
                (string.IsNullOrWhiteSpace(ofp.OriginSid)  ? "" : $"SID: {ofp.OriginSid}\n") +
                (string.IsNullOrWhiteSpace(ofp.DestStar)   ? "" : $"STAR: {ofp.DestStar}\n") +
                (string.IsNullOrWhiteSpace(ofp.DestApproach) ? "" : $"Approach: {ofp.DestApproach}\n") +
                $"Route: {ofp.Route}";
            InvokeUI(() => _sbInfoBox.Text = info);
            _announcer.AnnounceImmediate(
                $"SimBrief: {ofp.OriginIcao} to {ofp.DestIcao}" +
                (string.IsNullOrWhiteSpace(ofp.OriginSid) ? "" : $", SID {ofp.OriginSid}") +
                (string.IsNullOrWhiteSpace(ofp.DestStar)  ? "" : $", STAR {ofp.DestStar}") +
                (string.IsNullOrWhiteSpace(ofp.DestApproach) ? "" : $", approach {ofp.DestApproach}") +
                ". Use the G1000 SimBrief button to load the route into the FMS.");
        }
        catch (Exception ex) { _announcer.AnnounceImmediate($"SimBrief error: {ex.Message}"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string text) => InvokeUI(() => _statusBox.Text = text);

    private void InvokeUI(Action a)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(a);
        else if (IsHandleCreated) a();
    }

    private static void AddLabel(Panel p, string text, int x, int y, int w) =>
        p.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(w, 20) });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridge.FplStateReceived     -= OnFplState;
            _bridge.NavStateReceived     -= OnNavState;
            _bridge.FacilityDataReceived -= OnFacilityData;
            _bridge.CommandAck           -= OnCommandAck;
            _bridge.CommandError         -= OnCommandError;
            _bridge.BridgeConnected      -= OnBridgeConnected;
        }
        base.Dispose(disposing);
    }
}
