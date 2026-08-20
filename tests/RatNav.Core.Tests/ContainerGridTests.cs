namespace RatNav.Core.Tests;

using RatNav.Core.Stash;

/// <summary>
/// Finding a container's grid on a screenshot.
///
/// <para>Tested against images built here rather than against real screenshots, on purpose. A test
/// written from one person's screenshot proves the code works on that screenshot; these say what
/// must be true of any of them — evenly spaced lines, square cells, and a cell that varies is a
/// cell with something in it.</para>
/// </summary>
public class ContainerGridTests
{
    private const double Panel = 0.35;
    private const double Line = 0.10;

    /// <summary>Paints a grid: a flat panel with darker lines at every cell boundary.</summary>
    private static (double[] Pixels, int Width, int Height) Grid(
        int columns, int rows, int cell = 40, int left = 12, int top = 9,
        IEnumerable<(int Column, int Row)>? filled = null)
    {
        var width = left + columns * cell + left;
        var height = top + rows * cell + top;

        var pixels = new double[width * height];
        Array.Fill(pixels, Panel);

        for (var column = 0; column <= columns; column++)
        {
            var x = left + column * cell;

            for (var y = top; y <= top + rows * cell && y < height; y++)
                if (x < width) pixels[y * width + x] = Line;
        }

        for (var row = 0; row <= rows; row++)
        {
            var y = top + row * cell;

            for (var x = left; x <= left + columns * cell && x < width; x++)
                if (y < height) pixels[y * width + x] = Line;
        }

        // An "icon": a noisy block in the middle of the cell. What it is a picture of does not
        // matter — only that it is not flat, which is the whole test.
        foreach (var (column, row) in filled ?? [])
        {
            var seed = (column * 31) + (row * 17) + 3;

            for (var y = 0; y < cell - 10; y++)
            {
                for (var x = 0; x < cell - 10; x++)
                {
                    var px = left + column * cell + 5 + x;
                    var py = top + row * cell + 5 + y;

                    if (px >= width || py >= height) continue;

                    pixels[py * width + px] = ((seed + x * 7 + y * 13) % 17) / 17.0;
                }
            }
        }

        return (pixels, width, height);
    }

    [Fact]
    public void A_grid_is_found_and_measured()
    {
        var (pixels, width, height) = Grid(7, 7);
        var grid = ContainerGrid.Detect(pixels, width, height);

        Assert.NotNull(grid);
        Assert.Equal(7, grid!.Columns);
        Assert.Equal(7, grid.Rows);
        Assert.Equal(40, grid.CellSize, 1);
        Assert.Equal(49, grid.Cells.Count);
    }

    /// <summary>A scav junk box is 7×7. It is the shape this exists for.</summary>
    [Fact]
    public void The_grid_is_placed_where_it_actually_is()
    {
        var (pixels, width, height) = Grid(7, 7, left: 12, top: 9);
        var grid = ContainerGrid.Detect(pixels, width, height)!;

        Assert.Equal(12, grid.Left);
        Assert.Equal(9, grid.Top);
    }

    [Fact]
    public void An_empty_container_reads_as_empty()
    {
        var (pixels, width, height) = Grid(5, 5);

        Assert.Equal(0, ContainerGrid.Detect(pixels, width, height)!.Occupied);
    }

    [Fact]
    public void A_cell_with_something_in_it_is_marked_occupied()
    {
        var (pixels, width, height) = Grid(5, 5, filled: [(0, 0), (2, 3), (4, 4)]);
        var grid = ContainerGrid.Detect(pixels, width, height)!;

        Assert.Equal(3, grid.Occupied);

        Assert.Contains(grid.Cells, c => c is { Column: 0, Row: 0, Occupied: true });
        Assert.Contains(grid.Cells, c => c is { Column: 2, Row: 3, Occupied: true });
        Assert.Contains(grid.Cells, c => c is { Column: 1, Row: 1, Occupied: false });
    }

    /// <summary>
    /// The case that caught this out. Plenty of icons are close to a single colour at the size a
    /// cell is drawn, and a flat one is as smooth as an empty cell — variance alone reads it as
    /// nothing. It is nothing like the same shade, though, which is the other half of the test.
    /// </summary>
    [Fact]
    public void A_flat_coloured_item_is_still_something()
    {
        var (pixels, width, height) = Grid(5, 5, cell: 40, left: 12, top: 9);

        // A solid block, no texture at all, in the middle of one cell.
        for (var y = 0; y < 28; y++)
            for (var x = 0; x < 28; x++)
                pixels[(9 + 40 + 6 + y) * width + 12 + 80 + 6 + x] = 0.85;

        var grid = ContainerGrid.Detect(pixels, width, height)!;

        Assert.Contains(grid.Cells, c => c is { Column: 2, Row: 1, Occupied: true });
        Assert.Equal(1, grid.Occupied);
    }

    [Fact]
    public void A_cell_can_be_cut_out_of_the_image_by_its_bounds()
    {
        var (pixels, width, height) = Grid(4, 4, cell: 40, left: 12, top: 9);
        var grid = ContainerGrid.Detect(pixels, width, height)!;

        var (x, y, size) = grid.Bounds(new GridCell(2, 1, true));

        Assert.Equal(12 + 80, x);
        Assert.Equal(9 + 40, y);
        Assert.Equal(40, size);
    }

    /// <summary>
    /// A screenshot is a whole screen, not a cropped container. Everything else on it has to fail
    /// to keep step with the grid's spacing, which is what the evenness test is for.
    /// </summary>
    [Fact]
    public void Dark_edges_elsewhere_on_the_screen_do_not_become_grid_lines()
    {
        var (pixels, width, height) = Grid(6, 6);

        // A panel edge and a couple of stray dark columns, none of them evenly spaced.
        foreach (var x in new[] { 3, 5, width - 4 })
            for (var y = 0; y < height; y++)
                pixels[y * width + x] = Line;

        var grid = ContainerGrid.Detect(pixels, width, height);

        Assert.NotNull(grid);
        Assert.Equal(6, grid!.Columns);
        Assert.Equal(6, grid.Rows);
    }

    [Fact]
    public void A_picture_with_no_grid_in_it_finds_nothing()
    {
        var pixels = new double[200 * 200];
        Array.Fill(pixels, Panel);

        Assert.Null(ContainerGrid.Detect(pixels, 200, 200));
    }

    [Fact]
    public void A_grid_too_small_to_be_a_container_is_not_one()
    {
        var (pixels, width, height) = Grid(1, 1);

        Assert.Null(ContainerGrid.Detect(pixels, width, height));
    }

    [Fact]
    public void An_empty_image_is_not_an_error()
    {
        Assert.Null(ContainerGrid.Detect([], 0, 0));
        Assert.Null(ContainerGrid.Detect(new double[10], 100, 100));
    }

    /// <summary>Cells are square in Tarkov. Two spacings that disagree mean one of the runs caught
    /// something that is not a grid, and averaging them would hide that rather than fix it.</summary>
    [Fact]
    public void A_grid_whose_cells_are_not_square_is_rejected()
    {
        var width = 400;
        var height = 400;
        var pixels = new double[width * height];
        Array.Fill(pixels, Panel);

        for (var column = 0; column <= 6; column++)
            for (var y = 10; y < 300; y++)
                pixels[y * width + 10 + column * 40] = Line;

        for (var row = 0; row <= 6; row++)
            for (var x = 10; x < 300; x++)
                pixels[(10 + row * 25) * width + x] = Line;

        Assert.Null(ContainerGrid.Detect(pixels, width, height));
    }
}
