using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible mirror of the Citation Longitude's GTC touchscreen (approach A).
/// Drives the REAL avionics via G5000GtcClient — navigating/pressing here switches
/// and edits the in-sim GTC pages live (on GTC 2, the MFD-mode touchscreen).
///
/// Go-to-page combo jumps to any FMS/systems page; the list shows that page's live
/// controls (with ON/Off state); Enter presses the selected control (which may open
/// a sub-page — the list then re-reads to the new page automatically).
///
/// F5 refresh · Backspace = Back · Escape = close
/// </summary>
public sealed class G5000GtcForm : Form
{
    private readonly G5000GtcClient _gtc;
    private readonly ScreenReaderAnnouncer _announcer;

    private TextBox  _status   = null!;
    private ComboBox _pageJump = null!;
    private ListBox  _controls = null!;
    private G5000GtcPage? _page;
    private bool _busy;
    private bool _suppressJump;

    // Curated jump targets (real GTC view keys → friendly labels).
    private static readonly (string Key, string Label)[] Pages =
    {
        ("MfdHome", "Home"),
        ("FlightPlan", "Flight Plan"),
        ("DirectTo", "Direct-To"),
        ("Procedures", "Procedures"),
        ("Departure", "Departure (SID)"),
        ("Arrival", "Arrival (STAR)"),
        ("Approach", "Approach"),
        ("Hold", "Hold"),
        ("AdvancedVnavProfile", "VNAV Profile"),
        ("Perf", "Performance"),
        ("TakeoffData", "Takeoff Data"),
        ("LandingData", "Landing Data"),
        ("WeightAndFuel", "Weight & Fuel"),
        ("SpeedBugs", "Speed Bugs"),
        ("FlapSpeeds", "Flap Speeds"),
        ("WaypointInfo", "Waypoint Info"),
        ("AirportInfo", "Airport Info"),
        ("Nearest", "Nearest"),
        ("AircraftSystems", "Aircraft Systems"),
        ("ExteriorLights", "Exterior Lights"),
        ("CabinPressure", "Cabin Pressure"),
        ("Propulsion", "Propulsion"),
        ("Temp", "Temperature / ECS"),
        ("AvionicsSettings", "Avionics Settings"),
        ("Setup", "Setup"),
        ("Charts", "Charts"),
        ("SimBrief", "SimBrief"),
        ("Utilities", "Utilities"),
        ("Timer", "Timer"),
    };

