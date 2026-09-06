namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// The package folders navdatareader recorded for an airport (airport.scenery_local_path),
/// e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows". ONLY these
/// are ever opened (spec invariant: never the whole Community tree).
/// </summary>
public static class SceneryPackageLocator
{
    public static List<string> PackageDirs(string sceneryLocalPath, Func<string, bool> dirExists)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sceneryLocalPath)) return result;
        foreach (var raw in sceneryLocalPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.Contains('\\') && !raw.Contains('/')) continue;                       // "fs-base-genericairports": no folder
            if (raw.EndsWith("navigraph-navdata", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Contains(raw, StringComparer.OrdinalIgnoreCase)) continue;
            if (!dirExists(raw)) continue;
            result.Add(raw);
        }
        return result;
    }
}
