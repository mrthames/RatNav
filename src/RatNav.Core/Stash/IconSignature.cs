namespace RatNav.Core.Stash;

/// <summary>
/// A picture reduced to the little that survives being drawn at two different sizes, over two
/// different backgrounds, with a stack count written across one corner.
///
/// <para>Eight by eight, in three channels. Small enough that the differences between a screenshot
/// cell and a catalogue icon — compression, scaling, the game's own lighting — average out; large
/// enough to tell a wrench from a battery.</para>
/// </summary>
public sealed record IconSignature
{
    public const int Size = 8;

    /// <summary>Red, green and blue at each of the 64 points, each 0 to 1.</summary>
    public required IReadOnlyList<double> Values { get; init; }

    /// <summary>
    /// How unlike another signature this is, from 0 (identical) upwards.
    ///
    /// <para>Mean absolute difference rather than Euclidean distance: one wildly different point —
    /// a stack count printed over a corner, a highlight where the cursor was — should cost a
    /// little, not be squared into swamping everything else.</para>
    /// </summary>
    public double DistanceTo(IconSignature other)
    {
        if (other.Values.Count != Values.Count) return double.MaxValue;

        var total = 0.0;

        for (var i = 0; i < Values.Count; i++) total += Math.Abs(Values[i] - other.Values[i]);

        return total / Values.Count;
    }

    /// <summary>
    /// Builds a signature from an image, by averaging each of 64 blocks.
    ///
    /// <para>Averaging rather than sampling, because a screenshot cell and a catalogue icon are
    /// never the same size, and sampling would compare different parts of the picture.</para>
    /// </summary>
    /// <param name="rgb">
    /// Row-major pixels, three doubles each from 0 to 1: <c>rgb[(y * width + x) * 3 + channel]</c>.
    /// </param>
    public static IconSignature? From(IReadOnlyList<double> rgb, int width, int height)
    {
        if (width < Size || height < Size || rgb.Count < width * height * 3) return null;

        var values = new double[Size * Size * 3];

        for (var by = 0; by < Size; by++)
        {
            for (var bx = 0; bx < Size; bx++)
            {
                var x0 = bx * width / Size;
                var x1 = Math.Max(x0 + 1, (bx + 1) * width / Size);
                var y0 = by * height / Size;
                var y1 = Math.Max(y0 + 1, (by + 1) * height / Size);

                double r = 0, g = 0, b = 0;
                var count = 0;

                for (var y = y0; y < y1 && y < height; y++)
                {
                    for (var x = x0; x < x1 && x < width; x++)
                    {
                        var at = (y * width + x) * 3;

                        r += rgb[at];
                        g += rgb[at + 1];
                        b += rgb[at + 2];
                        count++;
                    }
                }

                if (count == 0) continue;

                var into = (by * Size + bx) * 3;

                values[into] = r / count;
                values[into + 1] = g / count;
                values[into + 2] = b / count;
            }
        }

        return new IconSignature { Values = values };
    }
}

/// <summary>One candidate for what is in a cell.</summary>
public sealed record IconMatch(string ItemId, string Name, double Distance)
{
    /// <summary>
    /// How sure this is, from 0 to 1, for saying so rather than presenting a guess as fact.
    ///
    /// <para>A perfect match never happens — the game draws its icons over a lit background and
    /// writes a stack count across one corner — so the scale is set from what a genuine match
    /// actually scores rather than from zero.</para>
    /// </summary>
    public double Confidence => Math.Clamp(1 - (Distance / 0.25), 0, 1);
}

/// <summary>
/// Names what is in a cell, by comparing it to the icons of items you already track.
///
/// <para>Against the items you track, not all 5,312. If an item is not wanted by a quest, an
/// upgrade, a goal or your watchlist, RatNav has no reason to count it — and a few hundred
/// candidates is a different problem from five thousand, both in accuracy and in how many icons
/// have to be downloaded before any of this works.</para>
/// </summary>
public static class IconMatcher
{
    /// <summary>
    /// Beyond this, the best candidate is not a match. Better to say "I do not know what this is"
    /// and let somebody pick, than to write a number against the wrong item.
    /// </summary>
    public const double TooFar = 0.16;

    /// <param name="candidates">Item id, display name, and the signature of its catalogue icon.</param>
    public static IReadOnlyList<IconMatch> Rank(
        IconSignature cell,
        IEnumerable<(string ItemId, string Name, IconSignature Signature)> candidates,
        int limit = 3)
    {
        return
        [
            .. candidates
                .Select(c => new IconMatch(c.ItemId, c.Name, cell.DistanceTo(c.Signature)))
                .Where(m => m.Distance <= TooFar)
                .OrderBy(m => m.Distance)
                .Take(limit)
        ];
    }
}
