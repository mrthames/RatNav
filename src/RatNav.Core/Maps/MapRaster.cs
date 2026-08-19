using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>A stitched raster map, and the patch of the game world it covers.</summary>
public sealed record RasterMap
{
    /// <summary>Where the stitched image was written.</summary>
    public required string Path { get; init; }

    /// <summary>World bounds the image covers, as <c>[[x1, z1], [x2, z2]]</c>.</summary>
    public required double[][] Bounds { get; init; }

    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
}

/// <summary>
/// Builds a single image from a map's raster tiles.
///
/// <para><b>Why tiles at all.</b> The vector map draws what is there but not what it looks like —
/// Woods reads as roads through empty ground because its buildings are near-black on dark teal.
/// The tiles are a proper drawn map, and they come from the same project as the SVG, by authors
/// RatNav already credits, which is what makes them reasonable to use where a commercial
/// mapmaker's would not be.</para>
///
/// <para><b>Alignment.</b> The tile pyramid is square and does not share the vector's extent, so
/// its own world bounds are worked out from the Leaflet transformation the manifest carries and
/// handed back with the image. Everything downstream then places it through the same coordinate
/// transform as every other pin, rather than a second one that could disagree.</para>
///
/// <para>Fetched once and kept: a zoom-3 map is 64 requests, which is a great deal to repeat and
/// nothing at all to store.</para>
/// </summary>
public static class MapRaster
{
    /// <summary>Tile edge in pixels. The slippy-map convention, and what these tiles are.</summary>
    public const int TileSize = 256;

    /// <summary>
    /// Detail to fetch. Zoom 3 is an 8×8 grid — 2048 pixels square, which is more than any overlay
    /// shows and few enough requests to be polite about.
    /// </summary>
    public const int PreferredZoom = 3;

    /// <summary>
    /// The world bounds a map's tile pyramid covers.
    ///
    /// <para>Leaflet's simple CRS puts a pyramid across <c>0..256</c> transformed units whatever
    /// the zoom, so inverting the transformation at those two points gives the extent in game
    /// coordinates — which is the only form anything else here understands.</para>
    /// </summary>
    public static double[][]? BoundsOf(MapImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.TileTransform is not [var scaleX, var offsetX, var scaleZ, var offsetZ]) return null;
        if (scaleX == 0 || scaleZ == 0) return null;

        const double span = TileSize;

        double[] Edge(double scale, double offset) =>
            [(0 - offset) / scale, (span - offset) / scale];

        var x = Edge(scaleX, offsetX);
        var z = Edge(scaleZ, offsetZ);

        return [[x[0], z[0]], [x[1], z[1]]];
    }

    /// <summary>
    /// Fetches and stitches a map's tiles, or returns null when it has none.
    /// </summary>
    /// <param name="stitch">
    /// Combines the fetched tiles into one image and writes it. Passed in rather than done here
    /// because Core targets no particular UI and image handling is not portable — the desktop app
    /// supplies it.
    /// </param>
    public static async Task<RasterMap?> BuildAsync(
        MapImage image,
        string cacheDirectory,
        HttpClient http,
        Func<IReadOnlyList<TilePlacement>, int, int, string, Task> stitch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stitch);

        if (image.TilePath is not { Length: > 0 } template) return null;
        if (BoundsOf(image) is not { } bounds) return null;

        var zoom = Math.Clamp(PreferredZoom, Math.Max(0, image.MinZoom), Math.Max(1, image.MaxZoom));
        var across = 1 << zoom;
        var size = across * TileSize;

        Directory.CreateDirectory(cacheDirectory);

        var name = $"{Sanitise(template)}-z{zoom}.png";
        var path = Path.Combine(cacheDirectory, name);

        if (File.Exists(path))
        {
            return new RasterMap { Path = path, Bounds = bounds, PixelWidth = size, PixelHeight = size };
        }

        var tiles = new List<TilePlacement>();

        for (var x = 0; x < across; x++)
        {
            for (var y = 0; y < across; y++)
            {
                var url = template
                    .Replace("{z}", zoom.ToString())
                    .Replace("{x}", x.ToString())
                    .Replace("{y}", y.ToString());

                try
                {
                    // A pyramid is not always square in content — the edges of a non-square map
                    // are simply absent rather than blank, so a 404 is expected and not a failure.
                    var response = await http.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode) continue;

                    tiles.Add(new TilePlacement(
                        await response.Content.ReadAsByteArrayAsync(ct),
                        x * TileSize,
                        y * TileSize));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // One missing tile is a hole in the picture, not a reason to have no picture.
                }
            }
        }

        if (tiles.Count == 0) return null;

        await stitch(tiles, size, size, path);

        return new RasterMap { Path = path, Bounds = bounds, PixelWidth = size, PixelHeight = size };
    }

    private static string Sanitise(string template)
    {
        var trimmed = template
            .Replace("https://", "")
            .Replace("http://", "")
            .Replace("/{z}/{x}/{y}.png", "");

        return string.Concat(trimmed.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
    }
}

/// <summary>One fetched tile and where it belongs in the stitched image.</summary>
public readonly record struct TilePlacement(byte[] Bytes, int X, int Y);
