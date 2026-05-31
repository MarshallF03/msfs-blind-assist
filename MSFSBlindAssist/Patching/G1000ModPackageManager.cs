using System.Text;

namespace MSFSBlindAssist.Patching;

/// <summary>
/// Installs / updates the zzz-g1000-accessibility Community package.
/// Injects g1000-accessibility-bridge.js into WTG1000_MFD.html so it
/// runs inside the G1000 NXi instrument and communicates with the C# server
/// on localhost:19778.
/// </summary>
public static class G1000ModPackageManager
{
    private const string PackageName  = "zzz-g1000-accessibility";
    private const string BridgeScript = "g1000-accessibility-bridge.js";
    private const int    BridgeVersion = 3;
    private const string VersionFile   = "bridge-version.txt";

    private static readonly string MfdRelPath = Path.Combine(
        "html_ui", "Pages", "VCockpit", "Instruments", "NavSystems", "WTG1000", "MFD");

    // ── Public API ────────────────────────────────────────────────────────────

    public static bool IsInstalled(string? communityPath = null)
    {
        communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
        if (communityPath == null) return false;
        return Directory.Exists(Path.Combine(communityPath, PackageName));
    }

    public static bool IsUpToDate(string? communityPath = null)
    {
        communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
        if (communityPath == null) return false;
        var vf = Path.Combine(communityPath, PackageName, VersionFile);
        return File.Exists(vf) && int.TryParse(File.ReadAllText(vf).Trim(), out int v) && v == BridgeVersion;
    }

    /// <summary>Install or update the package. Returns (success, needsRestart, message).</summary>
    public static (bool success, bool needsRestart, string message) InstallOrUpdate(
        string? communityPath = null)
    {
        try
        {
            communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
            if (communityPath == null) return (false, false, "Could not find MSFS Community folder");

            string packagePath = Path.Combine(communityPath, PackageName);
            bool existed = Directory.Exists(packagePath);

            // Wipe and reinstall to ensure clean state
            if (existed) try { Directory.Delete(packagePath, true); } catch { }

            string mfdDir = Path.Combine(packagePath, MfdRelPath);
            Directory.CreateDirectory(mfdDir);

            var utf8 = new UTF8Encoding(false);

            // Write bridge JS
            string bridgeJs = GetEmbeddedScript(BridgeScript);
            if (string.IsNullOrEmpty(bridgeJs))
                return (false, false, $"Could not read {BridgeScript} from resources");
            File.WriteAllText(Path.Combine(mfdDir, BridgeScript), bridgeJs, utf8);

            // Find and patch WTG1000_MFD.html
            string? originalHtml = FindOriginalHtml(communityPath);
            if (originalHtml == null)
                return (false, false, "WTG1000_MFD.html not found — is the WT G1000 NXi installed?");

            string html = File.ReadAllText(originalHtml);
            string tag  = $"\n<script src=\"{BridgeScript}\"></script>";
            if (!html.Contains(BridgeScript))
                html = html.Contains("</body>")
                    ? html.Replace("</body>", tag + "\n</body>")
                    : html + tag;
            File.WriteAllText(Path.Combine(mfdDir, "WTG1000_MFD.html"), html, utf8);

            // Manifest
            File.WriteAllText(Path.Combine(packagePath, "manifest.json"),
                "{\n  \"dependencies\": [],\n  \"content_type\": \"MISC\",\n" +
                "  \"title\": \"MSFS Blind Assist - G1000 Accessibility Bridge\",\n" +
                "  \"manufacturer\": \"\",\n  \"creator\": \"MSFS Blind Assist\",\n" +
                "  \"package_version\": \"1.0.0\",\n  \"minimum_game_version\": \"1.18.15\",\n" +
                "  \"release_notes\": { \"neutral\": { \"LastUpdate\": \"\", \"OlderHistory\": \"\" } },\n" +
                "  \"total_package_size\": \"0000000000000001000\"\n}", utf8);

            // Layout
            var entries = new List<string>();
            foreach (var f in Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories))
            {
                string fn = Path.GetFileName(f);
                if (fn is "layout.json" or "manifest.json" || fn == VersionFile) continue;
                string rel  = Path.GetRelativePath(packagePath, f).Replace('\\', '/');
                long   size = new FileInfo(f).Length;
                entries.Add($"    {{ \"path\": \"{rel}\", \"size\": {size}, \"date\": 0 }}");
            }
            File.WriteAllText(Path.Combine(packagePath, "layout.json"),
                "{\n  \"content\": [\n" + string.Join(",\n", entries) + "\n  ]\n}", utf8);

            File.WriteAllText(Path.Combine(packagePath, VersionFile), BridgeVersion.ToString(), utf8);

            return (true, !existed,
                existed ? "G1000 bridge updated — restart the sim to activate the new version."
                        : "G1000 bridge installed — restart the sim to activate.");
        }
        catch (Exception ex) { return (false, false, $"Install error: {ex.Message}"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindOriginalHtml(string communityPath)
    {
        string? packagesBase = Path.GetDirectoryName(communityPath);
        if (packagesBase == null) return null;

        foreach (string store in new[] { "OneStore", "Steam", "WinGDK" })
        foreach (string pkg in new[]
        {
            "workingtitle-instruments-g1000nxi",
            "workingtitle-instruments-g1000",
            "fs-base-avionics"
        })
        {
            string path = Path.Combine(packagesBase, "Official", store, pkg, MfdRelPath, "WTG1000_MFD.html");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string GetEmbeddedScript(string fileName)
    {
        try
        {
            string res = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
            if (File.Exists(res)) return File.ReadAllText(res);
            var asm  = System.Reflection.Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName));
            if (name != null)
            {
                using var stream = asm.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[G1000Mod] resource error: {ex.Message}");
        }
        return "";
    }
}
