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
    public static DetectedGrid? Detect(IReadOnlyList<double> brightness, int width, int height)
    {
        if (width <= 0 || height <= 0 || brightness.Count < width * height) return null;

        var columns = LinesAlong(brightness, width, height, vertical: true);
        var rows = LinesAlong(brightness, width, height, vertical: false);

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
    /// <para>A grid line is a column of pixels darker than the columns on either side of it. That
    /// is a low bar on its own, so evenness does the real work afterwards.</para>
    /// </summary>
    private static List<int> LinesAlong(
        IReadOnlyList<double> brightness, int width, int height, bool vertical)
    {
        var across = vertical ? width : height;
        var along = vertical ? height : width;

        var means = new double[across];

        for (var i = 0; i < across; i++)
        {
            var total = 0.0;

            for (var j = 0; j < along; j++)
                total += brightness[vertical ? j * width + i : i * width + j];

            means[i] = total / along;
        }

        var lines = new List<int>();

        for (var i = 1; i < across - 1; i++)
        {
            var darkerThanBoth = means[i] < means[i - 1] - LineContrast
                && means[i] < means[i + 1] - LineContrast;

            // A line two pixels wide would otherwise be two lines, and the spacing check below
            // would then reject a perfectly good grid.
            if (darkerThanBoth && (lines.Count == 0 || i - lines[^1] > 2)) lines.Add(i);
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

                // A quarter of a cell of slack. Grid lines land on whole pixels and the spacing
                // rarely divides evenly, so a strict match would reject most real screenshots.
                var slack = spacing * 0.25;

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
