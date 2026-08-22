using System.Text.RegularExpressions;
using System.Windows.Media;

// WinForms is referenced for the tray icon and brings a clashing Brush with it.
using Brush = System.Windows.Media.Brush;

namespace RatNav.App;

/// <summary>How one class of shape is meant to look, according to the map's own stylesheet.</summary>
/// <param name="FillOpacity">
/// How solid the fill is, from the stylesheet's <c>fill-opacity</c>. Null means it said nothing.
///
/// <para>Not a detail. Streets marks its sniper zones <c>.danger { fill:red; fill-opacity:.4 }</c>,
/// and dropping the second half turned three warnings into three solid red blocks covering the
/// map underneath them.</para>
/// </param>
/// <param name="Dash">The stroke dash pattern, which is how a hazard's outline says "keep out".</param>
public sealed record MapStyle(
    Brush? Fill,
    Brush? Stroke,
    double StrokeWidth,
    double? FillOpacity = null,
    IReadOnlyList<double>? Dash = null);

/// <summary>
/// Reads the colors a map was drawn in.
///
/// <para>These maps carry a stylesheet — <c>.trees { fill:#144043 }</c>, <c>.water {
/// fill:#4a6b96 }</c> — around fifteen colors that say what everything is. RatNav was discarding
/// all of it and recolouring every shape by role, which works over a dark game scene but reduces a
/// map like Woods, drawn with 481 shapes across forest, water, rock and road, to a few dozen
/// accent-colored outlines. It looked minimal because most of it had been thrown away, not
/// because it was not there.</para>
///
/// <para>So the palette is kept, and offered as an ink level of its own. The role-based treatment
/// is still the right answer over a firefight; this is the right answer when you want to read the
/// map as a map.</para>
/// </summary>
public static partial class MapPalette
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, MapStyle>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    /// <summary>The stylesheet as a lookup from class name to how it is drawn.</summary>
    public static IReadOnlyDictionary<string, MapStyle> Read(string svg, string cacheKey)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var styles = new Dictionary<string, MapStyle>(StringComparer.OrdinalIgnoreCase);
        var sheet = StyleBlock().Match(svg);

        if (sheet.Success)
        {
            foreach (Match rule in Rule().Matches(sheet.Groups[1].Value))
            {
                var body = rule.Groups["body"].Value;

                var style = new MapStyle(
                    Paint(Declaration(body, "fill")),
                    Paint(Declaration(body, "stroke")),
                    Width(Declaration(body, "stroke-width")),
                    Opacity(Declaration(body, "fill-opacity")),
                    Dashes(Declaration(body, "stroke-dasharray")));

                // A class can be listed several times — ".road_small { stroke-width:5 }" refines
                // ".road_tarmac" — so later rules fill in what earlier ones left out rather than
                // replacing them wholesale.
                foreach (var name in rule.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var key = name.Trim().TrimStart('.');
                    if (key.Length == 0) continue;

                    styles[key] = styles.TryGetValue(key, out var existing)
                        ? new MapStyle(
                            style.Fill ?? existing.Fill,
                            style.Stroke ?? existing.Stroke,
                            style.StrokeWidth > 0 ? style.StrokeWidth : existing.StrokeWidth,
                            style.FillOpacity ?? existing.FillOpacity,
                            style.Dash ?? existing.Dash)
                        : style;
                }
            }
        }

        lock (Gate) Cache[cacheKey] = styles;
        return styles;
    }

    /// <summary>The style for a shape, from the first of its classes the stylesheet names.</summary>
    public static MapStyle? For(IReadOnlyDictionary<string, MapStyle> styles, string classes)
    {
        MapStyle? found = null;

        foreach (var name in classes.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!styles.TryGetValue(name, out var style)) continue;

            // Later classes refine earlier ones, the way CSS does — "road_tarmac road_large" is a
            // tarmac road at the large width.
            found = found is null
                ? style
                : new MapStyle(
                    style.Fill ?? found.Fill,
                    style.Stroke ?? found.Stroke,
                    style.StrokeWidth > 0 ? style.StrokeWidth : found.StrokeWidth,
                    style.FillOpacity ?? found.FillOpacity,
                    style.Dash ?? found.Dash);
        }

        return found;
    }

    private static string? Declaration(string body, string property)
    {
        var match = Regex.Match(body, $@"(?:^|;)\s*{property}\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static Brush? Paint(string? value)
    {
        // "none" is a real answer meaning "do not paint this", and it has to stay distinct from
        // "nothing was said", which inherits.
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            if (new BrushConverter().ConvertFromString(value) is not Brush brush) return null;

            brush.Freeze();
            return brush;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return null;
        }
    }

    private static double Width(string? value) =>
        double.TryParse(value, out var width) ? width : 0;

    private static double? Opacity(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var opacity)
            ? Math.Clamp(opacity, 0, 1)
            : null;

    /// <summary>
    /// "6,6" or "4 2" — SVG allows either separator. A single number means equal dash and gap,
    /// which WPF does not assume, so it is doubled here.
    /// </summary>
    private static IReadOnlyList<double>? Dashes(string? value)
    {
        if (value is not { Length: > 0 }) return null;

        var parts = value
            .Split([',', ' ', '	'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToList();

        if (parts.Count == 0) return null;

        return parts.Count == 1 ? [parts[0], parts[0]] : parts;
    }

    [GeneratedRegex(@"<style[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex(@"(?<names>[^{}]+)\{(?<body>[^}]*)\}")]
    private static partial Regex Rule();
}
