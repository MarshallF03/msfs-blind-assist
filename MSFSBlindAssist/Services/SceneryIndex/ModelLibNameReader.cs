using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Model GUID → author's model name, from the `<ModelInfo … guid="{…}" … name="…">` XML
/// fragments MSFS embeds beside each model in a modelLib/objects BGL (verified on three packages).
/// Attribute order varies by developer, so each tag is scanned for both attributes independently.
/// </summary>
public static class ModelLibNameReader
{
    private static readonly Regex Tag = new(@"<ModelInfo\b([^>]*)>", RegexOptions.CultureInvariant);
    private static readonly Regex GuidAttr = new(@"guid=""\{?([0-9a-fA-F-]{36})\}?""", RegexOptions.CultureInvariant);
    private static readonly Regex NameAttr = new(@"name=""([^""]+)""", RegexOptions.CultureInvariant);

    public static Dictionary<Guid, string> Read(ReadOnlySpan<byte> bgl)
    {
        var map = new Dictionary<Guid, string>();
        string text = Encoding.Latin1.GetString(bgl);
        foreach (Match m in Tag.Matches(text))
        {
            string attrs = m.Groups[1].Value;
            var g = GuidAttr.Match(attrs); var n = NameAttr.Match(attrs);
            if (!g.Success || !n.Success) continue;
            if (Guid.TryParse(g.Groups[1].Value, out var guid)) map[guid] = n.Groups[1].Value;
        }
        return map;
    }
}
