using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

/// <summary>
/// These lock in the maths. Whether the *convention* matches tarkovdata's — particularly the
/// direction of <c>coordinateRotation</c> — is settled by the Pass 1 checkpoint against a real
/// screenshot on a real map, because no amount of synthetic testing can prove that.
/// </summary>
public class CoordinateTransformTests
{
    private static MapImage Square(int rotation = 0) => new()
    {
        SourceUrl = "https://example.invalid/test.svg",
        CoordinateRotation = rotation,
        Bounds = [[-100, -100], [100, 100]],
        PixelWidth = 1000,
        PixelHeight = 1000,
    };

    [Fact]
    public void Origin_lands_in_the_middle_of_a_centred_map()
    {
        var t = new CoordinateTransform(Square());
        var p = t.ToPixels(new GamePosition(0, 0, 0));

        Assert.Equal(500, p.X, 3);
        Assert.Equal(500, p.Y, 3);
    }

    [Fact]
    public void Corners_land_on_corners()
    {
        var t = new CoordinateTransform(Square());

        var topLeft = t.ToPixels(new GamePosition(-100, 0, -100));
        Assert.Equal(0, topLeft.X, 3);
        Assert.Equal(0, topLeft.Y, 3);

        var bottomRight = t.ToPixels(new GamePosition(100, 0, 100));
        Assert.Equal(1000, bottomRight.X, 3);
        Assert.Equal(1000, bottomRight.Y, 3);
    }

    [Fact]
    public void Bounds_given_back_to_front_flip_the_axis()
    {
        // Customs is stored as [[698, -307], [-371, 237]] — the first x is larger than the second.
        // That is deliberate upstream, and the normalization has to honour it rather than
        // sorting the bounds into min/max, which would mirror the map.
        var image = new MapImage
        {
            SourceUrl = "https://example.invalid/test.svg",
            CoordinateRotation = 0,
            Bounds = [[100, -100], [-100, 100]],
            PixelWidth = 1000,
            PixelHeight = 1000,
        };

        var t = new CoordinateTransform(image);

        var p = t.ToPixels(new GamePosition(100, 0, -100));
        Assert.Equal(0, p.X, 3);
        Assert.Equal(0, p.Y, 3);

        var q = t.ToPixels(new GamePosition(-100, 0, 100));
        Assert.Equal(1000, q.X, 3);
        Assert.Equal(1000, q.Y, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Pixel_conversion_round_trips_back_to_world_coordinates(int rotation)
    {
        var t = new CoordinateTransform(Square(rotation));
        var original = new GamePosition(37.5, 2.5, -62.25);

        var round = t.ToGamePosition(t.ToPixels(original), original.Y);

        Assert.Equal(original.X, round.X, 3);
        Assert.Equal(original.Z, round.Z, 3);
    }

    [Fact]
    public void Normalized_coordinates_are_the_pixel_ones_divided_by_image_size()
    {
        var t = new CoordinateTransform(Square());
        var n = t.ToNormalized(new GamePosition(50, 0, 0));

        Assert.Equal(0.75, n.X, 3);
        Assert.Equal(0.5, n.Y, 3);
    }

    [Fact]
    public void Rotation_is_applied_before_the_bounds()
    {
        // Under a 90 degree rotation a point on the +X axis should move onto the image's
        // vertical axis, not stay on its horizontal one.
        var t = new CoordinateTransform(Square(90));
        var p = t.ToNormalized(new GamePosition(100, 0, 0));

        Assert.Equal(0.5, p.X, 3);
        Assert.Equal(1.0, p.Y, 3);
    }

    [Fact]
    public void Ground_distance_ignores_which_floor_you_are_on()
    {
        var a = new GamePosition(0, 0, 0);
        var b = new GamePosition(3, 100, 4);

        Assert.Equal(5, CoordinateTransform.GroundDistance(a, b), 3);
    }

    [Theory]
    [InlineData(0, 10, 0)]      // due north
    [InlineData(10, 0, 90)]     // due east
    [InlineData(0, -10, 180)]   // due south
    [InlineData(-10, 0, 270)]   // due west
    public void Bearing_is_a_compass_heading(double dx, double dz, double expected)
    {
        var bearing = CoordinateTransform.BearingTo(
            new GamePosition(0, 0, 0),
            new GamePosition(dx, 0, dz));

        Assert.Equal(expected, bearing, 3);
    }

    [Theory]
    [InlineData(0, 30, 30)]      // target 30 degrees to the right
    [InlineData(0, 330, -30)]    // and to the left, rather than "330 right"
    [InlineData(350, 10, 20)]    // wrapping past north
    [InlineData(10, 190, 180)]   // directly behind
    public void Relative_bearing_reads_as_turn_left_or_right(double heading, double target, double expected)
    {
        Assert.Equal(expected, CoordinateTransform.RelativeBearing(heading, target), 3);
    }

    [Fact]
    public void Rejects_calibration_that_is_missing_bounds()
    {
        var broken = new MapImage
        {
            SourceUrl = "https://example.invalid/test.svg",
            CoordinateRotation = 0,
            Bounds = [[0, 0]],
        };

        Assert.Throws<ArgumentException>(() => new CoordinateTransform(broken));
    }
}
