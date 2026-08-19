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
/// <para><b>The rotation applied to a position is <c>180° − coordinateRotation</c>, not
/// <c>coordinateRotation</c>.</b> That is measured, not reasoned. Players marked their own
/// position on two maps with different rotations, and only this rule fits both:</para>
///
/// <list type="table">
///   <item><term>Customs, rotation 180 → apply 0°</term>
///         <description>pin 66.6%, 30.8% vs marked 66.7%, 30.9% — 0.1pp</description></item>
///   <item><term>Factory, rotation 90 → apply +90°</term>
///         <description>two points, 5.3pp and 6.1pp, both offset the same direction</description></item>
/// </list>
///
/// <para>Customs alone was misleading: at 180° the correct rule and "apply nothing" are the same
/// thing, so the first version of this class simply dropped the rotation and looked right. Factory
/// is where that fell apart. It was settled by the <i>direction</i> between two marked points
/// rather than either point alone — the candidate rules predicted bearings of 115° and 148° for
/// the same walk, and the measured bearing was 146°, which is well outside how far a click can
/// slip.</para>
///
/// <para>The residual on Factory is a consistent few percent in one direction on both points,
/// which reads as click bias rather than a calibration error. Worth revisiting if a map ever
/// looks systematically shifted.</para>
///
/// <para><b>Open:</b> The Lab is the only untested rotation (270°, so this predicts −90°).
/// Every other map is 180°, which Customs covers.</para>
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

    private readonly double _cos;
    private readonly double _sin;

    public CoordinateTransform(MapImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Bounds is not { Length: 2 } b || b[0] is not { Length: 2 } || b[1] is not { Length: 2 })
            throw new ArgumentException("Map bounds must be two corner pairs.", nameof(image));

        _image = image;

        var radians = (180.0 - image.CoordinateRotation) * Math.PI / 180.0;
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
    /// A compass heading turned into image space, so a facing cone drawn on the map points the
    /// same way the player does.
    ///
    /// The direction is <b>minus</b> the rotation, measured the same way as the position rule: on
    /// Customs a walk between two known points appeared on the image 180° from its world bearing,
    /// and on Factory 90° the other way. Adding instead of subtracting looks correct on every 180°
    /// map — because ±180 are the same angle — and points the cone sideways on Factory and The Lab.
    /// </summary>
    public double ToImageHeading(double headingDegrees)
        => ScreenshotFilename.Normalize(headingDegrees - _image.CoordinateRotation);

    private (double X, double Y) Rotate(double x, double z)
        => (x * _cos - z * _sin, x * _sin + z * _cos);

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
