namespace RatNav.Core.Stash;

/// <summary>A single cell of a container, and whether anything is in it.</summary>
public readonly record struct GridCell(int Column, int Row, bool Occupied);

/// <summary>Where a container's cells are on a screenshot, and which of them hold something.</summary>
public sealed record DetectedGrid
{
    public required int Columns { get; init; }
    public required int Rows { get; init; }

    /// <summary>The grid's outer edge on the image, in pixels.</summary>
    public required int Left { get; init; }
    public required int Top { get; init; }

    /// <summary>How far apart the cell lines are, in pixels. Cells are square in Tarkov.</summary>
    public required double CellSize { get; init; }

    public required IReadOnlyList<GridCell> Cells { get; init; }

    public int Occupied => Cells.Count(c => c.Occupied);

    /// <summary>Where one cell sits on the image, for cutting it out.</summary>
    public (int X, int Y, int Size) Bounds(GridCell cell) => (
        Left + (int)Math.Round(cell.Column * CellSize),
        Top + (int)Math.Round(cell.Row * CellSize),
        (int)Math.Round(CellSize));
}

/// <summary>
/// Finds a container's grid on a screenshot.
///
/// <para>This is the piece everything else stands on, and the reason the whole feature is
/// tractable at all: a scav junk box is a fixed grid of square cells with the same lines drawn
/// between them every time. There is no scrolling, no layout to infer, and no question about where
/// the container begins. Somebody without a box gets the same property by grouping items into one
/// block of their inventory and shooting that.</para>
///
/// <para>The detection is deliberately simple, because the input is: find the columns and rows
/// where a grid line runs, check they are evenly spaced, and take the largest such block. Anything
/// cleverer would be fitting to one person's screenshot.</para>
///
/// <para>It works on a brightness map rather than pixels so the caller owns every decision about
/// image formats, and so this can be tested without a single real screenshot.</para>
/// </summary>
public static class ContainerGrid
{
    /// <summary>Smallest grid worth calling a container. Below this it is noise in a menu.</summary>
    private const int SmallestGrid = 2;

    /// <summary>
    /// How much darker than its surroundings a line has to be. Tarkov draws cell borders as thin
    /// dark lines over a lighter panel, and this is what separates one from a shadow in an icon.
    /// </summary>
    private const double LineContrast = 0.12;

    /// <summary>
    /// How much a cell has to vary before it counts as holding something. An empty cell is a flat
    /// panel colour; a picture of a thing is not.
    /// </summary>
    private const double OccupiedVariance = 0.004;

    /// <summary>
    /// How far a cell's average can sit from the empty background before it counts as holding
    /// something, whatever its variance.
    ///
    /// <para>Variance alone misses a flat one. Plenty of icons are close to a single colour at the
    /// size a cell is drawn, and one of those is as smooth as an empty cell and nothing like the
    /// same shade — which is exactly the case that made a synthetic test box read as empty.</para>
    /// </summary>
    private const double OccupiedShade = 0.06;

    /// <param name="brightness">
    /// The image as brightness from 0 to 1, row-major: <c>brightness[y * width + x]</c>.
    /// </param>
    /// <summary>
    /// Every container on the screen, largest first.
    ///
    /// <para>An inventory screen is four containers at once — a backpack, a rig, pockets and a
    /// secure container — and reading only the biggest would miss three of them.</para>
    ///
    /// <para>Worn equipment is not among them, and that falls out rather than being ruled out: a
    /// weapon slot is one wide box and armour is one box, and neither is a run of evenly spaced
    /// cells. What this finds is what you are carrying, which is exactly what should be counted.</para>
    /// </summary>
    public static IReadOnlyList<DetectedGrid> DetectAll(
        IReadOnlyList<double> brightness, int width, int height, int most = 6)
    {
        if (width <= 0 || height <= 0 || brightness.Count < width * height) return [];

        var found = new List<DetectedGrid>();

        // A working copy, because each container is painted out once it has been read.
        //
        // Painting it out rather than filtering the line positions: two containers side by side
        // cover the same rows as each other, so throwing away rows inside the first one's bounds
        // would take the second one's rows with them.
        var working = brightness.ToArray();

        for (var attempt = 0; attempt < most; attempt++)
        {
            var grid = Detect(working, width, height, []);
            if (grid is null) break;

            found.Add(grid);

            var right = grid.Left + (int)Math.Round(grid.Columns * grid.CellSize);
            var bottom = grid.Top + (int)Math.Round(grid.Rows * grid.CellSize);

            var panel = Background(working, width, height, grid.Left, grid.Top);

            for (var y = Math.Max(0, grid.Top - 1); y <= Math.Min(height - 1, bottom + 1); y++)
                for (var x = Math.Max(0, grid.Left - 1); x <= Math.Min(width - 1, right + 1); x++)
                    working[y * width + x] = panel;
        }

        return found;
    }

