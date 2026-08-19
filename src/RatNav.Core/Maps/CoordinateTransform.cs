using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>A point on a map image, in pixels from the top-left.</summary>
public readonly record struct MapPoint(double X, double Y);

/// <summary>
/// Converts Escape from Tarkov world coordinates into pixels on a map image, and back.
///
/// Calibration comes from TarkovTracker/tarkovdata's maps.json, which gives each map a
/// <c>coordinateRotation</c> (degrees the image is turned relative to world axes) and
/// <c>bounds</c> — the two opposite corners of the image expressed in rotated world
/// coordinates. Bounds are corner pairs rather than min/max on purpose: a map whose image
/// runs opposite to the world axis has its first bound larger than its second, and the
/// normalization below flips direction naturally because the span is negative.
///
/// Only the ground plane matters here. EFT's Y axis is vertical (which floor you are on)
/// and is carried separately for multi-level maps.
/// </summary>
public sealed class CoordinateTransform
{
    private readonly MapImage _image;
    private readonly double _cos;
    private readonly double _sin;

    public CoordinateTransform(MapImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Bounds is not { Length: 2 } b || b[0] is not { Length: 2 } || b[1] is not { Length: 2 })
            throw new ArgumentException("Map bounds must be two corner pairs.", nameof(image));

        _image = image;

        var radians = image.CoordinateRotation * Math.PI / 180.0;
        _cos = Math.Cos(radians);
        _sin = Math.Sin(radians);
    }

    private double X1 => _image.Bounds[0][0];
    private double Y1 => _image.Bounds[0][1];
    private double X2 => _image.Bounds[1][0];
    private double Y2 => _image.Bounds[1][1];

    /// <summary>Where a world position falls on the map image, in pixels.</summary>
    public MapPoint ToPixels(GamePosition position)
    {
        var (rx, ry) = Rotate(position.X, position.Z);
        var (u, v) = Normalized(rx, ry);
        return new MapPoint(u * _image.PixelWidth, v * _image.PixelHeight);
    }

    /// <summary>
    /// Where a world position falls on the map image as a fraction of its size (0..1).
    /// Useful for renderers that scale the image themselves — which both of ours do.
    /// </summary>
    public MapPoint ToNormalized(GamePosition position)
    {
        var (rx, ry) = Rotate(position.X, position.Z);
        var (u, v) = Normalized(rx, ry);
        return new MapPoint(u, v);
    }

    /// <summary>The inverse: a point on the image back to world coordinates, for dropping pins by clicking.</summary>
    public GamePosition ToGamePosition(MapPoint pixel, double y = 0)
    {
        var u = _image.PixelWidth == 0 ? 0 : pixel.X / _image.PixelWidth;
        var v = _image.PixelHeight == 0 ? 0 : pixel.Y / _image.PixelHeight;

        var rx = X1 + u * (X2 - X1);
        var ry = Y1 + v * (Y2 - Y1);

        // Undo the rotation.
        var x = rx * _cos + ry * _sin;
        var z = -rx * _sin + ry * _cos;

        return new GamePosition(x, y, z);
    }

    /// <summary>
    /// A compass heading rotated into image space, so a facing cone drawn on the map points
    /// the same way the player does.
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

    private (double X, double Y) Rotate(double x, double z)
        => (x * _cos - z * _sin, x * _sin + z * _cos);

    private (double U, double V) Normalized(double rx, double ry)
    {
        var spanX = X2 - X1;
        var spanY = Y2 - Y1;

        return (
            spanX == 0 ? 0 : (rx - X1) / spanX,
            spanY == 0 ? 0 : (ry - Y1) / spanY);
    }
}
