using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Deliberately SEPARATE from IAirportDataProvider: the surroundings feature is the only
/// consumer, and widening the main interface would force every provider and test double to
/// grow a method. Callers probe with `provider as IAirportFacilitiesProvider`.
/// </summary>
public interface IAirportFacilitiesProvider
{
    AirportFacilities? GetAirportFacilities(string icao);
}
