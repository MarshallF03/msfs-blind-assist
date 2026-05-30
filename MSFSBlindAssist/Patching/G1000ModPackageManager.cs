using System.Text;

namespace MSFSBlindAssist.Patching;

/// <summary>
/// Installs the G1000 NXi accessibility bridge as an MSFS Community package.
/// Injects g1000-accessibility-bridge.js into WTG1000_MFD.html so it runs
/// alongside the live G1000 FMS code. Communicates with MSFS Blind Assist
/// via HTTP on localhost:19779.
///
/// Works for any aircraft using the Working Title G1000 NXi (C172, etc.)
/// </summary>
public static class G1000ModPackageManager
{
    private const string PackageName   = "zzz-g1000-accessibility";
    private const string BridgeScript  = "g1000-accessibility-bridge.js";
    private const int    BridgeVersion = 1;
    private const string VersionFile   = "bridge-version.txt";

    private static readonly string MfdRelPath = Path.Combine(
        "html_ui", "Pages", "VCockpit", "Instruments", "NavSystems", "WTG1000", "MFD");

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public static bool IsInstalled(string? communityPath = null)
    {
        communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
        if (communityPath == null) return false;
        return Directory.Exists(Path.Combine(communityPath, PackageName));
    }

    /// <summary>
    /// Install or update the G1000 accessibility bridge package.
    /// Returns (success, needsRestart, message).
    /// </summary>
    public static (bool success, bool needsRestart, string message) InstallOrUpdate(
        string? communityPath = null)
    {
        try
        {
            communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
            if (communityPath == null)
                return (false, false, "Could not find MSFS Community folder");

            string packagePath = Path.Combine(communityPath, PackageName);
            string versionPath = Path.Combine(packagePath, VersionFile);

            // Check if already up to date
            if (Directory.Exists(packagePath) && File.Exists(versionPath))
            {
                if (int.TryParse(File.ReadAllText(versionPath).Trim(), out int v) && v == BridgeVersion)
                    return (true, false, "G1000 bridge is up to date");
            }

            // Wipe and reinstall
            if (Directory.Exists(packagePath))
                try { Directory.Delete(packagePath, true); } catch { }

            string mfdDir = Path.Combine(packagePath, MfdRelPath);
            Directory.CreateDirectory(mfdDir);

            // Copy bridge JS
            string bridgeJs = GetEmbeddedScript(BridgeScript);
            if (string.IsNullOrEmpty(bridgeJs))
                return (false, false, $"Could not read {BridgeScript} from resources");

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(mfdDir, BridgeScript), bridgeJs, utf8);

            // Find and patch WTG1000_MFD.html
            string? originalHtml = FindOriginalHtml(communityPath);
            if (originalHtml == null)
                return (false, false,
                    "WTG1000_MFD.html not found — is the WT G1000 NXi installed?");

            string html = File.ReadAllText(originalHtml);
            string tag  = $"\n<script src=\"{BridgeScript}\"></script>";

            // Don't double-inject
            if (!html.Contains(BridgeScript))
            {
                // Inject just before </body> or at the end
                html = html.Contains("</body>")
                    ? html.Replace("</body>", tag + "\n</body>")
                    : html + tag;
            }

            File.WriteAllText(Path.Combine(mfdDir, "WTG1000_MFD.html"), html, utf8);

            // Manifest
            string manifest = "{\n" +
                "  \"dependencies\": [],\n" +
                "  \"content_type\": \"MISC\",\n" +
                "  \"title\": \"MSFS Blind Assist - G1000 Accessibility Bridge\",\n" +
                "  \"manufacturer\": \"\",\n" +
                "  \"creator\": \"MSFS Blind Assist\",\n" +
                "  \"package_version\": \"1.0.0\",\n" +
                "  \"minimum_game_version\": \"1.18.15\",\n" +
                "  \"release_notes\": { \"neutral\": { \"LastUpdate\": \"\", \"OlderHistory\": \"\" } },\n" +
                "  \"total_package_size\": \"0000000000000001000\"\n" +
                "}";
            File.WriteAllText(Path.Combine(packagePath, "manifest.json"), manifest, utf8);

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

            // Version marker
            File.WriteAllText(versionPath, BridgeVersion.ToString(), utf8);

            return (true, true,
                "G1000 accessibility bridge installed. Restart the sim to activate.");
        }
        catch (Exception ex)
        {
            return (false, false, $"Install error: {ex.Message}");
        }
    }

    public static (bool success, string message) Uninstall(string? communityPath = null)
    {
        try
        {
            communityPath ??= EFBModPackageManager.FindCommunityFolderPath();
            if (communityPath == null) return (false, "Community folder not found");
            string pkg = Path.Combine(communityPath, PackageName);
            if (Directory.Exists(pkg)) Directory.Delete(pkg, true);
            return (true, "G1000 bridge removed. Restart the sim.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? FindOriginalHtml(string communityPath)
    {
        // Community → ...\Packages → Official\OneStore or Steam
        string? packagesBase = Path.GetDirectoryName(communityPath);
        if (packagesBase == null) return null;

        string[] storePaths = { "OneStore", "Steam", "WinGDK" };
        foreach (string store in storePaths)
        {
            // WT G1000 NXi package name in MSFS 2020
            foreach (string pkgName in new[]
                { "workingtitle-instruments-g1000nxi",
                  "workingtitle-instruments-g1000",
                  "fs-base-avionics",          // fallback: built-in G1000
                  "asobo-vcockpits-instruments" })
            {
                string path = Path.Combine(packagesBase, "Official", store, pkgName, MfdRelPath, "WTG1000_MFD.html");
                System.Diagnostics.Debug.WriteLine($"[G1000Mod] checking: {path}");
                if (File.Exists(path)) return path;
            }
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
