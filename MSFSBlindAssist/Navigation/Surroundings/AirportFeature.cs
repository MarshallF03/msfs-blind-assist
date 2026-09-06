namespace MSFSBlindAssist.Navigation.Surroundings;

public enum FeatureKind { Terminal, Concourse, Fbo, Hangar, Tower, Fuel, Cargo, FireStation, Helipad, Apron, DeicePad, Office, Other }

/// <summary>Where a feature came from. Also the merge tie-break order in AirportFeatureCatalog.</summary>
public enum FeatureSource { Navdata, Gsx, Osm, Scenery }

public readonly record struct LatLon(double Lat, double Lon);

/// <summary>
/// One thing on the airport a pilot might want to know is beside them. READOUT ONLY: never a
/// graph node, never a routing input, never a hold-short input (spec invariant). Name is what
/// is SPOKEN, verbatim — a source must hand over human text, never a raw model or tag string.
/// </summary>
public sealed class AirportFeature
{
    public required FeatureKind Kind { get; init; }
    public string Name { get; init; } = "";
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    /// <summary>Closed polygon (OSM apron/terminal way) or null for a point feature.</summary>
    public IReadOnlyList<LatLon>? Footprint { get; init; }
    public required FeatureSource Source { get; init; }
    /// <summary>Short qualifier spoken after the name in the window: "Delta gates", "operator Jackson Hole Aviation".</summary>
    public string? Detail { get; init; }

    public bool HasName => !string.IsNullOrWhiteSpace(Name);
    public string SpokenName => HasName ? Name.Trim() : FeatureKindWords.Generic(Kind);
}

public static class FeatureKindWords
{
    /// <summary>What an UNNAMED feature of this kind is called. Other → "" (an unnamed Other is dropped upstream).</summary>
    public static string Generic(FeatureKind kind) => kind switch
    {
        FeatureKind.Terminal => "Terminal",
        FeatureKind.Concourse => "Concourse",
        FeatureKind.Fbo => "FBO",
        FeatureKind.Hangar => "Hangar",
        FeatureKind.Tower => "Control tower",
        FeatureKind.Fuel => "Fuel",
        FeatureKind.Cargo => "Cargo ramp",
        FeatureKind.FireStation => "Fire station",
        FeatureKind.Helipad => "Helipad",
        FeatureKind.Apron => "Apron",
        FeatureKind.DeicePad => "De-ice pad",
        FeatureKind.Office => "Airport office",
        _ => "",
    };
}
