using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible GPS Navigator — full mirror of the GNS 530 / G1000 NXi screen.
/// The currently-displayed page on the in-sim GPS is reflected in the form, and
/// every button/knob on the GPS can be pressed from our accessible button panel.
/// Falls back to reading GPS SimVars directly when the bridge mod package is not installed.
/// </summary>
public partial class GPSNavigatorForm : Form
{
    private readonly GNSBridgeServer _bridge;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _simConnect;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _diagnosticTimer;

    // Current mirrored state from bridge
    private JsonElement _currentPageState;
    private JsonElement _currentNavState;
    private string _currentWpName = "---";

    // Pending variable updates (for waypoint name read)
    private readonly Dictionary<string, Action<double>> _pendingUpdates = new();

    public GPSNavigatorForm(GNSBridgeServer bridge, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _bridge = bridge;
        _simConnect = simConnect;
        _announcer = announcer;
        InitializeComponent();

        _bridge.StateUpdated += OnBridgeStateUpdated;
        _simConnect.SimVarUpdated += OnSimVarUpdated;

        // Page state refresh from bridge (fast)
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _refreshTimer.Tick += (s, e) => RefreshFromSimVars();
        _refreshTimer.Start();

        // Diagnostic L-var polling (slower)
        _diagnosticTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _diagnosticTimer.Tick += (s, e) => UpdateDiagnostics();
        _diagnosticTimer.Start();
    }

