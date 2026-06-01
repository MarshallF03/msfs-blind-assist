using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible FMS window for the Cessna Citation Longitude (WT G3000/G5000).
/// Direct Coherent GT — requires MSFS Developer Mode ON. No bridge, no restart.
///
/// Mirrors the G1000 navigator but for the richer G5000 FMS: adds per-leg VNAV
/// SPEED constraints (Alt+S) alongside altitude constraints (Alt+A), and reports
/// VNAV / approach / glideslope coupling state.
///
/// Unlike the C172 (KAP140, which needed the GPSS heading-bug hack), the Longitude
/// has a proper Garmin autopilot that follows GPS/NAV (FMS) natively — direct-to a
/// leg and engage NAV, and it turns onto course on its own.
///
/// F5 = reconnect/refresh  |  Escape = close
/// </summary>
public sealed class G5000NavigatorForm : Form
{
    private readonly G5000FmsClient _fms;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _simbriefUsername;

    private G5000FplState?      _lastFpl;
    private G5000NavState?      _lastNav;
    private G5000FacilityData?  _lastFacility;
    private SimBriefOFP?        _lastOfp;
    private string _lastActiveIdent = "";

    // approach/glideslope callout state
    private bool _prevApprHold, _prevGsActive, _gsNotCapturedWarned;

    private TextBox    _statusBox   = null!;
    private TabControl _tabs        = null!;
    private TextBox    _navBox      = null!;
    private ListBox    _fplList     = null!;
    private TextBox    _fplInfoBox  = null!;
    private TextBox    _procAirport = null!;
    private ComboBox   _procType    = null!;
    private ListBox    _procList    = null!;
    private ComboBox   _rwyCombo    = null!;
    private ComboBox   _transCombo  = null!;
    private TextBox    _directToBox = null!;
    private TextBox    _sbBox       = null!;
    private Button     _sbLoadBtn   = null!;

