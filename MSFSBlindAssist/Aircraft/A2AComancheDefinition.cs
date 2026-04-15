using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// A2A Piper PA-24 Comanche 250 for MSFS.
/// Uses L-variables for almost all controls (read/write via SetLVar).
/// Custom S-TEC System 30 autopilot (not default MSFS AP).
/// Retractable gear, constant-speed prop, 6-cylinder Lycoming O-540-A.
/// </summary>
public class A2AComancheDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "A2A Comanche 250";
    public override string AircraftCode => "A2A_COMANCHE";

    // Warning debounce tracking
    private bool _lowFuelLeftWarned = false;
    private bool _lowFuelRightWarned = false;
    private bool _lowOilPressureWarned = false;
    private bool _highCHTWarned = false;
    private bool _rpmRedlineWarned = false;
    private bool _lowVoltageWarned = false;
    private bool _stallWarningActive = false;

    // S-TEC AP light state tracking for transition announcements
    private double _lastApRdyLight = -1;
    private double _lastApAltLight = -1;
    private double _lastApStLight = -1;
    private double _lastApHdLight = -1;
    private double _lastApTrkLoLight = -1;
    private double _lastApTrkHiLight = -1;

    // S-TEC has no standard MSFS AP bugs
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        var variables = GetBaseVariables();
        var comancheVars = new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // ===== ELECTRICAL =====

            ["CMNCH_BATTERY"] = new SimConnect.SimVarDefinition
            {
                Name = "Battery1Switch",
                DisplayName = "Battery Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_AVIONICS_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS MASTER SWITCH:1",
                DisplayName = "Avionics Master",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_AVIONICS_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_AVIONICS_MASTER",
                DisplayName = "Avionics Master Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== CIRCUIT BREAKERS (13 total) =====

            ["CMNCH_CB_GEAR_MOTOR"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerLandingGearMotor",
                DisplayName = "CB: Gear Motor",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_GEAR_IND"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerLandingGearIndicator",
                DisplayName = "CB: Gear Indicator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_GENERATOR"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerGeneratorOutput",
                DisplayName = "CB: Generator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_NAV_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerLightNav",
                DisplayName = "CB: Nav Lights",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_ENGINE_GAUGES"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerGaugesEngine",
                DisplayName = "CB: Engine Gauges",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_LANDING_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerLightLanding",
                DisplayName = "CB: Landing Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_BEACON"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerLightBeacon",
                DisplayName = "CB: Beacon",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_PITOT_HEAT"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerPitotHeat",
                DisplayName = "CB: Pitot Heat",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_NAVCOM2"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerNavCom2",
                DisplayName = "CB: Nav/Com 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_NAVCOM1"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerNavCom1",
                DisplayName = "CB: Nav/Com 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_STALL_WARN"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerWarningStall",
                DisplayName = "CB: Stall Warning",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_STARTER"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerStarter",
                DisplayName = "CB: Starter",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },
            ["CMNCH_CB_AUTOPILOT"] = new SimConnect.SimVarDefinition
            {
                Name = "BreakerAutopilot",
                DisplayName = "CB: Autopilot",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Pulled", [1] = "In" }
            },

            // ===== ENGINE CONTROLS =====

            ["CMNCH_MAGNETO"] = new SimConnect.SimVarDefinition
            {
                Name = "Magnetos1",
                DisplayName = "Magneto",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off", [1] = "Right", [2] = "Left", [3] = "Both", [4] = "Start"
                }
            },
            ["CMNCH_STARTER"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_StarterSwitch",
                DisplayName = "Starter",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                HelpText = "Engage the engine starter"
            },
            ["CMNCH_THROTTLE"] = new SimConnect.SimVarDefinition
            {
                Name = "Throttle1Position",
                DisplayName = "Throttle",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Enter throttle percentage (0 = idle, 100 = full)"
            },
            ["CMNCH_MIXTURE"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_MixtureManualLever",
                DisplayName = "Mixture",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Enter mixture percentage (0 = idle cutoff, 100 = full rich)"
            },
            ["CMNCH_PROP_RPM"] = new SimConnect.SimVarDefinition
            {
                Name = "RPMLever1Position",
                DisplayName = "Propeller RPM",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Enter prop RPM lever percentage (0 = low RPM, 100 = full forward)"
            },
            ["CMNCH_CARB_HEAT"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_CarbHeatSwitch",
                DisplayName = "Carb Heat",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ===== PRIMER =====

            ["CMNCH_PRIMER_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "PrimerState",
                DisplayName = "Primer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Locked", [1] = "Open", [2] = "Pump"
                }
            },
            ["CMNCH_PRIMER_PUMP"] = new SimConnect.SimVarDefinition
            {
                Name = "PrimerPump",
                DisplayName = "Primer Pump",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Pump the primer to inject fuel"
            },

            // ===== FUEL SYSTEM =====

            ["CMNCH_FUEL_SEL_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "FSelComancheLeftState",
                DisplayName = "Fuel Selector Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off", [1] = "On"
                }
            },
            ["CMNCH_FUEL_SEL_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "FSelComancheRightState",
                DisplayName = "Fuel Selector Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off", [1] = "On"
                }
            },
            ["CMNCH_FUEL_IND_SW"] = new SimConnect.SimVarDefinition
            {
                Name = "FuelIndicatorSwitch",
                DisplayName = "Fuel Indicator Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_FUEL_PUMP_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG FUEL PUMP SWITCH:1",
                DisplayName = "Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_FUEL_PUMP_TOGGLE"] = new SimConnect.SimVarDefinition
            {
                Name = "TOGGLE_ELECT_FUEL_PUMP1",
                DisplayName = "Fuel Pump Toggle",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },

            // ===== EXTERIOR LIGHTS =====

            ["CMNCH_BEACON"] = new SimConnect.SimVarDefinition
            {
                Name = "BeaconLightSwitch",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_STROBE"] = new SimConnect.SimVarDefinition
            {
                Name = "StrobeLightSwitch",
                DisplayName = "Strobe Lights",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_LANDING_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "LandingLightLeftSwitch",
                DisplayName = "Landing Light Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_LANDING_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "LandingLightRightSwitch",
                DisplayName = "Landing Light Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_NAV_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "NavInstrLightSwitchPct",
                DisplayName = "Nav/Instrument Lights",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "0 = off, above 10% turns on nav lights, higher = brighter instruments"
            },
            ["CMNCH_PITOT_HEAT"] = new SimConnect.SimVarDefinition
            {
                Name = "PitotHeatSwitch",
                DisplayName = "Pitot Heat",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ===== INTERIOR LIGHTS =====

            ["CMNCH_CABIN_FLOOD1"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinFlood1LightSwitch",
                DisplayName = "Cabin Flood Light Front",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_CABIN_FLOOD2"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinFlood2LightSwitch",
                DisplayName = "Cabin Flood Light Rear",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["CMNCH_CABIN_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinRedLightSwitch",
                DisplayName = "Cabin Red Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ===== FLIGHT CONTROLS =====

            ["CMNCH_FLAPS"] = new SimConnect.SimVarDefinition
            {
                Name = "LandFlapsPos",
                DisplayName = "Flaps",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up", [1] = "Approach 15", [2] = "Approach 25", [3] = "Full"
                }
            },
            ["CMNCH_GEAR"] = new SimConnect.SimVarDefinition
            {
                Name = "LandingGearLeverPos",
                DisplayName = "Landing Gear",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up", [1] = "Down"
                }
            },
            ["CMNCH_PARKING_BRAKE"] = new SimConnect.SimVarDefinition
            {
                Name = "ParkingBrakePosition",
                DisplayName = "Parking Brake",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Set" }
            },

            // ===== AUTOPILOT (S-TEC System 30) =====

            ["CMNCH_AP_MASTER"] = new SimConnect.SimVarDefinition
            {
                Name = "ApMaster",
                DisplayName = "Autopilot Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["CMNCH_AP_ALT_SW"] = new SimConnect.SimVarDefinition
            {
                Name = "ApAltSwitch",
                DisplayName = "AP Altitude Hold",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" }
            },
            ["CMNCH_AP_MODE_PUSH"] = new SimConnect.SimVarDefinition
            {
                Name = "ApModePushSwitch",
                DisplayName = "AP Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Cycles autopilot modes: Heading → Track Lo → Track Hi"
            },
            ["CMNCH_AP_DISCONNECT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApDisconnectSwitch",
                DisplayName = "AP Disconnect",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Disengage the autopilot"
            },
            ["CMNCH_AP_TURN_KNOB"] = new SimConnect.SimVarDefinition
            {
                Name = "ApTurnKnob",
                DisplayName = "AP Turn Knob",
                Type = SimConnect.SimVarType.LVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Bank angle: -50 (left) to 0 (center) to 50 (right)"
            },

            // AP indicator lights (continuous monitoring)
            ["CMNCH_AP_RDY_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApRdyLight",
                DisplayName = "AP Ready",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "AP ready off", [1] = "AP ready" }
            },
            ["CMNCH_AP_ALT_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApAltLight",
                DisplayName = "AP Altitude",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Altitude hold off", [1] = "Altitude hold" }
            },
            ["CMNCH_AP_ST_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApStLight",
                DisplayName = "AP Steering",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Steering off", [1] = "Steering active" }
            },
            ["CMNCH_AP_HD_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApHdLight",
                DisplayName = "AP Heading",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Heading mode off", [1] = "Heading mode" }
            },
            ["CMNCH_AP_TRKLO_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApTrkLoLight",
                DisplayName = "AP Track Lo",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Track low off", [1] = "Track low sensitivity" }
            },
            ["CMNCH_AP_TRKHI_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApTrkHiLight",
                DisplayName = "AP Track Hi",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Track high off", [1] = "Track high sensitivity" }
            },
            ["CMNCH_AP_TRIMUP_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApTrimUpLight",
                DisplayName = "AP Trim Up",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Trim up cleared", [1] = "Trim up required" }
            },
            ["CMNCH_AP_TRIMDN_LIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "ApTrimDnLight",
                DisplayName = "AP Trim Down",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Trim down cleared", [1] = "Trim down required" }
            },
            ["CMNCH_AP_LOW_VOLTAGE"] = new SimConnect.SimVarDefinition
            {
                Name = "ApLowVoltageFlag",
                DisplayName = "Low Voltage",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Voltage normal", [1] = "Low voltage warning" }
            },

            // ===== RADIOS =====

            // COM1
            ["CMNCH_COM1_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "COM1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_COM1_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "COM1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_COM1_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM_STBY_RADIO_SWAP",
                DisplayName = "COM1 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["CMNCH_COM1_ONOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "Com1OnOffVolume",
                DisplayName = "COM1 Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Volume/power knob (0 = off, higher = louder)"
            },
            // COM2
            ["CMNCH_COM2_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:2",
                DisplayName = "COM2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_COM2_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:2",
                DisplayName = "COM2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_COM2_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM2_RADIO_SWAP",
                DisplayName = "COM2 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            ["CMNCH_COM2_ONOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "Com2OnOffVolume",
                DisplayName = "COM2 Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "Volume/power knob (0 = off, higher = louder)"
            },
            // NAV1
            ["CMNCH_NAV1_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV ACTIVE FREQUENCY:1",
                DisplayName = "NAV1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_NAV1_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV STANDBY FREQUENCY:1",
                DisplayName = "NAV1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_NAV1_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV1_RADIO_SWAP",
                DisplayName = "NAV1 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // NAV2
            ["CMNCH_NAV2_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV ACTIVE FREQUENCY:2",
                DisplayName = "NAV2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_NAV2_STANDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV STANDBY FREQUENCY:2",
                DisplayName = "NAV2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_NAV2_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV2_RADIO_SWAP",
                DisplayName = "NAV2 Swap",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true
            },
            // ADF
            ["CMNCH_ADF_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "AdfModeSelectSwitch",
                DisplayName = "ADF Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "ADF", [1] = "ANT", [2] = "Off"
                }
            },
            // Transponder
            ["CMNCH_XPDR_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "XpdrModeKnobPos",
                DisplayName = "Transponder Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off", [1] = "Standby", [2] = "On", [3] = "Alt", [4] = "Test"
                }
            },
            ["CMNCH_XPDR_CODE"] = new SimConnect.SimVarDefinition
            {
                Name = "TRANSPONDER CODE:1",
                DisplayName = "Transponder Code",
                Type = SimConnect.SimVarType.SimVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_XPDR_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "XPNDR_SET",
                DisplayName = "Set Transponder",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            ["CMNCH_XPDR_IDENT"] = new SimConnect.SimVarDefinition
            {
                Name = "XpdrIdentSwitch",
                DisplayName = "Transponder Ident",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            // Audio selector
            ["CMNCH_AUDIO_COM"] = new SimConnect.SimVarDefinition
            {
                Name = "AudioComSwitch",
                DisplayName = "Audio COM Select",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "COM1", [1] = "COM2" }
            },
            ["CMNCH_AUDIO_SPEAKER"] = new SimConnect.SimVarDefinition
            {
                Name = "AudioSpkrSwitch",
                DisplayName = "Speaker",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            // DME
            ["CMNCH_DME_FUNC"] = new SimConnect.SimVarDefinition
            {
                Name = "DmeFunction",
                DisplayName = "DME Function",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off", [1] = "1", [2] = "2", [3] = "3", [4] = "4", [5] = "Hold"
                }
            },

            // ===== CABIN =====

            ["CMNCH_CABIN_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinTempControl",
                DisplayName = "Cabin Heat",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "0 = off, 100 = full heat"
            },
            ["CMNCH_DEFROSTER"] = new SimConnect.SimVarDefinition
            {
                Name = "WindowDefrosterControlKnob",
                DisplayName = "Defroster",
                Type = SimConnect.SimVarType.LVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                HelpText = "0 = off, 100 = full"
            },
            ["CMNCH_VENT_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinVentLeftLever",
                DisplayName = "Left Vent",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["CMNCH_VENT_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "CabinVentRightLever",
                DisplayName = "Right Vent",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["CMNCH_WINDOW_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "WindowLeft",
                DisplayName = "Left Window",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["CMNCH_DOOR"] = new SimConnect.SimVarDefinition
            {
                Name = "Door1Handle",
                DisplayName = "Door",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },

            // ===== CONTINUOUS MONITORING =====

            ["CMNCH_ENGINE_RPM"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_RPM",
                DisplayName = "Engine RPM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_MANIFOLD_PRESSURE"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_ManifoldPressure",
                DisplayName = "Manifold Pressure",
                Type = SimConnect.SimVarType.LVar,
                Units = "inHg",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_EGT"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_EGTGauge",
                DisplayName = "EGT",
                Type = SimConnect.SimVarType.LVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_CHT"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_CHTGauge",
                DisplayName = "CHT",
                Type = SimConnect.SimVarType.LVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_OIL_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_OilTempGauge",
                DisplayName = "Oil Temperature",
                Type = SimConnect.SimVarType.LVar,
                Units = "Fahrenheit",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_OIL_PRESSURE"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_OilPressureGauge",
                DisplayName = "Oil Pressure",
                Type = SimConnect.SimVarType.LVar,
                Units = "psi",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_FUEL_FLOW"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_GPH",
                DisplayName = "Fuel Flow",
                Type = SimConnect.SimVarType.LVar,
                Units = "gallons per hour",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_SUCTION"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_SuctionPressure",
                DisplayName = "Suction",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_AMMETER"] = new SimConnect.SimVarDefinition
            {
                Name = "Ammeter1",
                DisplayName = "Ammeter",
                Type = SimConnect.SimVarType.LVar,
                Units = "amps",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            // Fuel tank quantities — use the A2A tank L-vars (actual gallons), not FuelGauge1/2/3 (gauge needle positions)
            ["CMNCH_FUEL_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "FuelLeftWingTank",
                DisplayName = "Left Wing Fuel",
                Type = SimConnect.SimVarType.LVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_FUEL_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "FuelRightWingTank",
                DisplayName = "Right Wing Fuel",
                Type = SimConnect.SimVarType.LVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_FUEL_TIP_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "FuelLeftTipTank",
                DisplayName = "Left Tip Fuel",
                Type = SimConnect.SimVarType.LVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_FUEL_TIP_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "FuelRightTipTank",
                DisplayName = "Right Tip Fuel",
                Type = SimConnect.SimVarType.LVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CMNCH_TIP_TANKS_INSTALLED"] = new SimConnect.SimVarDefinition
            {
                Name = "TipTank",
                DisplayName = "Tip Tanks Installed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
            },
            ["CMNCH_STALL_WARNING"] = new SimConnect.SimVarDefinition
            {
                Name = "LightStallWarning",
                DisplayName = "Stall Warning",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stall warning cleared",
                    [1] = "Stall warning!"
                }
            },
            ["CMNCH_GEAR_LIGHTS"] = new SimConnect.SimVarDefinition
            {
                Name = "LGlights",
                DisplayName = "Gear Indicator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                AnnounceValueOnly = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Gear unsafe",
                    [1] = "Gear down and locked",
                    [2] = "Gear in transit"
                }
            },

            // ===== FLIGHT DATA (for hotkey readouts) =====

            ["CMNCH_AIRSPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "AirspeedNeedle",
                DisplayName = "Airspeed",
                Type = SimConnect.SimVarType.LVar,
                Units = "mph",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_VERTICAL_SPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "VerticalSpeed",
                DisplayName = "Vertical Speed",
                Type = SimConnect.SimVarType.LVar,
                Units = "feet per minute",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_HEADING"] = new SimConnect.SimVarDefinition
            {
                Name = "HeadingGyro",
                DisplayName = "Heading",
                Type = SimConnect.SimVarType.LVar,
                Units = "degrees",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_ALTIMETER"] = new SimConnect.SimVarDefinition
            {
                Name = "KOHLSMAN SETTING MB:1",
                DisplayName = "Altimeter Setting",
                Type = SimConnect.SimVarType.SimVar,
                Units = "millibars",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            // ===== WALKAROUND =====

            ["CMNCH_PITOT_COVER"] = new SimConnect.SimVarDefinition
            {
                Name = "PitotTubeCover",
                DisplayName = "Pitot Tube Cover",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Installed" }
            },

            // ===== MAINTENANCE / HANGAR VARIABLES (OnRequest, read by hangar form) =====

            ["CMNCH_MAINT_ENGINE_HOURS"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_Time",
                DisplayName = "Engine Hours",
                Type = SimConnect.SimVarType.LVar,
                Units = "hours",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_MAINT_AIRFRAME_HOURS"] = new SimConnect.SimVarDefinition
            {
                Name = "TotalTime",
                DisplayName = "Airframe Hours",
                Type = SimConnect.SimVarType.LVar,
                Units = "hours",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_MAINT_OIL_QTY"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_OilQuantity",
                DisplayName = "Oil Quantity",
                Type = SimConnect.SimVarType.LVar,
                Units = "number",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_MAINT_OIL_GRADE"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_OilGrade",
                DisplayName = "Oil Grade",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            ["CMNCH_MAINT_SPARK_TYPE"] = new SimConnect.SimVarDefinition
            {
                Name = "Eng1_SparkPlugType",
                DisplayName = "Spark Plug Type",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },
            // Cylinder compression (6 cylinders)
            ["CMNCH_MAINT_COMP_1"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[1]", DisplayName = "Cyl 1 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_COMP_2"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[2]", DisplayName = "Cyl 2 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_COMP_3"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[3]", DisplayName = "Cyl 3 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_COMP_4"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[4]", DisplayName = "Cyl 4 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_COMP_5"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[5]", DisplayName = "Cyl 5 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_COMP_6"] = new SimConnect.SimVarDefinition { Name = "Eng1_CylComp[6]", DisplayName = "Cyl 6 Compression", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            // Spark plugs (6 cylinders x 2 = 12)
            ["CMNCH_MAINT_PLUG_1L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl1_SparkPlugL", DisplayName = "Cyl 1 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_1R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl1_SparkPlugR", DisplayName = "Cyl 1 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_2L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl2_SparkPlugL", DisplayName = "Cyl 2 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_2R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl2_SparkPlugR", DisplayName = "Cyl 2 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_3L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl3_SparkPlugL", DisplayName = "Cyl 3 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_3R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl3_SparkPlugR", DisplayName = "Cyl 3 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_4L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl4_SparkPlugL", DisplayName = "Cyl 4 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_4R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl4_SparkPlugR", DisplayName = "Cyl 4 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_5L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl5_SparkPlugL", DisplayName = "Cyl 5 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_5R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl5_SparkPlugR", DisplayName = "Cyl 5 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_6L"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl6_SparkPlugL", DisplayName = "Cyl 6 Left Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_PLUG_6R"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Cyl6_SparkPlugR", DisplayName = "Cyl 6 Right Plug", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            // Component conditions
            ["CMNCH_MAINT_C_MAIN"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Main", DisplayName = "Crankshaft", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_CARB"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Carb", DisplayName = "Carburetor", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_MAGL"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_MagL", DisplayName = "Left Magneto", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_MAGR"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_MagR", DisplayName = "Right Magneto", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_OILPUMP"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_OilPump", DisplayName = "Oil Pump", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_OILSYS"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Oilsystem", DisplayName = "Oil System", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_FUELPUMP_M"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_FuelPumpMechanical", DisplayName = "Fuel Pump Mech", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_FUELPUMP_E"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_FuelPumpElectrical", DisplayName = "Fuel Pump Elec", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_FUELSYS"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Fuelsystem", DisplayName = "Fuel System", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_AIRFILTER"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_AirFilter", DisplayName = "Air Filter", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_VACUUM"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_VacuumPump", DisplayName = "Vacuum Pump", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_GENERATOR"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Generator", DisplayName = "Generator", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_STARTER"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Starter", DisplayName = "Starter", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_PROP"] = new SimConnect.SimVarDefinition { Name = "C_Eng1_Prop", DisplayName = "Propeller", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_BATT"] = new SimConnect.SimVarDefinition { Name = "C_Battery1", DisplayName = "Battery", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_TIREL"] = new SimConnect.SimVarDefinition { Name = "C_TireLeft", DisplayName = "Left Tire", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_TIRER"] = new SimConnect.SimVarDefinition { Name = "C_TireRight", DisplayName = "Right Tire", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_TIREC"] = new SimConnect.SimVarDefinition { Name = "C_TireCenter", DisplayName = "Nose Tire", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_BRAKEL"] = new SimConnect.SimVarDefinition { Name = "C_BrakesLeft", DisplayName = "Left Brakes", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_BRAKER"] = new SimConnect.SimVarDefinition { Name = "C_BrakesRight", DisplayName = "Right Brakes", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_GEARL"] = new SimConnect.SimVarDefinition { Name = "C_GearLeft", DisplayName = "Left Gear", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_GEARR"] = new SimConnect.SimVarDefinition { Name = "C_GearRight", DisplayName = "Right Gear", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_GEARC"] = new SimConnect.SimVarDefinition { Name = "C_GearCenter", DisplayName = "Nose Gear", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
            ["CMNCH_MAINT_C_GEARMOTOR"] = new SimConnect.SimVarDefinition { Name = "C_GearMainMotor", DisplayName = "Gear Motor", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest }
        };

        foreach (var kvp in comancheVars)
            variables[kvp.Key] = kvp.Value;
        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Electrical"] = new List<string> { "Battery and Generator", "Circuit Breakers" },
            ["Engine"] = new List<string> { "Engine Controls", "Fuel System", "Primer" },
            ["Lights"] = new List<string> { "Exterior Lights", "Interior Lights" },
            ["Flight Controls"] = new List<string> { "Controls", "Landing Gear" },
            ["Autopilot"] = new List<string> { "S-TEC System 30" },
            ["Radios"] = new List<string> { "COM", "NAV", "ADF", "Transponder", "Audio", "DME" },
            ["Cabin"] = new List<string> { "Climate", "Windows and Doors" },
            ["Safety"] = new List<string> { "Walkaround" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Battery and Generator"] = new List<string>
            {
                "CMNCH_BATTERY",
                "CMNCH_AVIONICS_STATE",
                "CMNCH_FUEL_PUMP_STATE"
            },
            ["Circuit Breakers"] = new List<string>
            {
                "CMNCH_CB_GEAR_MOTOR", "CMNCH_CB_GEAR_IND", "CMNCH_CB_GENERATOR",
                "CMNCH_CB_NAV_LIGHT", "CMNCH_CB_ENGINE_GAUGES", "CMNCH_CB_LANDING_LIGHT",
                "CMNCH_CB_BEACON", "CMNCH_CB_PITOT_HEAT", "CMNCH_CB_NAVCOM2",
                "CMNCH_CB_NAVCOM1", "CMNCH_CB_STALL_WARN", "CMNCH_CB_STARTER",
                "CMNCH_CB_AUTOPILOT"
            },
            ["Engine Controls"] = new List<string>
            {
                "CMNCH_MAGNETO", "CMNCH_STARTER",
                "CMNCH_THROTTLE", "CMNCH_MIXTURE", "CMNCH_PROP_RPM",
                "CMNCH_CARB_HEAT"
            },
            ["Fuel System"] = new List<string>
            {
                "CMNCH_FUEL_SEL_LEFT", "CMNCH_FUEL_SEL_RIGHT",
                "CMNCH_FUEL_IND_SW",
                "CMNCH_FUEL_PUMP_STATE"
            },
            ["Primer"] = new List<string>
            {
                "CMNCH_PRIMER_STATE", "CMNCH_PRIMER_PUMP"
            },
            ["Exterior Lights"] = new List<string>
            {
                "CMNCH_BEACON", "CMNCH_STROBE",
                "CMNCH_LANDING_LEFT", "CMNCH_LANDING_RIGHT",
                "CMNCH_NAV_LIGHT", "CMNCH_PITOT_HEAT"
            },
            ["Interior Lights"] = new List<string>
            {
                "CMNCH_CABIN_FLOOD1", "CMNCH_CABIN_FLOOD2", "CMNCH_CABIN_RED"
            },
            ["Controls"] = new List<string>
            {
                "CMNCH_FLAPS", "CMNCH_PARKING_BRAKE"
            },
            ["Landing Gear"] = new List<string>
            {
                "CMNCH_GEAR"
            },
            ["S-TEC System 30"] = new List<string>
            {
                "CMNCH_AP_MASTER", "CMNCH_AP_ALT_SW",
                "CMNCH_AP_MODE_PUSH", "CMNCH_AP_DISCONNECT",
                "CMNCH_AP_TURN_KNOB"
            },
            ["COM"] = new List<string>
            {
                "CMNCH_COM1_ONOFF", "CMNCH_COM1_ACTIVE", "CMNCH_COM1_STANDBY", "CMNCH_COM1_SWAP",
                "CMNCH_COM2_ONOFF", "CMNCH_COM2_ACTIVE", "CMNCH_COM2_STANDBY", "CMNCH_COM2_SWAP"
            },
            ["NAV"] = new List<string>
            {
                "CMNCH_NAV1_ACTIVE", "CMNCH_NAV1_STANDBY", "CMNCH_NAV1_SWAP",
                "CMNCH_NAV2_ACTIVE", "CMNCH_NAV2_STANDBY", "CMNCH_NAV2_SWAP"
            },
            ["ADF"] = new List<string>
            {
                "CMNCH_ADF_MODE"
            },
            ["Transponder"] = new List<string>
            {
                "CMNCH_XPDR_MODE", "CMNCH_XPDR_CODE", "CMNCH_XPDR_SET", "CMNCH_XPDR_IDENT"
            },
            ["Audio"] = new List<string>
            {
                "CMNCH_AUDIO_COM", "CMNCH_AUDIO_SPEAKER"
            },
            ["DME"] = new List<string>
            {
                "CMNCH_DME_FUNC"
            },
            ["Climate"] = new List<string>
            {
                "CMNCH_CABIN_TEMP", "CMNCH_DEFROSTER",
                "CMNCH_VENT_LEFT", "CMNCH_VENT_RIGHT"
            },
            ["Windows and Doors"] = new List<string>
            {
                "CMNCH_WINDOW_LEFT", "CMNCH_DOOR"
            },
            ["Walkaround"] = new List<string>
            {
                "CMNCH_PITOT_COVER"
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
            ["Battery and Generator"] = new List<string> { "CMNCH_AMMETER" },
            ["Fuel System"] = new List<string> { "CMNCH_FUEL_LEFT", "CMNCH_FUEL_RIGHT", "CMNCH_FUEL_TIP_LEFT", "CMNCH_FUEL_TIP_RIGHT" },
            ["Engine Controls"] = new List<string>
            {
                "CMNCH_ENGINE_RPM", "CMNCH_MANIFOLD_PRESSURE", "CMNCH_EGT", "CMNCH_CHT",
                "CMNCH_OIL_TEMP", "CMNCH_OIL_PRESSURE", "CMNCH_FUEL_FLOW", "CMNCH_SUCTION"
            },
            ["S-TEC System 30"] = new List<string>
            {
                "CMNCH_AP_RDY_LIGHT", "CMNCH_AP_ALT_LIGHT", "CMNCH_AP_ST_LIGHT",
                "CMNCH_AP_HD_LIGHT", "CMNCH_AP_TRKLO_LIGHT", "CMNCH_AP_TRKHI_LIGHT"
            },
            ["Landing Gear"] = new List<string> { "CMNCH_GEAR_LIGHTS" }
        };
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>();
    }

    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        // S-TEC doesn't use standard AP events — handled in HandleHotkeyAction
        return new Dictionary<HotkeyAction, string>();
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // Note: ReadHeading, ReadSpeed, ReadAltitude, ReadVerticalSpeed, ReadHeadingMagnetic,
            // ReadAirspeedIndicated, ReadAltitudeMSL, ReadAltitudeAGL, ReadGroundSpeed, ReadMachSpeed,
            // ReadHeadingTrue, ReadBankAngle, ReadPitch are ALL handled universally by MainForm.

            // Output+Shift+V — read vertical speed
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                double? vs = simConnect.GetCachedVariableValue("CMNCH_VERTICAL_SPEED");
                if (vs.HasValue)
                    announcer.AnnounceImmediate($"Vertical speed {vs.Value:F0} feet per minute");
                else
                {
                    simConnect.RequestVariable("CMNCH_VERTICAL_SPEED");
                    announcer.AnnounceImmediate("Requesting vertical speed");
                }
                return true;
            }

            // Output+F — read fuel quantity (all 4 tanks: wing + tip)
            case HotkeyAction.ReadFuelQuantity:
            {
                double? left = simConnect.GetCachedVariableValue("CMNCH_FUEL_LEFT");
                double? right = simConnect.GetCachedVariableValue("CMNCH_FUEL_RIGHT");
                double? tipLeft = simConnect.GetCachedVariableValue("CMNCH_FUEL_TIP_LEFT");
                double? tipRight = simConnect.GetCachedVariableValue("CMNCH_FUEL_TIP_RIGHT");
                double? tipInstalled = simConnect.GetCachedVariableValue("CMNCH_TIP_TANKS_INSTALLED");

                if (left.HasValue && right.HasValue)
                {
                    bool hasTips = tipInstalled.HasValue && tipInstalled.Value > 0.5;
                    double total = left.Value + right.Value;
                    string tipText = "";
                    if (hasTips)
                    {
                        double tipL = tipLeft.GetValueOrDefault();
                        double tipR = tipRight.GetValueOrDefault();
                        total += tipL + tipR;
                        tipText = $", Left Tip {tipL:F1}, Right Tip {tipR:F1}";
                    }
                    announcer.AnnounceImmediate(
                        $"Left Wing {left.Value:F1}, Right Wing {right.Value:F1}{tipText}, Total {total:F1} gallons");
                }
                else
                {
                    simConnect.RequestVariable("CMNCH_FUEL_LEFT");
                    simConnect.RequestVariable("CMNCH_FUEL_RIGHT");
                    simConnect.RequestVariable("CMNCH_FUEL_TIP_LEFT");
                    simConnect.RequestVariable("CMNCH_FUEL_TIP_RIGHT");
                    simConnect.RequestVariable("CMNCH_TIP_TANKS_INSTALLED");
                    announcer.AnnounceImmediate("Requesting fuel quantity");
                }
                return true;
            }

            // Output+Shift+F — fuel info with flow and endurance
            case HotkeyAction.ReadFuelInfo:
            {
                double? left = simConnect.GetCachedVariableValue("CMNCH_FUEL_LEFT");
                double? right = simConnect.GetCachedVariableValue("CMNCH_FUEL_RIGHT");
                double? tipLeft = simConnect.GetCachedVariableValue("CMNCH_FUEL_TIP_LEFT");
                double? tipRight = simConnect.GetCachedVariableValue("CMNCH_FUEL_TIP_RIGHT");
                double? tipInstalled = simConnect.GetCachedVariableValue("CMNCH_TIP_TANKS_INSTALLED");
                double? flow = simConnect.GetCachedVariableValue("CMNCH_FUEL_FLOW");

                if (left.HasValue && right.HasValue)
                {
                    bool hasTips = tipInstalled.HasValue && tipInstalled.Value > 0.5;
                    double total = left.Value + right.Value;
                    string tipText = "";
                    if (hasTips)
                    {
                        double tipL = tipLeft.GetValueOrDefault();
                        double tipR = tipRight.GetValueOrDefault();
                        total += tipL + tipR;
                        tipText = $", Tips {tipL + tipR:F1}";
                    }
                    string flowText = flow.HasValue ? $", Flow {flow.Value:F1} G P H" : "";
                    double? endurance = (flow.HasValue && flow.Value > 0.5) ? total / flow.Value : null;
                    string enduranceText = endurance.HasValue
                        ? $", Endurance {endurance.Value:F1} hours" : "";
                    announcer.AnnounceImmediate(
                        $"Left {left.Value:F1}, Right {right.Value:F1}{tipText}, Total {total:F1} gallons{flowText}{enduranceText}");
                }
                else
                {
                    announcer.AnnounceImmediate("Fuel data not available");
                }
                return true;
            }

            // Output+L — read flaps position
            case HotkeyAction.ReadFlaps:
            {
                double? flaps = simConnect.GetCachedVariableValue("CMNCH_FLAPS");
                if (flaps.HasValue)
                {
                    string position = (int)flaps.Value switch
                    {
                        0 => "Up",
                        1 => "Approach 15",
                        2 => "Approach 25",
                        3 => "Full",
                        _ => flaps.Value.ToString()
                    };
                    announcer.AnnounceImmediate($"Flaps {position}");
                }
                else
                {
                    simConnect.RequestVariable("CMNCH_FLAPS");
                    announcer.AnnounceImmediate("Requesting flaps");
                }
                return true;
            }

            // Output+Shift+G — read gear position
            case HotkeyAction.ReadGear:
            {
                double? gear = simConnect.GetCachedVariableValue("CMNCH_GEAR");
                double? lights = simConnect.GetCachedVariableValue("CMNCH_GEAR_LIGHTS");
                if (gear.HasValue)
                {
                    string gearPos = (int)gear.Value == 0 ? "up" : "down";
                    string lightStatus = "";
                    if (lights.HasValue)
                    {
                        lightStatus = (int)lights.Value switch
                        {
                            0 => ", unsafe",
                            1 => ", three green",
                            2 => ", in transit",
                            _ => ""
                        };
                    }
                    announcer.AnnounceImmediate($"Gear {gearPos}{lightStatus}");
                }
                else
                {
                    simConnect.RequestVariable("CMNCH_GEAR");
                    simConnect.RequestVariable("CMNCH_GEAR_LIGHTS");
                    announcer.AnnounceImmediate("Requesting gear position");
                }
                return true;
            }

            // Output+B — read altimeter setting
            case HotkeyAction.ReadAltimeter:
            {
                double? mb = simConnect.GetCachedVariableValue("CMNCH_ALTIMETER");
                if (mb.HasValue)
                {
                    double inHg = mb.Value * 0.02953;
                    int hpa = (int)Math.Round(mb.Value);
                    if (Math.Abs(inHg - 29.92) < 0.005)
                        announcer.AnnounceImmediate("Altimeter standard");
                    else
                        announcer.AnnounceImmediate($"Altimeter: {hpa}, {inHg:F2}");
                }
                else
                {
                    simConnect.RequestVariable("CMNCH_ALTIMETER");
                    announcer.AnnounceImmediate("Requesting altimeter");
                }
                return true;
            }

            // Output+Shift+W — read gross weight
            case HotkeyAction.ReadGrossWeightKg:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            }

            // S-TEC autopilot toggle
            case HotkeyAction.ToggleAutopilot1:
            {
                double? current = simConnect.GetCachedVariableValue("CMNCH_AP_MASTER");
                double newVal = (current.HasValue && current.Value > 0) ? 0 : 1;
                simConnect.SetLVar("ApMaster", newVal);
                return true;
            }

            // Input dialogs
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUInputDialog(
                    "Set Altimeter", "Barometric Pressure", "28.00 to 31.00 inHg",
                    "KOHLSMAN_SET", simConnect, announcer, parentForm,
                    input =>
                    {
                        if (double.TryParse(input, out double val) && val >= 28.00 && val <= 31.00)
                            return (true, string.Empty);
                        return (false, "Enter barometric pressure between 28.00 and 31.00 inHg");
                    },
                    value => (uint)(value * 16));
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("CMNCH_HEADING");
    }

    public override void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("CMNCH_AIRSPEED");
    }

    public override void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("INDICATED_ALTITUDE");
    }

    public override void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.RequestVariable("CMNCH_VERTICAL_SPEED");
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Low fuel warnings (3 tanks, warn below 3 gallons)
        if (varName == "CMNCH_FUEL_LEFT")
        {
            if (value > 0 && value < 3.0 && !_lowFuelLeftWarned)
            {
                announcer.Announce($"Warning: Left fuel low, {value:F1} gallons");
                _lowFuelLeftWarned = true;
            }
            else if (value >= 3.0) _lowFuelLeftWarned = false;
            return true;
        }
        if (varName == "CMNCH_FUEL_RIGHT")
        {
            if (value > 0 && value < 3.0 && !_lowFuelRightWarned)
            {
                announcer.Announce($"Warning: Right fuel low, {value:F1} gallons");
                _lowFuelRightWarned = true;
            }
            else if (value >= 3.0) _lowFuelRightWarned = false;
            return true;
        }
        if (varName == "CMNCH_FUEL_TIP_LEFT" || varName == "CMNCH_FUEL_TIP_RIGHT")
        {
            // Silence continuous tip tank updates — low-fuel warning on main tanks is sufficient
            return true;
        }

        // Low oil pressure warning (below 25 psi)
        if (varName == "CMNCH_OIL_PRESSURE")
        {
            if (value > 0 && value < 25 && !_lowOilPressureWarned)
            {
                announcer.Announce($"Warning: Oil pressure low, {value:F0} P S I");
                _lowOilPressureWarned = true;
            }
            else if (value >= 25) _lowOilPressureWarned = false;
            return true;
        }

        // High CHT warning (above 450°F)
        if (varName == "CMNCH_CHT")
        {
            if (value > 450 && !_highCHTWarned)
            {
                announcer.Announce($"Warning: CHT high, {value:F0} degrees");
                _highCHTWarned = true;
            }
            else if (value <= 450) _highCHTWarned = false;
            return true;
        }

        // RPM redline (above 2575 for the O-540-A)
        if (varName == "CMNCH_ENGINE_RPM")
        {
            if (value > 2575 && !_rpmRedlineWarned)
            {
                announcer.Announce($"Warning: RPM redline, {value:F0}");
                _rpmRedlineWarned = true;
            }
            else if (value <= 2575) _rpmRedlineWarned = false;
            return true;
        }

        // Suppress noisy continuous gauge updates
        if (varName == "CMNCH_OIL_TEMP" || varName == "CMNCH_EGT" ||
            varName == "CMNCH_FUEL_FLOW" || varName == "CMNCH_AMMETER" ||
            varName == "CMNCH_SUCTION" || varName == "CMNCH_MANIFOLD_PRESSURE")
        {
            return true;
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Avionics master uses K-event toggle (not L-var)
        if (varKey == "CMNCH_AVIONICS_STATE")
        {
            simConnect.SendEvent("TOGGLE_AVIONICS_MASTER");
            return true;
        }

        // Fuel pump uses K-event toggle (not L-var)
        if (varKey == "CMNCH_FUEL_PUMP_STATE")
        {
            simConnect.SendEvent("TOGGLE_ELECT_FUEL_PUMP1");
            return true;
        }

        // Magneto — must use K:MAGNETO1_SET event (L-var is read-only state)
        if (varKey == "CMNCH_MAGNETO")
        {
            simConnect.SendEvent("MAGNETO1_SET", (uint)value);
            if (value == 4)
            {
                // Start position: also engage the starter
                simConnect.SetLVar("Eng1_StarterSwitch", 1);
            }
            return true;
        }

        // Starter button press — engage starter with magnetos on both
        if (varKey == "CMNCH_STARTER")
        {
            simConnect.SendEvent("MAGNETO1_SET", 3);
            simConnect.SetLVar("Eng1_StarterSwitch", 1);
            return true;
        }

        // Primer pump press
        if (varKey == "CMNCH_PRIMER_PUMP")
        {
            simConnect.SetLVar("PrimerPump", 100);
            return true;
        }

        // AP disconnect and mode push — momentary press
        if (varKey == "CMNCH_AP_DISCONNECT")
        {
            simConnect.SetLVar("ApDisconnectSwitch", 1);
            return true;
        }
        if (varKey == "CMNCH_AP_MODE_PUSH")
        {
            simConnect.SetLVar("ApModePushSwitch", 1);
            return true;
        }

        // Transponder ident — momentary press
        if (varKey == "CMNCH_XPDR_IDENT")
        {
            simConnect.SetLVar("XpdrIdentSwitch", 1);
            return true;
        }

        // Generic Event-type handler (COM/NAV swap buttons, transponder set)
        if (varDef.Type == SimConnect.SimVarType.Event)
        {
            simConnect.SendEvent(varDef.Name, (uint)value);
            return true;
        }

        // All other L-vars: fall through to generic MainForm SetLVar handler
        return false;
    }
}
