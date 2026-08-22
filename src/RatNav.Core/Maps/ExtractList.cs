namespace RatNav.Core.Maps;

using System.Text.RegularExpressions;

/// <summary>How a row in the game's list reads.</summary>
public enum ExtractRowKind
{
    /// <summary>Listed with no time against it: open to you.</summary>
    Open,

    /// <summary>
    /// Listed with <c>??:??:??</c>: the game has not settled its availability.
    ///
    /// <para>Still worth showing. That row means a condition exists — a car to pay for, a switch,
    /// a partner, an item you might be carrying or could still find — and a condition you could
    /// meet is a way out you might take. What is genuinely unavailable is left off the list.</para>
    /// </summary>
    Conditional,

    /// <summary>
    /// A transit to another map, not a way out of the raid.
    ///
    /// <para>The game lists these alongside the extracts and colors them differently. Offering
    /// one as an extract would send somebody to the wrong side of the map to go and play somewhere
    /// else rather than to leave with their loot.</para>
    /// </summary>
    Transit,
}

/// <summary>One row read off the game's extraction list.</summary>
public sealed record ExtractRow(string Name, ExtractRowKind Kind);

/// <summary>
/// Reads the game's own extraction list — the panel that opens on <c>O</c>.
///
/// <para>The rows have a shape, and reading them as loose text throws it away. Each is an
/// <c>EXFIL01</c>-style id, then the extract's name, then sometimes a time. That structure carries
/// three separate facts: which extracts are in play at all, which of them are conditional, and
/// which rows are not extracts.</para>
///
/// <para><b>What matters most is what the list leaves out.</b> A map has a dozen or more extracts
/// and a given raid offers a handful; the ones that never appear are not available to you, and
/// nothing else RatNav can see says so.</para>
/// </summary>
public static class ExtractList
{
    /// <summary>
    /// The row id the game prints before each name. Matched loosely because this comes from OCR:
    /// zero and O trade places, and one and I do.
    /// </summary>
    private static readonly Regex RowId =
        new(@"^\s*(?<kind>EXFIL|TRANSIT|EXF[I1L]L|TRANS[I1]T)\s*[O0-9IL]{1,3}\s*[:.\-]?\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A time against a row: either a real countdown or the game's "not settled" mark.</summary>
    private static readonly Regex Trailing =
        new(@"\s*(\?{1,2}\s*[:.]\s*\?{1,2}\s*[:.]\s*\?{1,2}|\d{1,2}\s*[:.]\s*\d{2}\s*([:.]\s*\d{2})?)\s*$",
            RegexOptions.Compiled);

    /// <summary>
    /// The rows in a screen reading, in the order they appeared.
    ///
    /// <para>Lines that are not rows — the panel's heading, the raid timer, whatever else the
    /// capture caught — return nothing, because they carry no id.</para>
    /// </summary>
    public static IReadOnlyList<ExtractRow> Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<ExtractRow>();

        foreach (var raw in lines)
        {
            if (raw is not { Length: > 0 }) continue;

            var line = raw.Trim();
            var id = RowId.Match(line);

            // No id, no row. The heading and the raid clock both sit in this panel and neither
            // names a way out.
            if (!id.Success) continue;

            var transit = id.Groups["kind"].Value.StartsWith("TRANS", StringComparison.OrdinalIgnoreCase);
            var rest = line[id.Length..].Trim();

            var timed = Trailing.Match(rest);
            var conditional = timed.Success && timed.Value.Contains('?');

            var name = (timed.Success ? rest[..timed.Index] : rest).Trim();

            if (name.Length == 0) continue;

            rows.Add(new ExtractRow(
                name,
                transit ? ExtractRowKind.Transit
                    : conditional ? ExtractRowKind.Conditional
                    : ExtractRowKind.Open));
        }

        return rows;
    }
}
