using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

// WinForms is referenced for the tray icon and brings a clashing Size with it.
using Size = System.Windows.Size;

namespace RatNav.App;

/// <summary>One drawn shape from a map, with the role its class gives it.</summary>
public sealed record MapShape(Geometry Geometry, MapShapeRole Role);

/// <summary>
/// What a shape is, taken from the semantic classes the map SVGs carry — <c>.building</c>,
/// <c>.trees</c>, <c>.road_tarmac</c>. This is what lets the overlay draw a map by meaning rather
/// than by copying its colours.
/// </summary>
public enum MapShapeRole
{
    Terrain,
    Structure,
    Boundary,
    Route,
    Hazard,
    Other,
}

/// <summary>
/// Turns a map's SVG into WPF geometry, so the overlay can draw a real map without a browser in it.
///
/// <para>This works because SVG path data and WPF's path mini-language are the same syntax down to
/// the command letters — <c>M</c>, <c>L</c>, <c>C</c>, <c>A</c>, <c>Z</c> — so a path's <c>d</c>
/// attribute can be handed straight to <see cref="Geometry.Parse"/>. Anything that does not parse
/// is skipped rather than aborting the map, since one unusual shape should not cost the other five
/// hundred.</para>
///
/// <para>Parsing is done once per map and cached: these files hold several hundred paths, and
/// re-parsing them on every position fix would be the one thing in RatNav that actually cost
/// frames.</para>
/// </summary>
public static partial class MapGeometry
{
    private static readonly Dictionary<string, IReadOnlyList<MapShape>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>The map's own drawing size, needed to scale it into the overlay.</summary>
    public static Size ViewBoxOf(string svg)
    {
        var match = ViewBox().Match(svg);
        if (!match.Success) return new Size(1000, 1000);

        var parts = match.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return new Size(1000, 1000);

        return double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
               double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) &&
               w > 0 && h > 0
            ? new Size(w, h)
            : new Size(1000, 1000);
    }

    /// <summary>
    /// Every drawable shape in one floor of a map, or in the whole map when no floor is named.
    /// Results are frozen, which lets WPF share them across threads and skip change tracking.
    /// </summary>
    public static IReadOnlyList<MapShape> Parse(string svg, string? floorLayer, string cacheKey)
    {
        var key = $"{cacheKey}|{floorLayer}";

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var scope = floorLayer is { Length: > 0 } ? ExtractGroup(svg, floorLayer) ?? svg : svg;
        var shapes = new List<MapShape>();

        foreach (Match match in PathElement().Matches(scope))
        {
            var data = match.Groups["d"].Value;
            if (data.Length == 0) continue;

            Geometry geometry;
            try
            {
                geometry = Geometry.Parse(data);
            }
            catch (FormatException)
            {
                // An unusual path is not worth losing the map over.
                continue;
            }

            geometry.Freeze();
            shapes.Add(new MapShape(geometry, RoleOf(match.Groups["class"].Value)));
        }

        lock (Gate) Cache[key] = shapes;
        return shapes;
    }

    /// <summary>
    /// Pulls one top-level group out of the drawing — a floor. Brace-free brute force: find the
    /// group's opening tag, then walk forward counting nesting until it closes.
    /// </summary>
    private static string? ExtractGroup(string svg, string id)
    {
        var open = new Regex($"<g[^>]*id=\"{Regex.Escape(id)}\"[^>]*>", RegexOptions.IgnoreCase);
        var start = open.Match(svg);
        if (!start.Success) return null;

        var depth = 0;
        var index = start.Index;

        foreach (Match tag in GroupTag().Matches(svg, start.Index))
        {
            if (tag.Value.StartsWith("</", StringComparison.Ordinal))
            {
                if (--depth == 0) return svg[index..(tag.Index + tag.Length)];
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                depth++;
            }
        }

        return null;
    }

    private static MapShapeRole RoleOf(string classes)
    {
        // Same vocabulary the service's ink modes use, so the overlay and the web app describe the
        // same map the same way.
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (name.ToLowerInvariant())
            {
                case "trees" or "land" or "rock" or "water" or "gravel" or "cement" or "tarmac" or "wood" or "misc":
                    return MapShapeRole.Terrain;
                case "building" or "floor" or "locked":
                    return MapShapeRole.Structure;
                case "map_border" or "fence" or "wall":
                    return MapShapeRole.Boundary;
                case "road_tarmac" or "road_gravel" or "railroad" or "powerline":
                    return MapShapeRole.Route;
                case "danger" or "danger_small":
                    return MapShapeRole.Hazard;
            }
        }

        return MapShapeRole.Other;
    }

    [GeneratedRegex("""<svg[^>]*\sviewBox\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBox();

    [GeneratedRegex("""<path[^>]*?(?:class="(?<class>[^"]*)")?[^>]*?\sd="(?<d>[^"]+)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex PathElement();

    [GeneratedRegex("</?g\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex GroupTag();
}
