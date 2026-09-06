using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.SceneryIndex;

public sealed record ClassifiedModel(FeatureKind Kind, string Name);

/// <summary>
/// Author-chosen model names → spoken feature names. The ONLY path by which a scenery model
/// name may reach speech (spec invariant): everything unrecognised is dropped, and what is
/// kept is rewritten into human text. Every rule here is pinned by a measured name in
/// SceneryModelNameClassifierTests; extend the tables there first.
/// </summary>
public static class SceneryModelNameClassifier
{
    private static readonly Regex StopList = new(
        @"\b(fences?|lights?|rooflights?|poles?|aircon|hvac|vehicles?|cars?|carparks?|trucks?|vans?|cargovan|loaders?|cones?|signs?|markings?|lines?|jetways?|bridges?|pylons?|silos?|lod|shadows?|decals?|grass|trees?|pedestrian|crossing|tickets?|platform|gates?|safegate|base|stairs?|railing|barrier|bollards?|hydrant)\d*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Order matters: first match wins.
    private static readonly (Regex Rx, FeatureKind Kind)[] Kinds =
    {
        (new Regex(@"\b(hangars?|hangers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Hangar),
        (new Regex(@"\b(concourse|pier|satellite)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Concourse),
        (new Regex(@"\bterminal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Terminal),
        (new Regex(@"\b(tower|atc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Tower),
        (new Regex(@"\b(fbo|aviation|jet ?cent(er|re))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fbo),
        (new Regex(@"\b(fire|arff|rescue)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.FireStation),
        (new Regex(@"\b(cargo|freight|fedex|ups|dhl)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Cargo),
        (new Regex(@"\b(deice|de ?ice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.DeicePad),
        (new Regex(@"\b(fuel|fueltank|avgas|tank)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fuel),
        (new Regex(@"\b(office|admin|cafe|restaurant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Office),
    };

    public static ClassifiedModel? Classify(string modelName, string icao)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        var tokens = Tokenize(modelName, icao);
        if (tokens.Count == 0) return null;
        string joined = string.Join(" ", tokens);
        if (StopList.IsMatch(joined)) return null;

        FeatureKind? kind = null;
        foreach (var (rx, k) in Kinds) if (rx.IsMatch(joined)) { kind = k; break; }
        if (kind == null) return null;

        // A hangar named by another kind's word ("Cessna Service Hangar" contains no other keyword) is
        // fine; but "Narrows Aviation Hangar" must be a HANGAR, not an FBO — Hangar is checked first.
        var kept = new List<string>(tokens);
        if (kind is FeatureKind.Concourse or FeatureKind.Terminal)
        {
            int kw = kept.FindIndex(t => Regex.IsMatch(t, @"^(concourse|pier|satellite|terminal)$", RegexOptions.IgnoreCase));
            kept = kw >= 0 && kw + 1 < kept.Count ? new List<string> { kept[kw], kept[kw + 1] } : new List<string> { kept[Math.Max(kw, 0)] };
        }
        else
        {
            // Trailing single letter: a part label only when the token before it carries a digit.
            if (kept.Count >= 2 && kept[^1].Length == 1 && char.IsLetter(kept[^1][0]) && kept[^2].Any(char.IsDigit))
                kept.RemoveAt(kept.Count - 1);
            // Trailing 1–2 digit token: a part number, unless removing it leaves just the kind word.
            if (kept.Count >= 2 && Regex.IsMatch(kept[^1], @"^\d{1,2}$"))
            {
                var without = kept.Take(kept.Count - 1).ToList();
                bool bareKind = without.Count == 1 && Kinds.Any(x => x.Rx.IsMatch(without[0]));
                if (!(bareKind && kind == FeatureKind.Hangar)) kept = without;   // "Hangar 1" keeps its number; "Tower 1" does not
            }
        }

        string name = string.Join(" ", kept.Select(Pretty));
        if (kind == FeatureKind.Fuel && kept.Count == 1) name = "Fuel";
        return new ClassifiedModel(kind.Value, name);
    }

    private static List<string> Tokenize(string model, string icao)
    {
        string s = model.Trim();
        if (!string.IsNullOrEmpty(icao))
            s = Regex.Replace(s, $@"^{Regex.Escape(icao)}\d*[_\- ]?", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");                   // HubCafe → Hub Cafe
        return s.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string Pretty(string t)
    {
        if (Regex.IsMatch(t, @"^\d+$")) return int.Parse(t, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (Regex.IsMatch(t, @"^\d+[A-Za-z]$")) return int.Parse(t[..^1], CultureInfo.InvariantCulture) + t[^1..].ToUpperInvariant();
        if (Regex.IsMatch(t, @"^hangers?$", RegexOptions.IgnoreCase)) return "Hangar";
        if (t.Length <= 3 && t.All(char.IsUpper)) return t;
        return char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();
    }
}
