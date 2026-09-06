// Every row is a real model name measured in an installed package on 2026-09-06.
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryModelNameClassifierTests
{
    [Theory]
    [InlineData("KTIW_Cessna_Service_Hanger", "KTIW", FeatureKind.Hangar, "Cessna Service Hangar")]
    [InlineData("KTIW_ATP_Hanger", "KTIW", FeatureKind.Hangar, "ATP Hangar")]
    [InlineData("KTIW_Pavco_Hanger", "KTIW", FeatureKind.Hangar, "Pavco Hangar")]
    [InlineData("KTIW_Narrows_Aviation_Hangar_Large_1", "KTIW", FeatureKind.Hangar, "Narrows Aviation Hangar Large")]
    [InlineData("KTIW_Hangar_09_B", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KTIW_Hangar_09B_1", "KTIW", FeatureKind.Hangar, "Hangar 9B")]
    [InlineData("KTIW_Hangar_09", "KTIW", FeatureKind.Hangar, "Hangar 9")]
    [InlineData("KJAC_Hangar_1", "KJAC", FeatureKind.Hangar, "Hangar 1")]
    [InlineData("KJAC_Hangar_2", "KJAC", FeatureKind.Hangar, "Hangar 2")]
    [InlineData("KTIW_Outskirt_Hangars_A", "KTIW", FeatureKind.Hangar, "Outskirt Hangars A")]
    [InlineData("KTIW_hangar_blue_octogon", "KTIW", FeatureKind.Hangar, "Hangar Blue Octogon")]
    [InlineData("Hangar_04", "KTIW", FeatureKind.Hangar, "Hangar 4")]
    [InlineData("Control_Tower_1", "KTIW", FeatureKind.Tower, "Control Tower")]
    [InlineData("KJAC_Tower", "KJAC", FeatureKind.Tower, "Tower")]
    [InlineData("tower_01", "KATL", FeatureKind.Tower, "Tower")]
    [InlineData("Fueltank", "KTIW", FeatureKind.Fuel, "Fuel")]
    [InlineData("Airport_Office_1", "KTIW", FeatureKind.Office, "Airport Office")]
    [InlineData("HubCafe_1", "KTIW", FeatureKind.Office, "Hub Cafe")]
    [InlineData("concourse_a_02", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_a_interface_12m_b36", "KATL", FeatureKind.Concourse, "Concourse A")]
    [InlineData("concourse_t_canopy_01", "KATL", FeatureKind.Concourse, "Concourse T")]
    [InlineData("northwestern_cargo_01", "KATL", FeatureKind.Cargo, "Northwestern Cargo")]
    [InlineData("southern_hangar_01", "KATL", FeatureKind.Hangar, "Southern Hangar")]
    public void Classifies_measured_model_names(string model, string icao, FeatureKind kind, string name)
    {
        var c = SceneryModelNameClassifier.Classify(model, icao);
        Assert.NotNull(c);
        Assert.Equal(kind, c!.Kind);
        Assert.Equal(name, c.Name);
    }

    [Theory]
    [InlineData("KTIW_Fence2")]
    [InlineData("concourse_a_aircon4")]
    [InlineData("terminal_light1")]
    [InlineData("ground_terminal_light_01")]
    [InlineData("concourse_e_rooflight01")]
    [InlineData("concourse_t_carparks_02")]
    [InlineData("concourse_t_pedestrian_crossing01")]
    [InlineData("jetway_base")]
    [InlineData("safegate01")]
    [InlineData("KATL2020_truck_fuel")]
    [InlineData("KATL2020_cargovan")]
    [InlineData("KATL2020_cargo_loader_01")]
    [InlineData("KJAC_Vehicles_Fuel_Truck_JHA")]
    [InlineData("KTIW_Bridge1")]
    [InlineData("KTIW_Silo")]
    [InlineData("KTIW_Pylon")]
    [InlineData("ViewingPlatform1")]
    [InlineData("gates")]
    [InlineData("fence_black")]
    [InlineData("")]
    public void Drops_noise_and_unclassifiable_names(string model)
        => Assert.Null(SceneryModelNameClassifier.Classify(model, "KTIW"));
}
