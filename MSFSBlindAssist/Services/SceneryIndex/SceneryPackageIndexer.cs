using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Tier 3: what the installed scenery actually models. For each package folder: every *.bgl
/// is read once for ModelInfo names and once for LibraryObject placements; placements whose
/// GUID resolves in-package are classified; same (Kind, Name) placements merge to a centroid.
/// Result cached per package as JSON under cacheDir, keyed on layout.json length + mtime —
/// the user's OWN local files, so a disk cache is fine (unlike OSM data). Call from a
/// background thread or a hotkey handler: the first read of a 100 MB objects.BGL takes a
/// few hundred ms; every later call is a JSON read.
/// </summary>
public sealed class SceneryPackageIndexer
{
    private readonly string _cacheDir;
    private readonly object _lock = new();
    public string LastStatus { get; private set; } = "";

    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        public string Package { get; set; } = "";
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        public int Placements { get; set; }
        public int Unresolved { get; set; }
        public List<Entry> Features { get; set; } = new();
    }
    private sealed class Entry { public FeatureKind Kind { get; set; } public string Name { get; set; } = ""; public double Lat { get; set; } public double Lon { get; set; } }

    public SceneryPackageIndexer(string cacheDir) { _cacheDir = cacheDir; }

    public IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs)
    {
        var all = new List<AirportFeature>();
        var status = new List<string>();
        foreach (var dir in packageDirs)
        {
            try
            {
                var cf = LoadOrBuild(dir, icao);
                all.AddRange(cf.Features.Select(e => new AirportFeature { Kind = e.Kind, Name = e.Name, Lat = e.Lat, Lon = e.Lon, Source = FeatureSource.Scenery }));
                status.Add($"{cf.Features.Count} features from {Path.GetFileName(dir)} ({cf.Placements} placements, {cf.Unresolved} base-library)");
            }
            catch (Exception ex)
            {
                Log.Warn("SceneryIndex", $"{icao}: {Path.GetFileName(dir)}: {ex.Message}");
                status.Add($"{Path.GetFileName(dir)}: unreadable");
            }
        }
        LastStatus = status.Count == 0 ? $"{icao}: no add-on package" : $"{icao}: " + string.Join("; ", status);
        return all;
    }

    private CacheFile LoadOrBuild(string dir, string icao)
    {
        string layout = Path.Combine(dir, "layout.json");
        var info = new FileInfo(layout);
        long len = info.Exists ? info.Length : 0, ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;

        // Generate cache file name using SHA-256 hash of normalized full path
        string fullPath = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath))).Substring(0, 8);
        string leafName = Path.GetFileName(dir.TrimEnd('\\', '/'));
        string cachePath = Path.Combine(_cacheDir, $"{leafName}-{hash}.json");

        lock (_lock)
        {
            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), JsonOptions);
                    if (cached != null && cached.SchemaVersion == CurrentSchemaVersion && cached.LayoutLength == len && cached.LayoutTicks == ticks) return cached;
                }
                catch { /* rebuild */ }
            }

            var names = new Dictionary<Guid, string>();
            var placements = new List<ScenePlacement>();
            foreach (var bgl in Directory.EnumerateFiles(dir, "*.bgl", SearchOption.AllDirectories))
            {
                // Asobo's base ModelLib BGLs run 1.2-1.4 GB and OOM the Latin-1 string reader
                // below on a full ReadAllBytes; Community packages this indexer actually needs
                // to read are measured at ≤ ~100 MB, so 600 MB is a generous ceiling that
                // never trims a real Community package and always skips the base libs.
                long size = new FileInfo(bgl).Length;
                if (size > 600L * 1024 * 1024)
                {
                    Log.Warn("SceneryIndex", $"skipping {bgl}: {size / (1024 * 1024)} MB exceeds the 600 MB reader limit");
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(bgl);
                foreach (var kv in ModelLibNameReader.Read(bytes)) names[kv.Key] = kv.Value;
                placements.AddRange(BglPlacementReader.Read(bytes));
            }

            var groups = new Dictionary<(FeatureKind, string), List<LatLon>>();
            int unresolved = 0;
            foreach (var p in placements)
            {
                if (!names.TryGetValue(p.ModelGuid, out var model)) { unresolved++; continue; }
                var c = SceneryModelNameClassifier.Classify(model, icao);
                if (c == null) continue;
                if (!groups.TryGetValue((c.Kind, c.Name), out var pts)) groups[(c.Kind, c.Name)] = pts = new List<LatLon>();
                pts.Add(new LatLon(p.Lat, p.Lon));
            }

            var cf = new CacheFile { SchemaVersion = CurrentSchemaVersion, Package = dir, LayoutLength = len, LayoutTicks = ticks, Placements = placements.Count, Unresolved = unresolved };
            foreach (var ((kind, name), pts) in groups)
            {
                var cen = SurroundingsGeometry.Centroid(pts);
                cf.Features.Add(new Entry { Kind = kind, Name = name, Lat = cen.Lat, Lon = cen.Lon });
            }
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cf, JsonOptions));
            Log.Info("SceneryIndex", $"{icao}: indexed {Path.GetFileName(dir)}: {cf.Features.Count} features, {placements.Count} placements, {unresolved} unresolved");
            return cf;
        }
    }
}
