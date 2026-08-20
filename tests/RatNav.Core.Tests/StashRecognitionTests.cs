namespace RatNav.Core.Tests;

using RatNav.Core.Stash;

/// <summary>Reducing a picture to the little that survives being drawn twice at two sizes.</summary>
public class IconSignatureTests
{
    /// <summary>A flat block of one colour, at whatever size.</summary>
    private static IconSignature Flat(double r, double g, double b, int size = 64)
    {
        var pixels = new double[size * size * 3];

        for (var i = 0; i < size * size; i++)
        {
            pixels[i * 3] = r;
            pixels[i * 3 + 1] = g;
            pixels[i * 3 + 2] = b;
        }

        return IconSignature.From(pixels, size, size)!;
    }

    /// <summary>Left half one colour, right half another — something with structure to compare.</summary>
    private static IconSignature Halves(double left, double right, int size = 64)
    {
        var pixels = new double[size * size * 3];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = x < size / 2 ? left : right;
                var at = (y * size + x) * 3;

                pixels[at] = value;
                pixels[at + 1] = value;
                pixels[at + 2] = value;
            }
        }

        return IconSignature.From(pixels, size, size)!;
    }

    [Fact]
    public void The_same_picture_is_no_distance_from_itself()
    {
        var signature = Halves(0.2, 0.8);

        Assert.Equal(0, signature.DistanceTo(signature), 6);
    }

    /// <summary>
    /// The whole point. A screenshot cell and a catalogue icon are never the same size, and a
    /// signature that changed with size would compare nothing to nothing.
    /// </summary>
    [Fact]
    public void The_same_picture_at_two_sizes_matches()
    {
        var small = Halves(0.2, 0.8, size: 32);
        var large = Halves(0.2, 0.8, size: 96);

        Assert.True(small.DistanceTo(large) < 0.01);
    }

    [Fact]
    public void Different_pictures_are_far_apart()
    {
        Assert.True(Halves(0.1, 0.9).DistanceTo(Halves(0.9, 0.1)) > 0.3);
    }

    [Fact]
    public void Colour_is_part_of_it()
    {
        Assert.True(Flat(0.8, 0.1, 0.1).DistanceTo(Flat(0.1, 0.1, 0.8)) > 0.2);
    }

    /// <summary>A stack count written across one corner should cost a little, not everything.</summary>
    [Fact]
    public void A_number_printed_over_a_corner_does_not_break_the_match()
    {
        const int size = 64;

        var clean = new double[size * size * 3];
        Array.Fill(clean, 0.4);

        var stamped = (double[])clean.Clone();

        // A bright block over the bottom-right corner, the way the game prints a stack count.
        for (var y = size - 14; y < size; y++)
            for (var x = size - 18; x < size; x++)
                for (var c = 0; c < 3; c++)
                    stamped[(y * size + x) * 3 + c] = 1.0;

        var before = IconSignature.From(clean, size, size)!;
        var after = IconSignature.From(stamped, size, size)!;

        Assert.True(after.DistanceTo(before) < IconMatcher.TooFar);
    }

    [Fact]
    public void A_picture_smaller_than_the_signature_is_not_one()
    {
        Assert.Null(IconSignature.From(new double[4 * 4 * 3], 4, 4));
    }

    [Fact]
    public void The_nearest_candidates_come_back_in_order()
    {
        var cell = Halves(0.2, 0.8);

        var ranked = IconMatcher.Rank(cell,
        [
            ("far", "Far", Halves(0.9, 0.1)),
            ("exact", "Exact", Halves(0.2, 0.8)),
            ("near", "Near", Halves(0.22, 0.78)),
        ]);

        Assert.Equal("exact", ranked[0].ItemId);
        Assert.Equal("near", ranked[1].ItemId);
    }

    /// <summary>
    /// Better to say "I do not know what this is" and let somebody pick than to write a number
    /// against the wrong item.
    /// </summary>
    [Fact]
    public void Nothing_close_enough_comes_back_as_nothing()
    {
        Assert.Empty(IconMatcher.Rank(Halves(0.1, 0.9), [("far", "Far", Halves(0.9, 0.1))]));
    }

    [Fact]
    public void Confidence_falls_as_the_match_gets_worse()
    {
        Assert.Equal(1, new IconMatch("a", "A", 0).Confidence, 3);
        Assert.True(new IconMatch("a", "A", 0.02).Confidence > 0.8);
        Assert.True(new IconMatch("a", "A", 0.15).Confidence < 0.5);
    }
}

