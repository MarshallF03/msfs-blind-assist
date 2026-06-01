using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
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

    // Continuously cached (so the output readout hotkeys can read instantly).
    // Auto-announcement is suppressed in ProcessSimVarUpdate — we only speak on demand.
    private static SimVarDefinition Cached(string name, string display, string units) => new()
    {
        Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
    };

    // Keys whose continuous updates we cache but never auto-announce.
    private static readonly HashSet<string> SuppressedKeys = new()
    {
        "LON_AP_ALT_STATE", "LON_AP_HDG_STATE", "LON_AP_VS_STATE", "LON_AP_SPD_STATE",
        "LON_FUEL_FLOW1", "LON_FUEL_FLOW2", "LON_FUEL_TOTAL_LBS",
        "LON_GPS_WP_DIST", "LON_GPS_ETE", "LON_GS",
        "LON_NAV1_FREQ", "LON_NAV1_HASLOC", "LON_NAV1_HASGS", "LON_NAV1_CDI", "LON_NAV1_GSI", "LON_NAV1_DME"
    };

    // AP mode state rendered as a toggle button (shows ON / Off). Continuously
    // monitored so the button reflects the live mode and announces engage/disengage.
    private static SimVarDefinition ApBtn(string name, string display) => new()
    {
        Name = name, DisplayName = display, Type = SimVarType.SimVar, Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, RenderAsButton = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
    };

    /// <summary>The Longitude's CDP FMS client (set by MainForm). Autopilot commands
    /// route through this because SimConnect events do not drive the WT autopilot.</summary>
    public SimConnect.G5000FmsClient? Fms { get; set; }

    // AP mode button key → the K-event that toggles it (sent via CDP).
    private static readonly Dictionary<string, string> ApEventMap = new()
    {
        ["LON_AP_MASTER"] = "AP_MASTER",
        ["LON_AP_FD"]     = "TOGGLE_FLIGHT_DIRECTOR",
        ["LON_AP_YD"]     = "YAW_DAMPER_TOGGLE",
        ["LON_AP_HDG"]    = "AP_HDG_HOLD",
        ["LON_AP_NAV"]    = "AP_NAV1_HOLD",
        ["LON_AP_APR"]    = "AP_APR_HOLD",
        ["LON_AP_ALT"]    = "AP_ALT_HOLD",
        ["LON_AP_FLC"]    = "FLIGHT_LEVEL_CHANGE",
        ["LON_AP_VS"]     = "AP_VS_HOLD",
    };

    // AP value-set panel key → (K-event, unit) sent via CDP.
    private static readonly Dictionary<string, (string ev, string unit)> ApValueMap = new()
    {
        ["LON_AP_ALT_SET"] = ("AP_ALT_VAR_SET_ENGLISH", "feet"),
        ["LON_AP_HDG_SET"] = ("HEADING_BUG_SET", "degrees"),
        ["LON_AP_VS_SET"]  = ("AP_VS_VAR_SET_ENGLISH", "feet per minute"),
        ["LON_AP_SPD_SET"] = ("AP_SPD_VAR_SET", "knots"),
    };

    public override Dictionary<string, SimVarDefinition> GetVariables()
    {
        var v = GetBaseVariables();

        // ===== AUTOPILOT (Garmin GFC) =====
        // Mode controls are STATE variables rendered as toggle buttons (show ON/Off)
        // and routed through CDP in HandleUIVariableSet — SimConnect events do not
        // drive the WT autopilot (verified live).
        v["LON_AP_MASTER"] = ApBtn("AUTOPILOT MASTER", "Autopilot Master");
        v["LON_AP_FD"]     = ApBtn("AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director");
        v["LON_AP_YD"]     = ApBtn("AUTOPILOT YAW DAMPER", "Yaw Damper");
        v["LON_AP_HDG"]    = ApBtn("AUTOPILOT HEADING LOCK", "HDG mode");
        v["LON_AP_NAV"]    = ApBtn("AUTOPILOT NAV1 LOCK", "NAV mode");
        v["LON_AP_APR"]    = ApBtn("AUTOPILOT APPROACH HOLD", "Approach mode");
        v["LON_AP_ALT"]    = ApBtn("AUTOPILOT ALTITUDE LOCK", "Altitude hold");
        v["LON_AP_FLC"]    = ApBtn("AUTOPILOT FLIGHT LEVEL CHANGE", "FLC (flight level change)");
        v["LON_AP_VS"]     = ApBtn("AUTOPILOT VERTICAL HOLD", "Vertical speed mode");

        v["LON_AP_ALT_STATE"] = Cached("AUTOPILOT ALTITUDE LOCK VAR", "Selected Altitude", "feet");
        v["LON_AP_ALT_SET"]   = Evt("AP_ALT_VAR_SET_ENGLISH", "Set Selected Altitude", help: "Enter altitude in feet");
        v["LON_AP_HDG_STATE"] = Cached("AUTOPILOT HEADING LOCK DIR", "Selected Heading", "degrees");
        v["LON_AP_HDG_SET"]   = Evt("HEADING_BUG_SET", "Set Selected Heading", help: "Enter heading 0 to 359");
        v["LON_AP_VS_STATE"]  = Cached("AUTOPILOT VERTICAL HOLD VAR", "Selected Vertical Speed", "feet/minute");
        v["LON_AP_VS_SET"]    = Evt("AP_VS_VAR_SET_ENGLISH", "Set Vertical Speed", help: "Enter feet per minute (negative for descent)");
        v["LON_AP_SPD_STATE"] = Cached("AUTOPILOT AIRSPEED HOLD VAR", "Selected Airspeed", "knots");
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
        v["LON_FUEL_TOTAL"]     = Display("FUEL TOTAL QUANTITY", "Total Fuel", "gallons");
        v["LON_FUEL_TOTAL_LBS"] = Cached("FUEL TOTAL QUANTITY WEIGHT", "Total Fuel Weight", "pounds");
        v["LON_FUEL_FLOW1"]     = Cached("ENG FUEL FLOW PPH:1", "Engine 1 Fuel Flow", "pounds per hour");
        v["LON_FUEL_FLOW2"]     = Cached("ENG FUEL FLOW PPH:2", "Engine 2 Fuel Flow", "pounds per hour");

        // ===== ENGINES =====
        v["LON_ENG1_N1"] = Display("TURB ENG N1:1", "Engine 1 N1", "percent");
        v["LON_ENG2_N1"] = Display("TURB ENG N1:2", "Engine 2 N1", "percent");
        v["LON_ENG1_ITT"] = Display("TURB ENG ITT:1", "Engine 1 ITT", "celsius");
        v["LON_ENG2_ITT"] = Display("TURB ENG ITT:2", "Engine 2 ITT", "celsius");

        // ===== NAV / GPS readout sources (cached for output hotkeys) =====
        v["LON_GPS_WP_DIST"] = Cached("GPS WP DISTANCE", "Distance to next waypoint", "nautical miles");
        v["LON_GPS_ETE"]     = Cached("GPS ETE", "Time to destination", "seconds");
        v["LON_GS"]          = Cached("GPS GROUND SPEED", "Ground speed", "knots");
        v["LON_NAV1_FREQ"]   = Cached("NAV ACTIVE FREQUENCY:1", "NAV1 frequency", "MHz");
        v["LON_NAV1_HASLOC"] = Cached("NAV HAS LOCALIZER:1", "Localizer present", "Bool");
        v["LON_NAV1_HASGS"]  = Cached("NAV HAS GLIDE SLOPE:1", "Glideslope present", "Bool");
        v["LON_NAV1_CDI"]    = Cached("NAV CDI:1", "Localizer deviation", "number");
        v["LON_NAV1_GSI"]    = Cached("NAV GSI:1", "Glideslope deviation", "number");
        v["LON_NAV1_DME"]    = Cached("NAV DME:1", "DME distance", "nautical miles");

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
            "LON_AP_MASTER", "LON_AP_FD", "LON_AP_YD"
        },
        ["Autopilot Modes"] = new List<string>
        {
            "LON_AP_HDG", "LON_AP_NAV", "LON_AP_APR",
            "LON_AP_ALT", "LON_AP_FLC", "LON_AP_VS"
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
            "LON_FUEL_TOTAL", "LON_FUEL_TOTAL_LBS", "LON_FUEL_FLOW1", "LON_FUEL_FLOW2"
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
            // ── Output readouts (Shift+A/S/H/V + fuel) — read the SELECTED AP values ──
            case HotkeyAction.ReadAltitude:
                AnnounceCached(simConnect, announcer, "LON_AP_ALT_STATE", "Selected altitude", "feet", 0);
                return true;
            case HotkeyAction.ReadHeading:
                AnnounceCached(simConnect, announcer, "LON_AP_HDG_STATE", "Selected heading", "degrees", 0);
                return true;
            case HotkeyAction.ReadSpeed:
                AnnounceCached(simConnect, announcer, "LON_AP_SPD_STATE", "Selected airspeed", "knots", 0);
                return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                AnnounceCached(simConnect, announcer, "LON_AP_VS_STATE", "Selected vertical speed", "feet per minute", 0);
                return true;

            case HotkeyAction.ReadFuelQuantity:
            case HotkeyAction.ReadFuelInfo:
            {
                double? lbs   = simConnect.GetCachedVariableValue("LON_FUEL_TOTAL_LBS");
                double? flow1 = simConnect.GetCachedVariableValue("LON_FUEL_FLOW1");
                double? flow2 = simConnect.GetCachedVariableValue("LON_FUEL_FLOW2");
                var parts = new List<string>();
                if (lbs.HasValue) parts.Add($"Total fuel {lbs.Value:F0} pounds");
                if (flow1.HasValue && flow2.HasValue)
                {
                    double total = flow1.Value + flow2.Value;
                    parts.Add($"fuel flow {flow1.Value:F0} and {flow2.Value:F0}, total {total:F0} pounds per hour");
                    if (lbs.HasValue && total > 1.0)
                        parts.Add($"endurance {lbs.Value / total:F1} hours");
                }
                announcer.AnnounceImmediate(parts.Count > 0 ? string.Join(", ", parts) : "Fuel data not available yet");
                return true;
            }

            case HotkeyAction.ReadDistanceToDest:
            {
                double? wp  = simConnect.GetCachedVariableValue("LON_GPS_WP_DIST");
                double? ete = simConnect.GetCachedVariableValue("LON_GPS_ETE");
                var parts = new List<string>();
                if (wp.HasValue && wp.Value > 0) parts.Add($"Next waypoint {wp.Value:F1} miles");
                if (ete.HasValue && ete.Value > 0 && ete.Value < 86400)
                {
                    int m = (int)Math.Round(ete.Value / 60.0);
                    parts.Add(m >= 60 ? $"destination {m / 60} hours {m % 60} minutes" : $"destination {m} minutes");
                }
                announcer.AnnounceImmediate(parts.Count > 0 ? string.Join(", ", parts) : "Distance to destination not available");
                return true;
            }

            case HotkeyAction.ReadILSGuidance:
            {
                double? freq   = simConnect.GetCachedVariableValue("LON_NAV1_FREQ");
                double? hasLoc = simConnect.GetCachedVariableValue("LON_NAV1_HASLOC");
                double? cdi    = simConnect.GetCachedVariableValue("LON_NAV1_CDI");
                double? hasGs  = simConnect.GetCachedVariableValue("LON_NAV1_HASGS");
                double? gsi    = simConnect.GetCachedVariableValue("LON_NAV1_GSI");
                double? dme    = simConnect.GetCachedVariableValue("LON_NAV1_DME");
                if (!freq.HasValue || freq.Value < 108)
                {
                    announcer.AnnounceImmediate("NAV 1 not tuned to an ILS");
                    return true;
                }
                var parts = new List<string> { $"NAV 1 {freq.Value:F2}" };
                if (hasLoc.HasValue && hasLoc.Value > 0)
                {
                    if (cdi.HasValue)
                        parts.Add(Math.Abs(cdi.Value) < 5 ? "localizer centered"
                            : cdi.Value > 0 ? $"localizer {Math.Abs(cdi.Value) / 12.7:F1} dots right"
                                            : $"localizer {Math.Abs(cdi.Value) / 12.7:F1} dots left");
                    if (hasGs.HasValue && hasGs.Value > 0 && gsi.HasValue)
                        parts.Add(Math.Abs(gsi.Value) < 5 ? "glideslope centered"
                            : gsi.Value > 0 ? $"glideslope {Math.Abs(gsi.Value) / 12.7:F1} dots low (fly down)"
                                            : $"glideslope {Math.Abs(gsi.Value) / 12.7:F1} dots high (fly up)");
                    if (dme.HasValue && dme.Value > 0) parts.Add($"DME {dme.Value:F1} miles");
                }
                else parts.Add("no localizer signal");
                announcer.AnnounceImmediate(string.Join(", ", parts));
                return true;
            }

            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                return FcuSetViaCdp("Set Selected Altitude", "Altitude", "0 to 45000 feet",
                    "AP_ALT_VAR_SET_ENGLISH", "feet", announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 0 && v <= 45000, "Enter 0 to 45000 feet"),
                    new List<ToggleButtonDef>
                    {
                        new("&FLC climb/descend to selected", () => ApStateText(simConnect, "LON_AP_FLC"),
                            () => { _ = Fms?.SendApCommandAsync("FLIGHT_LEVEL_CHANGE"); }),
                        new("&VNAV", () => "press to toggle",
                            () => { _ = Fms?.SendApCommandAsync("AP_VNAV_HOLD"); }),
                        new("&Altitude hold", () => ApStateText(simConnect, "LON_AP_ALT"),
                            () => { _ = Fms?.SendApCommandAsync("AP_ALT_HOLD"); }),
                    });

            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                return FcuSetViaCdp("Set Heading Bug", "Heading", "0 to 359",
                    "HEADING_BUG_SET", "degrees", announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 0 && v <= 359, "Enter a heading 0 to 359"),
                    new List<ToggleButtonDef>
                    {
                        new("&HDG mode", () => ApStateText(simConnect, "LON_AP_HDG"),
                            () => { _ = Fms?.SendApCommandAsync("AP_HDG_HOLD"); }),
                        new("&NAV mode (follow flight plan)", () => ApStateText(simConnect, "LON_AP_NAV"),
                            () => { _ = Fms?.SendApCommandAsync("AP_NAV1_HOLD"); }),
                    });

            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                return FcuSetViaCdp("Set Selected Airspeed", "Airspeed", "80 to 350 knots",
                    "AP_SPD_VAR_SET", "knots", announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= 80 && v <= 350, "Enter 80 to 350 knots"),
                    new List<ToggleButtonDef>
                    {
                        new("&FLC (speed mode)", () => ApStateText(simConnect, "LON_AP_FLC"),
                            () => { _ = Fms?.SendApCommandAsync("FLIGHT_LEVEL_CHANGE"); }),
                    });

            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                return FcuSetViaCdp("Set Vertical Speed", "Vertical Speed", "-6000 to 6000 fpm",
                    "AP_VS_VAR_SET_ENGLISH", "feet per minute", announcer, parentForm,
                    input => (double.TryParse(input, out double v) && v >= -6000 && v <= 6000, "Enter -6000 to 6000 fpm"),
                    new List<ToggleButtonDef>
                    {
                        new("&VS mode", () => ApStateText(simConnect, "LON_AP_VS"),
                            () => { _ = Fms?.SendApCommandAsync("AP_VS_HOLD"); }),
                        new("&Altitude hold", () => ApStateText(simConnect, "LON_AP_ALT"),
                            () => { _ = Fms?.SendApCommandAsync("AP_ALT_HOLD"); }),
                    });
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    private static string ApStateText(SimConnectManager sc, string key)
        => (sc.GetCachedVariableValue(key) ?? 0) > 0 ? "ON" : "Off";

    /// <summary>Show an accessible value dialog (with optional AP mode toggle buttons)
    /// and send the value-set through CDP — SimConnect events don't drive the WT
    /// autopilot. Non-modal so other windows stay accessible (airliner pattern).</summary>
    private bool FcuSetViaCdp(string title, string param, string range, string kEvent, string unit,
        ScreenReaderAnnouncer announcer, Form parentForm, Func<string, (bool, string)> validator,
        List<ToggleButtonDef>? toggles = null)
    {
        if (Fms == null)
        {
            announcer.AnnounceImmediate("FMS link not ready. Open the Longitude FMS window once with input mode then Shift M.");
            return true;
        }
        var dlg = new ValueInputForm(title, param, range, announcer, validator,
            toggles ?? new List<ToggleButtonDef>(),
            input => { if (double.TryParse(input.Trim(), out double val)) _ = Fms.SendApCommandAsync(kEvent, val, unit); });
        dlg.ShowCancelButton = false;
        dlg.Show(parentForm);
        return true;
    }

    /// <summary>Route autopilot mode toggles and value-sets through the CDP channel —
    /// SimConnect TransmitClientEvent does not reach the WT G3000/G5000 autopilot.</summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (ApEventMap.TryGetValue(varKey, out string? ev))
        {
            if (Fms == null) { announcer.AnnounceImmediate("FMS link not ready. Open the Longitude FMS window once."); return true; }
            _ = Fms.SendApCommandAsync(ev);
            return true;
        }
        if (ApValueMap.TryGetValue(varKey, out (string ev, string unit) vm))
        {
            if (Fms == null) { announcer.AnnounceImmediate("FMS link not ready. Open the Longitude FMS window once."); return true; }
            _ = Fms.SendApCommandAsync(vm.ev, value, vm.unit);
            return true;
        }
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    private static void AnnounceCached(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string key, string label, string units, int decimals)
    {
        double? v = simConnect.GetCachedVariableValue(key);
        if (v.HasValue)
            announcer.AnnounceImmediate($"{label} {v.Value.ToString("F" + decimals)} {units}");
        else
        {
            simConnect.RequestVariable(key);
            announcer.AnnounceImmediate($"{label} not available yet");
        }
    }

    /// <summary>Suppress auto-announcement of the continuously-cached readout variables —
    /// they exist only so the output hotkeys can read them instantly, on demand.</summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (SuppressedKeys.Contains(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    public override Dictionary<string, string> GetButtonStateMapping() => new()
    {
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
