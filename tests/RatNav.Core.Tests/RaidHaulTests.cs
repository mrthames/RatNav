namespace RatNav.Core.Tests;

using RatNav.Core.Stash;

/// <summary>
/// What a raid produced, from what you were carrying at each end of it. The only way to count a
/// haul without reading the game's memory.
/// </summary>
public class RaidHaulTests
{
    private static Dictionary<string, int> Carrying(params (string Item, int Count)[] items) =>
        items.ToDictionary(i => i.Item, i => i.Count, StringComparer.Ordinal);

    [Fact]
    public void What_was_not_there_before_is_what_you_found()
    {
        var haul = RaidHaul.Compare(Carrying(("wires", 1)), Carrying(("wires", 4)));

        var line = Assert.Single(haul);

        Assert.Equal("wires", line.ItemId);
        Assert.Equal(3, line.Change);
    }

    [Fact]
    public void Something_carried_in_and_out_untouched_is_not_a_line_at_all()
    {
        Assert.Empty(RaidHaul.Compare(Carrying(("salewa", 2)), Carrying(("salewa", 2))));
    }

    [Fact]
    public void Something_you_took_in_and_left_with_more_of_counts_only_the_difference()
    {
        var line = Assert.Single(RaidHaul.Compare(Carrying(("bolts", 5)), Carrying(("bolts", 9))));

        Assert.Equal(4, line.Change);
        Assert.Equal(5, line.Before);
        Assert.Equal(9, line.After);
    }

    /// <summary>
    /// Dying with three of something is worth knowing about, and a tracker that only ever counted
    /// upwards would drift away from your stash one raid at a time.
    /// </summary>
    [Fact]
    public void Losing_something_is_reported_too()
    {
        var line = Assert.Single(RaidHaul.Compare(Carrying(("salewa", 3)), Carrying()));

        Assert.Equal(-3, line.Change);
    }

    [Fact]
    public void Found_things_come_before_lost_ones()
    {
        var haul = RaidHaul.Compare(
            Carrying(("salewa", 2), ("wires", 1)),
            Carrying(("wires", 6)));

        Assert.Equal("wires", haul[0].ItemId);
        Assert.Equal(5, haul[0].Change);
        Assert.Equal(-2, haul[1].Change);
    }

    [Fact]
    public void The_biggest_find_leads()
    {
        var haul = RaidHaul.Compare(
            Carrying(),
            Carrying(("one", 1), ("many", 9), ("some", 4)));

        Assert.Equal(["many", "some", "one"], haul.Select(l => l.ItemId));
    }

    [Fact]
    public void An_empty_raid_either_way_is_not_an_error()
    {
        Assert.Empty(RaidHaul.Compare(Carrying(), Carrying()));
    }

    [Fact]
    public void An_item_is_named_the_way_somebody_would_read_it()
    {
        var haul = RaidHaul.Compare(
            Carrying(), Carrying(("wires", 2)), id => id == "wires" ? "Bundle of wires" : null);

        Assert.Equal("Bundle of wires", Assert.Single(haul).Name);
    }

    /// <summary>An inventory screen is four containers at once — a backpack, a rig, pockets and a
    /// secure container — and what you are carrying is all of them together.</summary>
    [Fact]
    public void Several_containers_add_up_to_what_you_are_carrying()
    {
        var total = RaidHaul.Total([
            Carrying(("wires", 2), ("bolts", 1)),
            Carrying(("wires", 3)),
            Carrying(("salewa", 1)),
        ]);

        Assert.Equal(5, total["wires"]);
        Assert.Equal(1, total["bolts"]);
        Assert.Equal(1, total["salewa"]);
    }

    [Fact]
    public void Nothing_carried_adds_up_to_nothing()
    {
        Assert.Empty(RaidHaul.Total([]));
    }
}

/// <summary>
/// An inventory screen holds several containers at once. Reading only the biggest would count a
/// backpack and miss the rig, the pockets and the secure container.
/// </summary>
public class MultipleContainerTests
{
    private const double Panel = 0.35;
    private const double Line = 0.10;

    /// <summary>Paints one grid into an existing image.</summary>
    private static void Paint(
        double[] pixels, int width, int columns, int rows, int cell, int left, int top)
    {
        for (var column = 0; column <= columns; column++)
            for (var y = top; y <= top + rows * cell; y++)
                pixels[y * width + left + column * cell] = Line;

        for (var row = 0; row <= rows; row++)
            for (var x = left; x <= left + columns * cell; x++)
                pixels[(top + row * cell) * width + x] = Line;
    }

    [Fact]
    public void Every_container_on_the_screen_is_found()
    {
        const int width = 700;
        const int height = 400;

        var pixels = new double[width * height];
        Array.Fill(pixels, Panel);

        Paint(pixels, width, columns: 6, rows: 5, cell: 40, left: 30, top: 30);   // a backpack
        Paint(pixels, width, columns: 4, rows: 3, cell: 30, left: 330, top: 30);  // a rig
        Paint(pixels, width, columns: 3, rows: 2, cell: 25, left: 500, top: 30);  // a secure box

        var grids = ContainerGrid.DetectAll(pixels, width, height);

        Assert.True(grids.Count >= 3, $"found {grids.Count} containers, expected at least 3");

        // Largest first, which is the order they are worth reading in.
        Assert.Equal(6, grids[0].Columns);
        Assert.Equal(5, grids[0].Rows);
    }

    [Fact]
    public void One_container_comes_back_once_rather_than_repeatedly()
    {
        const int width = 400;
        const int height = 300;

        var pixels = new double[width * height];
        Array.Fill(pixels, Panel);

        Paint(pixels, width, columns: 5, rows: 4, cell: 40, left: 30, top: 30);

        Assert.Single(ContainerGrid.DetectAll(pixels, width, height));
    }

    [Fact]
    public void A_screen_with_no_containers_finds_none()
    {
        var pixels = new double[300 * 300];
        Array.Fill(pixels, Panel);

        Assert.Empty(ContainerGrid.DetectAll(pixels, 300, 300));
    }
}
