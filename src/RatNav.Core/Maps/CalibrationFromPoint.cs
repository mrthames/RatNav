namespace RatNav.Core.Maps;

using RatNav.Core.Model;

/// <summary>What one marked position says about how a map is laid out.</summary>
public sealed record PointCalibration
{
    public required AxisMapping Mapping { get; init; }

    /// <summary>How far the winning mapping put the position from where it was marked, 0 to 1.</summary>
    public required double Miss { get; init; }

    /// <summary>How far the next-best mapping missed by. Far larger is what makes this decisive.</summary>
    public required double RunnerUpMiss { get; init; }

    /// <summary>
    /// Whether this settles it.
    ///
    /// <para>A wrong mapping mirrors the map, so it misses by a large fraction of it — nothing
    /// like a slightly imprecise click. Both halves are needed: the ratio catches a click that
    /// landed nowhere near anything, and the absolute floor catches a position so near the center
    /// that mirroring barely moves it, where every answer is close and none of them is confirmed.</para>
    /// </summary>
    public bool Decisive => Miss <= 0.12 && RunnerUpMiss >= 0.25 && RunnerUpMiss >= Miss * 3;
}

/// <summary>
/// Settles a map's layout from one position somebody marked.
///
/// <para>Calibration needs exactly one thing no data can supply. Aspect ratio decides which world
/// axis runs across the image, and published extract positions usually decide the signs — but
/// where extracts sit comfortably inside the border, flipping an axis mirrors the whole layout
/// without pushing anything off the edge, and nothing in the data distinguishes that from the
/// truth. Four maps are stuck there. A person saying "I was standing here" is not stuck at all.</para>
///
/// <para>The margin is enormous, which is what makes this safe to do by clicking. A wrong mapping
/// is a mirror image, so it misses by something like half the map; a hurried click misses by a few
/// percent. There is no realistic way to click badly enough to pick the wrong answer, and if
/// somehow the answers come out close together it says so rather than choosing.</para>
/// </summary>
public static class CalibrationFromPoint
{
    /// <param name="world">Where the player actually was, from a screenshot filename.</param>
    /// <param name="marked">Where they say that is on the image, 0 to 1 across and down.</param>
    public static PointCalibration Solve(
        MapImage image,
        GamePosition world,
        MapPoint marked)
    {
        var scored = Candidates(image.Mapping.Swapped)
            .Select(mapping => (Mapping: mapping, Miss: Miss(image, mapping, world, marked)))
            .OrderBy(s => s.Miss)
            .ToArray();

        return new PointCalibration
        {
            Mapping = scored[0].Mapping,
            Miss = scored[0].Miss,
            RunnerUpMiss = scored[1].Miss,
        };
    }

    /// <summary>
    /// The arrangements one marked position can choose between: the signs, with the orientation
    /// held where it is.
    ///
    /// <para>Orientation is not up for a vote. Every drawing states its own rotation, and on a
    /// near-square map — which is exactly the shape that leaves the signs ambiguous — swapping the
    /// axes moves a point barely further than a hurried click does. Asking one click to settle
    /// both would be asking it to settle the half it cannot see.</para>
    /// </summary>
    public static IEnumerable<AxisMapping> Candidates(bool swapped)
    {
        foreach (var signA in new[] { 1, -1 })
        foreach (var signB in new[] { 1, -1 })
        {
            yield return new AxisMapping(swapped, signA, signB);
        }
    }

    private static double Miss(MapImage image, AxisMapping mapping, GamePosition world, MapPoint marked)
    {
        var transform = new CoordinateTransform(image with { Mapping = mapping });
        var landed = transform.ToNormalized(world);

        var dx = landed.X - marked.X;
        var dy = landed.Y - marked.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
