// Builds a fake package on disk: a modelLib BGL holding ModelInfo XML for three GUIDs and an
// objects BGL placing them (two parts of one concourse, one fence). Same synthetic builder as
// BglPlacementReaderTests.
using System.Text;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-scenery-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string MakePackage()
    {
        string pkg = Path.Combine(_root, "pkg", "scenery");
        Directory.CreateDirectory(pkg);
        var gA1 = Guid.NewGuid(); var gA2 = Guid.NewGuid(); var gF = Guid.NewGuid();
        string xml = $"<ModelInfo guid=\"{{{gA1}}}\" name=\"concourse_a_01\"/><ModelInfo guid=\"{{{gA2}}}\" name=\"concourse_a_02\"/><ModelInfo guid=\"{{{gF}}}\" name=\"KTIW_Fence2\"/>";
        var lib = new byte[0x38 + 20].Concat(Encoding.Latin1.GetBytes(xml)).ToArray();
        BitConverter.TryWriteBytes(lib.AsSpan(0, 4), 0x19920201u);            // a BGL with zero sections + XML tail
        File.WriteAllBytes(Path.Combine(pkg, "modelLib.BGL"), lib);
        File.WriteAllBytes(Path.Combine(pkg, "objects.bgl"), BglPlacementReaderTests.BuildBgl((33.640, -84.430, 0, gA1), (33.642, -84.430, 0, gA2), (33.650, -84.440, 0, gF)));
        File.WriteAllText(Path.Combine(_root, "pkg", "layout.json"), "{}");
        return Path.Combine(_root, "pkg");
    }

    [Fact]
    public void Indexes_a_package_merging_parts_and_dropping_noise_then_serves_from_cache()
    {
        string pkg = MakePackage();
        string cache = Path.Combine(_root, "cache");
        var indexer = new SceneryPackageIndexer(cache);

        var features = indexer.GetFeatures("KATL", new[] { pkg });
        var only = Assert.Single(features);
        Assert.Equal(FeatureKind.Concourse, only.Kind);
        Assert.Equal("Concourse A", only.Name);
        Assert.InRange(only.Lat, 33.6409, 33.6411);
        Assert.Equal(FeatureSource.Scenery, only.Source);
        Assert.Single(Directory.GetFiles(cache, "*.json"));
        Assert.Contains("1 features", indexer.LastStatus);

        // Second call: cache hit (delete the BGLs to prove nothing is re-read).
        File.Delete(Path.Combine(pkg, "scenery", "objects.bgl"));
        Assert.Single(indexer.GetFeatures("KATL", new[] { pkg }));

        // layout.json change → rebuild → now empty because the placements are gone.
        File.WriteAllText(Path.Combine(pkg, "layout.json"), "{ \"changed\": true }");
        Assert.Empty(indexer.GetFeatures("KATL", new[] { pkg }));
    }
}
