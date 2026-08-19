using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

// WinForms is referenced for the tray icon and brings a clashing Size with it.
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace RatNav.App;

/// <summary>
/// One drawn shape from a map: its outline, the role its class gives it, and the classes
/// themselves — kept so the map's own stylesheet can be applied when drawing it in full colour.
/// </summary>
public sealed record MapShape(Geometry Geometry, MapShapeRole Role, string Classes = "");

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

    /// <summary>Drop shadows and the like — real in the source, noise on a translucent overlay.</summary>
    Decoration,

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
/// <para><b>Classes are inherited from enclosing groups.</b> These maps put their semantics on the
/// <c>&lt;g&gt;</c> wrapper — <c>&lt;g id="buildings" class="building"&gt;</c> — and leave the
/// several hundred paths inside it bare. Reading the class off the path alone finds nothing at
/// all, which is exactly how a map with 484 perfectly good shapes in it renders as an empty box.
/// </para>
///
/// <para>Parsing is done once per map and cached: these files hold several hundred paths, and
/// re-parsing them on every position fix would be the one thing in RatNav that actually cost
/// frames.</para>
/// </summary>
public static partial class MapGeometry
{
    private static readonly Dictionary<string, IReadOnlyList<MapShape>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

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

        // A group's class applies to everything inside it, so the stack is what carries meaning
        // down to the paths. Innermost wins: a locked group inside a building group is locked.
        var groups = new Stack<string>();

        foreach (Match token in Drawable().Matches(scope))
        {
            var text = token.Value;

            if (text.StartsWith("</", StringComparison.Ordinal))
            {
                if (groups.Count > 0) groups.Pop();
                continue;
            }

            if (text.StartsWith("<g", StringComparison.OrdinalIgnoreCase))
            {
                // Self-closing groups hold nothing, so they never go on the stack.
                if (text.EndsWith("/>", StringComparison.Ordinal)) continue;

                // An id is a fair fallback: a group called "Rocks" says what it is with no class.
                var declared = Attribute(text, "class");
                groups.Push(declared.Length > 0 ? declared : Attribute(text, "id"));
                continue;
            }

            var geometry = GeometryOf(text);
            if (geometry is null) continue;

            geometry.Freeze();

            var own = Attribute(text, "class");
            var classes = own.Length > 0 ? own : Inherited(groups);

            shapes.Add(new MapShape(geometry, RoleOf(classes), classes));
        }

        lock (Gate) Cache[key] = shapes;
        return shapes;
    }

    /// <summary>The nearest enclosing class that means something, so a bare wrapper does not hide it.</summary>
    private static string Inherited(Stack<string> groups)
    {
        foreach (var name in groups)
        {
            if (RoleOf(name) != MapShapeRole.Other) return name;
        }

        return "";
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static Geometry? GeometryOf(string tag)
    {
        if (tag.StartsWith("<path", StringComparison.OrdinalIgnoreCase))
        {
            var data = Attribute(tag, "d");
            if (data.Length == 0) return null;

            try
            {
                return Geometry.Parse(data);
            }
            catch (FormatException)
            {
                // An unusual path is not worth losing the map over.
                return null;
            }
        }

        // Circles carry the things worth seeing on a nav overlay — mine fields and sniper zones.
        if (Number(tag, "cx", out var cx) && Number(tag, "cy", out var cy) && Number(tag, "r", out var r))
            return new EllipseGeometry(new Point(cx, cy), r, r);

        return null;
    }

    private static bool Number(string tag, string name, out double value) =>
        double.TryParse(
            Attribute(tag, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string? ExtractGroup(string svg, string id)
    {
        var open = new Regex($"<g[^>]*id=\"{Regex.Escape(id)}\"[^>]*>", RegexOptions.IgnoreCase);
        var start = open.Match(svg);
        if (!start.Success) return null;

        var depth = 0;

        foreach (Match tag in GroupTag().Matches(svg, start.Index))
        {
            if (tag.Value.StartsWith("</", StringComparison.Ordinal))
            {
                if (--depth == 0) return svg[start.Index..(tag.Index + tag.Length)];
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                depth++;
            }
        }

        return null;
    }

    /// <summary>
    /// The role a class name carries. The vocabulary is taken from the maps themselves rather than
    /// invented, and unrecognised names fall through to <see cref="MapShapeRole.Other"/>, which is
    /// drawn faintly rather than dropped — a new map should look thin, never blank.
    /// </summary>
    internal static MapShapeRole RoleOf(string classes)
    {
        foreach (var name in classes.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (name.ToLowerInvariant())
            {
                case "shadow":
                    return MapShapeRole.Decoration;

                case "building" or "buildings" or "floor" or "floors" or "locked" or "stairs" or
                     "structure" or "bunker" or "bunkers" or "basement" or "docks" or "plane" or
                     "chopper" or "cilinders" or "ramps":
                    return MapShapeRole.Structure;

                case "border" or "limit" or "fence" or "fences" or "wall":
                    return MapShapeRole.Boundary;

                case "road" or "roads" or "tarmac" or "railroad" or "powerline" or "powerlines" or
                     "pavement" or "connector" or "passages" or "tunnels":
                    return MapShapeRole.Route;

                case "danger" or "mine" or "mines" or "sniper" or "drones":
                    return MapShapeRole.Hazard;

                case "trees" or "forest" or "land" or "rock" or "rocks" or "water" or "gravel" or
                     "cement" or "concrete" or "wood" or "dirt" or "dirty" or "terrain" or
                     "ground" or "green" or "misc":
                    return MapShapeRole.Terrain;
            }
        }

        return MapShapeRole.Other;
    }

    [GeneratedRegex("""<svg[^>]*\sviewBox\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBox();

    /// <summary>
    /// Every token that matters, in document order: group open, group close, path, circle. One
    /// pass keeps nesting straight, which reading paths on their own cannot do.
    /// </summary>
    [GeneratedRegex("""</g\s*>|<g\b[^>]*>|<path\b[^>]*>|<circle\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex Drawable();

    [GeneratedRegex("""</?g\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex GroupTag();
}
