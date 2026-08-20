using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

public class ScreenshotFilenameTests
{
    [Fact]
    public void Parses_the_documented_filename_format()
    {
        const string name = "2024-03-17[14-30]_-193.0, 2.6, -111.7_0.0, 0.7, 0.0, -0.7_12.34 (0).png";

        Assert.True(ScreenshotFilename.TryParse(name, out var fix));

        Assert.Equal(-193.0, fix.Position.X, 3);
        Assert.Equal(2.6, fix.Position.Y, 3);
        Assert.Equal(-111.7, fix.Position.Z, 3);

        Assert.Equal(0.7, fix.Rotation.Y, 3);
        Assert.Equal(-0.7, fix.Rotation.W, 3);

        Assert.Equal(new DateTime(2024, 3, 17, 14, 30, 0), fix.TakenAt.DateTime);
    }

    [Fact]
    public void Parses_a_full_path_not_just_a_name()
    {
        const string path =
            @"C:\Users\someone\Documents\Escape from Tarkov\Screenshots\2024-03-17[14-30]_10.5, 2.0, -20.25_0, 0, 0, 1_1.0 (0).png";

        Assert.True(ScreenshotFilename.TryParse(path, out var fix));
        Assert.Equal(10.5, fix.Position.X, 3);
        Assert.Equal(-20.25, fix.Position.Z, 3);
        Assert.Equal(path, fix.SourceFile);
    }

    [Fact]
    public void Tolerates_a_seconds_component_in_the_timestamp()
    {
        const string name = "2024-03-17[14-30-45]_1, 2, 3_0, 0, 0, 1_1.0 (0).png";

        Assert.True(ScreenshotFilename.TryParse(name, out var fix));
        Assert.Equal(45, fix.TakenAt.Second);
    }

    [Fact]
    public void Survives_extra_segments_appearing_in_the_format()
    {
        // BSG has changed this filename layout before. Parsing structurally rather than by a
        // literal pattern means new segments are ignored instead of fatal.
        const string name = "2024-03-17[14-30]_something_1, 2, 3_0, 0, 0, 1_9.9_extra (0).png";

        Assert.True(ScreenshotFilename.TryParse(name, out var fix));
        Assert.Equal(1, fix.Position.X, 3);
        Assert.Equal(1, fix.Rotation.W, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("screenshot.png")]
    [InlineData("2024-03-17[14-30]_no numbers here.png")]
    public void Returns_false_for_screenshots_without_coordinates(string name)
    {
        // Menu and hideout screenshots carry no position. That is not an error.
        Assert.False(ScreenshotFilename.TryParse(name, out _));
    }

    [Fact]
    public void Identity_rotation_faces_north()
    {
        Assert.Equal(0, ScreenshotFilename.HeadingFrom(new Quaternion(0, 0, 0, 1)), 3);
    }

    [Fact]
    public void Quarter_turn_about_the_vertical_axis_faces_east()
    {
        // 90 degrees about Y, as a quaternion: (0, sin45, 0, cos45).
        var q = new Quaternion(0, Math.Sqrt(0.5), 0, Math.Sqrt(0.5));
        Assert.Equal(90, ScreenshotFilename.HeadingFrom(q), 3);
    }

    [Fact]
    public void Turning_right_in_game_increases_the_heading()
    {
        // Ground truth, from two screenshots taken four seconds apart on Factory: the player stood
        // still and turned 90 degrees to his right. The position moved 0.25 m, so this really is
        // one spot and two facings — which is the only way to tell a correct heading sign from an
        // inverted one. An inverted formula would report roughly -92 here and the overlay's facing
        // cone would point behind the player.
        var before = ScreenshotFilename.HeadingFrom(new Quaternion(-0.00088, 0.99901, -0.03784, -0.02331));
        var after = ScreenshotFilename.HeadingFrom(new Quaternion(-0.01327, 0.67700, -0.01221, -0.73576));

        Assert.Equal(182.67, before, 1);
        Assert.Equal(274.77, after, 1);

        var turned = ScreenshotFilename.Normalize(after - before);
        Assert.InRange(turned, 85, 100);
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(370, 10)]
    [InlineData(360, 0)]
    public void Normalize_wraps_angles_into_a_single_turn(double input, double expected)
    {
        Assert.Equal(expected, ScreenshotFilename.Normalize(input), 3);
    }
}
