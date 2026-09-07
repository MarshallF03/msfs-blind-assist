using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Flysimware Learjet 35A — a stock-systems bizjet: every control is an XML ModelBehavior over
/// a plain L:var, a B: input event or a stock K: event. No SDK, no WASM systems module, no
/// Coherent-only state (assessed live 2026-09-07).
///
/// STUDY-LEVEL, NOT SIMPLIFIED. Every control the vendor manual maps is exposed; derived
/// readouts are additions, never substitutions; MSFSBA reports and never decides.
///
/// TRANSPORTS (measured, see docs/learjet35a-variables.md):
///   • L:GENERIC_&lt;node&gt; 0/1 and L:GENERIC_Momentary_&lt;node&gt; 0/1/2 — written through the
///     calculator path (SetLVar). They stick and they drive the downstream effect.
///   • L:XMLVAR_&lt;node&gt;_Position — rotary selectors, same transport.
///   • (&gt;B:GENERIC_&lt;node&gt;_Set) — for the few controls whose JS listens for the H: event the
///     input event also fires (the Davtron clock).
///   • Stock K: events — autopilot, lights, radios, trim, brakes, circuits.
///   • H: events over the Coherent socket — GNS 530/430 bezel, GTX 345 keys.
///
/// The panel tree is the vendor manual's own map (LEARJET_35A_MSFS_MANUAL pages 1-2),
/// section by section, panel by panel.
///
/// ⚠️ Reads during development are verified through a Coherent view (tools/coherent-eval.ps1),
/// never through the SimConnect MCP's MobiFlight read path — it returned 0 for variables a
/// Coherent read proved were 1.
/// </summary>
public partial class FlysimwareLearjet35ADefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Flysimware Learjet 35A";
    public override string AircraftCode => "FLYSIMWARE_LJ35A";

    // ==================================================================================
    // Panel structure — the vendor manual's map. Sections are its cockpit areas, panels
    // its named panels. Panel names key a FLAT dictionary, so none repeats.
    // ==================================================================================

    public override Dictionary<string, List<string>> GetPanelStructure() => new()
    {
        ["Glareshield"] = new() { "Reverser Panel", "Fire Protection", "FC-530 Autopilot", "Annunciator Panel" },
        ["Pilot Panel"] = new() { "Pilot Flight Instruments", "Standby Attitude", "Davtron Clock", "Landing Gear" },
        ["Copilot Panel"] = new() { "Copilot Flight Instruments" },
        ["Engine Panel"] = new() { "Engine Instruments" },
        ["Navigation Panel"] = new() { "GNS 530", "GTX 345 Transponder" },
        ["Pilot's Sidewall"] = new() { "Pilot Lighting" },
        ["Copilot's Sidewall"] = new() { "Copilot Lighting" },
        ["Audio"] = new() { "Pilot Audio Panel" },
        ["Anti-Ice and Fuel Computer Panel"] = new() { "Anti-Ice", "Fuel Computers and Avionics" },
        ["Start Panel"] = new() { "Engine Start" },
        ["Test Panel"] = new() { "Systems Test" },
        ["Lower Center Panel"] = new() { "Lower Center Switches" },
        ["Pressurization Panel"] = new() { "Pressurization" },
        ["Climate and Lights Panel"] = new() { "Climate and Exterior Lights" },
        ["Throttle Quadrant"] = new() { "Thrust and Flaps" },
        ["Fuel System"] = new() { "Fuel" },
        ["Center Pedestal"] = new() { "Trim and Steering", "Yaw Damper", "Collins Radios" },
        ["Yoke"] = new() { "Yoke Switches" },
        ["EFB Tablet"] = new() { "Tablet" },
        ["Cabin and Ground"] = new() { "Cabin Door", "Cabin", "Ground Equipment", "Payload" },
        ["Simulation"] = new() { "Aircraft Options" }
    };

    // ==================================================================================
    // Panel controls. EVERY panel gets an entry even while empty — MainForm's panel build
    // returns early for a panel absent from GetPanelControls() and the panel then renders
    // completely blank (the HS787 Flight Data trap). Built panels override their placeholder,
    // one line each.
    // ==================================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = new Dictionary<string, List<string>>();
        foreach (var panels in GetPanelStructure().Values)
            foreach (var panel in panels)
                controls[panel] = new List<string>();

        // ---- populated panels ----

        return controls;
    }

    // ==================================================================================
    // Variables — one Build<Panel>Variables() per panel file.
    // ==================================================================================

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();
        void Add(Dictionary<string, SimVarDefinition> more)
        {
            foreach (var kv in more) vars[kv.Key] = kv.Value;
        }

        // ---- one Add(Build<Panel>Variables()) per panel ----

        return vars;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var display = new Dictionary<string, List<string>>();

        // ---- display["<Panel>"] = <Panel>Display per panel ----

        return display;
    }

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // FC-530: the altitude preselect takes a value (ALERTER_DIGITAL) and the heading bug is
    // a stock value set; V/S and speed are captured at engagement and nudged, so those two
    // are increment/decrement.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    /// <summary>
    /// Light bizjet numbers. The vendor publishes no Vref; 120 kt is the class figure at mid
    /// weight and is an ESTIMATE until a landing is measured.
    /// </summary>
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg = 4.0,
        ReferenceVrefKnots = 120.0,
        MaxPitchRateDegPerSec = 2.5,
        MaxBankRateDegPerSec = 4.0,
        GlideslopeAltitudeBiasFt = 40.0,
        FlareAltitudeBiasFt = 20.0,
        FlareTriggerWheelHeightFt = 25.0,
        FlareTargetPitchDeg = 4.0,
        TonePitchRangeDeg = 10.0
    };

    public override double TaxiTurnLeadSeconds => 0.9;

    // ==================================================================================
    // Writes — each panel owns its keys; the router tries them in turn.
    // ==================================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // ---- if (Handle<Panel>Set(varKey, value, simConnect)) return true; per panel ----
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ==================================================================================
    // Updates — returning true means handled; the generic announcer never runs for that key.
    // ==================================================================================

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (IsSilentCachedReadout(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    // ==================================================================================
    // Hotkeys — the readouts live in .Hotkeys.cs; only the app-level plumbing is here.
    // ==================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            case HotkeyAction.MonitorManager:
                (parentForm as MainForm)?.ShowLj35MonitorManagerDialog();
                return true;
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Releases every Coherent display window this definition opened. Called by MainForm on an
    /// aircraft switch; the windows are added by the display tasks and disposed here.
    /// </summary>
    public void DisposeWindows()
    {
        // ---- the GNS and tablet windows are disposed here once they exist ----
    }
}
