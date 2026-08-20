using System.Globalization;
using System.Text;
using RatNav.Core.Model;

namespace RatNav.Core.Data;

/// <summary>One candidate for what an item is, with how sure we are.</summary>
public sealed record ItemMatch
{
    public required ItemDef Item { get; init; }

    /// <summary>0 to 1. Above ~0.8 is a confident read; below ~0.6 is a guess worth showing but not trusting.</summary>
    public required double Confidence { get; init; }

    /// <summary>The line of text this matched, so a wrong answer is explainable rather than mysterious.</summary>
    public required string MatchedText { get; init; }
}

/// <summary>
/// Turns text read off the screen into an item.
///
/// <para>The input is OCR output, which means it is <b>wrong in specific ways</b>: <c>l</c> for
/// <c>1</c>, <c>rn</c> for <c>m</c>, dropped accents, and stray characters picked up from whatever
/// the tooltip was drawn over. Exact matching fails constantly on that, so this scores edit
/// distance against every item name and short name and reports how sure it is — a confident read
/// and a guess look different to the caller, and the UI can say so.</para>
///
/// <para>Every line of the capture is tried, because a tooltip carries the name alongside a
/// description, weight, and durability, and which line is which is not knowable in advance. The
/// best line wins.</para>
/// </summary>
public static class ItemMatcher
{
    /// <summary>Below this, a match is noise rather than a guess. Tuned so junk text returns nothing.</summary>
    private const double Floor = 0.55;

    /// <summary>
    /// The most likely items for some text read off the screen, best first.
    ///
    /// <para><b>Full names only.</b> The game prints an abbreviation on each inventory cell and the
    /// real name in the tooltip under your cursor, and the capture contains both — along with the
    /// abbreviations of every neighbouring cell. Matching abbreviations too meant a compass in a
    /// backpack read as a golden neck chain, because six cells beside it said "GoldChain" and one
    /// truncated cell said "Compa". The tooltip said "EYE MK.2 professional hand-held compass", and
    /// that is the only thing in the picture that names the item you are pointing at.</para>
    /// </summary>
    /// <param name="lines">Text lines from the capture, in any order.</param>
    /// <param name="items">Everything it could be.</param>
    /// <param name="limit">How many candidates to return.</param>
    public static IReadOnlyList<ItemMatch> Identify(
        IEnumerable<string> lines,
        IReadOnlyList<ItemDef> items,
        int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);

        // Tooltips are noisy. Very short fragments match far too much, and very long ones are
        // descriptions rather than names.
        var candidates = lines
            .Select(Normalize)
            .Where(l => l.Length is >= 3 and <= 64)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0) return [];

        var best = new Dictionary<string, ItemMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var name = Normalize(item.Name);

            foreach (var line in candidates)
            {
                var score = Similarity(line, name);

                if (score < Floor) continue;

                if (!best.TryGetValue(item.Id, out var existing) || score > existing.Confidence)
                {
                    best[item.Id] = new ItemMatch
                    {
                        Item = item,
                        Confidence = score,
                        MatchedText = line,
                    };
                }
            }
        }

        return
        [
            .. best.Values
                .OrderByDescending(m => m.Confidence)
                .ThenBy(m => m.Item.Name.Length)
                .Take(limit)
        ];
    }

    /// <summary>
    /// Case, punctuation, and accents removed. Tarkov item names are full of hyphens, slashes and
    /// parentheses that OCR renders inconsistently, and none of them help identify anything.
    /// </summary>
    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// How alike two strings are, 0 to 1.
    ///
    /// <para>A name contained whole in the line scores near the top: a tooltip line is often the
    /// name plus a size or a count, and edit distance alone would punish that badly.</para>
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        if (b.Length >= 5 && a.Contains(b, StringComparison.Ordinal))
        {
            // Scaled by how much of the line the name accounts for, so a short name inside a long
            // sentence does not outrank the item the sentence is actually about.
            return 0.9 * b.Length / a.Length + 0.05;
        }

        var distance = Distance(a, b);
        return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// Levenshtein distance, two rows at a time. Called a few tens of thousands of times per
    /// identification, so it allocates two small arrays rather than a full matrix.
    /// </summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