    public static DetectedGrid? Detect(IReadOnlyList<double> brightness, int width, int height) =>
        Detect(brightness, width, height, []);

    private static DetectedGrid? Detect(
        IReadOnlyList<double> brightness,
        int width,
        int height,
        IReadOnlyList<(int Left, int Top, int Right, int Bottom)> claimed)
    {
        if (width <= 0 || height <= 0 || brightness.Count < width * height) return null;

        var columns = LinesAlong(brightness, width, height, vertical: true);
        var rows = LinesAlong(brightness, width, height, vertical: false);

        // Lines belonging to a container already found are taken out of the running, so the next
        // pass finds the next container rather than the same one again.
        columns = [.. columns.Where(x => !claimed.Any(c => x >= c.Left - 2 && x <= c.Right + 2))];
        rows = [.. rows.Where(y => !claimed.Any(c => y >= c.Top - 2 && y <= c.Bottom + 2))];

        if (LargestEvenRun(columns) is not { } columnRun) return null;
        if (LargestEvenRun(rows) is not { } rowRun) return null;

        // Cells are square. Where the two spacings disagree, one of the runs caught something that
        // is not a grid, and averaging would hide that rather than fix it.
        if (Math.Abs(columnRun.Spacing - rowRun.Spacing) > columnRun.Spacing * 0.15) return null;

        var size = (columnRun.Spacing + rowRun.Spacing) / 2;

        // What an empty cell looks like, taken from the panel just outside the grid. Assuming the
        // majority of cells are empty would be wrong exactly when it matters — a full box.
        var background = Background(brightness, width, height, columnRun.Start, rowRun.Start);

        var cells = new List<GridCell>();

        for (var row = 0; row < rowRun.Count; row++)
        {
            for (var column = 0; column < columnRun.Count; column++)
            {
                var x = columnRun.Start + (int)Math.Round(column * size);
                var y = rowRun.Start + (int)Math.Round(row * size);

                cells.Add(new GridCell(
                    column,
                    row,
                    HoldsSomething(
                        brightness, width, height, x, y, (int)Math.Round(size), background)));
            }
        }

        return new DetectedGrid
        {
            Columns = columnRun.Count,
            Rows = rowRun.Count,
            Left = columnRun.Start,
            Top = rowRun.Start,
            CellSize = size,
            Cells = cells,
        };
    }

    /// <summary>
    /// The x positions of vertical lines, or the y positions of horizontal ones.
    ///
    /// <para>Counted, not averaged. Averaging a whole column works only when the grid spans the
    /// whole picture — and an inventory screen holds four small containers scattered across it, so
    /// a line running a fifth of the height is diluted into nothing by the panel above and below
    /// it. Counting how many pixels in that column are darker than their neighbours does not care
    /// how much of the height the line covers.</para>
    /// </summary>
    /// <summary>
    /// The two steps, exposed so a detection that finds nothing can be diagnosed from a test.
    ///
    /// <para>These earned their place: a four-container screen was reading as no containers at
    /// all, and the difference between "no lines were found" and "the lines were found and then
    /// chained into one impossible grid" is invisible from the outside. It was the second.</para>
    /// </summary>
    internal static List<int> LinesForDiagnosis(
        IReadOnlyList<double> brightness, int width, int height, bool vertical) =>
        LinesAlong(brightness, width, height, vertical);

    /// <inheritdoc cref="LinesForDiagnosis"/>
    internal static (int Start, int Count, double Spacing)? RunForDiagnosis(List<int> lines) =>
        LargestEvenRun(lines);

