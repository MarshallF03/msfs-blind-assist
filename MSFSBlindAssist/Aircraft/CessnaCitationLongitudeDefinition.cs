using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna Citation Longitude (ICAO C700) — Working Title G3000/G5000 avionics.
///
/// The FMS is handled by the dedicated G5000NavigatorForm (direct Coherent GT to
/// the MFD), opened via the FMC hotkey. This definition provides the SimVar-based
/// systems panels (autopilot, lights, flight controls, anti-ice, electrical, fuel,
/// engines) through BA's native panel framework — confirmed live 2026-06-01 that
/// the Longitude responds to standard SimVars/K-events for all of these.
///
/// The Longitude uses a Garmin GFC autopilot that follows GPS/NAV (FMS) natively,
/// so it needs none of the C172's KAP140 GPSS heading-bug workaround.
///
/// GTC-only screens (performance/TOLD, charts) are a later increment — see
/// docs/longitude-g5000.md.
/// </summary>
public sealed class CessnaCitationLongitudeDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Cessna Citation Longitude";
    public override string AircraftCode => "CITATION_LONGITUDE";

    public override FCUControlType GetAltitudeControlType()      => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType()       => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType()         => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    private static SimVarDefinition State(string name, string display,
        Dictionary<double, string>? values = null, string units = "Bool") => new()
    {
        Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest,
        ValueDescriptions = values ?? new Dictionary<double, string> { [0] = "Off", [1] = "On" }
    };

    private static SimVarDefinition OnOff(string name, string display) => State(name, display);

    private static SimVarDefinition Evt(string name, string display, bool button = false, string? help = null) => new()
    {
        Name = name, DisplayName = display, Type = SimVarType.Event,
        UpdateFrequency = UpdateFrequency.Never, RenderAsButton = button, HelpText = help ?? ""
    };

    private static SimVarDefinition Display(string name, string display, string units) => new()
    {
        Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest
    };

    public override Dictionary<string, SimVarDefinition> GetVariables()
    {
        var v = GetBaseVariables();

        // ===== AUTOPILOT (Garmin GFC) =====
        v["LON_AP_MASTER_STATE"]  = OnOff("AUTOPILOT MASTER", "Autopilot Master");
        v["LON_AP_MASTER_TOGGLE"] = Evt("AP_MASTER", "Autopilot Master Toggle");
        v["LON_FD_STATE"]         = OnOff("AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director");
        v["LON_FD_TOGGLE"]        = Evt("TOGGLE_FLIGHT_DIRECTOR", "Flight Director Toggle");
        v["LON_YD_STATE"]         = OnOff("AUTOPILOT YAW DAMPER", "Yaw Damper");
        v["LON_YD_TOGGLE"]        = Evt("YAW_DAMPER_TOGGLE", "Yaw Damper Toggle");

        v["LON_AP_HDG_BTN"]  = Evt("AP_HDG_HOLD", "HDG mode", button: true);
        v["LON_AP_NAV_BTN"]  = Evt("AP_NAV1_HOLD", "NAV mode", button: true);
        v["LON_AP_APR_BTN"]  = Evt("AP_APR_HOLD", "Approach mode", button: true);
        v["LON_AP_ALT_BTN"]  = Evt("AP_ALT_HOLD", "Altitude hold", button: true);
        v["LON_AP_VS_BTN"]   = Evt("AP_VS_HOLD", "Vertical speed mode", button: true);
        v["LON_AP_FLC_BTN"]  = Evt("FLIGHT_LEVEL_CHANGE", "Flight level change / speed mode", button: true);
        v["LON_AP_VNAV_BTN"] = Evt("AP_VNAV_HOLD", "VNAV mode", button: true);

        v["LON_AP_ALT_STATE"] = Display("AUTOPILOT ALTITUDE LOCK VAR", "Selected Altitude", "feet");
        v["LON_AP_ALT_SET"]   = Evt("AP_ALT_VAR_SET_ENGLISH", "Set Selected Altitude", help: "Enter altitude in feet");
        v["LON_AP_HDG_STATE"] = Display("AUTOPILOT HEADING LOCK DIR", "Selected Heading", "degrees");
        v["LON_AP_HDG_SET"]   = Evt("HEADING_BUG_SET", "Set Selected Heading", help: "Enter heading 0 to 359");
        v["LON_AP_VS_STATE"]  = Display("AUTOPILOT VERTICAL HOLD VAR", "Selected Vertical Speed", "feet/minute");
        v["LON_AP_VS_SET"]    = Evt("AP_VS_VAR_SET_ENGLISH", "Set Vertical Speed", help: "Enter feet per minute (negative for descent)");
        v["LON_AP_SPD_STATE"] = Display("AUTOPILOT AIRSPEED HOLD VAR", "Selected Airspeed", "knots");
        v["LON_AP_SPD_SET"]   = Evt("AP_SPD_VAR_SET", "Set Selected Airspeed", help: "Enter knots");

        // ===== EXTERIOR LIGHTS =====
        v["LON_BEACON_STATE"]   = OnOff("LIGHT BEACON", "Beacon");
        v["LON_BEACON_TOGGLE"]  = Evt("TOGGLE_BEACON_LIGHTS", "Beacon Toggle");
        v["LON_NAV_STATE"]      = OnOff("LIGHT NAV", "Navigation Lights");
        v["LON_NAV_TOGGLE"]     = Evt("TOGGLE_NAV_LIGHTS", "Nav Lights Toggle");
        v["LON_STROBE_STATE"]   = OnOff("LIGHT STROBE", "Strobes");
        v["LON_STROBE_TOGGLE"]  = Evt("STROBES_TOGGLE", "Strobe Toggle");
        v["LON_TAXI_STATE"]     = OnOff("LIGHT TAXI", "Taxi Light");
        v["LON_TAXI_TOGGLE"]    = Evt("TOGGLE_TAXI_LIGHTS", "Taxi Light Toggle");
        v["LON_LANDING_STATE"]  = OnOff("LIGHT LANDING", "Landing Lights");
        v["LON_LANDING_TOGGLE"] = Evt("LANDING_LIGHTS_TOGGLE", "Landing Lights Toggle");
        v["LON_LOGO_STATE"]     = OnOff("LIGHT LOGO", "Logo Light");
        v["LON_LOGO_TOGGLE"]    = Evt("TOGGLE_LOGO_LIGHTS", "Logo Light Toggle");

        // ===== FLIGHT CONTROLS =====
        v["LON_GEAR_STATE"]   = State("GEAR HANDLE POSITION", "Landing Gear",
            new Dictionary<double, string> { [0] = "Up", [1] = "Down" }, "percent over 100");
        v["LON_GEAR_TOGGLE"]  = Evt("GEAR_TOGGLE", "Gear Toggle");
        v["LON_FLAPS_INDEX"]  = Display("FLAPS HANDLE INDEX", "Flaps Detent", "number");
        v["LON_FLAPS_PCT"]    = Display("TRAILING EDGE FLAPS LEFT PERCENT", "Flaps Position", "percent");
        v["LON_FLAPS_INCR"]   = Evt("FLAPS_INCR", "Flaps Down One", button: true);
        v["LON_FLAPS_DECR"]   = Evt("FLAPS_UP", "Flaps Up One", button: true);
        v["LON_SPOILERS_STATE"]  = Display("SPOILERS HANDLE POSITION", "Speedbrakes", "percent");
        v["LON_SPOILERS_TOGGLE"] = Evt("SPOILERS_TOGGLE", "Speedbrakes Toggle", button: true);
        v["LON_PARK_BRAKE_STATE"]  = OnOff("BRAKE PARKING POSITION", "Parking Brake");
        v["LON_PARK_BRAKE_TOGGLE"] = Evt("PARKING_BRAKES", "Parking Brake Toggle");

        // ===== ANTI-ICE =====
        v["LON_DEICE_STATE"]   = OnOff("STRUCTURAL DEICE SWITCH", "Airframe De-Ice");
        v["LON_DEICE_TOGGLE"]  = Evt("TOGGLE_STRUCTURAL_DEICE", "Airframe De-Ice Toggle");
        v["LON_PITOT_STATE"]   = OnOff("PITOT HEAT", "Pitot Heat");
        v["LON_PITOT_TOGGLE"]  = Evt("PITOT_HEAT_TOGGLE", "Pitot Heat Toggle");

        // ===== ELECTRICAL =====
        v["LON_BATTERY_STATE"]  = OnOff("ELECTRICAL MASTER BATTERY", "Battery Master");
        v["LON_BATTERY_TOGGLE"] = Evt("TOGGLE_MASTER_BATTERY", "Battery Master Toggle");

        // ===== FUEL =====
        v["LON_FUEL_TOTAL"] = Display("FUEL TOTAL QUANTITY", "Total Fuel", "gallons");
        v["LON_FUEL_LBS"]   = Display("FUEL TOTAL QUANTITY WEIGHT", "Total Fuel Weight", "pounds");

        // ===== ENGINES =====
        v["LON_ENG1_N1"] = Display("TURB ENG N1:1", "Engine 1 N1", "percent");
        v["LON_ENG2_N1"] = Display("TURB ENG N1:2", "Engine 2 N1", "percent");
        v["LON_ENG1_ITT"] = Display("TURB ENG ITT:1", "Engine 1 ITT", "celsius");
        v["LON_ENG2_ITT"] = Display("TURB ENG ITT:2", "Engine 2 ITT", "celsius");

        return v;
    }

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Autopilot"]       = new List<string> { "Autopilot Master", "Autopilot Modes", "Autopilot Settings" },
        ["Lights"]          = new List<string> { "Exterior Lights" },
        ["Flight Controls"] = new List<string> { "Gear, Flaps, Speedbrakes" },
        ["Systems"]         = new List<string> { "Anti-Ice", "Electrical" },
        ["Fuel & Engines"]  = new List<string> { "Fuel", "Engines" },
    };

    protected override Dictionary<string, List<string>> BuildPanelControls() => new()
    {
        ["Autopilot Master"] = new List<string>
        {
            "LON_AP_MASTER_STATE", "LON_AP_MASTER_TOGGLE",
            "LON_FD_STATE", "LON_FD_TOGGLE",
            "LON_YD_STATE", "LON_YD_TOGGLE"
        },
        ["Autopilot Modes"] = new List<string>
        {
            "LON_AP_HDG_BTN", "LON_AP_NAV_BTN", "LON_AP_APR_BTN",
            "LON_AP_ALT_BTN", "LON_AP_VS_BTN", "LON_AP_FLC_BTN", "LON_AP_VNAV_BTN"
        },
        ["Autopilot Settings"] = new List<string>
        {
            "LON_AP_ALT_STATE", "LON_AP_ALT_SET",
            "LON_AP_HDG_STATE", "LON_AP_HDG_SET",
            "LON_AP_VS_STATE",  "LON_AP_VS_SET",
            "LON_AP_SPD_STATE", "LON_AP_SPD_SET"
        },
        ["Exterior Lights"] = new List<string>
        {
            "LON_BEACON_STATE", "LON_NAV_STATE", "LON_STROBE_STATE",
            "LON_TAXI_STATE", "LON_LANDING_STATE", "LON_LOGO_STATE"
        },
        ["Gear, Flaps, Speedbrakes"] = new List<string>
        {
            "LON_GEAR_STATE", "LON_GEAR_TOGGLE",
            "LON_FLAPS_INDEX", "LON_FLAPS_PCT", "LON_FLAPS_INCR", "LON_FLAPS_DECR",
            "LON_SPOILERS_STATE", "LON_SPOILERS_TOGGLE",
            "LON_PARK_BRAKE_STATE", "LON_PARK_BRAKE_TOGGLE"
        },
        ["Anti-Ice"] = new List<string>
        {
            "LON_DEICE_STATE", "LON_DEICE_TOGGLE",
            "LON_PITOT_STATE", "LON_PITOT_TOGGLE"
        },
        ["Electrical"] = new List<string>
        {
            "LON_BATTERY_STATE", "LON_BATTERY_TOGGLE"
        },
        ["Fuel"] = new List<string>
        {
            "LON_FUEL_TOTAL", "LON_FUEL_LBS"
        },
        ["Engines"] = new List<string>
        {
            "LON_ENG1_N1", "LON_ENG2_N1", "LON_ENG1_ITT", "LON_ENG2_ITT"
        },
    };

    /// <summary>
    /// FCU value-setting input hotkeys, matching the airliners:
    ///   Ctrl+A = altitude, Ctrl+H = heading, Ctrl+S = speed, Ctrl+V = vertical speed.
    /// All four events verified live to engage on the Longitude's Garmin GFC
    /// (2026-06-01). NOTE: setting an altitude alone does NOT climb — engage a
    /// vertical mode (FLC or VS) from the Autopilot Modes panel, and the jet climbs
    /// to the selected altitude.
    /// </summary>
    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Selected Altitude", "Altitude", "0 to 45000 feet",
                    "AP_ALT_VAR_SET_ENGLISH", simConnect, announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 0 && v <= 45000,
                              "Enter 0 to 45000 feet"));

            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Heading Bug", "Heading", "0 to 359",
                    "HEADING_BUG_SET", simConnect, announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 0 && v <= 359,
                              "Enter a heading 0 to 359"));

            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Selected Airspeed", "Airspeed", "80 to 350 knots",
                    "AP_SPD_VAR_SET", simConnect, announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 80 && v <= 350,
                              "Enter 80 to 350 knots"));

            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Vertical Speed", "Vertical Speed", "-6000 to 6000 fpm",
                    "AP_VS_VAR_SET_ENGLISH", simConnect, announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= -6000 && v <= 6000,
                              "Enter -6000 to 6000 fpm"),
                    value => value >= 0 ? (uint)value : (uint)(65536 + value));
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    public override Dictionary<string, string> GetButtonStateMapping() => new()
    {
        ["LON_AP_MASTER_TOGGLE"]  = "LON_AP_MASTER_STATE",
        ["LON_FD_TOGGLE"]         = "LON_FD_STATE",
        ["LON_YD_TOGGLE"]         = "LON_YD_STATE",
        ["LON_BEACON_TOGGLE"]     = "LON_BEACON_STATE",
        ["LON_NAV_TOGGLE"]        = "LON_NAV_STATE",
        ["LON_STROBE_TOGGLE"]     = "LON_STROBE_STATE",
        ["LON_TAXI_TOGGLE"]       = "LON_TAXI_STATE",
        ["LON_LANDING_TOGGLE"]    = "LON_LANDING_STATE",
        ["LON_LOGO_TOGGLE"]       = "LON_LOGO_STATE",
        ["LON_GEAR_TOGGLE"]       = "LON_GEAR_STATE",
        ["LON_PARK_BRAKE_TOGGLE"] = "LON_PARK_BRAKE_STATE",
        ["LON_DEICE_TOGGLE"]      = "LON_DEICE_STATE",
        ["LON_PITOT_TOGGLE"]      = "LON_PITOT_STATE",
        ["LON_BATTERY_TOGGLE"]    = "LON_BATTERY_STATE",
    };
}