    // 787-style Alt+letter quick jumps to GTC pages.
    private static readonly Dictionary<Keys, (string Key, string Label)> AltShortcuts = new()
    {
        [Keys.F] = ("FlightPlan", "Flight Plan"),
        [Keys.I] = ("Initialization", "Initialization"),
        [Keys.D] = ("DirectTo", "Direct-To"),
        [Keys.R] = ("Procedures", "Procedures"),
        [Keys.V] = ("AdvancedVnavProfile", "VNAV Profile"),
        [Keys.P] = ("Perf", "Performance"),
        [Keys.H] = ("Hold", "Hold"),
        [Keys.N] = ("Nearest", "Nearest"),
        [Keys.W] = ("WaypointInfo", "Waypoint Info"),
        [Keys.Y] = ("AircraftSystems", "Aircraft Systems"),
        [Keys.T] = ("TakeoffData", "Takeoff Data"),
        [Keys.L] = ("LandingData", "Landing Data"),
        [Keys.E] = ("WeightAndFuel", "Weight and Fuel"),
        [Keys.C] = ("Charts", "Charts"),
        [Keys.B] = ("SimBrief", "SimBrief"),
        [Keys.M] = ("MfdHome", "Home"),
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Alt) == Keys.Alt && (keyData & Keys.Control) == 0 && (keyData & Keys.Shift) == 0)
        {
            if (AltShortcuts.TryGetValue(keyData & Keys.KeyCode, out var t)) { JumpToKey(t.Key, t.Label); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void JumpToKey(string key, string label)
    {
        _announcer.AnnounceImmediate($"Opening {label}");
        if (await _gtc.NavigateAsync(key)) { await Task.Delay(450); await RefreshAsync(announce: true); }
        else _announcer.AnnounceImmediate($"{label} not available from here");
    }

    public G5000GtcForm(G5000GtcClient gtc, ScreenReaderAnnouncer announcer)
    {
        _gtc = gtc;
        _announcer = announcer;
        BuildUi();

        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5)        { e.Handled = e.SuppressKeyPress = true; await RefreshAsync(announce: true); }
            else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; Hide(); }
            else if (e.KeyCode == Keys.Back && !_controls.Focused)
            { e.Handled = e.SuppressKeyPress = true; await BackAsync(); }
        };
    }

    public async Task ConnectAndRefreshAsync()
    {
        SetStatus("Connecting to Longitude GTC…");
        if (!await _gtc.TryConnectAsync())
        {
            var pages = await G5000GtcClient.ListPagesAsync();
            string msg = pages.Count == 0
                ? "Dev mode not running — enable in MSFS Options → General → Developers"
                : "GTC page not found. Visible: " + string.Join(", ",
                    pages.Where(p => !string.IsNullOrWhiteSpace(p.title)).Select(p => p.title).Take(5));
            SetStatus(msg);
            _announcer.AnnounceImmediate(msg);
            return;
        }
        await _gtc.EnsureMfdModeAsync();
        await RefreshAsync(announce: true);
    }

    public void EnsureVisible() => _ = ConnectAndRefreshAsync();

    // ── Actions ────────────────────────────────────────────────────────────

    private async Task RefreshAsync(bool announce)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var page = await _gtc.ReadActivePageAsync();
            _page = page;
            InvokeUI(() =>
            {
                int sel = _controls.SelectedIndex;
                _controls.Items.Clear();
                if (page != null)
                    foreach (var c in page.Controls) _controls.Items.Add(c.DisplayText);
                if (sel >= 0 && sel < _controls.Items.Count) _controls.SelectedIndex = sel;
                else if (_controls.Items.Count > 0) _controls.SelectedIndex = 0;
                SetStatus(page == null
                    ? "Could not read page — press F5"
                    : $"Page: {Friendly(page.Key)}   ({page.Controls.Count} controls)   F5 refresh · Backspace back");
            });
            if (announce && page != null)
                _announcer.AnnounceImmediate($"{Friendly(page.Key)}, {page.Controls.Count} controls");
        }
        finally { _busy = false; }
    }

    private async void JumpToPage()
    {
        if (_suppressJump || _pageJump.SelectedIndex < 0) return;
        var (key, label) = Pages[_pageJump.SelectedIndex];
        _announcer.AnnounceImmediate($"Opening {label}");
        if (await _gtc.NavigateAsync(key))
        {
            await Task.Delay(450);
            await RefreshAsync(announce: true);
        }
        else _announcer.AnnounceImmediate($"{label} not available on this page");
    }

    private async void PressSelected()
    {
        int i = _controls.SelectedIndex;
        if (_page == null || i < 0 || i >= _page.Controls.Count) return;
        var ctrl = _page.Controls[i];
        _announcer.AnnounceImmediate($"Pressing {ctrl.Text}");
        bool ok = await _gtc.PressByTextAsync(ctrl.Text);
        if (!ok) { _announcer.AnnounceImmediate("Press failed"); return; }
        await Task.Delay(500);            // let the GTC react (value change or sub-page)
        await RefreshAsync(announce: true);
    }

    private async Task BackAsync()
    {
        _announcer.AnnounceImmediate("Back");
        await _gtc.GoBackAsync();
        await Task.Delay(450);
        await RefreshAsync(announce: true);
    }

    private async Task HomeAsync()
    {
        _announcer.AnnounceImmediate("Home");
        await _gtc.GoHomeAsync();
        await Task.Delay(450);
        await RefreshAsync(announce: true);
    }

    private static string Friendly(string key)
    {
        foreach (var (k, l) in Pages) if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return l;
        return key;
    }

    // ── UI ──────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "Longitude GTC (live touchscreen)";
        Size = new System.Drawing.Size(560, 620);
        StartPosition = FormStartPosition.CenterScreen;

        _status = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Height = 24,
            Text = "Press F5 to connect", AccessibleName = "GTC status" };

        var jumpPanel = new Panel { Dock = DockStyle.Top, Height = 30 };
        jumpPanel.Controls.Add(new Label { Text = "&Go to page:", Location = new System.Drawing.Point(4, 6), Size = new System.Drawing.Size(80, 20) });
        _pageJump = new ComboBox { Location = new System.Drawing.Point(88, 3), Size = new System.Drawing.Size(220, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Go to GTC page" };
        foreach (var (_, label) in Pages) _pageJump.Items.Add(label);
        _pageJump.SelectedIndexChanged += (_, _) => JumpToPage();
        jumpPanel.Controls.Add(_pageJump);

        _controls = new ListBox { Dock = DockStyle.Fill, AccessibleName = "GTC page controls",
            AccessibleDescription = "Enter to press the selected control" };
        _controls.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return) { e.Handled = e.SuppressKeyPress = true; PressSelected(); }
            else if (e.KeyCode == Keys.Back) { e.Handled = e.SuppressKeyPress = true; _ = BackAsync(); }
        };

        // No &mnemonics here — Alt+letters are reserved for page jumps (ProcessCmdKey).
        // Use Backspace (back) and F5 (refresh) for these.
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var backBtn = new Button { Text = "Back (Backspace)", AutoSize = true };
        backBtn.Click += async (_, _) => await BackAsync();
        var homeBtn = new Button { Text = "Home page", AutoSize = true };
        homeBtn.Click += async (_, _) => await HomeAsync();
        var refreshBtn = new Button { Text = "Refresh (F5)", AutoSize = true };
        refreshBtn.Click += async (_, _) => await RefreshAsync(announce: true);
        btnRow.Controls.Add(backBtn);
        btnRow.Controls.Add(homeBtn);
        btnRow.Controls.Add(refreshBtn);

        Controls.Add(_controls);
        Controls.Add(btnRow);
        Controls.Add(jumpPanel);
        Controls.Add(_status);
    }

    private void SetStatus(string t) => InvokeUI(() => _status.Text = t);
    private void InvokeUI(Action a) { if (IsHandleCreated && InvokeRequired) BeginInvoke(a); else if (IsHandleCreated) a(); }

    protected override void Dispose(bool disposing) => base.Dispose(disposing);
}