    public G5000NavigatorForm(G5000FmsClient fms, ScreenReaderAnnouncer announcer, string simbriefUsername = "")
    {
        _fms = fms;
        _announcer = announcer;
        _simbriefUsername = simbriefUsername;

        _fms.Connected += (_, _) =>
        {
            SetStatus($"Connected — {_fms.PageTitle}  •  F5 to refresh");
            _announcer.AnnounceImmediate("Longitude FMS connected. Flight plan loading.");
        };
        _fms.Disconnected += (_, _) => SetStatus("Disconnected — press F5 to reconnect");
        _fms.FplUpdated += OnFpl;
        _fms.NavUpdated += OnNav;

        BuildUi();

        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)     { _ = ConnectAndRefreshAsync(); e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = e.SuppressKeyPress = true; }
        };
    }

    public async Task ConnectAndRefreshAsync()
    {
        if (_fms.IsConnected) return;
        SetStatus("Connecting to Longitude MFD via dev mode…");
        bool ok = await _fms.TryConnectAsync();
        if (!ok)
        {
            var pages = await G5000FmsClient.ListPagesAsync();
            string found = pages.Count == 0
                ? "Dev mode not running — enable in MSFS Options → General → Developers"
                : "Longitude MFD not found. Visible pages: " + string.Join(", ",
                    pages.Where(p => !string.IsNullOrWhiteSpace(p.title)).Select(p => p.title).Take(5));
            SetStatus(found);
            _announcer.AnnounceImmediate(found);
        }
    }

    public void EnsureVisible()
    {
        if (!_fms.IsConnected) _ = ConnectAndRefreshAsync();
    }

    // ── Events from FMS client ────────────────────────────────────────────────

    private void OnFpl(object? sender, G5000FplState state)
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
                _fplList.Items.Add((l.Active ? "► " : "  ") + l.DisplayText);
            if (sel >= 0 && sel < _fplList.Items.Count) _fplList.SelectedIndex = sel;
            else if (state.ActiveLeg >= 0 && state.ActiveLeg < _fplList.Items.Count) _fplList.SelectedIndex = state.ActiveLeg;

            _fplInfoBox.Text = (string.IsNullOrWhiteSpace(state.Origin) && string.IsNullOrWhiteSpace(state.Dest))
                ? $"{state.Legs.Count} legs"
                : $"{state.Origin} → {state.Dest}   ({state.Legs.Count} legs)";
        });
    }

    private void OnNav(object? sender, G5000NavState nav)
    {
        _lastNav = nav;

        // Approach / glideslope capture callouts (the Longitude flies real ILS/RNAV).
        if (nav.ApprHold && !_prevApprHold) _announcer.AnnounceImmediate("Approach mode armed");
        if (!nav.ApprHold && _prevApprHold) { _announcer.AnnounceImmediate("Approach mode off"); _gsNotCapturedWarned = false; }
        if (nav.GsActive && !_prevGsActive) _announcer.AnnounceImmediate("Glideslope captured, descending");
        if (!nav.GsActive && _prevGsActive && nav.ApprHold) _announcer.AnnounceImmediate("Glideslope lost");
        _prevApprHold = nav.ApprHold;
        _prevGsActive = nav.GsActive;

        InvokeUI(() =>
        {
            string src = nav.GpsDrivesNav ? "GPS (magenta)" : (nav.NavHasNav ? "NAV/LOC (green)" : "NAV (no signal)");
            var apModes = new List<string>();
            if (nav.NavHold)    apModes.Add("NAV");
            if (nav.HdgHold)    apModes.Add("HDG");
            if (nav.AltHold)    apModes.Add("ALT");
            if (nav.VnavActive) apModes.Add("VNAV");
            if (nav.ApprHold)   apModes.Add("APPR");
            _navBox.Text =
                $"To: {(_lastActiveIdent.Length > 0 ? _lastActiveIdent : "—")}   {nav.Dist:F1} nm   brg {nav.Brg}°\r\n" +
                $"DTK {nav.Dtk}°   XTK {nav.Xtk:F2} nm   GS {nav.Gs} kt   ETE {FmtEte(nav.Ete)}\r\n" +
                $"Autopilot: {(nav.ApMaster ? "ON" : "off")}   modes: {(apModes.Count > 0 ? string.Join(" ", apModes) : "—")}\r\n" +
                $"Selected altitude: {nav.SelAlt} ft\r\n" +
                $"NAV1 source: {src}\r\n" +
                $"Approach: {(nav.ApprActive ? "active" : nav.ApprLoaded ? "loaded" : "—")}   " +
                $"Glideslope: {(nav.GsActive ? "captured" : nav.GsArm ? "armed" : "—")}";
        });
    }

    private static string FmtEte(int sec)
    {
        if (sec <= 0 || sec > 86400) return "—";
        int m = sec / 60, s = sec % 60;
        return m >= 60 ? $"{m / 60}h{m % 60:00}m" : $"{m}:{s:00}";
    }

    // ── Flight plan actions ─────────────────────────────────────────────────

    private void FplList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            int i = _fplList.SelectedIndex;
            if (i < 0 || _lastFpl == null || i >= _lastFpl.Legs.Count) return;
            _ = DirectToLegAsync(_lastFpl.Legs[i]);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            RemoveSelectedWaypoint();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.A && e.Alt)
        {
            PromptAltitudeConstraint();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.S && e.Alt)
        {
            PromptSpeedConstraint();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private async Task DirectToLegAsync(G5000FplLeg leg)
    {
        _announcer.AnnounceImmediate($"Direct to {leg.Ident}…");
        bool ok = await _fms.DirectToLegIndexAsync(leg.Index);
        if (!ok) ok = await _fms.DirectToAsync(leg.Ident);
        _announcer.AnnounceImmediate(ok
            ? $"Direct to {leg.Ident}. Engage NAV mode and the autopilot will turn onto course."
            : $"Direct-to {leg.Ident} failed.");
    }

    private async void RemoveSelectedWaypoint()
    {
        int i = _fplList.SelectedIndex;
        if (i < 0 || _lastFpl == null || i >= _lastFpl.Legs.Count) return;
        var leg = _lastFpl.Legs[i];
        bool ok = await _fms.RemoveWaypointAsync(leg.Index);
        _announcer.AnnounceImmediate(ok ? $"Removed {leg.Ident}" : $"Could not remove {leg.Ident}");
    }

    private void PromptAltitudeConstraint()
    {
        int i = _fplList.SelectedIndex;
        if (i < 0 || _lastFpl == null || i >= _lastFpl.Legs.Count) { _announcer.AnnounceImmediate("Select a waypoint first"); return; }
        var leg = _lastFpl.Legs[i];
        var form = new ValueInputForm(
            $"Altitude constraint for {leg.Ident}", "Altitude in feet (0 to clear)",
            $"Enter a crossing altitude in feet for {leg.Ident}, or 0 to remove the constraint.",
            _announcer,
            input => (int.TryParse(input.Trim(), out int a) && a >= 0 && a <= 60000, "Enter 0 to 60000"));
        form.FormClosed += async (_, _) =>
        {
            if (form.DialogResult != DialogResult.OK || !int.TryParse(form.InputValue.Trim(), out int alt)) return;
            bool ok = await _fms.SetAltitudeConstraintAsync(leg.Index, alt);
            _announcer.AnnounceImmediate(ok
                ? (alt > 0 ? $"{leg.Ident} altitude set to {alt} feet" : $"{leg.Ident} altitude constraint cleared")
                : "Altitude constraint failed");
        };
        form.Show(this);
    }

    private void PromptSpeedConstraint()
    {
        int i = _fplList.SelectedIndex;
        if (i < 0 || _lastFpl == null || i >= _lastFpl.Legs.Count) { _announcer.AnnounceImmediate("Select a waypoint first"); return; }
        var leg = _lastFpl.Legs[i];
        var form = new ValueInputForm(
            $"Speed constraint for {leg.Ident}", "Speed in knots (0 to clear)",
            $"Enter a speed constraint in knots for {leg.Ident}, or 0 to remove it.",
            _announcer,
            input => (int.TryParse(input.Trim(), out int s) && s >= 0 && s <= 400, "Enter 0 to 400"));
        form.FormClosed += async (_, _) =>
        {
            if (form.DialogResult != DialogResult.OK || !int.TryParse(form.InputValue.Trim(), out int kt)) return;
            bool ok = await _fms.SetSpeedConstraintAsync(leg.Index, kt);
            _announcer.AnnounceImmediate(ok
                ? (kt > 0 ? $"{leg.Ident} speed set to {kt} knots" : $"{leg.Ident} speed constraint cleared")
                : "Speed constraint failed");
        };
        form.Show(this);
    }

    // ── Procedures ───────────────────────────────────────────────────────────

    private async void LoadProcs_Click(object? sender, EventArgs e)
    {
        string icao = _procAirport.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(icao)) { _announcer.AnnounceImmediate("Enter airport ICAO"); return; }
        string slot = _procType.SelectedIndex == 2 ? "dep" : "arr";
        _announcer.AnnounceImmediate($"Loading {icao} procedures…");
        var fac = await _fms.LoadAirportAsync(icao, slot);
        if (fac == null) { _announcer.AnnounceImmediate($"Could not load {icao}"); return; }
        _lastFacility = fac;
        InvokeUI(PopulateProcList);
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
        _rwyCombo.SelectedIndex = 0;
        _transCombo.Items.Clear(); _transCombo.Items.Add("No transition (vectors)");
        foreach (var (_, n) in proc.Transitions) _transCombo.Items.Add(n);
        _transCombo.SelectedIndex = 0;
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

        _announcer.AnnounceImmediate($"Loading {proc.Name}…");
        bool ok = _procType.SelectedIndex == 0
            ? await _fms.InsertApproachAsync(slot, proc.Index, transIdx)
            : _procType.SelectedIndex == 1
                ? await _fms.InsertArrivalAsync(slot, proc.Index, rwyIdx, transIdx)
                : await _fms.InsertDepartureAsync(slot, proc.Index, rwyIdx, transIdx);
        _announcer.AnnounceImmediate(ok ? $"{proc.Name} loaded" : $"Failed to load {proc.Name}");
    }

    // ── Direct-to ─────────────────────────────────────────────────────────────

    private async void DirectTo_Execute()
    {
        string ident = _directToBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ident)) { _announcer.AnnounceImmediate("Enter a waypoint ICAO"); return; }
        _announcer.AnnounceImmediate($"Direct to {ident}…");
        bool ok = await _fms.DirectToAsync(ident);
        if (!ok) { _announcer.AnnounceImmediate($"Direct-to {ident} failed"); return; }
        InvokeUI(() => _directToBox.Clear());
        _announcer.AnnounceImmediate($"Direct to {ident}. Engage NAV mode to fly it.");
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
            _lastOfp = ofp;
            InvokeUI(() => _sbLoadBtn.Enabled = true);
            string info =
                $"{ofp.OriginIcao} → {ofp.DestIcao}\r\n" +
                $"Cruise: FL{ofp.InitialAltitude}\r\n" +
                (string.IsNullOrWhiteSpace(ofp.OriginSid)    ? "" : $"SID: {ofp.OriginSid}\r\n") +
                (string.IsNullOrWhiteSpace(ofp.DestStar)     ? "" : $"STAR: {ofp.DestStar}\r\n") +
                (string.IsNullOrWhiteSpace(ofp.DestApproach) ? "" : $"Approach: {ofp.DestApproach}\r\n") +
                $"Route: {ofp.Route}";
            InvokeUI(() => _sbBox.Text = info);
            _announcer.AnnounceImmediate($"{ofp.OriginIcao} to {ofp.DestIcao}. Press Load route into G5000 to build the flight plan.");
        }
        catch (Exception ex) { _announcer.AnnounceImmediate($"SimBrief error: {ex.Message}"); }
    }

    private async Task LoadSimbriefRouteIntoFmsAsync()
    {
        var ofp = _lastOfp;
        if (ofp == null) { _announcer.AnnounceImmediate("Load SimBrief info first."); return; }
        if (string.IsNullOrWhiteSpace(ofp.OriginIcao) || string.IsNullOrWhiteSpace(ofp.DestIcao))
        { _announcer.AnnounceImmediate("SimBrief plan has no origin or destination."); return; }

        var enroute = ofp.NavLog
            .Where(f => !f.IsSidStar
                        && !string.IsNullOrWhiteSpace(f.Ident)
                        && !f.Ident.Equals(ofp.OriginIcao, StringComparison.OrdinalIgnoreCase)
                        && !f.Ident.Equals(ofp.DestIcao, StringComparison.OrdinalIgnoreCase)
                        && !f.Type.Equals("apt", StringComparison.OrdinalIgnoreCase))
            .Select(f => (f.Ident, f.Lat, f.Lon))
            .ToList();

        _sbLoadBtn.Enabled = false;
        _announcer.AnnounceImmediate($"Building {ofp.OriginIcao} to {ofp.DestIcao}, {enroute.Count} waypoints. Please wait.");
        try
        {
            string result = await _fms.LoadSimBriefRouteAsync(ofp.OriginIcao, ofp.DestIcao, enroute);
            _announcer.AnnounceImmediate(result);
        }
        catch (Exception ex) { _announcer.AnnounceImmediate($"Route load error: {ex.Message}"); }
        finally { _sbLoadBtn.Enabled = true; }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "Citation Longitude FMS";
        Size = new System.Drawing.Size(680, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _statusBox = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Height = 24,
            Text = "Press F5 to connect", AccessibleName = "Status" };

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
        var page = new TabPage("Nav") { AccessibleName = "Navigation tab" };
        _navBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            AccessibleName = "Navigation status", ScrollBars = ScrollBars.Vertical };
        page.Controls.Add(_navBox);
        return page;
    }

    private TabPage BuildFplTab()
    {
        var page = new TabPage("Flight Plan") { AccessibleName = "Flight plan tab" };
        var panel = new Panel { Dock = DockStyle.Fill };

        _fplInfoBox = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Height = 24, AccessibleName = "Flight plan summary" };
        _fplList = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Flight plan legs",
            AccessibleDescription = "Enter: direct-to. Delete: remove. Alt+A: altitude. Alt+S: speed." };
        _fplList.KeyDown += FplList_KeyDown;

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight };
        var invertBtn = new Button { Text = "&Invert", AutoSize = true };
        invertBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.InvertPlanAsync() ? "Plan inverted" : "Invert failed");
        var clearBtn = new Button { Text = "&Clear plan", AutoSize = true };
        clearBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.ClearPlanAsync() ? "Plan cleared" : "Clear failed");
        var helpLbl = new Label { Text = "Enter=Direct-to  Del=Remove  Alt+A=Alt  Alt+S=Speed", AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText, Padding = new Padding(8, 8, 0, 0) };
        btnRow.Controls.Add(invertBtn);
        btnRow.Controls.Add(clearBtn);
        btnRow.Controls.Add(helpLbl);

        panel.Controls.Add(_fplList);
        panel.Controls.Add(btnRow);
        panel.Controls.Add(_fplInfoBox);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildProcTab()
    {
        var page = new TabPage("Procedures") { AccessibleName = "Procedures tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        Lbl(panel, "&Airport ICAO:", 8, y, 120);
        _procAirport = new TextBox { Location = new System.Drawing.Point(132, y - 2), Size = new System.Drawing.Size(110, 24),
            AccessibleName = "Airport ICAO", PlaceholderText = "e.g. EKCH" };
        panel.Controls.Add(_procAirport); y += 30;

        Lbl(panel, "Procedure &type:", 8, y, 120);
        _procType = new ComboBox { Location = new System.Drawing.Point(132, y - 2), Size = new System.Drawing.Size(150, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Procedure type" };
        _procType.Items.AddRange(new[] { "Approaches", "STARs", "SIDs" });
        _procType.SelectedIndex = 0;
        _procType.SelectedIndexChanged += (_, _) => PopulateProcList();
        panel.Controls.Add(_procType); y += 30;

        var loadBtn = new Button { Location = new System.Drawing.Point(132, y), Size = new System.Drawing.Size(130, 28), Text = "&Load procedures" };
        loadBtn.Click += LoadProcs_Click;
        panel.Controls.Add(loadBtn); y += 36;

        _procList = new ListBox { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(630, 120), AccessibleName = "Procedure list" };
        _procList.SelectedIndexChanged += ProcList_Changed;
        panel.Controls.Add(_procList); y += 128;

        Lbl(panel, "&Runway:", 8, y, 75);
        _rwyCombo = new ComboBox { Location = new System.Drawing.Point(86, y - 2), Size = new System.Drawing.Size(200, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Runway" };
        panel.Controls.Add(_rwyCombo); y += 30;

        Lbl(panel, "Trans&ition:", 8, y, 75);
        _transCombo = new ComboBox { Location = new System.Drawing.Point(86, y - 2), Size = new System.Drawing.Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Transition" };
        panel.Controls.Add(_transCombo); y += 36;

        var actBtn = new Button { Location = new System.Drawing.Point(86, y), Size = new System.Drawing.Size(200, 28), Text = "Load &selected procedure" };
        actBtn.Click += ActivateProc_Click;
        panel.Controls.Add(actBtn); y += 38;

        Lbl(panel, "Approach actions:", 8, y, 200); y += 24;
        var apprBtn = new Button { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(190, 28), Text = "Acti&vate Approach" };
        apprBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.ActivateApproachAsync() ? "Approach activated" : "Activate approach failed");
        panel.Controls.Add(apprBtn);
        var vtfBtn = new Button { Location = new System.Drawing.Point(206, y), Size = new System.Drawing.Size(190, 28), Text = "Vectors To &Final" };
        vtfBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.ActivateVtfAsync() ? "Vectors to final activated" : "Vectors to final failed");
        panel.Controls.Add(vtfBtn); y += 34;
        var missedBtn = new Button { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(190, 28), Text = "Activate &Missed Approach" };
        missedBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.ActivateMissedApproachAsync() ? "Missed approach activated" : "Missed approach failed");
        panel.Controls.Add(missedBtn);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildDirectTab()
    {
        var page = new TabPage("Direct-To / SimBrief") { AccessibleName = "Direct-to and SimBrief tab" };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 8;

        panel.Controls.Add(new Label { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(560, 20),
            Text = "DIRECT-TO", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;
        Lbl(panel, "&Waypoint:", 8, y, 80);
        _directToBox = new TextBox { Location = new System.Drawing.Point(90, y - 2), Size = new System.Drawing.Size(120, 24),
            AccessibleName = "Direct-to ICAO", PlaceholderText = "e.g. GASKO", MaxLength = 7 };
        _directToBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Return) { DirectTo_Execute(); e.Handled = e.SuppressKeyPress = true; } };
        panel.Controls.Add(_directToBox); y += 30;
        var dtoBtn = new Button { Location = new System.Drawing.Point(90, y), Size = new System.Drawing.Size(110, 28), Text = "&Direct-To" };
        dtoBtn.Click += (_, _) => DirectTo_Execute();
        panel.Controls.Add(dtoBtn); y += 36;
        var cancelBtn = new Button { Location = new System.Drawing.Point(90, y), Size = new System.Drawing.Size(140, 28), Text = "&Cancel Direct-To" };
        cancelBtn.Click += async (_, _) => _announcer.AnnounceImmediate(await _fms.CancelDirectToAsync() ? "Direct-to cancelled" : "Cancel failed");
        panel.Controls.Add(cancelBtn); y += 48;

        panel.Controls.Add(new Label { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(560, 20),
            Text = "SIMBRIEF", Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) });
        y += 24;
        _sbBox = new TextBox { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(630, 68), Multiline = true,
            ReadOnly = true, AccessibleName = "SimBrief info" };
        panel.Controls.Add(_sbBox); y += 76;
        var sbBtn = new Button { Location = new System.Drawing.Point(8, y), Size = new System.Drawing.Size(150, 28), Text = "Load &SimBrief info" };
        sbBtn.Click += async (_, _) => await LoadSimbriefAsync();
        panel.Controls.Add(sbBtn);
        _sbLoadBtn = new Button { Location = new System.Drawing.Point(166, y), Size = new System.Drawing.Size(190, 28),
            Text = "Load &route into G5000", Enabled = false, AccessibleName = "Load SimBrief route into G5000 flight plan" };
        _sbLoadBtn.Click += async (_, _) => await LoadSimbriefRouteIntoFmsAsync();
        panel.Controls.Add(_sbLoadBtn);

        page.Controls.Add(panel);
        return page;
    }

    private static void Lbl(Panel p, string t, int x, int y, int w) =>
        p.Controls.Add(new Label { Text = t, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, 20) });

    private void SetStatus(string t) => InvokeUI(() => _statusBox.Text = t);
    private void InvokeUI(Action a) { if (IsHandleCreated && InvokeRequired) BeginInvoke(a); else if (IsHandleCreated) a(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fms.FplUpdated -= OnFpl; _fms.NavUpdated -= OnNav; }
        base.Dispose(disposing);
    }
}
