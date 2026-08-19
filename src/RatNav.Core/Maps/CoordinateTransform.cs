using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>A point on a map image, in pixels from the top-left.</summary>
public readonly record struct MapPoint(double X, double Y);

/// <summary>
/// Converts Escape from Tarkov world coordinates into pixels on a map image, and back.
///
/// Calibration comes from TarkovTracker/tarkovdata's maps.json, which gives each map
/// <c>bounds</c> — two opposite corners of the image, in world coordinates — and a
/// <c>coordinateRotation</c>.
///
/// <para><b>Position mapping does not use coordinateRotation.</b> That is not an assumption; it
/// was measured. A screenshot taken on Customs at world <c>(-14.44, -139.32)</c>, from a spot
/// the player identified on the map, sits at <c>66.7%, 30.9%</c> of the image. Mapping the raw
/// coordinates straight through the bounds puts it at <c>66.6%, 30.8%</c> — a one-pixel match.
/// Rotating first, by any of the plausible conventions, misses by half the map:</para>
///
/// <list type="table">
///   <item><term>raw x/z through bounds</term><description>66.6%, 30.8% — matches</description></item>
///   <item><term>rotate 180° about origin</term><description>63.9%, 82.0%</description></item>
///   <item><term>rotate 180° about bounds centre</term><description>33.4%, 69.2%</description></item>
///   <item><term>axis swap</term><description>78.3%, 53.8%</description></item>
/// </list>
///
/// <para>So <c>coordinateRotation</c> describes how the map was <i>drawn</i> relative to game
/// north, which is what a heading needs (see <see cref="ToImageHeading"/>) and what a position
/// does not — the bounds already carry the drawing's orientation.</para>
///
/// <para><b>Open:</b> this was confirmed on a 180° map. Every map in the data is 180° except
/// Factory (90°) and The Lab (270°), and a 90° map is the only case where a wrong convention
/// would be obvious. Until a screenshot from one of those is checked, treat them as unverified.</para>
///
/// <para>Bounds are corner pairs rather than min/max on purpose: a map whose image runs opposite
/// to the world axis has its first bound larger than its second, and the normalization below
/// flips direction naturally because the span comes out negative.</para>
///
/// <para>Only the ground plane matters here. EFT's Y axis is vertical (which floor you are on)
/// and is carried separately for multi-level maps.</para>
/// </summary>
public sealed class CoordinateTransform
{
    private readonly MapImage _image;

    public CoordinateTransform(MapImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Bounds is not { Length: 2 } b || b[0] is not { Length: 2 } || b[1] is not { Length: 2 })
            throw new ArgumentException("Map bounds must be two corner pairs.", nameof(image));

        _image = image;
    }

    private double X1 => _image.Bounds[0][0];
    private double Y1 => _image.Bounds[0][1];
    private double X2 => _image.Bounds[1][0];
    private double Y2 => _image.Bounds[1][1];

    /// <summary>Where a world position falls on the map image, in pixels.</summary>
    public MapPoint ToPixels(GamePosition position)
    {
        var (u, v) = Normalized(position.X, position.Z);
        return new MapPoint(u * _image.PixelWidth, v * _image.PixelHeight);
    }

    /// <summary>
    /// Where a world position falls on the map image as a fraction of its size (0..1).
    /// Useful for renderers that scale the image themselves — which both of ours do.
    /// </summary>
    public MapPoint ToNormalized(GamePosition position)
    {
        var (u, v) = Normalized(position.X, position.Z);
        return new MapPoint(u, v);
    }

    /// <summary>The inverse: a point on the image back to world coordinates, for dropping pins by clicking.</summary>
    public GamePosition ToGamePosition(MapPoint pixel, double y = 0)
    {
        var u = _image.PixelWidth == 0 ? 0 : pixel.X / _image.PixelWidth;
        var v = _image.PixelHeight == 0 ? 0 : pixel.Y / _image.PixelHeight;

        var x = X1 + u * (X2 - X1);
        var z = Y1 + v * (Y2 - Y1);

        return new GamePosition(x, y, z);
    }

    /// <summary>
    /// A compass heading turned into image space, so a facing cone drawn on the map points the
    /// same way the player does. Unlike positions, a heading genuinely does need
    /// <c>coordinateRotation</c>: the bounds carry the drawing's orientation for points, but an
    /// angle has to be turned to match it.
    ///
    /// The sign is still unconfirmed — it takes two screenshots from one spot facing 90° apart
    /// to tell "+rotation" from "−rotation", and both shots so far were facing north.
    /// </summary>
    public double ToImageHeading(double headingDegrees)
        => ScreenshotFilename.Normalize(headingDegrees + _image.CoordinateRotation);

    /// <summary>Straight-line distance between two world positions on the ground plane, in metres.</summary>
    public static double GroundDistance(GamePosition a, GamePosition b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Compass bearing from one world position to another, in degrees (0 = north, clockwise).
    /// </summary>
    public static double BearingTo(GamePosition from, GamePosition to)
        => ScreenshotFilename.Normalize(Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI);

    /// <summary>
    /// How far to turn to face a target, in degrees: negative is left, positive is right.
    /// This is what the overlay actually shows — "30° right" beats "bearing 147°".
    /// </summary>
    public static double RelativeBearing(double headingDegrees, double targetBearingDegrees)
    {
        var delta = ScreenshotFilename.Normalize(targetBearingDegrees - headingDegrees);
        return delta > 180.0 ? delta - 360.0 : delta;
    }

    private (double U, double V) Normalized(double rx, double ry)
    {
        var spanX = X2 - X1;
        var spanY = Y2 - Y1;

        return (
            spanX == 0 ? 0 : (rx - X1) / spanX,
            spanY == 0 ? 0 : (ry - Y1) / spanY);
    }
}