/// <summary>
/// The row of bandages somebody put in on purpose, which is how a scrolling stash gets shot in
/// pieces without counting the overlap twice.
/// </summary>
public class SeparatorRowTests
{
    private static IconSignature Shade(double value)
    {
        var pixels = new double[16 * 16 * 3];
        Array.Fill(pixels, value);

        return IconSignature.From(pixels, 16, 16)!;
    }

    private static DetectedGrid Grid(int columns, int rows) => new()
    {
        Columns = columns,
        Rows = rows,
        Left = 0,
        Top = 0,
        CellSize = 40,
        Cells = [.. from row in Enumerable.Range(0, rows)
                    from column in Enumerable.Range(0, columns)
                    select new GridCell(column, row, true)],
    };

    [Fact]
    public void A_row_of_one_repeated_item_is_a_divider()
    {
        var grid = Grid(7, 3);

        var cells = new Dictionary<(int, int), IconSignature>();

        for (var column = 0; column < 7; column++)
        {
            cells[(column, 0)] = Shade(0.1 + column * 0.1);   // assorted loot
            cells[(column, 1)] = Shade(0.5);                  // the bandages
            cells[(column, 2)] = Shade(0.2 + column * 0.05);  // more loot
        }

        Assert.Equal([1], SeparatorRows.Find(grid, cells));
    }

    [Fact]
    public void A_row_with_a_gap_in_it_is_not_a_divider()
    {
        var grid = Grid(7, 1);

        var cells = new Dictionary<(int, int), IconSignature>();

        for (var column = 0; column < 6; column++) cells[(column, 0)] = Shade(0.5);

        Assert.Empty(SeparatorRows.Find(grid, cells));
    }

    [Fact]
    public void A_full_row_of_assorted_loot_is_not_a_divider()
    {
        var grid = Grid(7, 1);

        var cells = new Dictionary<(int, int), IconSignature>();

        for (var column = 0; column < 7; column++) cells[(column, 0)] = Shade(0.1 + column * 0.12);

        Assert.Empty(SeparatorRows.Find(grid, cells));
    }

    /// <summary>Bandages you put there on purpose are not loot, and forty of them on a shopping
    /// list would be its own small betrayal.</summary>
    [Fact]
    public void The_divider_row_is_never_counted()
    {
        var grid = Grid(7, 4);

        Assert.Equal([0, 1, 3], SeparatorRows.RowsToCount(grid, [2]));
    }

    /// <summary>
    /// The second screenshot of a scrolling page: shoot it so the divider shows again, and
    /// everything above the divider is what you already counted.
    /// </summary>
    [Fact]
    public void Only_what_is_below_the_divider_counts_on_the_next_screenshot()
    {
        var grid = Grid(7, 6);

        Assert.Equal([3, 4, 5], SeparatorRows.RowsToCount(grid, [2], after: 2));
    }

    [Fact]
    public void With_no_divider_every_row_counts()
    {
        Assert.Equal([0, 1, 2], SeparatorRows.RowsToCount(Grid(7, 3), []));
    }

    /// <summary>A row of two is as likely to be two of a thing you are keeping.</summary>
    [Fact]
    public void A_container_too_narrow_for_a_meaningful_divider_has_none()
    {
        var grid = Grid(2, 1);

        var cells = new Dictionary<(int, int), IconSignature>
        {
            [(0, 0)] = Shade(0.5),
            [(1, 0)] = Shade(0.5),
        };

        Assert.Empty(SeparatorRows.Find(grid, cells));
    }
}
