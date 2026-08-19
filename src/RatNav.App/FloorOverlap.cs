using System.Windows;

// WinForms is referenced for the tray icon and brings clashing drawing types with it.
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace RatNav.App;

/// <summary>
/// Works out which parts of one floor actually sit on top of another.
///
/// <para><b>Why this exists.</b> Ghosting a whole floor treats every part of it as a conflict, but
/// floors only conflict where they overlap — a stairwell drawn directly above a corridor is
/// ambiguous, a warehouse off the other end of the map is not. Faded everything looked like a map
/// seen through frosted glass, when most of what was faded had nothing competing with it at
/// all.</para>
///
/// <para><b>How.</b> A coarse occupancy grid over the map's own drawing area. The active floor
/// marks the cells its shapes cover; anything on another floor is then a lookup rather than a
/// comparison against every shape. Bounding boxes rather than true geometry: exact overlap of a
/// few hundred paths against a few hundred more is far more work than this needs to be, and a box
/// that is slightly too eager only means something is faded that could have been solid.</para>
/// </summary>
public sealed class FloorOverlap
{
    /// <summary>
    /// Grid resolution. Fine enough that a single building does not claim a quarter of the map,
    /// coarse enough that the whole thing is a few thousand cells rather than a bitmap.
    /// </summary>
    private const int Cells = 96;

    private readonly bool[,] _occupied = new bool[Cells, Cells];
    private readonly Rect _extent;

    private FloorOverlap(Rect extent) => _extent = extent;

    /// <summary>Marks the area covered by the floor you are standing on.</summary>
    public static FloorOverlap Of(IReadOnlyList<MapShape> floor, Size viewBox)
    {
        var overlap = new FloorOverlap(new Rect(0, 0, Math.Max(1, viewBox.Width), Math.Max(1, viewBox.Height)));

        foreach (var shape in floor)
        {
            // Terrain is the ground everything sits on, so counting it would mark the entire map
            // as occupied and fade every other floor — which is the behaviour being removed.
            if (shape.Role is MapShapeRole.Terrain or MapShapeRole.Decoration) continue;

            overlap.Mark(shape.Geometry.Bounds);
        }

        return overlap;
    }

    /// <summary>True when a shape sits over something on the floor being drawn.</summary>
    public bool Conflicts(MapShape shape)
    {
        var bounds = shape.Geometry.Bounds;
        if (bounds.IsEmpty) return false;

        var (x0, y0, x1, y1) = Range(bounds);

        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                if (_occupied[x, y]) return true;
            }
        }

        return false;
    }

    private void Mark(Rect bounds)
    {
        if (bounds.IsEmpty) return;

        var (x0, y0, x1, y1) = Range(bounds);

        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++) _occupied[x, y] = true;
        }
    }

    private (int X0, int Y0, int X1, int Y1) Range(Rect bounds)
    {
        var cellWidth = _extent.Width / Cells;
        var cellHeight = _extent.Height / Cells;

        return (
            Clamp((int)Math.Floor(bounds.Left / cellWidth)),
            Clamp((int)Math.Floor(bounds.Top / cellHeight)),
            Clamp((int)Math.Floor(bounds.Right / cellWidth)),
            Clamp((int)Math.Floor(bounds.Bottom / cellHeight)));
    }

    private static int Clamp(int index) => Math.Clamp(index, 0, Cells - 1);
}
