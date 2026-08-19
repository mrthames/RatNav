using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RatNav.Core.Model;

namespace RatNav.Core.Data;

/// <summary>
/// Supplies map images, the coordinate calibration that goes with them, floor definitions and
/// named landmarks.
///
/// <para><b>Source: tarkov.dev's own map metadata</b> (<c>src/data/maps.json</c> in the tarkov-dev
/// repo) with images from <c>assets.tarkov.dev</c>. This replaced TarkovTracker/tarkovdata, which
/// was the obvious first choice and turned out to be both stale and internally inconsistent:</para>
///
/// <list type="bullet">
///   <item>Its Interchange map predates a whole area of the map that the game has had for a while,
///   and its image is square, which made the calibration ambiguous by construction.</item>
///   <item>Its Reserve bounds are simply wrong — the same image, but a vertical range about 140
///   metres too tall, which put positions roughly 75 m out.</item>
///   <item>Its Factory bounds list the corners in the opposite order from every other map, which
///   made Factory look like it needed swapped axes when it did not.</item>
/// </list>
///
/// <para>With tarkov.dev's numbers, all ten maps calibrate with a plain <c>(x, z)</c> mapping and
/// no rotation at all — the aspect-ratio check agrees on every one, to within 1.4%. The days spent
/// deriving rotation rules were spent compensating for bad inputs. See docs/calibration.md.</para>
///
/// <para>Images are downloaded to the local cache and never committed: they are community
/// mapmakers' work (credited per map through <see cref="MapImage.Author"/>), and redistributing
/// them here would be both rude and legally murky.</para>
/// </summary>
public sealed partial class MapAssets(HttpClient http, string cacheDirectory)
{
    public const string CalibrationUrl =
        "https://raw.githubusercontent.com/the-hideout/tarkov-dev/main/src/data/maps.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string ImageCacheDirectory => Path.Combine(cacheDirectory, "maps");

