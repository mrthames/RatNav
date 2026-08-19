using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

public class CalibrationSolverTests
{
    /// <summary>Customs: a wide image and a wide world, so the axis order is not in doubt.</summary>
    private static readonly double[][] CustomsBounds = [[698, -307], [-371, 237]];

    /// <summary>Factory: nearly square in world terms but taller than wide as an image.</summary>
    private static readonly double[][] FactoryBounds = [[-67, 69], [76.6, -65.5]];

    private static GamePosition At(double x, double z) => new(x, 0, z);

    [Fact]
    public void A_map_confirmed_in_game_short_circuits_everything_else()
    {
        // Deliberately hand it nonsense evidence: a verified answer must win anyway.
        var solved = CalibrationSolver.Solve("customs", CustomsBounds, 1, 1, []);

        Assert.Equal(CalibrationConfidence.Verified, solved.Confidence);
        Assert.Equal(CalibrationSolver.VerifiedMappings["customs"], solved.Mapping);
    }

    [Fact]
    public void Factory_keeps_the_swapped_mapping_its_marked_positions_proved()
    {
        var solved = CalibrationSolver.Solve("factory", FactoryBounds, 131, 142, []);

        Assert.Equal(CalibrationConfidence.Verified, solved.Confidence);
        Assert.True(solved.Mapping.Swapped);
        Assert.Equal(-1, solved.Mapping.SignU);
        Assert.Equal(1, solved.Mapping.SignV);
    }

    [Fact]
    public void A_wide_image_over_a_wide_world_gives_a_direct_mapping()
    {
        // Extracts placed near the real corners of Customs, so only one sign choice keeps them
        // all on the image.
        GamePosition[] extracts =
        [
            At(650, -280), At(-340, 200), At(200, -153), At(-100, 100), At(400, 50), At(0, -250),
        ];

        var solved = CalibrationSolver.Solve("unknown-map", CustomsBounds, 1062, 535, extracts);

        Assert.False(solved.Mapping.Swapped);
        Assert.Equal(CalibrationConfidence.Derived, solved.Confidence);
        Assert.Contains("extract", solved.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_tall_image_over_a_square_world_gives_a_swapped_mapping()
    {
        GamePosition[] extracts =
        [
            At(73, -29), At(-63, 60), At(20, -55), At(-10, 40), At(50, 10), At(-40, -20),
        ];

        var solved = CalibrationSolver.Solve("unknown-map", FactoryBounds, 131, 142, extracts);

        Assert.True(solved.Mapping.Swapped);
    }

    [Fact]
    public void A_square_image_cannot_reveal_the_axis_order_and_says_so()
    {
        // Interchange is exactly this case: 977x977 pixels over a 894x891 world. Both
        // arrangements fit equally, so there is nothing to conclude.
        double[][] bounds = [[-447, -445], [447, 446]];
        GamePosition[] extracts = [At(100, 100), At(-100, -100), At(200, -200), At(-200, 200)];

        var solved = CalibrationSolver.Solve("unknown-map", bounds, 977, 977, extracts);

        Assert.Equal(CalibrationConfidence.Weak, solved.Confidence);
        Assert.Contains("square", solved.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extracts_sitting_well_inside_the_map_cannot_rule_out_a_mirror()
    {
        // Flipping an axis mirrors the layout without pushing anything off the edge, so this
        // must report weak rather than picking one and sounding confident.
        GamePosition[] extracts = [At(10, 10), At(-10, -10), At(20, -20), At(-20, 20)];

        var solved = CalibrationSolver.Solve("unknown-map", CustomsBounds, 1062, 535, extracts);

        Assert.Equal(CalibrationConfidence.Weak, solved.Confidence);
        Assert.Contains("mirrored", solved.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Too_few_extracts_is_not_enough_to_conclude_anything()
    {
        var solved = CalibrationSolver.Solve("unknown-map", CustomsBounds, 1062, 535, [At(650, -280)]);

        Assert.Equal(CalibrationConfidence.Weak, solved.Confidence);
    }

    [Fact]
    public void Missing_bounds_are_reported_rather_than_guessed_around()
    {
        var solved = CalibrationSolver.Solve("unknown-map", [[0, 0]], 100, 100, []);

        Assert.Equal(CalibrationConfidence.Unknown, solved.Confidence);
        Assert.Equal(AxisMapping.Direct, solved.Mapping);
    }

    [Theory]
    [InlineData(false, 1, 1, "( x, z)")]
    [InlineData(false, -1, 1, "(-x, z)")]
    [InlineData(true, 1, -1, "( z,-x)")]
    public void Mappings_describe_themselves_readably(bool swapped, int u, int v, string expected)
    {
        // These strings end up in the UI explaining why a map might be wrong, so they matter.
        Assert.Equal(expected.Replace(" ", ""), new AxisMapping(swapped, u, v).ToString().Replace(" ", ""));
    }

    [Fact]
    public void Applying_and_reversing_a_mapping_round_trips()
    {
        foreach (var mapping in new[]
                 {
                     new AxisMapping(false, 1, 1), new AxisMapping(false, -1, 1),
                     new AxisMapping(true, 1, -1), new AxisMapping(true, -1, -1),
                 })
        {
            var (u, v) = mapping.Apply(37.5, -62.25);
            var (x, z) = mapping.Reverse(u, v);

            Assert.Equal(37.5, x, 9);
            Assert.Equal(-62.25, z, 9);
        }
    }
}
