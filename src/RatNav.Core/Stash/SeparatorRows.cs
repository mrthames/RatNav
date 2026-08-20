namespace RatNav.Core.Stash;

/// <summary>
/// Finds the rows somebody filled with one cheap item to mark a boundary.
///
/// <para>A scav junk box is a fixed grid and needs none of this. A stash is a scrolling page, which
/// cannot be shot in one go, and two screenshots of a scrolling page have no reliable way to say
/// where one ends and the other begins — scroll position is not written anywhere, and matching on
/// content fails precisely when content repeats, which in a stash is constantly.</para>
///
/// <para>So you make a landmark: fill one whole row with a single cheap identical item. A row where
/// every cell holds the same thing is not loot, it is a divider. It is never counted, and two
/// screenshots that both show it are known to overlap exactly there.</para>
/// </summary>
public static class SeparatorRows
{
    /// <summary>
    /// How alike two cells have to be to count as the same item.
    ///
    /// <para>Tighter than the threshold for naming an item against a catalogue icon, and it can
    /// afford to be: these are two cells from the same screenshot, drawn by the same renderer at
    /// the same size. What separates them is a stack count and a pixel of lighting.</para>
    /// </summary>
    public const double SameItem = 0.05;

    /// <summary>
    /// The rows that are dividers rather than loot.
    ///
    /// <para>A row qualifies when every cell in it holds something, and all of them look alike. A
    /// row of genuine loot that happens to be full will not be uniform; a row of bandages is.</para>
    /// </summary>
    public static IReadOnlyList<int> Find(
        DetectedGrid grid, IReadOnlyDictionary<(int Column, int Row), IconSignature> cells)
    {
        var separators = new List<int>();

        for (var row = 0; row < grid.Rows; row++)
        {
            // A grid one or two cells wide cannot have a meaningful separator: a row of two is as
            // likely to be two of the same thing you are actually keeping.
            if (grid.Columns < 3) break;

            var signatures = new List<IconSignature>();
            var full = true;

            for (var column = 0; column < grid.Columns; column++)
            {
                if (!cells.TryGetValue((column, row), out var signature)) { full = false; break; }

                signatures.Add(signature);
            }

            if (!full || signatures.Count < grid.Columns) continue;

            var uniform = signatures.Skip(1).All(s => signatures[0].DistanceTo(s) <= SameItem);

            if (uniform) separators.Add(row);
        }

        return separators;
    }

    /// <summary>
    /// The rows worth counting, given where the dividers are.
    ///
    /// <para>The divider itself is never counted — it is bandages you put there on purpose, and
    /// adding forty of them to a shopping list would be its own small betrayal.</para>
    ///
    /// <para>When <paramref name="after"/> is set, only what comes below that divider counts. That
    /// is how a second screenshot of the same scrolling page is read: shoot it so the divider is
    /// visible again, and everything above it is the part you already counted.</para>
    /// </summary>
    public static IReadOnlyList<int> RowsToCount(
        DetectedGrid grid, IReadOnlyList<int> separators, int? after = null)
    {
        var floor = after is { } divider ? divider : -1;

        return
        [
            .. Enumerable.Range(0, grid.Rows)
                .Where(row => row > floor)
                .Where(row => !separators.Contains(row))
        ];
    }
}
