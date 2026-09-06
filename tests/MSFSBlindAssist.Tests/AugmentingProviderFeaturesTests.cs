using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class AugmentingProviderFeaturesTests
{
    [Fact]
    public void Fallback_sourced_features_are_bbox_filtered_and_area_sourced_are_not()
    {
        var data = new AirportTaxiData { Source = "osm", FeaturesFromFallback = true };
        data.Features.Add(new AirportFeature { Kind = FeatureKind.Fuel, Name = "Chevron on the highway", Lat = 47.30, Lon = -122.60, Source = FeatureSource.Osm });
        data.Features.Add(new AirportFeature { Kind = FeatureKind.Tower, Name = "Control Tower", Lat = 47.2712, Lon = -122.5731, Source = FeatureSource.Osm });
        var bbox = new AirportFacilities { Icao = "KTIW", LeftLon = -122.5794, RightLon = -122.5448, TopLat = 47.2750, BottomLat = 47.2608 };

        var filtered = AugmentingAirportDataProvider.FilterFeatures(new[] { data }, bbox);
        Assert.Equal("Control Tower", Assert.Single(filtered).Name);

        data.FeaturesFromFallback = false;
        Assert.Equal(2, AugmentingAirportDataProvider.FilterFeatures(new[] { data }, bbox).Count);
    }
}
