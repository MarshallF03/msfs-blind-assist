using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.GPS;

/// <summary>
/// Accessible GPS Navigator form — provides full screen reader access to the
/// GNS 530 flight plan, navigation state, and GPS commands.
/// Works with any aircraft that has a Working Title GNS 530/430.
/// </summary>
public partial class GPSNavigatorForm : Form
{
    private readonly GNSBridgeServer _bridge;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _simConnect;
    private System.Windows.Forms.Timer _refreshTimer;

    // Current state
    private JsonElement _currentFlightPlan;
    private JsonElement _currentNavState;

    // Track waypoint name from WAYPOINT_INFO event
    private string _currentWpName = "---";

    public GPSNavigatorForm(GNSBridgeServer bridge, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _bridge = bridge;
        _simConnect = simConnect;
        _announcer = announcer;
        InitializeComponent();

        _bridge.StateUpdated += OnBridgeStateUpdated;
        _simConnect.SimVarUpdated += OnSimVarUpdated;

        // Auto-refresh timer — reads SimVars directly as fallback when bridge isn't connected
        _refreshTimer = new System.Windows.Forms.Timer();
        _refreshTimer.Interval = 2000;
        _refreshTimer.Tick += (s, e) => RefreshFromSimVars();
        _refreshTimer.Start();
    }

    public void ShowForm()
    {
        if (!Visible)
        {
            Show();
            BringToFront();
        }
        else
        {
            BringToFront();
        }
        // Request fresh state
        _bridge.SendCommand("request_state");
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (e.VarName == "WAYPOINT_INFO" && !string.IsNullOrEmpty(e.Description))
        {
            if (InvokeRequired)
                Invoke(() => HandleWaypointInfo(e.Description));
            else
                HandleWaypointInfo(e.Description);
        }
    }

    private void HandleWaypointInfo(string description)
    {
        // Description format: "IDENT, 12.3 NM, 045 degrees" or "No active waypoint"
        _currentWpName = description.Contains(",") ? description.Split(',')[0].Trim() : description;
        UpdateNavFromSimVars();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _bridge.StateUpdated -= OnBridgeStateUpdated;
        _simConnect.SimVarUpdated -= OnSimVarUpdated;
        // Don't dispose — hide instead so it can be re-shown
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnBridgeStateUpdated(object? sender, GNSStateUpdateEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnBridgeStateUpdated(sender, e));
            return;
        }

