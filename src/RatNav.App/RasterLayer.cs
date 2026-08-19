using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.App;

/// <summary>
/// Turns a map's raster tiles into one image the overlay can draw beneath the vector.
///
/// <para>The stitching lives here rather than in Core because Core targets no particular UI and
/// image composition is not portable. Core decides which tiles and where; this puts them on a
/// bitmap.</para>
/// </summary>
public static class RasterLayer
{
    /// <summary>Builds and caches the raster for a map, or returns null when it has no tiles.</summary>
    public static Task<RasterMap?> BuildAsync(
        MapImage image, string cacheDirectory, HttpClient http, CancellationToken ct = default) =>
        MapRaster.BuildAsync(image, cacheDirectory, http, Stitch, ct);

    private static Task Stitch(IReadOnlyList<TilePlacement> tiles, int width, int height, string path)
    {
        var canvas = new DrawingVisual();

        using (var drawing = canvas.RenderOpen())
        {
            foreach (var tile in tiles)
            {
                var bitmap = Decode(tile.Bytes);
                if (bitmap is null) continue;

                drawing.DrawImage(
                    bitmap,
                    new System.Windows.Rect(tile.X, tile.Y, MapRaster.TileSize, MapRaster.TileSize));
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        // Written whole then moved, so an interrupted fetch cannot leave half a map behind that
        // would be treated as cached and never repaired.
        var temp = path + ".tmp";

        using (var file = File.Create(temp)) encoder.Save(file);
        File.Move(temp, path, overwrite: true);

        return Task.CompletedTask;
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException)
        {
            // One unreadable tile is a hole, not a failure.
            return null;
        }
    }
}
