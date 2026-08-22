using RatNav.Core.Model;

namespace RatNav.Core.Maps;

/// <summary>A point on a map image, in pixels from the top-left.</summary>
public readonly record struct MapPoint(double X, double Y);

/// <summary>
/// Converts Escape from Tarkov world coordinates into pixels on a map image, and back.
///
/// <para>Calibration is an <see cref="AxisMapping"/> — which world axis runs across the image,
/// which runs down it, and the sign of each — combined with the <c>bounds</c> that
/// TarkovTracker/tarkovdata publishes per map. The mapping is solved per map by
/// <see cref="CalibrationSolver"/>, because it is a property of how each drawing was made and
/// cannot be computed from the declared rotation. Trying to compute it produced a rule that fit
/// two maps and was badly wrong on a third; docs/calibration.md has the full account.</para>
///
/// <para>Bounds are corner pairs, not min/max. A map whose image runs opposite to the world axis
/// has its first bound larger than its second, and the normalization below honors that on its
/// own — sorting them into min/max would mirror the map.</para>
///
/// <para>Only the ground plane matters here. EFT's Y axis is vertical — which floor you are on —
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

    private AxisMapping Mapping => _image.Mapping;

    private double X1 => _image.Bounds[0][0];
    private double Y1 => _image.Bounds[0][1];
    private double X2 => _image.Bounds[1][0];
    private double Y2 => _image.Bounds[1][1];

    /// <summary>Where a world position falls on the map image, in pixels.</summary>
    public MapPoint ToPixels(GamePosition position)
    {
        var (u, v) = Normalized(position);
        return new MapPoint(u * _image.PixelWidth, v * _image.PixelHeight);
    }

    /// <summary>
    /// Where a world position falls on the image as a fraction of its size (0..1).
    /// This is the primary form: both renderers scale the image themselves.
    /// </summary>
    public MapPoint ToNormalized(GamePosition position)
    {
        var (u, v) = Normalized(position);
        return new MapPoint(u, v);
    }

    /// <summary>The inverse: a point on the image back to world coordinates, for dropping pins by clicking.</summary>
    public GamePosition ToGamePosition(MapPoint pixel, double y = 0)
    {
        var u = _image.PixelWidth == 0 ? 0 : pixel.X / _image.PixelWidth;
        var v = _image.PixelHeight == 0 ? 0 : pixel.Y / _image.PixelHeight;

        var a = X1 + u * (X2 - X1);
        var b = Y1 + v * (Y2 - Y1);

        var (x, z) = Mapping.Reverse(a, b);
        return new GamePosition(x, y, z);
    }

    /// <summary>
    /// A compass heading turned into image space, so a facing cone drawn on the map points the
    /// same way the player does.
    ///
    /// <para>This is <i>derived from the position mapping</i> rather than being a rule of its own:
    /// it projects a short step in the given direction and measures the angle that step makes on
    /// the image. Doing it this way means the cone cannot disagree with the pins — an earlier
    /// version kept a separate heading rule, and separate rules drift apart. The image's
    /// proportions are included, because an angle on a non-square image is not the same angle in
    /// normalized space.</para>
    /// </summary>
    public double ToImageHeading(double headingDegrees)
    {
        var radians = headingDegrees * Math.PI / 180.0;

        // Bearing 0 is +Z (north) and 90 is +X (east), matching BearingTo.
        var from = ToNormalized(new GamePosition(0, 0, 0));
        var to = ToNormalized(new GamePosition(Math.Sin(radians), 0, Math.Cos(radians)));

        var width = _image.PixelWidth > 0 ? _image.PixelWidth : 1;
        var height = _image.PixelHeight > 0 ? _image.PixelHeight : 1;

        var du = (to.X - from.X) * width;
        var dv = (to.Y - from.Y) * height;

        // Screen space: 0 is up, angles increase clockwise, and V grows downward.
        return ScreenshotFilename.Normalize(Math.Atan2(du, -dv) * 180.0 / Math.PI);
    }

    /// <summary>Straight-line distance between two world positions on the ground plane, in meters.</summary>
    public static double GroundDistance(GamePosition a, GamePosition b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Compass bearing from one world position to another, in degrees (0 = north, clockwise).</summary>
    public static double BearingTo(GamePosition from, GamePosition to)
        => ScreenshotFilename.Normalize(Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI);

    /// <summary>
    /// How far to turn to face a target, in degrees: negative is left, positive is right.
    /// This is what the overlay shows — "30° right" beats "bearing 147°".
    /// </summary>
    public static double RelativeBearing(double headingDegrees, double targetBearingDegrees)
    {
        var delta = ScreenshotFilename.Normalize(targetBearingDegrees - headingDegrees);
        return delta > 180.0 ? delta - 360.0 : delta;
    }

    private (double U, double V) Normalized(GamePosition position)
    {
        var (a, b) = Mapping.Apply(position.X, position.Z);

        var spanU = X2 - X1;
        var spanV = Y2 - Y1;

        return (
            spanU == 0 ? 0 : (a - X1) / spanU,
            spanV == 0 ? 0 : (b - Y1) / spanV);
    }
}
