namespace RatNav.Core.Maps;

using RatNav.Core.Model;

/// <summary>
/// Works out which of a map's extracts the game just listed on screen.
///
/// <para>The map knows every extract it has. Only some are open on any given run — wrong side,
/// wrong faction, wrong conditions — and the game will tell you which if you ask it, by putting
/// them on screen. Reading that leaves the map showing the ones you can actually use.</para>
///
/// <para>The matching has to be generous. OCR over a game scene drops characters, reads <c>l</c>
/// as <c>1</c>, and splits one name across two lines; and the game abbreviates where the wiki does
/// not. So a line matches an extract when either contains the other once both are reduced to
/// letters and digits, and when a name has several words, on the distinctive ones.</para>
/// </summary>
public static class ExtractMatcher
{
    /// <summary>
    /// Words too common to identify anything. "Gate" alone matches four extracts on Customs, so a
    /// line reading "gate" should match none of them rather than all of them.
    /// </summary>
    private static readonly HashSet<string> TooCommon = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "of", "gate", "exit", "extract", "extraction", "road", "old", "new",
        "north", "south", "east", "west", "checkpoint", "camp", "road to", "co", "op", "coop",
    };

    /// <summary>
    /// The extracts named by the lines read off the screen.
    ///
    /// <para>Returns names as the map spells them, not as the screen did — everything downstream
    /// keys on the map's own name, and half of the point is turning a misread line back into
    /// one.</para>
    /// </summary>
    public static IReadOnlyList<string> Match(
        IEnumerable<string> lines, IEnumerable<MapExtract> extracts)
    {
        var candidates = extracts
            .Where(e => e.Name is { Length: > 0 })
            .Select(e => (e.Name, Key: Reduce(e.Name), Words: Distinctive(e.Name)))
            .Where(e => e.Key.Length > 0)
            .ToList();

        var found = new List<string>();

        foreach (var line in lines)
        {
            var key = Reduce(line);

            // Two characters of OCR noise should not be able to claim an extract.
            if (key.Length < 4) continue;

            // Nor should a line made only of words every extract shares. "Gate" is half the
            // extracts on Customs, so a line reading "Gate" should claim none of them — and
            // without this the containment check below would hand it all of them.
            if (Distinctive(line).Count == 0) continue;

            foreach (var candidate in candidates)
            {
                if (found.Contains(candidate.Name)) continue;
                if (!Matches(key, candidate.Key, candidate.Words)) continue;

                found.Add(candidate.Name);
            }
        }

        return found;
    }

    private static bool Matches(string line, string extract, IReadOnlyList<string> distinctive)
    {
        // The whole name, either way round: the screen line usually carries the name plus a timer
        // or a status word, and sometimes carries less of the name than the map has.
        if (line.Contains(extract, StringComparison.Ordinal)) return true;
        if (line.Length >= 6 && extract.Contains(line, StringComparison.Ordinal)) return true;

        // A short line only counts if the name starts with it. Names lose their tail, not their
        // head — both OCR truncation and the game's own abbreviation cut the end — so "RUAF" is
        // RUAF Roadblock, while "Taxi" is not Primorsky Ave Taxi V-Ex, even though the letters are
        // in there.
        if (extract.StartsWith(line, StringComparison.Ordinal)) return true;

        // Otherwise: most of the distinctive words, including at least one long one.
        //
        // Not all of them, because one misread word would then lose the extract entirely — and
        // this reads a game scene, where one misread word is the normal case. "Klimov Shopping
        // Mall Exfil" coming back as "Klimov Shopping Mall Exit" still has three of four, and
        // three of those four are unmistakable.
        //
        // The long word is what stops the leniency turning into noise: matching on short fragments
        // alone is how every extract on a map ends up claiming the same line.
        if (distinctive.Count == 0) return false;

        var matched = distinctive.Where(word => line.Contains(word, StringComparison.Ordinal)).ToList();

        return matched.Count * 2 >= distinctive.Count
            && matched.Any(word => word.Length >= 5);
    }

    /// <summary>Letters and digits only, lowercased — everything OCR is unreliable about, removed.</summary>
    private static string Reduce(string text) =>
        new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static IReadOnlyList<string> Distinctive(string name) =>
    [
        .. name
            .Split([' ', '-', '(', ')', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !TooCommon.Contains(word))
            .Select(Reduce)
            .Where(word => word.Length >= 4)
    ];
}
