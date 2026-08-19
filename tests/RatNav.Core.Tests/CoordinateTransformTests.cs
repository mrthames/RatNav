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
    private static MapImage Square(AxisMapping? mapping = null) => new()
    {
        SourceUrl = "https://example.invalid/test.svg",
        CoordinateRotation = 180,
        Bounds = [[-100, -100], [100, 100]],
        PixelWidth = 1000,
        PixelHeight = 1000,
        Mapping = mapping ?? AxisMapping.Direct,
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
            Mapping = AxisMapping.Direct,
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
    [InlineData(false, 1, 1)]
    [InlineData(false, -1, 1)]
    [InlineData(true, 1, -1)]
    [InlineData(true, -1, -1)]
    public void Pixel_conversion_round_trips_back_to_world_coordinates(bool swapped, int signU, int signV)
    {
        var t = new CoordinateTransform(Square(new AxisMapping(swapped, signU, signV)));
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
            Bounds = [[698, -307], [-372, 237]],
            Mapping = AxisMapping.Direct,
        };

        var point = new CoordinateTransform(customs)
            .ToNormalized(new GamePosition(-14.44, 1.44, -139.32));

        Assert.Equal(0.667, point.X, 2);
        Assert.Equal(0.309, point.Y, 2);
    }

    // The Factory equivalent of the Customs test above is deliberately absent. Its ground truth
    // was a position marked against TarkovTracker/tarkovdata's Factory drawing, and RatNav now
    // uses tarkov.dev's — a different revision with 180 paths against 161 and a different
    // viewBox. Percentages measured on one image say nothing about the other, so keeping the old
    // numbers would have been a test that passed while describing a map nobody sees.

    [Fact]
    public void Declared_rotation_does_not_affect_anything()
    {
        // coordinateRotation is kept for display only. Deriving the mapping from it produced a
        // rule that fit two maps and put every one of The Lab's extracts off the image.
        var a = Square() with { CoordinateRotation = 90 };
        var b = Square() with { CoordinateRotation = 270 };

        var pa = new CoordinateTransform(a).ToNormalized(new GamePosition(50, 0, -25));
        var pb = new CoordinateTransform(b).ToNormalized(new GamePosition(50, 0, -25));

        Assert.Equal(pa.X, pb.X, 9);
        Assert.Equal(pa.Y, pb.Y, 9);
    }

    [Fact]
    public void A_swapped_mapping_sends_the_x_axis_down_the_image()
    {
        var direct = new CoordinateTransform(Square()).ToNormalized(new GamePosition(100, 0, 0));
        Assert.Equal(1.0, direct.X, 6);
        Assert.Equal(0.5, direct.Y, 6);

        var swapped = new CoordinateTransform(Square(new AxisMapping(true, 1, 1)))
            .ToNormalized(new GamePosition(100, 0, 0));
        Assert.Equal(0.5, swapped.X, 6);
        Assert.Equal(1.0, swapped.Y, 6);
    }

    [Fact]
    public void A_heading_follows_the_same_mapping_as_the_pins()
    {
        // The cone is derived from the position mapping rather than from a rule of its own, so
        // the two cannot drift apart. With these bounds both spans are positive, so world east
        // runs right across the image and world north runs down it.
        var direct = new CoordinateTransform(Square());
        Assert.Equal(180, direct.ToImageHeading(0), 3);   // north -> down
        Assert.Equal(90, direct.ToImageHeading(90), 3);   // east  -> right

        // Flipping the vertical axis flips which way north points, and the heading follows
        // without anything else being told about it.
        var flipped = new CoordinateTransform(Square(new AxisMapping(false, 1, -1)));
        Assert.Equal(0, flipped.ToImageHeading(0), 3);
    }

    [Fact]
    public void Real_customs_headings_match_what_was_measured_in_game()
    {
        // Measured, not assumed: walking between two screenshot positions on Customs produced a
        // world bearing of 250.1 degrees and an on-image bearing of 70.3 — the image is a half
        // turn from world north. Customs' bounds run backwards on the horizontal axis, which is
        // what produces that without any rotation being applied.
        var customs = new MapImage
        {
            SourceUrl = "https://example.invalid/Customs.svg",
            CoordinateRotation = 180,
            Bounds = [[698, -307], [-372, 237]],
            PixelWidth = 1062,
            PixelHeight = 535,
            Mapping = AxisMapping.Direct,
        };

        var image = new CoordinateTransform(customs).ToImageHeading(250.1);

        Assert.Equal(70.3, image, 0);
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
