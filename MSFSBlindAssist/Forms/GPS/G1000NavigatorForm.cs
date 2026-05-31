using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using System.Text;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible G1000 NXi FMS window — no bridge, no Community package.
/// Uses direct Coherent GT WebSocket (same approach as the A380 tools).
/// Requires MSFS Developer Mode ON. No sim restart, no injection.
///
/// Hotkey: Input mode [ then Shift+M
/// F5 = reconnect/refresh  |  Escape = close
/// </summary>
public sealed class G1000NavigatorForm : Form
{
    private readonly G1000FmsClient _fms;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _simbriefUsername;

    private G1000FplState?  _lastFpl;
    private G1000NavState?  _lastNav;
    private G1000FacilityData? _lastFacility;
    private string _lastActiveIdent = "";
    private bool   _gpsDrivesNav;

    // ── UI ────────────────────────────────────────────────────────────────────
    private TextBox    _statusBox    = null!;
    private TabControl _tabs         = null!;
    private TextBox    _navBox       = null!;
    private Button     _gpsNavBtn    = null!;
    private ListBox    _fplList      = null!;
    private TextBox    _fplInfoBox   = null!;
    private TextBox    _procAirport  = null!;
    private ComboBox   _procType     = null!;
    private ListBox    _procList     = null!;
    private ComboBox   _rwyCombo     = null!;
    private ComboBox   _transCombo   = null!;
    private TextBox    _directToBox  = null!;
    private TextBox    _sbBox        = null!;

