using System.Text;

namespace MSFSBlindAssist.Patching;

/// <summary>
/// Manages the GNS 530 accessibility bridge mod package.
/// Creates a zzz-gns530-accessibility package in the MSFS Community folder
/// that overrides WT530.html to inject our bridge script.
/// Works for any aircraft using the Working Title GNS 530 (Cessna 172, A2A Comanche, etc.)
/// </summary>
public static class GNSModPackageManager
{
    private const string PackageName = "zzz-gps-accessibility";
    private const string GNS530BridgeScript = "gns530-accessibility-bridge.js";
    private const string G1000BridgeScript = "g1000-accessibility-bridge.js";

    // Relative paths inside the package for each instrument override
    private static readonly string GNS530HtmlRelPath = Path.Combine(
        "html_ui", "Pages", "VCockpit", "Instruments", "NavSystems", "GPS", "WT530");
    private static readonly string G1000MFDHtmlRelPath = Path.Combine(
        "html_ui", "Pages", "VCockpit", "Instruments", "NavSystems", "WTG1000", "MFD");

    /// <summary>
    /// Finds the MSFS Community folder path.
    /// </summary>
    public static string? FindCommunityFolderPath()
    {
        // Check common locations
        string[] searchPaths = new[]
        {
            // MS Store version
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
            // Steam version
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft Flight Simulator", "Packages", "Community"),
            // MSFS 2024 MS Store
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
            // MSFS 2024 Steam
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft Flight Simulator 2024", "Packages", "Community")
        };

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Finds an original HTML file from a Working Title package in the Official folder.
    /// </summary>
    public static string? FindOriginalHtml(string communityPath, string packageName, string relPath, string fileName)
    {
        // Community path is like .../LocalCache/Packages/Community
        // Official path is like .../LocalCache/Packages/Official/OneStore or /Steam
        var packagesBase = Path.GetDirectoryName(communityPath); // .../LocalCache/Packages
        if (packagesBase != null)
        {
            var searchPaths = new[]
            {
                Path.Combine(packagesBase, "Official", "OneStore", packageName, relPath, fileName),
                Path.Combine(packagesBase, "Official", "Steam", packageName, relPath, fileName)
            };

            foreach (var path in searchPaths)
            {
                System.Diagnostics.Debug.WriteLine($"[GPS Mod] Checking: {path}");
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Installs or updates the GNS 530 bridge mod package.
    /// Returns true if the package was created/updated (requires sim restart).
    /// </summary>
    public static (bool success, bool needsRestart, string message) InstallOrUpdate(string? communityPath = null)
    {
        try
        {
            communityPath ??= FindCommunityFolderPath();
            if (communityPath == null)
                return (false, false, "Could not find MSFS Community folder");

            string packagePath = Path.Combine(communityPath, PackageName);
            bool packageExists = Directory.Exists(packagePath);
            bool anyUpdated = false;
            var messages = new List<string>();

            // --- GNS 530 Bridge ---
            string gns530BridgeJs = GetEmbeddedScript(GNS530BridgeScript);
            if (!string.IsNullOrEmpty(gns530BridgeJs))
            {
                string gns530Dir = Path.Combine(packagePath, GNS530HtmlRelPath);
                Directory.CreateDirectory(gns530Dir);
                string gns530JsDest = Path.Combine(gns530Dir, GNS530BridgeScript);
                string gns530Html = Path.Combine(gns530Dir, "WT530.html");

                if (!File.Exists(gns530JsDest) || File.ReadAllText(gns530JsDest) != gns530BridgeJs)
                {
                    File.WriteAllText(gns530JsDest, gns530BridgeJs, Encoding.UTF8);
                    anyUpdated = true;
                }

                if (!File.Exists(gns530Html))
                {
                    string? originalHtml = FindOriginalHtml(communityPath,
                        "workingtitle-instruments-garmin-gns", GNS530HtmlRelPath, "WT530.html");
                    if (originalHtml != null)
                    {
                        string htmlContent = File.ReadAllText(originalHtml);
                        string scriptTag = $"\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/NavSystems/GPS/WT530/{GNS530BridgeScript}\"></script>";
                        if (htmlContent.Contains("WT530B.js"))
                            htmlContent = htmlContent.Replace(
                                "import-script=\"/Pages/VCockpit/Instruments/NavSystems/GPS/WT530/WT530B.js\"></script>",
                                "import-script=\"/Pages/VCockpit/Instruments/NavSystems/GPS/WT530/WT530B.js\"></script>" + scriptTag);
                        else
                            htmlContent += scriptTag;
                        File.WriteAllText(gns530Html, htmlContent, Encoding.UTF8);
                        anyUpdated = true;
                        messages.Add("GNS 530 bridge installed");
                    }
                    else
                    {
                        messages.Add("GNS 530: original HTML not found (package may not be installed)");
                    }
                }
            }

            // --- G1000 NXi Bridge ---
            string g1000BridgeJs = GetEmbeddedScript(G1000BridgeScript);
            if (!string.IsNullOrEmpty(g1000BridgeJs))
            {
                string g1000Dir = Path.Combine(packagePath, G1000MFDHtmlRelPath);
                Directory.CreateDirectory(g1000Dir);
                string g1000JsDest = Path.Combine(g1000Dir, G1000BridgeScript);
                string g1000Html = Path.Combine(g1000Dir, "WTG1000_MFD.html");

                if (!File.Exists(g1000JsDest) || File.ReadAllText(g1000JsDest) != g1000BridgeJs)
                {
                    File.WriteAllText(g1000JsDest, g1000BridgeJs, Encoding.UTF8);
                    anyUpdated = true;
                }

                if (!File.Exists(g1000Html))
                {
                    string? originalHtml = FindOriginalHtml(communityPath,
                        "workingtitle-instruments-g1000", G1000MFDHtmlRelPath, "WTG1000_MFD.html");
                    if (originalHtml != null)
                    {
                        string htmlContent = File.ReadAllText(originalHtml);
                        string scriptTag = $"\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/NavSystems/WTG1000/MFD/{G1000BridgeScript}\"></script>";
                        if (htmlContent.Contains("MFD.js"))
                            htmlContent = htmlContent.Replace(
                                "import-script=\"/Pages/VCockpit/Instruments/NavSystems/WTG1000/MFD/MFD.js\"></script>",
                                "import-script=\"/Pages/VCockpit/Instruments/NavSystems/WTG1000/MFD/MFD.js\"></script>" + scriptTag);
                        else
                            htmlContent += scriptTag;
                        File.WriteAllText(g1000Html, htmlContent, Encoding.UTF8);
                        anyUpdated = true;
                        messages.Add("G1000 NXi bridge installed");
                    }
                    else
                    {
                        messages.Add("G1000: original HTML not found (package may not be installed)");
                    }
                }
            }

            if (!anyUpdated && packageExists)
                return (true, false, "GPS bridge package is up to date");

            // Create manifest.json
            string manifest = @"{
    ""dependencies"": [],
    ""content_type"": ""SCENERY"",
    ""title"": ""GPS Accessibility Bridge (GNS 530 + G1000)"",
    ""manufacturer"": """",
    ""creator"": ""MSFS Blind Assist"",
    ""package_version"": ""1.0.0"",
    ""minimum_game_version"": ""1.0.0"",
    ""release_notes"": {
        ""neutral"": {
            ""LastUpdate"": """",
            ""OlderHistory"": """"
        }
    }
}";
            File.WriteAllText(Path.Combine(packagePath, "manifest.json"), manifest, Encoding.UTF8);

            // Create layout.json with file sizes
            var layoutEntries = new List<string>();
            foreach (var file in Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "layout.json" || Path.GetFileName(file) == "manifest.json")
                    continue;
                var relativePath = Path.GetRelativePath(packagePath, file).Replace('\\', '/');
                var size = new FileInfo(file).Length;
                layoutEntries.Add($"    {{ \"path\": \"{relativePath}\", \"size\": {size}, \"date\": 0 }}");
            }
            string layout = "{\n  \"content\": [\n" + string.Join(",\n", layoutEntries) + "\n  ]\n}";
            File.WriteAllText(Path.Combine(packagePath, "layout.json"), layout, Encoding.UTF8);

            bool needsRestart = !packageExists;
            string detail = messages.Count > 0 ? string.Join(". ", messages) : "Scripts updated";
            string msg = needsRestart
                ? $"GPS bridge package installed ({detail}). Restart the sim to activate."
                : $"GPS bridge package updated ({detail}).";

            return (true, needsRestart, msg);
        }
        catch (Exception ex)
        {
            return (false, false, $"Error installing GNS bridge: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the mod package is installed.
    /// </summary>
    public static bool IsInstalled()
    {
        var communityPath = FindCommunityFolderPath();
        if (communityPath == null) return false;
        return Directory.Exists(Path.Combine(communityPath, PackageName));
    }

    private static string GetEmbeddedScript(string fileName)
    {
        try
        {
            // Read from Resources folder
            string resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
            if (File.Exists(resourcePath))
                return File.ReadAllText(resourcePath);

            // Try embedded resource
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName));
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GPS Mod] Error reading {fileName}: {ex.Message}");
        }
        return "";
    }
}
