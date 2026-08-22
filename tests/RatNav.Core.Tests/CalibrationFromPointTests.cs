namespace RatNav.Core.Tests;

using RatNav.Core.Maps;
using RatNav.Core.Model;

/// <summary>
/// Settling a map's layout from one position somebody marked, which is the only thing that can
/// settle the four maps where the data genuinely cannot.
/// </summary>
public class CalibrationFromPointTests
{
    /// <summary>Woods' real bounds and image proportions, from tarkov.dev.</summary>
    private static MapImage Woods(AxisMapping? mapping = null) => new()
    {
        SourceUrl = "woods.svg",
        Bounds = [[646, -914], [-761, 442]],
        CoordinateRotation = 180,
        PixelWidth = 1407,
        PixelHeight = 1356,
        Mapping = mapping ?? AxisMapping.Direct,
    };

    /// <summary>Where a position lands under the mapping we believe is right.</summary>
    private static MapPoint Landed(GamePosition world) =>
        new CoordinateTransform(Woods()).ToNormalized(world);

    [Fact]
    public void A_position_marked_where_it_actually_is_confirms_the_mapping()
    {
        var world = new GamePosition(-484, 0, -504);
        var solved = CalibrationFromPoint.Solve(Woods(), world, Landed(world));

        Assert.Equal(AxisMapping.Direct, solved.Mapping);
        Assert.True(solved.Decisive);
    }

    /// <summary>
    /// The whole reason this is safe to do by clicking. A wrong mapping mirrors the map and misses
    /// by a large fraction of it; a hurried click misses by a few percent, and cannot flip the
    /// answer.
    /// </summary>
    [Fact]
    public void A_click_a_few_percent_off_still_picks_the_right_mapping()
    {
        var world = new GamePosition(-484, 0, -504);
        var truth = Landed(world);
        var slipped = new MapPoint(truth.X + 0.04, truth.Y - 0.03);

        var solved = CalibrationFromPoint.Solve(Woods(), world, slipped);

        Assert.Equal(AxisMapping.Direct, solved.Mapping);
        Assert.True(solved.Decisive);
        Assert.True(solved.RunnerUpMiss > solved.Miss * 3);
    }

    [Fact]
    public void A_mirrored_map_is_detected_and_named()
    {
        var world = new GamePosition(-484, 0, -504);

        // What the player would have marked if the map's X ran the other way.
        var mirrored = new AxisMapping(false, -1, 1);
        var marked = new CoordinateTransform(Woods(mirrored)).ToNormalized(world);

        var solved = CalibrationFromPoint.Solve(Woods(), world, marked);

        Assert.Equal(mirrored, solved.Mapping);
        Assert.True(solved.Decisive);
    }

    /// <summary>
    /// Orientation is not up for a vote — the drawing states it. On a near-square map, which is
    /// exactly the shape that leaves the signs ambiguous, swapping the axes moves a point barely
    /// further than a hurried click does, so asking one click to settle both would be asking it to
    /// settle the half it cannot see.
    /// </summary>
    [Fact]
    public void The_orientation_is_held_where_the_drawing_says_it_is()
    {
        var swapped = new AxisMapping(true, 1, 1);
        var world = new GamePosition(200, 0, -300);
        var marked = new CoordinateTransform(Woods(swapped)).ToNormalized(world);

        var solved = CalibrationFromPoint.Solve(Woods(swapped), world, marked);

        Assert.True(solved.Mapping.Swapped);
        Assert.Equal(swapped, solved.Mapping);
    }

    /// <summary>Four sign arrangements, and exactly one of them is right.</summary>
    [Fact]
    public void Every_arrangement_of_the_signs_is_considered()
    {
        Assert.Equal(4, CalibrationFromPoint.Candidates(swapped: false).Distinct().Count());
    }

    /// <summary>
    /// A click nowhere near where the player was settles nothing. Saying so beats recording a
    /// mapping nobody actually confirmed.
    /// </summary>
    [Fact]
    public void A_click_that_matches_nothing_is_not_decisive()
    {
        var solved = CalibrationFromPoint.Solve(
            Woods(), new GamePosition(-484, 0, -504), new MapPoint(0.5, 0.5));

        Assert.False(solved.Decisive);
    }

    /// <summary>
    /// Near the middle of the map every mapping lands in nearly the same place, so nothing is
    /// distinguished. That has to read as "settled nothing" rather than as an answer.
    /// </summary>
    [Fact]
    public void A_position_near_the_centre_cannot_settle_anything()
    {
        var center = new GamePosition((646 - 761) / 2.0, 0, (-914 + 442) / 2.0);

        Assert.False(CalibrationFromPoint.Solve(Woods(), center, Landed(center)).Decisive);
    }
}