    public G1000NavigatorForm(G1000FmsClient fms, ScreenReaderAnnouncer announcer,
        string simbriefUsername = "")
    {
        _fms              = fms;
        _announcer        = announcer;
        _simbriefUsername = simbriefUsername;

        _fms.Connected    += (_, _) =>
        {
            SetStatus($"Connected — {_fms.PageTitle}  •  F5 to refresh");
            _announcer.AnnounceImmediate("G1000 connected. Flight plan loading.");
        };
        _fms.Disconnected += (_, _) => SetStatus("Disconnected — press F5 to reconnect");
        _fms.FplUpdated   += OnFpl;
        _fms.NavUpdated   += OnNav;

        BuildUi();

        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        KeyDown     += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)     { _ = ConnectAndRefreshAsync(); e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
        };
    }

    // ── Events from FMS client ────────────────────────────────────────────────

    private void OnFpl(object? sender, G1000FplState state)
    {
        _lastFpl = state;
        var active = state.Legs.FirstOrDefault(l => l.Active);
        if (active != null && active.Ident != _lastActiveIdent)
        {
            _lastActiveIdent = active.Ident;
            string dist = active.Dist > 0.05 ? $"  {active.Dist:F1} nm" : "";
            _announcer.AnnounceImmediate($"Active: {active.Ident}{dist}");
        }

        InvokeUI(() =>
        {
            int sel = _fplList.SelectedIndex;
            _fplList.Items.Clear();
            foreach (var l in state.Legs)
                _fplList.Items.Add((l.Active ? "▶ " : "  ") + l.DisplayText);
            if (sel >= 0 && sel < _fplList.Items.Count)
                _fplList.SelectedIndex = sel;
            else if (state.ActiveLeg >= 0 && state.ActiveLeg < _fplList.Items.Count)
                _fplList.SelectedIndex = state.ActiveLeg;

            _fplInfoBox.Text = (string.IsNullOrWhiteSpace(state.Origin) && string.IsNullOrWhiteSpace(state.Dest))
                ? $"{state.Legs.Count} legs"
                : $"{(state.Origin.Length > 0 ? state.Origin : "----")} → {(state.Dest.Length > 0 ? state.Dest : "----")}  ({state.Legs.Count} legs)";

            if (!string.IsNullOrWhiteSpace(state.Dest) && string.IsNullOrWhiteSpace(_procAirport.Text))
                _procAirport.Text = state.Dest;
        });
    }

    private void OnNav(object? sender, G1000NavState nav)
    {
        _lastNav = nav;
        bool gpsChanged = _gpsDrivesNav != nav.GpsDrivesNav;
        _gpsDrivesNav = nav.GpsDrivesNav;
        if (gpsChanged)
            _announcer.AnnounceImmediate(nav.GpsDrivesNav ? "GPS drives NAV 1: ON" : "GPS drives NAV 1: OFF");

        InvokeUI(() =>
        {
            _gpsNavBtn.Text = nav.GpsDrivesNav
                ? "GPS → NAV1: ON  (click OFF)"
                : "GPS → NAV1: OFF  (click ON  ← needed for autopilot to follow)";
            _gpsNavBtn.BackColor = nav.GpsDrivesNav ? System.Drawing.Color.DarkGreen : System.Drawing.Color.DarkRed;
            _gpsNavBtn.ForeColor = System.Drawing.Color.White;

            var active = _lastFpl?.Legs.FirstOrDefault(l => l.Active);
            var sb = new StringBuilder();
            sb.AppendLine($"Waypoint : {active?.Ident ?? "---"}");
            if (nav.Dist > 0.01) sb.AppendLine($"Distance : {nav.Dist:F2} nm");
            if (nav.Brg > 0)     sb.AppendLine($"Bearing  : {nav.Brg}°");
            if (nav.Ete > 0)     sb.AppendLine($"ETE      : {nav.EteFormatted}");
            if (nav.Dtk > 0)     sb.AppendLine($"Track    : {nav.Dtk}°");
            if (Math.Abs(nav.Xtk) > 0.01) sb.AppendLine($"XTK      : {Math.Abs(nav.Xtk):F2} nm {(nav.Xtk < 0 ? "L" : "R")}");
            if (nav.Gs > 1)      sb.AppendLine($"GS       : {nav.Gs} kts");
            sb.AppendLine();
            sb.AppendLine($"Approach : {nav.ApproachModeText}{(nav.ApprLoaded ? " (loaded)" : "")}{(nav.ApprActive ? " (active)" : "")}");
            sb.AppendLine($"Direct-To: {(nav.IsDirectTo ? "YES" : "no")}");
            _navBox.Text = sb.ToString().TrimEnd();
        });
    }

    // ── Connect / refresh ─────────────────────────────────────────────────────

    public async Task ConnectAndRefreshAsync()
    {
        if (_fms.IsConnected) return;
        SetStatus("Connecting to G1000 MFD via dev mode…");
        bool ok = await _fms.TryConnectAsync();
        if (!ok)
        {
            var pages = await G1000FmsClient.ListPagesAsync();
            string found = pages.Count == 0
                ? "Dev mode not running — enable in MSFS Options → General → Developers"
                : "G1000 MFD not found. Visible pages: " + string.Join(", ",
                    pages.Where(p => !string.IsNullOrWhiteSpace(p.title)).Select(p => p.title).Take(5));
            SetStatus(found);
            _announcer.AnnounceImmediate(found);
        }
    }

    public void EnsureVisible()
    {
        if (!_fms.IsConnected) _ = ConnectAndRefreshAsync();
    }

    // ── FPL list actions ──────────────────────────────────────────────────────

    private void FplList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return) return;
        int i = _fplList.SelectedIndex;
        if (i < 0 || _lastFpl == null || i >= _lastFpl.Legs.Count) return;
        var leg = _lastFpl.Legs[i];
        _ = DirectToAndFollowAsync(leg);
        e.Handled = e.SuppressKeyPress = true;
    }

    /// <summary>
    /// The main "go there" action — works for any waypoint in the FPL, including ones
    /// before the current active leg (turn around / go back). Uses createDirectToRandom
    /// which works in any direction, then immediately engages GPS→NAV1 + NAV mode so
    /// the autopilot starts turning straight away. One keypress does everything.
    /// </summary>
    private async Task DirectToAndFollowAsync(G1000FplLeg leg)
    {
        _announcer.AnnounceImmediate($"Going direct to {leg.Ident}…");

        // Prefer createDirectToExisting (uses FMS segment indices, reliable for any direction)
        // Fall back to createDirectToRandom if segment info not available
        bool ok = leg.HasSegmentInfo
            ? await _fms.DirectToExistingAsync(leg.SegIdx, leg.SegLeg)
            : await _fms.DirectToAsync(leg.Ident);

        if (!ok)
        {
            // Last resort: try by ident even if primary failed
            ok = await _fms.DirectToAsync(leg.Ident);
        }

        if (!ok)
        {
            _announcer.AnnounceImmediate($"Direct-to {leg.Ident} failed. Try the Direct-To tab instead.");
            return;
        }

        // Give the G1000 FMS time to process the direct-to and stabilise
        // before we try to engage NAV mode (too early = AP state in flux)
        await Task.Delay(1200);

        // Engage GPS→NAV1 + NAV mode so the autopilot starts turning
        string followResult = await _fms.FollowGpsPlanAsync();
        _announcer.AnnounceImmediate($"Direct to {leg.Ident}. {followResult}");
    }

    // ── Procedures ────────────────────────────────────────────────────────────

    private async void LoadProcs_Click(object? sender, EventArgs e)
    {
        string icao = _procAirport.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter airport ICAO"); return; }
        string slot = _procType.SelectedIndex == 2 ? "dep" : "arr";
        _announcer.AnnounceImmediate($"Loading {icao} procedures…");
        var fac = await _fms.LoadAirportAsync(icao, slot);
        if (fac == null) { _announcer.AnnounceImmediate($"Could not load {icao}"); return; }
        _lastFacility = fac;
        InvokeUI(() => { PopulateProcList(); });
        _announcer.AnnounceImmediate(
            $"{fac.Name}: {fac.Departures.Count} SIDs, {fac.Arrivals.Count} STARs, {fac.Approaches.Count} approaches");
    }

    private void PopulateProcList()
    {
        _procList.Items.Clear();
        if (_lastFacility == null) return;
        var list = _procType.SelectedIndex == 0 ? _lastFacility.Approaches
                 : _procType.SelectedIndex == 1 ? _lastFacility.Arrivals
                 :                                _lastFacility.Departures;
        foreach (var p in list) _procList.Items.Add($"[{p.Index}] {p.Name}");
        if (_procList.Items.Count > 0) _procList.SelectedIndex = 0;
    }

    private void ProcList_Changed(object? sender, EventArgs e)
    {
        if (_lastFacility == null || _procList.SelectedIndex < 0) return;
        var list = _procType.SelectedIndex == 0 ? _lastFacility.Approaches
                 : _procType.SelectedIndex == 1 ? _lastFacility.Arrivals
                 :                                _lastFacility.Departures;
        int i = _procList.SelectedIndex;
        if (i >= list.Count) return;
        var proc = list[i];
        _rwyCombo.Items.Clear(); _rwyCombo.Items.Add("Any runway");
        foreach (var (_, n) in proc.Runways) _rwyCombo.Items.Add(n);
        if (_rwyCombo.Items.Count > 0) _rwyCombo.SelectedIndex = 0;
        _transCombo.Items.Clear(); _transCombo.Items.Add("No transition (vectors)");
        foreach (var (_, n) in proc.Transitions) _transCombo.Items.Add(n);
        if (_transCombo.Items.Count > 0) _transCombo.SelectedIndex = 0;
    }

    private async void ActivateProc_Click(object? sender, EventArgs e)
    {
        if (_lastFacility == null || _procList.SelectedIndex < 0)
        { _announcer.AnnounceImmediate("Load procedures first"); return; }
        var list = _procType.SelectedIndex == 0 ? _lastFacility.Approaches
                 : _procType.SelectedIndex == 1 ? _lastFacility.Arrivals
                 :                                _lastFacility.Departures;
        int pi = _procList.SelectedIndex;
        if (pi >= list.Count) return;
        var proc = list[pi];
        int rwyIdx   = _rwyCombo.SelectedIndex   <= 0 ? -1 : _rwyCombo.SelectedIndex - 1;
        int transIdx = _transCombo.SelectedIndex <= 0 ? -1 : _transCombo.SelectedIndex - 1;
        string slot  = _lastFacility.Slot;

        _announcer.AnnounceImmediate($"Activating {proc.Name}…");
        bool ok = _procType.SelectedIndex == 0
            ? await _fms.InsertApproachAsync(slot, proc.Index, transIdx)
            : _procType.SelectedIndex == 1
                ? await _fms.InsertArrivalAsync(slot, proc.Index, rwyIdx, transIdx)
                : await _fms.InsertDepartureAsync(slot, proc.Index, rwyIdx, transIdx);
        _announcer.AnnounceImmediate(ok ? $"{proc.Name} activated" : $"Failed to activate {proc.Name}");
    }

    // ── Direct-to ─────────────────────────────────────────────────────────────

    private async void DirectTo_Execute()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident)) { _announcer.AnnounceImmediate("Enter a waypoint ICAO"); return; }

        _announcer.AnnounceImmediate($"Going direct to {ident}…");
        bool ok = await _fms.DirectToAsync(ident);
        if (!ok) { _announcer.AnnounceImmediate($"Direct-to {ident} failed"); return; }

        InvokeUI(() => _directToBox.Clear());
        await Task.Delay(1200);
        string followResult = await _fms.FollowGpsPlanAsync();
        _announcer.AnnounceImmediate($"Direct to {ident}. {followResult}");
    }

    // ── SimBrief ──────────────────────────────────────────────────────────────

    private async Task LoadSimbriefAsync()
    {
        if (string.IsNullOrWhiteSpace(_simbriefUsername))
        { _announcer.AnnounceImmediate("No SimBrief username — File, SimBrief Settings"); return; }
        _announcer.AnnounceImmediate("Fetching SimBrief plan…");
        try
        {
            var ofp = await new SimBriefService().FetchFullOFPAsync(_simbriefUsername);
            string info =
                $"{ofp.OriginIcao} → {ofp.DestIcao}\n" +
                $"Cruise: FL{ofp.InitialAltitude}  {ofp.CruiseMach}M\n" +
                (string.IsNullOrWhiteSpace(ofp.OriginSid)    ? "" : $"SID: {ofp.OriginSid}\n") +
                (string.IsNullOrWhiteSpace(ofp.DestStar)     ? "" : $"STAR: {ofp.DestStar}\n") +
                (string.IsNullOrWhiteSpace(ofp.DestApproach) ? "" : $"Approach: {ofp.DestApproach}\n") +
                $"Route: {ofp.Route}";
            InvokeUI(() => _sbBox.Text = info);
            _announcer.AnnounceImmediate(
                $"{ofp.OriginIcao} to {ofp.DestIcao}" +
                (string.IsNullOrWhiteSpace(ofp.OriginSid) ? "" : $", SID {ofp.OriginSid}") +
                (string.IsNullOrWhiteSpace(ofp.DestStar)  ? "" : $", STAR {ofp.DestStar}") +
                ". Use G1000 SimBrief button to load route into FMS.");
        }
        catch (Exception ex) { _announcer.AnnounceImmediate($"SimBrief error: {ex.Message}"); }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "G1000 FMS Navigator";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(680, 600); MinimumSize = new Size(520, 460);
        KeyPreview = true; ShowInTaskbar = true;

        _statusBox = new TextBox { Dock = DockStyle.Top, Height = 26, ReadOnly = true,
            Text = "Press F5 to connect (dev mode must be ON)", AccessibleName = "G1000 status" };

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

        _navBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Active nav info", Text = "Waiting for data…" };

        // BIG button — the main IFR action
        var followBtn = new Button
        {
            Dock = DockStyle.Top, Height = 40,
            Text = "FOLLOW GPS PLAN  (GPS→NAV1 + KAP140 NAV mode)",
            AccessibleName = "Follow GPS flight plan",
            AccessibleDescription = "Enables GPS drives NAV1 and engages KAP140 NAV mode so autopilot follows the active FPL leg. Smart — reads state first.",
            BackColor = System.Drawing.Color.DarkBlue, ForeColor = System.Drawing.Color.White,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        followBtn.Click += async (_, _) =>
        {
            string result = await _fms.FollowGpsPlanAsync();
            _announcer.AnnounceImmediate(result);
        };

        // Secondary toggle for GPS drives NAV1 alone
        _gpsNavBtn = new Button { Dock = DockStyle.Top, Height = 28,
            Text = "GPS → NAV1: OFF  (toggle only)",
            AccessibleName = "Toggle GPS drives NAV 1",
            BackColor = System.Drawing.Color.DarkRed, ForeColor = System.Drawing.Color.White };
        _gpsNavBtn.Click += async (_, _) =>
        {
            bool newState = await _fms.ToggleGpsDrivesNavAsync();
            _announcer.AnnounceImmediate($"GPS drives NAV 1: {(newState ? "ON" : "OFF")}");
        };

        var refreshBtn = new Button { Dock = DockStyle.Top, Height = 26, Text = "&Reconnect / Refresh (F5)" };
        refreshBtn.Click += (_, _) => { if (!_fms.IsConnected) _ = ConnectAndRefreshAsync(); };

        page.Controls.Add(_navBox);
        page.Controls.Add(_gpsNavBtn);
        page.Controls.Add(followBtn);
        page.Controls.Add(refreshBtn);
        return page;
    }

    private TabPage BuildFplTab()
    {
        var page = new TabPage("Flight Plan") { AccessibleName = "Flight plan tab" };
        _fplInfoBox = new TextBox { Dock = DockStyle.Top, Height = 26, ReadOnly = true, AccessibleName = "Route" };
        _fplList = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Flight plan legs",
            AccessibleDescription = "Press Enter on a leg to activate it (go direct)" };
        _fplList.KeyDown += FplList_KeyDown;
        page.Controls.Add(_fplList);
        page.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 18,
            Text = "Enter = Direct-To + auto-engage NAV mode (works forwards AND backwards)" });
        page.Controls.Add(_fplInfoBox);
        return page;
    }

    private TabPage BuildProcTab()
    {
        var page  = new TabPage("Procedures") { AccessibleName = "Procedures tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        Lbl(panel, "&Airport ICAO:", 8, y, 120);
        _procAirport = new TextBox { Location = new Point(132, y-2), Size = new Size(110, 24),
            AccessibleName = "Airport ICAO", PlaceholderText = "e.g. EGBB" };
        panel.Controls.Add(_procAirport); y += 30;

        Lbl(panel, "Procedure &type:", 8, y, 120);
        _procType = new ComboBox { Location = new Point(132, y-2), Size = new Size(150, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Procedure type" };
        _procType.Items.AddRange(new[] { "Approaches", "STARs", "SIDs" });
        _procType.SelectedIndex = 0;
        _procType.SelectedIndexChanged += (_, _) => PopulateProcList();
        panel.Controls.Add(_procType); y += 30;

        var loadBtn = new Button { Location = new Point(132, y), Size = new Size(130, 28), Text = "&Load procedures" };
        loadBtn.Click += LoadProcs_Click;
        panel.Controls.Add(loadBtn); y += 36;

        _procList = new ListBox { Location = new Point(8, y), Size = new Size(610, 130),
            AccessibleName = "Procedure list" };
        _procList.SelectedIndexChanged += ProcList_Changed;
        panel.Controls.Add(_procList); y += 138;

        Lbl(panel, "&Runway:", 8, y, 75);
        _rwyCombo = new ComboBox { Location = new Point(86, y-2), Size = new Size(200, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Runway" };
        panel.Controls.Add(_rwyCombo); y += 30;

        Lbl(panel, "Trans&ition:", 8, y, 75);
        _transCombo = new ComboBox { Location = new Point(86, y-2), Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Transition" };
        panel.Controls.Add(_transCombo); y += 36;

        var actBtn = new Button { Location = new Point(86, y), Size = new Size(160, 28), Text = "&Activate selected" };
        actBtn.Click += ActivateProc_Click;
        panel.Controls.Add(actBtn);

        page.Controls.Add(panel); return page;
    }

    private TabPage BuildDirectTab()
    {
        var page  = new TabPage("Direct-To / SimBrief") { AccessibleName = "Direct-to and SimBrief tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        panel.Controls.Add(new Label { Location = new Point(8, y), Size = new Size(560, 20),
            Text = "DIRECT-TO", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;
        Lbl(panel, "&Waypoint:", 8, y, 80);
        _directToBox = new TextBox { Location = new Point(90, y-2), Size = new Size(120, 24),
            AccessibleName = "Direct-to ICAO", PlaceholderText = "e.g. NUGRA", MaxLength = 7 };
        _directToBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Return) { DirectTo_Execute(); e.Handled = e.SuppressKeyPress = true; } };
        panel.Controls.Add(_directToBox); y += 30;
        var dtoBtn = new Button { Location = new Point(90, y), Size = new Size(110, 28), Text = "&Direct-To" };
        dtoBtn.Click += (_, _) => DirectTo_Execute();
        panel.Controls.Add(dtoBtn); y += 36;
        var cancelBtn = new Button { Location = new Point(90, y), Size = new Size(140, 28), Text = "&Cancel Direct-To" };
        cancelBtn.Click += async (_, _) =>
        { bool ok = await _fms.CancelDirectToAsync(); _announcer.AnnounceImmediate(ok ? "Direct-to cancelled" : "Cancel failed"); };
        panel.Controls.Add(cancelBtn); y += 48;

        panel.Controls.Add(new Label { Location = new Point(8, y), Size = new Size(560, 20),
            Text = "SIMBRIEF", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;
        _sbBox = new TextBox { Location = new Point(8, y), Size = new Size(600, 68), Multiline = true,
            ReadOnly = true, AccessibleName = "SimBrief info" };
        panel.Controls.Add(_sbBox); y += 76;
        var sbBtn = new Button { Location = new Point(8, y), Size = new Size(150, 28), Text = "Load &SimBrief info" };
        sbBtn.Click += async (_, _) => await LoadSimbriefAsync();
        panel.Controls.Add(sbBtn);

        page.Controls.Add(panel); return page;
    }

    private static void Lbl(Panel p, string t, int x, int y, int w) =>
        p.Controls.Add(new Label { Text = t, Location = new Point(x, y), Size = new Size(w, 20) });

    private void SetStatus(string t) => InvokeUI(() => _statusBox.Text = t);
    private void InvokeUI(Action a) { if (IsHandleCreated && InvokeRequired) BeginInvoke(a); else if (IsHandleCreated) a(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fms.FplUpdated -= OnFpl; _fms.NavUpdated -= OnNav; }
        base.Dispose(disposing);
    }
}
