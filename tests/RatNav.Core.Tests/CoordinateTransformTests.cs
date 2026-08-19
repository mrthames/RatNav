using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

/// <summary>
/// These lock in the maths. The convention question — whether <c>coordinateRotation</c> applies
/// to positions — was settled by measurement, not by reasoning; see
/// <see cref="Real_customs_screenshot_lands_where_the_player_stood"/>, which is the ground truth
/// the rest of this file is consistent with.
/// </summary>
public class CoordinateTransformTests
{
    // 180 is the identity case under the 180-minus-rotation rule, and it is also what almost
    // every real map declares — so it is the right default for tests about bounds rather than
    // rotation.
    private static MapImage Square(int rotation = 180) => new()
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
            CoordinateRotation = 180,
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
    public void Real_customs_screenshot_lands_where_the_player_stood()
    {
        // Ground truth. Escape from Tarkov wrote this position into a screenshot filename, and
        // the player identified the spot on the map at 66.7%, 30.9%. Mapping raw x/z straight
        // through the bounds hits it; rotating first misses by half the map, which is how we
        // learned coordinateRotation is not a position transform.
        var customs = new MapImage
        {
            SourceUrl = "https://example.invalid/Customs.svg",
            CoordinateRotation = 180,
            Bounds = [[698, -307], [-371, 237]],
        };

        var point = new CoordinateTransform(customs)
            .ToNormalized(new GamePosition(-14.44, 1.44, -139.32));

        Assert.Equal(0.667, point.X, 2);
        Assert.Equal(0.309, point.Y, 2);
    }

    [Fact]
    public void Real_factory_screenshots_land_where_the_player_stood()
    {
        // The map that proved the rule. Factory's rotation is 90, and at 90 the candidate
        // conventions separate — unlike Customs at 180, where several of them coincide.
        var factory = new MapImage
        {
            SourceUrl = "https://example.invalid/Factory.svg",
            CoordinateRotation = 90,
            Bounds = [[-67, 69], [76.6, -65.5]],
        };

        var t = new CoordinateTransform(factory);

        var turn = t.ToNormalized(new GamePosition(44.16, 5.89, 39.67));
        Assert.Equal(0.212, turn.X, 1);
        Assert.Equal(0.233, turn.Y, 1);

        var extract = t.ToNormalized(new GamePosition(58.66, 1.81, 66.15));
        Assert.Equal(0.041, extract.X, 1);
        Assert.Equal(0.127, extract.Y, 1);

        // The clincher was the direction between the two, not either point alone: the rival
        // convention predicted 115 degrees for this walk where the player measured 146.
        var du = extract.X - turn.X;
        var dv = extract.Y - turn.Y;
        var bearing = Math.Atan2(-dv * 141.80, du * 131.57) * 180 / Math.PI;
        Assert.InRange(ScreenshotFilename.Normalize(bearing), 138, 156);
    }

    [Theory]
    [InlineData(180, 0)]     // Customs and almost every other map
    [InlineData(90, 90)]     // Factory
    [InlineData(270, -90)]   // The Lab — predicted, not yet measured
    public void The_applied_rotation_is_180_minus_the_declared_one(int declared, int applied)
    {
        // A point on the +X axis, so the rotation is easy to see.
        var actual = new CoordinateTransform(Square(declared)).ToNormalized(new GamePosition(100, 0, 0));

        var radians = applied * Math.PI / 180.0;
        var expectedX = (100 * Math.Cos(radians) + 100) / 200;
        var expectedY = (100 * Math.Sin(radians) + 100) / 200;

        Assert.Equal(expectedX, actual.X, 6);
        Assert.Equal(expectedY, actual.Y, 6);
    }

    [Theory]
    [InlineData(0, 30, 30)]
    [InlineData(180, 30, 210)]
    [InlineData(90, 350, 260)]
    [InlineData(270, 0, 90)]
    public void A_heading_is_turned_against_how_the_map_was_drawn(int rotation, double heading, double expected)
    {
        // Minus, not plus. Adding is indistinguishable on a 180 map and points the facing cone
        // sideways on Factory and The Lab.
        var t = new CoordinateTransform(Square(rotation));
        Assert.Equal(expected, t.ToImageHeading(heading), 3);
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
            CoordinateRotation = 180,
            Bounds = [[0, 0]],
        };

        Assert.Throws<ArgumentException>(() => new CoordinateTransform(broken));
    }
}