    public void ShowForm()
    {
        if (!Visible)
        {
            Show();
            BringToFront();
        }
        else BringToFront();
        _bridge.SendCommand("request_state");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _refreshTimer.Stop();
            _diagnosticTimer.Stop();
            _bridge.StateUpdated -= OnBridgeStateUpdated;
            _simConnect.SimVarUpdated -= OnSimVarUpdated;
        }
    }

    // --- Bridge state updates ---

    private void OnBridgeStateUpdated(object? sender, GNSStateUpdateEventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnBridgeStateUpdated(sender, e)); return; }
        switch (e.Type)
        {
            case "page_state":
                _currentPageState = e.Data;
                UpdatePageDisplay();
                break;
            case "nav_state":
                _currentNavState = e.Data;
                UpdateNavDisplay();
                break;
            case "connected":
                statusLabel.Text = "Bridge connected";
                break;
            case "command_ack":
                // Force a refresh after a command is acknowledged
                _bridge.SendCommand("request_state");
                break;
            case "command_error":
                _announcer.AnnounceImmediate("GPS command failed");
                break;
        }
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (e.VarName == "WAYPOINT_INFO" && !string.IsNullOrEmpty(e.Description))
        {
            if (InvokeRequired) Invoke(() => HandleWaypointInfo(e.Description));
            else HandleWaypointInfo(e.Description);
            return;
        }

        if (_pendingUpdates.TryGetValue(e.VarName, out var callback))
        {
            _pendingUpdates.Remove(e.VarName);
            if (InvokeRequired) Invoke(() => callback(e.Value));
            else callback(e.Value);
        }
    }

    private void HandleWaypointInfo(string description)
    {
        _currentWpName = description.Contains(",") ? description.Split(',')[0].Trim() : description;
        UpdateNavDisplay();
    }

    // --- Display updates ---

    private void UpdatePageDisplay()
    {
        if (_currentPageState.ValueKind == JsonValueKind.Undefined)
        {
            pageContentList.Items.Clear();
            return;
        }

        try
        {
            string groupLabel = GetString(_currentPageState, "groupLabel", "");
            string pageTitle = GetString(_currentPageState, "pageTitle", "");
            string pageType = GetString(_currentPageState, "pageType", "");
            int selected = _currentPageState.TryGetProperty("selected", out var selProp) ? selProp.GetInt32() : -1;

            pageGroupLabel.Text = $"{groupLabel}: {pageTitle}";
            pageGroupLabel.AccessibleName = $"{groupLabel} {pageTitle}";

            pageContentList.Items.Clear();

            if (_currentPageState.TryGetProperty("items", out var itemsArr) &&
                itemsArr.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var item in itemsArr.EnumerateArray())
                {
                    string text = GetString(item, "text", "?");
                    bool isActive = item.TryGetProperty("isActive", out var actProp) && actProp.GetBoolean();
                    string prefix = isActive ? ">>> " : (idx == selected ? "-> " : "    ");
                    pageContentList.Items.Add(prefix + text);
                    idx++;
                }
            }

            if (selected >= 0 && selected < pageContentList.Items.Count)
            {
                pageContentList.SelectedIndex = selected;
            }

            if (pageContentList.Items.Count == 0)
            {
                pageContentList.Items.Add($"[No list items for this page type: {pageType}]");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] UpdatePageDisplay error: {ex.Message}");
        }
    }

    private void UpdateNavDisplay()
    {
        try
        {
            if (_currentNavState.ValueKind != JsonValueKind.Undefined)
            {
                double dist = _currentNavState.TryGetProperty("nextWpDist", out var d) ? d.GetDouble() : 0;
                double bearing = _currentNavState.TryGetProperty("nextWpBearing", out var b) ? b.GetDouble() : 0;
                double ete = _currentNavState.TryGetProperty("nextWpEte", out var e2) ? e2.GetDouble() : 0;
                double dtk = _currentNavState.TryGetProperty("dtk", out var dt) ? dt.GetDouble() : 0;
                double xtk = _currentNavState.TryGetProperty("xtk", out var xt) ? xt.GetDouble() : 0;
                double gs = _currentNavState.TryGetProperty("groundSpeed", out var gsp) ? gsp.GetDouble() : 0;
                bool gpsDrives = _currentNavState.TryGetProperty("gpsDrivesNav", out var gd) && gd.GetBoolean();

                int eteMin = (int)(ete / 60);
                int eteSec = (int)(ete % 60);
                string xtkDir = xtk > 0 ? "R" : xtk < 0 ? "L" : "";
                string xtkStr = Math.Abs(xtk) < 0.01 ? "On track" : $"{Math.Abs(xtk):F2} NM {xtkDir}";

                navInfoLabel.Text = $"Next: {_currentWpName}  |  {dist:F1} NM  |  {bearing:F0}°  |  ETE {eteMin}:{eteSec:D2}";
                navDetailLabel.Text = $"DTK: {dtk:F0}°  |  XTK: {xtkStr}  |  GS: {gs:F0} kts  |  GPS→NAV: {(gpsDrives ? "YES" : "No")}";
            }
            else
            {
                UpdateNavFromSimVars();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] UpdateNavDisplay error: {ex.Message}");
        }
    }

    // --- SimVar fallback ---

    private void RefreshFromSimVars()
    {
        if (!_simConnect.IsConnected) return;

        try
        {
            _simConnect.RequestVariable("GPS_WP_DISTANCE");
            _simConnect.RequestVariable("GPS_WP_BEARING");
            _simConnect.RequestVariable("GPS_WP_ETE");
            _simConnect.RequestVariable("GPS_FLIGHT_PLAN_WP_COUNT");
            _simConnect.RequestVariable("GPS_FLIGHT_PLAN_WP_INDEX");
            _simConnect.RequestVariable("GPS_IS_ACTIVE_FLIGHT_PLAN");
            _simConnect.RequestVariable("GPS_IS_DIRECTTO");
            _simConnect.RequestVariable("GPS_WP_DESIRED_TRACK");
            _simConnect.RequestVariable("GPS_WP_CROSS_TRK");
            _simConnect.RequestVariable("GPS_GROUND_SPEED");
            _simConnect.RequestVariable("GPS_DRIVES_NAV1");
            _simConnect.RequestVariable("GPS_APPROACH_LOADED");
            _simConnect.RequestVariable("GPS_APPROACH_ACTIVE");
            _simConnect.RequestGPSWaypointInfo();
        }
        catch { }

        if (!_bridge.IsBridgeConnected)
        {
            UpdateNavFromSimVars();
        }
    }

    private void UpdateNavFromSimVars()
    {
        try
        {
            double? wpDist = _simConnect.GetCachedVariableValue("GPS_WP_DISTANCE");
            double? wpBrg = _simConnect.GetCachedVariableValue("GPS_WP_BEARING");
            double? wpEte = _simConnect.GetCachedVariableValue("GPS_WP_ETE");
            double? fplCount = _simConnect.GetCachedVariableValue("GPS_FLIGHT_PLAN_WP_COUNT");
            double? fplIndex = _simConnect.GetCachedVariableValue("GPS_FLIGHT_PLAN_WP_INDEX");
            double? fplActive = _simConnect.GetCachedVariableValue("GPS_IS_ACTIVE_FLIGHT_PLAN");
            double? isDto = _simConnect.GetCachedVariableValue("GPS_IS_DIRECTTO");
            double? dtk = _simConnect.GetCachedVariableValue("GPS_WP_DESIRED_TRACK");
            double? xtk = _simConnect.GetCachedVariableValue("GPS_WP_CROSS_TRK");
            double? gs = _simConnect.GetCachedVariableValue("GPS_GROUND_SPEED");
            double? gpsDrives = _simConnect.GetCachedVariableValue("GPS_DRIVES_NAV1");
            double? apprLoaded = _simConnect.GetCachedVariableValue("GPS_APPROACH_LOADED");
            double? apprActive = _simConnect.GetCachedVariableValue("GPS_APPROACH_ACTIVE");

            string distStr = wpDist.HasValue ? $"{wpDist.Value:F1} NM" : "---";
            string brgStr = wpBrg.HasValue ? $"{wpBrg.Value:F0}°" : "---";
            int eteMin = wpEte.HasValue ? (int)(wpEte.Value / 60) : 0;
            int eteSec = wpEte.HasValue ? (int)(wpEte.Value % 60) : 0;
            string eteStr = wpEte.HasValue ? $"{eteMin}:{eteSec:D2}" : "---";

            navInfoLabel.Text = $"Next: {_currentWpName}  |  {distStr}  |  {brgStr}  |  ETE {eteStr}";

            string dtkStr = dtk.HasValue ? $"{dtk.Value:F0}°" : "---";
            string xtkVal = xtk.HasValue
                ? (Math.Abs(xtk.Value) < 0.01 ? "On track" : $"{Math.Abs(xtk.Value):F2} NM {(xtk.Value > 0 ? "R" : "L")}")
                : "---";
            string gsStr = gs.HasValue ? $"{gs.Value:F0} kts" : "---";
            string gpsDrivesStr = gpsDrives.HasValue && gpsDrives.Value > 0 ? "YES" : "No";

            navDetailLabel.Text = $"DTK: {dtkStr}  |  XTK: {xtkVal}  |  GS: {gsStr}  |  GPS→NAV: {gpsDrivesStr}";

            bool hasFpl = fplActive.HasValue && fplActive.Value > 0;
            int wpCount = fplCount.HasValue ? (int)fplCount.Value : 0;
            int wpIdx = fplIndex.HasValue ? (int)fplIndex.Value : 0;
            bool dto = isDto.HasValue && isDto.Value > 0;
            bool apprLd = apprLoaded.HasValue && apprLoaded.Value > 0;
            bool apprAct = apprActive.HasValue && apprActive.Value > 0;

            // Only update status/page content from SimVars if we don't have bridge data
            if (_currentPageState.ValueKind == JsonValueKind.Undefined)
            {
                string status = hasFpl
                    ? $"Flight plan: waypoint {wpIdx + 1} of {wpCount}"
                    : "No active flight plan";
                if (dto) status += " | DIRECT-TO";
                if (apprAct) status += " | APPROACH ACTIVE";
                else if (apprLd) status += " | Approach loaded";

                pageGroupLabel.Text = status;

                if (pageContentList.Items.Count == 0 && hasFpl)
                {
                    pageContentList.Items.Add($"{wpCount} waypoints (install bridge for full list)");
                    pageContentList.Items.Add($"Active: waypoint {wpIdx + 1} of {wpCount}");
                    if (dto) pageContentList.Items.Add("Mode: DIRECT-TO");
                    if (apprLd) pageContentList.Items.Add($"Approach: {(apprAct ? "ACTIVE" : "loaded")}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] UpdateNavFromSimVars error: {ex.Message}");
        }
    }

    // --- Diagnostics ---

    private void UpdateDiagnostics()
    {
        if (!_simConnect.IsConnected)
        {
            diagLabel.Text = "Not connected to sim";
            return;
        }

        try
        {
            _simConnect.RequestVariable("MSFSBA_GPSBridge_Loaded");
            _simConnect.RequestVariable("MSFSBA_GPSBridge_FmsFound");
            _simConnect.RequestVariable("MSFSBA_GPSBridge_ServerConnected");
            _simConnect.RequestVariable("MSFSBA_GPSBridge_ErrorCode");
            _simConnect.RequestVariable("MSFSBA_GPSBridge_InstrumentType");
        }
        catch { }

        double? loaded = _simConnect.GetCachedVariableValue("MSFSBA_GPSBridge_Loaded");
        double? fmsFound = _simConnect.GetCachedVariableValue("MSFSBA_GPSBridge_FmsFound");
        double? srvConn = _simConnect.GetCachedVariableValue("MSFSBA_GPSBridge_ServerConnected");
        double? errCode = _simConnect.GetCachedVariableValue("MSFSBA_GPSBridge_ErrorCode");
        double? instType = _simConnect.GetCachedVariableValue("MSFSBA_GPSBridge_InstrumentType");

        string loadedStr = (loaded.HasValue && loaded.Value > 0) ? "Yes" : "No";
        string fmsStr = (fmsFound.HasValue && fmsFound.Value > 0) ? "Yes" : "No";
        string srvStr = _bridge.IsBridgeConnected ? "Yes" : "No";
        string err = errCode.HasValue ? ErrorCodeToText((int)errCode.Value) : "Unknown";
        string inst = instType.HasValue ? InstrumentTypeToText((int)instType.Value) : "Unknown";

        diagLabel.Text =
            $"Bridge JS loaded: {loadedStr}  |  FMS found: {fmsStr}  |  HTTP connected: {srvStr}  |  " +
            $"Instrument: {inst}  |  Last error: {err}";
        diagLabel.AccessibleName = diagLabel.Text;
    }

    private static string ErrorCodeToText(int code) => code switch
    {
        0 => "None",
        1 => "Instrument not found",
        2 => "FMS not ready",
        3 => "Page container not ready",
        4 => "HTTP server unreachable",
        99 => "Fatal script error",
        _ => code.ToString()
    };

    private static string InstrumentTypeToText(int code) => code switch
    {
        0 => "None",
        1 => "GNS 530",
        2 => "GNS 430",
        3 => "G1000 MFD",
        _ => code.ToString()
    };

    // --- Button handlers ---

    private void SendInteractionEvent(string eventName)
    {
        _bridge.SendCommand("interaction_event", new Dictionary<string, object> { ["event"] = eventName });
    }

    private void FplButton_Click(object? sender, EventArgs e) { SendInteractionEvent("FPL"); _announcer.AnnounceImmediate("Flight plan page"); }
    private void ProcButton_Click(object? sender, EventArgs e) { SendInteractionEvent("PROC"); _announcer.AnnounceImmediate("Procedures page"); }
    private void VnavButton_Click(object? sender, EventArgs e) { SendInteractionEvent("VNAV"); _announcer.AnnounceImmediate("V NAV"); }
    private void MenuButton_Click(object? sender, EventArgs e) { SendInteractionEvent("MENU"); _announcer.AnnounceImmediate("Menu"); }
    private void MsgButton_Click(object? sender, EventArgs e) { SendInteractionEvent("MSG"); _announcer.AnnounceImmediate("Messages"); }
    private void ObsButton_Click(object? sender, EventArgs e) { SendInteractionEvent("OBS"); _announcer.AnnounceImmediate("O B S toggle"); }
    private void DirectToButton_Click(object? sender, EventArgs e) { SendInteractionEvent("DirectTo"); _announcer.AnnounceImmediate("Direct to"); }
    private void EntButton_Click(object? sender, EventArgs e) { SendInteractionEvent("ENT"); _announcer.AnnounceImmediate("Enter"); }
    private void ClrButton_Click(object? sender, EventArgs e) { SendInteractionEvent("CLR"); _announcer.AnnounceImmediate("Clear"); }
    private void CursorButton_Click(object? sender, EventArgs e) { SendInteractionEvent("RightKnobPush"); _announcer.AnnounceImmediate("Cursor"); }

    // Knob handlers
    private void LeftInnerIncButton_Click(object? sender, EventArgs e) => SendInteractionEvent("LeftInnerInc");
    private void LeftInnerDecButton_Click(object? sender, EventArgs e) => SendInteractionEvent("LeftInnerDec");
    private void LeftOuterIncButton_Click(object? sender, EventArgs e) => SendInteractionEvent("LeftOuterInc");
    private void LeftOuterDecButton_Click(object? sender, EventArgs e) => SendInteractionEvent("LeftOuterDec");
    private void RightInnerIncButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RightInnerInc");
    private void RightInnerDecButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RightInnerDec");
    private void RightOuterIncButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RightOuterInc");
    private void RightOuterDecButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RightOuterDec");

    private void RangeInButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RangeIncrease");
    private void RangeOutButton_Click(object? sender, EventArgs e) => SendInteractionEvent("RangeDecrease");

    private void GpsDrivesNavButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("gps_drives_nav");
        _announcer.AnnounceImmediate("GPS drives NAV toggled");
    }

    private void ActivateLegButton_Click(object? sender, EventArgs e)
    {
        int idx = pageContentList.SelectedIndex;
        if (idx < 0) { _announcer.AnnounceImmediate("No waypoint selected"); return; }
        _bridge.SendCommand("activate_leg", new Dictionary<string, object> { ["index"] = idx });
        _announcer.AnnounceImmediate($"Activating leg {idx + 1}");
    }

    private void ReadCurrentButton_Click(object? sender, EventArgs e)
    {
        double? dist = _simConnect.GetCachedVariableValue("GPS_WP_DISTANCE");
        double? brg = _simConnect.GetCachedVariableValue("GPS_WP_BEARING");
        double? ete = _simConnect.GetCachedVariableValue("GPS_WP_ETE");
        int eteMin = ete.HasValue ? (int)(ete.Value / 60) : 0;

        if (dist.HasValue)
        {
            _announcer.AnnounceImmediate(
                $"Next waypoint {_currentWpName}, {dist.Value:F1} nautical miles, bearing {brg?.ToString("F0") ?? "unknown"} degrees, {eteMin} minutes");
        }
        else
        {
            _announcer.AnnounceImmediate("No navigation data available");
        }
    }

    private void InstallBridgeButton_Click(object? sender, EventArgs e)
    {
        var (success, needsRestart, message) = Patching.GNSModPackageManager.InstallOrUpdate();
        _announcer.AnnounceImmediate(message);
        statusLabel.Text = message;
    }

    // List box keyboard navigation — arrow keys rotate the right inner knob
    private void PageContentList_KeyDown(object? sender, KeyEventArgs e)
    {
        // Only send knob rotations if bridge is connected — otherwise let native navigation work
        if (!_bridge.IsBridgeConnected) return;

        switch (e.KeyCode)
        {
            case Keys.Down:
                SendInteractionEvent("RightInnerInc");
                e.Handled = true;
                break;
            case Keys.Up:
                SendInteractionEvent("RightInnerDec");
                e.Handled = true;
                break;
            case Keys.Enter:
                SendInteractionEvent("ENT");
                e.Handled = true;
                break;
        }
    }

    // --- Helpers ---

    private static string GetString(JsonElement element, string property, string defaultValue)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }
}
