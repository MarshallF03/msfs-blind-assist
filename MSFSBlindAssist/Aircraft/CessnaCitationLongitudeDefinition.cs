using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna Citation Longitude (ICAO C700) — Working Title G3000/G5000 avionics.
///
/// First-cut definition. The crown-jewel FMS is handled by the dedicated
/// G5000NavigatorForm (direct Coherent GT to the MFD), opened via the FMC hotkey.
/// SimVar-based panels and the GTC touchscreen systems panels (Temp / Propulsion /
/// ExteriorLights / CabinPressure) are layered on in later increments — see
/// docs/longitude-g5000.md for the complete avionics map.
///
/// This jet uses a proper Garmin autopilot that follows GPS/NAV (FMS) natively,
/// so it needs none of the C172's KAP140 GPSS heading-bug workaround.
/// </summary>
public sealed class CessnaCitationLongitudeDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Cessna Citation Longitude";
    public override string AircraftCode => "CITATION_LONGITUDE";

    // Garmin G5000 FCU/MCP values are set directly (autopilot select knobs).
    public override FCUControlType GetAltitudeControlType()      => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType()       => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType()         => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimVarDefinition> GetVariables()
    {
        // Start from the shared base set (ground state, etc.); jet-specific panels
        // are added in later increments.
        var vars = GetBaseVariables();
        return vars;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
        => new();

    protected override Dictionary<string, List<string>> BuildPanelControls()
        => new();

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
        => new();

    public override Dictionary<string, string> GetButtonStateMapping()
        => new();
}