    /// <summary>
    /// Fetches calibration for every map tarkov.dev draws.
    ///
    /// Entries carry their own key and name, so maps still render when the game-data API is
    /// unavailable — these are different hosts and there is no reason an outage at one should
    /// blank the other.
    /// </summary>
    public async Task<IReadOnlyList<MapCalibration>> GetCalibrationAsync(CancellationToken ct = default)
    {
        string body;
        try
        {
            body = await http.GetStringAsync(CalibrationUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TarkovDevException("Could not fetch map calibration from tarkov.dev.", ex);
        }

        List<MapGroup>? groups;
        try
        {
            groups = JsonSerializer.Deserialize<List<MapGroup>>(body, Json);
        }
        catch (JsonException ex)
        {
            throw new TarkovDevException("tarkov.dev's maps.json was not in the expected shape.", ex);
        }

        var result = new List<MapCalibration>();

        foreach (var group in groups ?? [])
        {
            foreach (var variant in group.Maps ?? [])
            {
                // Several projections exist per map — 3D views, tile pyramids. Only the flat
                // interactive SVG is something we can plot a coordinate on.
                if (!string.Equals(variant.Projection, "interactive", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (variant.Key is not { Length: > 0 } key) continue;
                if (variant.SvgPath is not { Length: > 0 } svgPath) continue;
                if (variant.Bounds is not { Length: 2 }) continue;

                result.Add(new MapCalibration
                {
                    Key = key,
                    Name = group.NormalizedName ?? key,
                    Image = new MapImage
                    {
                        SourceUrl = svgPath,
                        CoordinateRotation = (int)Math.Round(variant.CoordinateRotation ?? 0),
                        Bounds = variant.Bounds,
                        DefaultFloor = variant.SvgLayer,
                        Author = variant.Author,
                        AuthorLink = variant.AuthorLink,
                        Floors = ReadFloors(variant),
                    },
                    Labels = ReadLabels(variant),
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Floors, bottom to top, each with the world height band it covers.
    ///
    /// The height band is what lets RatNav pick the floor for you: a screenshot filename carries
    /// the player's elevation, so the right level of a multi-storey map can be chosen without
    /// anyone touching a control mid-raid.
    /// </summary>
    private static IReadOnlyList<MapFloor> ReadFloors(MapVariant variant)
    {
        var floors = new List<MapFloor>();

        if (variant.SvgLayer is { Length: > 0 } baseLayer)
        {
            var range = variant.HeightRange;
            floors.Add(new MapFloor
            {
                Name = "Ground",
                Layer = baseLayer,
                MinHeight = range is { Length: 2 } ? range[0] : null,
                MaxHeight = range is { Length: 2 } ? range[1] : null,
            });
        }

        foreach (var layer in variant.Layers ?? [])
        {
            if (layer.SvgLayer is not { Length: > 0 } id) continue;

            var extent = (layer.Extents ?? []).FirstOrDefault(e => e.Height is { Length: 2 });

            floors.Add(new MapFloor
            {
                Name = layer.Name ?? id,
                Layer = id,
                MinHeight = extent?.Height?[0],
                MaxHeight = extent?.Height?[1],
            });
        }

        return floors;
    }

    /// <summary>
    /// Named places — "Big Red", "Dorms", "Resort", "Sawmill". These are what players actually
    /// call parts of a map, so a route reads as somewhere to go rather than a coordinate.
    /// </summary>
    private static IReadOnlyList<MapLabel> ReadLabels(MapVariant variant) =>
    [
        .. (variant.Labels ?? [])
            .Where(l => l.Text is { Length: > 0 } && l.Position is { Length: 2 })
            .Select(l => new MapLabel
            {
                Text = l.Text!,
                Position = new GamePosition(l.Position![0], 0, l.Position[1]),
                MinHeight = l.Bottom,
                MaxHeight = l.Top,
            })
    ];

    /// <summary>
    /// Ensures a map image is on disk and returns its local path, downloading it once.
    /// Returns null if it cannot be fetched — a missing image degrades the map view rather than
    /// breaking planning.
    /// </summary>
    public async Task<string?> EnsureImageAsync(MapImage image, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ImageCacheDirectory);

        var fileName = Path.GetFileName(new Uri(image.SourceUrl).LocalPath);
        var localPath = Path.Combine(ImageCacheDirectory, fileName);

        if (File.Exists(localPath)) return localPath;

        try
        {
            var bytes = await http.GetByteArrayAsync(image.SourceUrl, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads a map image if needed and reads its intrinsic size, which
    /// <see cref="Maps.CalibrationSolver"/> compares against the map's world bounds.
    /// </summary>
    public async Task<(int Width, int Height)?> GetImageSizeAsync(MapImage image, CancellationToken ct = default)
    {
        var path = await EnsureImageAsync(image, ct);
        if (path is null) return null;

        try
        {
            // The header carries the dimensions; these files run to hundreds of kilobytes and
            // there is no reason to read the geometry to find out how big it is.
            using var reader = new StreamReader(path);
            var buffer = new char[4096];
            var read = await reader.ReadAsync(buffer, ct);

            return ReadSvgSize(new string(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an SVG's intrinsic pixel size from its width/height attributes, falling back to its
    /// viewBox. Renderers scale the image anyway — normalized coordinates are the primary path —
    /// but the natural size is what makes the aspect-ratio check possible.
    /// </summary>
    public static (int Width, int Height)? ReadSvgSize(string svgMarkup)
    {
        var width = ReadLength(WidthAttribute().Match(svgMarkup));
        var height = ReadLength(HeightAttribute().Match(svgMarkup));

        if (width is > 0 && height is > 0)
            return ((int)width.Value, (int)height.Value);

        var viewBox = ViewBoxAttribute().Match(svgMarkup);
        if (!viewBox.Success) return null;

        var parts = viewBox.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return null;

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vw) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh))
            return null;

        return vw > 0 && vh > 0 ? ((int)vw, (int)vh) : null;
    }

    private static double? ReadLength(Match match)
    {
        if (!match.Success) return null;
        var raw = match.Groups[1].Value.TrimEnd('p', 'x', 'P', 'X').Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex("""<svg[^>]*\swidth\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex WidthAttribute();

    [GeneratedRegex("""<svg[^>]*\sheight\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex HeightAttribute();

    [GeneratedRegex("""<svg[^>]*\sviewBox\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBoxAttribute();

    // ---- Wire shapes for tarkov.dev's maps.json.

    private sealed record MapGroup(string? NormalizedName, List<MapVariant>? Maps);

    private sealed record MapVariant(
        string? Key, string? Projection, string? SvgPath, string? SvgLayer,
        double[][]? Bounds, double? CoordinateRotation, double[]? HeightRange,
        string? Author, string? AuthorLink,
        List<LayerEntry>? Layers, List<LabelEntry>? Labels);

    private sealed record LayerEntry(string? Name, string? SvgLayer, List<ExtentEntry>? Extents);
    private sealed record ExtentEntry(double[]? Height);
    private sealed record LabelEntry(string? Text, double[]? Position, double? Top, double? Bottom);
}
