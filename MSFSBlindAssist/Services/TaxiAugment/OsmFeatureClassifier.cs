using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// One Overpass element → one AirportFeature, or null when it is not one (taxiway, stand,
/// holding point, nameless generic building). The FBO lexicon is deliberately a name test:
/// OSM has no reliable FBO tag, but FBOs name themselves the same way everywhere.
/// </summary>
public static class OsmFeatureClassifier
{
    private static readonly Regex FboLexicon = new(@"\b(aviation|jet ?cent(er|re)|fbo|air ?cent(er|re)|flight support|signature|atlantic|million air|executive|general aviation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CargoLexicon = new(@"\b(cargo|freight|fedex|ups|dhl)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DeiceLexicon = new(@"de-?ic", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AirportFeature? Classify(JsonElement el)
    {
        if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) return null;
        string aeroway = Tag(tags, "aeroway"), building = Tag(tags, "building"), amenity = Tag(tags, "amenity");
        string manMade = Tag(tags, "man_made"), office = Tag(tags, "office");
        string name = Tag(tags, "name");
        if (name.Length == 0) name = Tag(tags, "ref");
        string op = Tag(tags, "operator");
        string nameAndOp = name + " " + op;

        FeatureKind? kind = null;
        switch (aeroway)
        {
            case "terminal":
                kind = name.StartsWith("Concourse", StringComparison.OrdinalIgnoreCase) ? FeatureKind.Concourse
                     : Tag(tags, "terminal:type") == "general_aviation" || FboLexicon.IsMatch(nameAndOp) ? FeatureKind.Fbo
                     : CargoLexicon.IsMatch(name) ? FeatureKind.Cargo
                     : FeatureKind.Terminal;
                break;
            case "hangar": kind = FeatureKind.Hangar; break;
            case "apron": kind = DeiceLexicon.IsMatch(name) ? FeatureKind.DeicePad : FeatureKind.Apron; break;
            case "tower": case "control_tower": kind = FeatureKind.Tower; break;
            case "fuel": kind = FeatureKind.Fuel; break;
            case "helipad": kind = FeatureKind.Helipad; break;
            case "taxiway": case "parking_position": case "gate": case "holding_position": case "runway": return null;
        }
        if (kind == null)
        {
            if (building == "hangar") kind = FeatureKind.Hangar;
            else if (building == "terminal") kind = FeatureKind.Terminal;
            else if (manMade == "tower" && Tag(tags, "tower:type") == "aircraft_control") kind = FeatureKind.Tower;
            else if (amenity == "fuel") kind = FeatureKind.Fuel;
            else if (amenity == "fire_station") kind = FeatureKind.FireStation;
            else if (name.Length > 0 && (office.Length > 0 || building.Length > 0))
                kind = FboLexicon.IsMatch(nameAndOp) ? FeatureKind.Fbo : CargoLexicon.IsMatch(name) ? FeatureKind.Cargo : FeatureKind.Office;
        }
        if (kind == null) return null;
        if (kind == FeatureKind.Office && name.Length == 0) return null;

        if (!OsmTaxiSource.TryRepresentativePoint(el, out double lat, out double lon)) return null;

        IReadOnlyList<LatLon>? footprint = null;
        if (kind is FeatureKind.Apron or FeatureKind.DeicePad or FeatureKind.Terminal or FeatureKind.Concourse)
            footprint = Footprint(el);
        if (footprint != null && footprint.Count >= 3)
        {
            var c = SurroundingsGeometry.Centroid(footprint);
            if (SurroundingsGeometry.Contains(footprint, c.Lat, c.Lon)) { lat = c.Lat; lon = c.Lon; }
        }

        return new AirportFeature
        {
            Kind = kind.Value, Name = name.Trim(), Lat = lat, Lon = lon, Footprint = footprint,
            Source = FeatureSource.Osm, Detail = op.Length > 0 && kind == FeatureKind.Fbo ? $"operator {op}" : null,
        };
    }

    private static string Tag(JsonElement tags, string key)
        => tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>A closed way's vertices (closing duplicate dropped), or null. Relations (multipolygons) arrive with `center` only.</summary>
    private static IReadOnlyList<LatLon>? Footprint(JsonElement el)
    {
        if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array) return null;
        var pts = new List<LatLon>();
        foreach (var g in geom.EnumerateArray())
            if (g.TryGetProperty("lat", out var la) && g.TryGetProperty("lon", out var lo))
                pts.Add(new LatLon(la.GetDouble(), lo.GetDouble()));
        if (pts.Count >= 2 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }
}