    private static List<int> LinesAlong(
        IReadOnlyList<double> brightness, int width, int height, bool vertical)
    {
        var across = vertical ? width : height;
        var along = vertical ? height : width;

        var counts = new int[across];

        for (var i = 1; i < across - 1; i++)
        {
            var count = 0;

            for (var j = 0; j < along; j++)
            {
                var here = brightness[vertical ? j * width + i : i * width + j];
                var before = brightness[vertical ? j * width + i - 1 : (i - 1) * width + j];
                var after = brightness[vertical ? j * width + i + 1 : (i + 1) * width + j];

                if (here < before - LineContrast && here < after - LineContrast) count++;
            }

            counts[i] = count;
        }

        // A line has to be a line rather than a smudge: a decent share of the longest run on the
        // picture. Proportional rather than absolute, because a container can be forty pixels tall
        // or four hundred.
        var longest = counts.Max();
        if (longest < 8) return [];

        var enough = Math.Max(6, longest / 5);

        var lines = new List<int>();

        for (var i = 1; i < across - 1; i++)
        {
            if (counts[i] < enough) continue;

            // A line two pixels wide would otherwise be two lines, and the spacing check below
            // would then reject a perfectly good grid. The darker of the pair wins.
            if (lines.Count > 0 && i - lines[^1] <= 2)
            {
                if (counts[i] > counts[lines[^1]]) lines[^1] = i;
                continue;
            }

            lines.Add(i);
        }

        return lines;
    }

    /// <summary>
    /// The longest run of evenly spaced lines, which is what a grid is.
    ///
    /// <para>Everything else that looked like a line — a panel edge, the bar down the side of the
    /// interface, a dark strip in an icon — fails to keep step, and falls out here.</para>
    /// </summary>
    private static (int Start, int Count, double Spacing)? LargestEvenRun(List<int> lines)
    {
        if (lines.Count < SmallestGrid + 1) return null;

        (int Start, int Count, double Spacing)? best = null;

        for (var first = 0; first < lines.Count - 1; first++)
        {
            for (var second = first + 1; second < lines.Count; second++)
            {
                var spacing = (double)(lines[second] - lines[first]);
                if (spacing < 8) continue;

                var run = 2;
                var expected = lines[second] + spacing;

                // Slack, but capped in pixels rather than as a share of the spacing.
                //
                // A quarter of a cell sounds reasonable and is not: at a spacing of eighty it is
                // twenty pixels, which is wide enough to step from one container to the next one
                // along and call the pair a single grid. On an inventory screen holding four
                // containers that is exactly what happened — the run chained across all of them
                // and the cells-are-square check then threw the whole detection away.
                //
                // Lines land on whole pixels and drift by rounding, not by much, so a few pixels
                // is all the room a genuine grid ever needs.
                var slack = Math.Min(spacing * 0.12, 4);

                while (lines.Any(l => Math.Abs(l - expected) <= slack))
                {
                    run++;
                    expected += spacing;
                }

                // Lines bound cells, so N+1 lines make N cells.
                var cells = run - 1;

                if (cells >= SmallestGrid && (best is null || cells > best.Value.Count))
                    best = (lines[first], cells, spacing);
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a cell holds something, from how much its middle varies.
    ///
    /// <para>An empty cell is a flat panel colour. An icon is not flat — whatever it is a picture
    /// of. Variance says that without knowing anything about what the picture shows.</para>
    ///
    /// <para>The middle only: the edges are the grid lines themselves, and including them would
    /// make every cell look busy.</para>
    /// </summary>
    /// <summary>
    /// The panel colour just outside the grid, which is what an empty cell is painted in.
    ///
    /// <para>Taken from the margin rather than from the cells themselves: judging the background
    /// from the cells assumes most of them are empty, which is wrong precisely when the container
    /// is full.</para>
    /// </summary>
    private static double Background(
        IReadOnlyList<double> brightness, int width, int height, int left, int top)
    {
        var samples = new List<double>();

        for (var y = Math.Max(0, top - 6); y < Math.Min(height, top - 1); y++)
            for (var x = left; x < Math.Min(width, left + 40); x++)
                samples.Add(brightness[y * width + x]);

        for (var y = top; y < Math.Min(height, top + 40); y++)
            for (var x = Math.Max(0, left - 6); x < Math.Min(width, left - 1); x++)
                samples.Add(brightness[y * width + x]);

        if (samples.Count == 0) return 0;

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static bool HoldsSomething(
        IReadOnlyList<double> brightness, int width, int height,
        int x, int y, int size, double background)
    {
        var inset = Math.Max(2, size / 6);

        var left = x + inset;
        var top = y + inset;
        var right = Math.Min(width, x + size - inset);
        var bottom = Math.Min(height, y + size - inset);

        if (right - left < 2 || bottom - top < 2) return false;

        var values = new List<double>();

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                if (px < 0 || py < 0 || px >= width || py >= height) continue;

                values.Add(brightness[py * width + px]);
            }
        }

        if (values.Count == 0) return false;

        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;

        // Either signal is enough. A textured icon varies; a flat one sits at the wrong shade.
        return variance > OccupiedVariance || Math.Abs(mean - background) > OccupiedShade;
    }
}
