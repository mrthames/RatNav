using System.Globalization;
using System.Text;

namespace RatNav.Core;

/// <summary>
/// Makes text comparable the way a person searching would expect.
///
/// <para><b>Why this is needed.</b> Escape from Tarkov's names are typeset, not typed. They use a
/// typographic apostrophe — <c>What’s on the Flash Drive?</c> — where anyone searching types a
/// straight one, and the two are different characters. Searching for what you can see on screen
/// returned nothing, which reads as the quest being missing rather than the search being
/// literal.</para>
///
/// <para>The same applies to hyphens, accents, and the odd trailing question mark. So punctuation
/// is dropped entirely rather than trying to map every variant to every other: what remains is
/// letters, digits, and single spaces, on both sides of the comparison.</para>
/// </summary>
public static class SearchText
{
    /// <summary>Characters that join a word rather than break it.</summary>
    private static readonly char[] Apostrophes = ['\'', '’', 'ʼ', '`', '´'];

    /// <summary>Lower case, no accents, no punctuation, single spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            // Accents are separate characters after decomposition, so dropping them here turns
            // "Kübel" and "Kubel" into the same word.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
                continue;
            }

            // An apostrophe joins a word rather than separating it, so it disappears without
            // leaving a gap: "What’s" and "whats" have to be the same thing, because the second
            // is what people actually type. Every other mark becomes a space.
            if (Apostrophes.Contains(ch)) continue;

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Whether some text contains what was searched for, ignoring how either is punctuated.
    /// </summary>
    public static bool Contains(string? haystack, string? needle)
    {
        var query = Normalize(needle);

        return query.Length != 0
            && Normalize(haystack).Contains(query, StringComparison.Ordinal);
    }
}