        switch (e.Type)
        {
            case "flight_plan":
                _currentFlightPlan = e.Data;
                UpdateFlightPlanDisplay();
                break;

            case "nav_state":
                _currentNavState = e.Data;
                UpdateNavDisplay();
                break;

            case "command_ack":
                break;

            case "command_error":
                _announcer.AnnounceImmediate("GPS command failed");
                break;
        }
    }

    private void UpdateFlightPlanDisplay()
    {
        try
        {
            flightPlanList.Items.Clear();

            if (_currentFlightPlan.ValueKind == JsonValueKind.Undefined)
            {
                flightPlanList.Items.Add("No flight plan data — is the GNS bridge installed?");
                return;
            }

            bool active = false;
            if (_currentFlightPlan.TryGetProperty("active", out var activeProp))
                active = activeProp.GetBoolean();

            if (!active)
            {
                flightPlanList.Items.Add("No active flight plan");
                return;
            }

            // Origin and destination
            string origin = GetString(_currentFlightPlan, "origin", "");
            string destination = GetString(_currentFlightPlan, "destination", "");
            if (!string.IsNullOrEmpty(origin) || !string.IsNullOrEmpty(destination))
            {
                flightPlanList.Items.Add($"Route: {origin} to {destination}");
            }

            // Direct-To status
            bool directTo = false;
            if (_currentFlightPlan.TryGetProperty("directTo", out var dtProp))
                directTo = dtProp.GetBoolean();
            if (directTo)
            {
                string dtTarget = GetString(_currentFlightPlan, "directToTarget", "");
                flightPlanList.Items.Add($"DIRECT TO: {dtTarget}");
            }

            // Approach info
            if (_currentFlightPlan.TryGetProperty("approach", out var apprProp))
            {
                bool apprLoaded = false;
                if (apprProp.TryGetProperty("loaded", out var loadedProp))
                    apprLoaded = loadedProp.GetBoolean();
                if (apprLoaded)
                {
                    bool apprActive = false;
                    if (apprProp.TryGetProperty("active", out var activeProp2))
                        apprActive = activeProp2.GetBoolean();
                    string apprName = GetString(apprProp, "name", "");
                    flightPlanList.Items.Add($"Approach: {apprName} {(apprActive ? "(ACTIVE)" : "(loaded)")}");
                }
            }

            flightPlanList.Items.Add("---");

            // Waypoints
            int activeLegIndex = 0;
            if (_currentFlightPlan.TryGetProperty("activeLegIndex", out var aliProp))
                activeLegIndex = aliProp.GetInt32();

            if (_currentFlightPlan.TryGetProperty("waypoints", out var wpArray) &&
                wpArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var wp in wpArray.EnumerateArray())
                {
                    int index = wp.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                    string ident = GetString(wp, "ident", "???");
                    string type = GetString(wp, "type", "");
                    double distance = wp.TryGetProperty("distance", out var distProp) ? distProp.GetDouble() : 0;
                    double dtk = wp.TryGetProperty("dtk", out var dtkProp) ? dtkProp.GetDouble() : 0;
                    bool isMissed = wp.TryGetProperty("isMissedApproach", out var maProp) && maProp.GetBoolean();

                    string activeMarker = (index == activeLegIndex) ? ">>> " : "    ";
                    string typeStr = !string.IsNullOrEmpty(type) ? $" ({type})" : "";
                    string distStr = distance > 0 ? $" {distance:F1} NM" : "";
                    string dtkStr = dtk > 0 ? $" {dtk:F0}°" : "";
                    string missedStr = isMissed ? " [MISSED]" : "";

                    flightPlanList.Items.Add($"{activeMarker}{ident}{typeStr}{distStr}{dtkStr}{missedStr}");
                }
            }

            // Update status
            int wpCount = _currentFlightPlan.TryGetProperty("waypointCount", out var wcProp) ? wcProp.GetInt32() : 0;
            statusLabel.Text = $"Flight plan: {wpCount} waypoints, active leg {activeLegIndex + 1}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] Error updating display: {ex.Message}");
        }
    }

    private void UpdateNavDisplay()
    {
        try
        {
            if (_currentNavState.ValueKind == JsonValueKind.Undefined)
            {
                navInfoLabel.Text = "No navigation data";
                return;
            }

            string nextWp = GetString(_currentNavState, "nextWpId", "---");
            double dist = _currentNavState.TryGetProperty("nextWpDist", out var d) ? d.GetDouble() : 0;
            double bearing = _currentNavState.TryGetProperty("nextWpBearing", out var b) ? b.GetDouble() : 0;
            double ete = _currentNavState.TryGetProperty("nextWpEte", out var e2) ? e2.GetDouble() : 0;
            double dtk = _currentNavState.TryGetProperty("dtk", out var dt) ? dt.GetDouble() : 0;
            double xtk = _currentNavState.TryGetProperty("xtk", out var xt) ? xt.GetDouble() : 0;
            double gs = _currentNavState.TryGetProperty("groundSpeed", out var gsProp) ? gsProp.GetDouble() : 0;
            bool gpsDrives = _currentNavState.TryGetProperty("gpsDrivesNav", out var gdProp) && gdProp.GetBoolean();

            // Format ETE as minutes:seconds
            int eteMin = (int)(ete / 60);
            int eteSec = (int)(ete % 60);

            // Format XTK direction
            string xtkDir = xtk > 0 ? "R" : xtk < 0 ? "L" : "";
            string xtkStr = Math.Abs(xtk) > 0.01 ? $"{Math.Abs(xtk):F2} NM {xtkDir}" : "On track";

            navInfoLabel.Text = $"Next: {nextWp}  |  {dist:F1} NM  |  {bearing:F0}°  |  ETE {eteMin}:{eteSec:D2}";

            navDetailLabel.Text = $"DTK: {dtk:F0}°  |  XTK: {xtkStr}  |  GS: {gs:F0} kts  |  GPS→NAV: {(gpsDrives ? "YES" : "No")}";

            // Update connection status
            connectionLabel.Text = _bridge.IsBridgeConnected
                ? "Bridge: Connected"
                : "Bridge: Not connected — install mod package and restart sim";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] Error updating nav: {ex.Message}");
        }
    }

    // --- Button handlers ---

    private void DirectToButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("direct_to");
        _announcer.AnnounceImmediate("Direct-To page opened on GPS");
    }

    private void CancelDirectToButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("cancel_direct_to");
        _announcer.AnnounceImmediate("Direct-To cancelled");
    }

    private void GpsDrivesNavButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("gps_drives_nav");
        _announcer.AnnounceImmediate("GPS drives NAV toggled");
    }

    private void ObsToggleButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("obs_toggle");
        _announcer.AnnounceImmediate("OBS mode toggled");
    }

    private void ActivateLegButton_Click(object? sender, EventArgs e)
    {
        if (flightPlanList.SelectedIndex >= 0)
        {
            // Account for header lines (Route, Direct-To, Approach, separator)
            // Find the actual waypoint index from the selected item
            string item = flightPlanList.SelectedItem?.ToString() ?? "";
            // Items starting with ">>>" or spaces are waypoints
            if (item.StartsWith(">>>") || item.StartsWith("    "))
            {
                // Extract the waypoint ident to announce
                string ident = item.Trim().Split(' ')[0].TrimStart('>').Trim();
                int wpIndex = flightPlanList.SelectedIndex; // rough mapping

                _bridge.SendCommand("activate_leg", new Dictionary<string, object> { ["index"] = wpIndex });
                _announcer.AnnounceImmediate($"Activating leg to {ident}");
            }
            else
            {
                _announcer.AnnounceImmediate("Select a waypoint first");
            }
        }
        else
        {
            _announcer.AnnounceImmediate("No waypoint selected");
        }
    }

    private void FlightPlanButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("gps_button", new Dictionary<string, object> { ["event"] = "GPS_FLIGHTPLAN_BUTTON" });
        _announcer.AnnounceImmediate("Flight plan page");
    }

    private void NearestButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("gps_button", new Dictionary<string, object> { ["event"] = "GPS_NEAREST_BUTTON" });
        _announcer.AnnounceImmediate("Nearest airports page");
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        _bridge.SendCommand("request_state");
        _announcer.AnnounceImmediate("Refreshing GPS data");
    }

    private void ReadCurrentButton_Click(object? sender, EventArgs e)
    {
        // Read the current navigation state aloud
        // Try bridge data first, fall back to SimVar data
        if (_currentNavState.ValueKind != JsonValueKind.Undefined)
        {
            string nextWp = GetString(_currentNavState, "nextWpId", "none");
            double dist = _currentNavState.TryGetProperty("nextWpDist", out var d) ? d.GetDouble() : 0;
            double bearing = _currentNavState.TryGetProperty("nextWpBearing", out var b) ? b.GetDouble() : 0;
            double ete = _currentNavState.TryGetProperty("nextWpEte", out var e2) ? e2.GetDouble() : 0;
            int eteMin = (int)(ete / 60);

            _announcer.AnnounceImmediate($"Next waypoint {nextWp}, {dist:F1} nautical miles, bearing {bearing:F0} degrees, {eteMin} minutes");
        }
        else
        {
            // Use SimVar fallback data
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
    }

    /// <summary>
    /// Fallback: reads GPS data directly from SimConnect SimVars.
    /// Works even without the bridge JS installed — gives basic nav data.
    /// </summary>
    private void RefreshFromSimVars()
    {
        if (!_simConnect.IsConnected) return;

        // If bridge is connected and sending data, use that instead
        if (_bridge.IsBridgeConnected && _currentNavState.ValueKind != JsonValueKind.Undefined)
        {
            UpdateNavDisplay();
            return;
        }

        // Request GPS SimVars through the registered variable system
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
            _simConnect.RequestVariable("GPS_COURSE_TO_STEER");
            // Request waypoint name (comes back via WAYPOINT_INFO event)
            _simConnect.RequestGPSWaypointInfo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] SimVar request error: {ex.Message}");
        }

        // Update display from cached SimVar values
        UpdateNavFromSimVars();
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

            // Build nav info text
            string nextWp = _currentWpName;

            string distStr = wpDist.HasValue ? $"{wpDist.Value:F1} NM" : "---";
            string brgStr = wpBrg.HasValue ? $"{wpBrg.Value:F0}°" : "---";
            int eteMin = wpEte.HasValue ? (int)(wpEte.Value / 60) : 0;
            int eteSec = wpEte.HasValue ? (int)(wpEte.Value % 60) : 0;
            string eteStr = wpEte.HasValue ? $"{eteMin}:{eteSec:D2}" : "---";

            navInfoLabel.Text = $"Next: {nextWp}  |  {distStr}  |  {brgStr}  |  ETE: {eteStr}";
            navInfoLabel.AccessibleName = $"Next waypoint {nextWp}, {distStr}, bearing {brgStr}, E T E {eteStr}";

            string dtkStr = dtk.HasValue ? $"{dtk.Value:F0}°" : "---";
            string xtkVal = xtk.HasValue ? (Math.Abs(xtk.Value) < 0.01 ? "On track" : $"{Math.Abs(xtk.Value):F2} NM {(xtk.Value > 0 ? "R" : "L")}") : "---";
            string gsStr = gs.HasValue ? $"{gs.Value:F0} kts" : "---";
            string gpsDrivesStr = gpsDrives.HasValue && gpsDrives.Value > 0 ? "YES" : "No";

            navDetailLabel.Text = $"DTK: {dtkStr}  |  XTK: {xtkVal}  |  GS: {gsStr}  |  GPS→NAV: {gpsDrivesStr}";

            // Status
            bool hasFpl = fplActive.HasValue && fplActive.Value > 0;
            int wpCount = fplCount.HasValue ? (int)fplCount.Value : 0;
            int wpIdx = fplIndex.HasValue ? (int)fplIndex.Value : 0;
            bool dto = isDto.HasValue && isDto.Value > 0;
            bool apprLd = apprLoaded.HasValue && apprLoaded.Value > 0;
            bool apprAct = apprActive.HasValue && apprActive.Value > 0;

            string statusParts = hasFpl ? $"Flight plan active: waypoint {wpIdx + 1} of {wpCount}" : "No active flight plan";
            if (dto) statusParts += " | DIRECT-TO";
            if (apprAct) statusParts += " | APPROACH ACTIVE";
            else if (apprLd) statusParts += " | Approach loaded";

            statusLabel.Text = statusParts;

            // Connection status
            connectionLabel.Text = _bridge.IsBridgeConnected
                ? "Bridge: Connected"
                : "Bridge: Not connected — using SimVar fallback (basic data only)";

            // If flight plan list is empty and we have data, show a message
            if (flightPlanList.Items.Count == 0 && hasFpl)
            {
                flightPlanList.Items.Clear();
                flightPlanList.Items.Add($"Flight plan: {wpCount} waypoints (install bridge for full list)");
                flightPlanList.Items.Add($"Current: waypoint {wpIdx + 1} of {wpCount}");
                if (dto) flightPlanList.Items.Add("Mode: DIRECT-TO");
                if (apprLd) flightPlanList.Items.Add($"Approach: {(apprAct ? "ACTIVE" : "loaded")}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Nav] SimVar display error: {ex.Message}");
        }
    }

    private void InstallBridgeButton_Click(object? sender, EventArgs e)
    {
        var (success, needsRestart, message) = Patching.GNSModPackageManager.InstallOrUpdate();
        _announcer.AnnounceImmediate(message);
        connectionLabel.Text = message;
    }

    // --- Helpers ---

    private static string GetString(JsonElement element, string property, string defaultValue)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }
}
