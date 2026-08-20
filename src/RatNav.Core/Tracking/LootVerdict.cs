namespace RatNav.Core.Tracking;

/// <summary>How strongly a line argues for keeping something.</summary>
public enum VerdictWeight
{
    /// <summary>Found-in-raid, and you cannot buy your way out of it later.</summary>
    Critical,

    /// <summary>Something you are working on wants it.</summary>
    Wanted,

    /// <summary>Worth knowing, not worth deciding on.</summary>
    Background,

    /// <summary>Nothing you are working on wants it.</summary>
    Ignore,
}

/// <summary>One line of the answer.</summary>
public sealed record VerdictLine(string Text, VerdictWeight Weight);

/// <summary>What you are looking at, and whether to pick it up.</summary>
public sealed record ItemVerdict
{
    /// <summary>The one line to read if you read nothing else.</summary>
    public required string Headline { get; init; }

    public required VerdictWeight Weight { get; init; }

    /// <summary>The reasons, strongest first. Short enough to read standing over a pile of loot.</summary>
    public required IReadOnlyList<VerdictLine> Lines { get; init; }
}

/// <summary>
/// The answer to "do I pick this up?", in the order the question is actually asked.
///
/// <para>The card this replaces listed every quest that has ever wanted the item, every hideout
/// level, and every barter it appears in. All of that is true and almost none of it is the
/// question — a card you read while standing over loot with footsteps nearby has to answer in one
/// glance, and it was answering by listing.</para>
///
/// <para>So: is anything you are working on asking for this, is it on your watchlist, and what is
/// left over gets one line saying how much was left over rather than reciting it.</para>
/// </summary>
public static class LootVerdict
{
    /// <param name="questNeed">How many active quests want, and the nearest one's name.</param>
    /// <param name="hideoutNeed">How many upgrades in view want, and the nearest one's name.</param>
    /// <param name="tradeNeed">How many the barters and crafts you picked want, and for what.</param>
    /// <param name="watch">Your own target and what you hold against it, when you set one.</param>
    /// <param name="foundInRaid">Whether any of the above insists on found-in-raid.</param>
    /// <param name="otherQuests">Quests that want it but which you have not started.</param>
    /// <param name="otherBarters">Barters that take it but which you are not working towards.</param>
    public static ItemVerdict For(
        (int Count, string? For) questNeed,
        (int Count, string? For) hideoutNeed,
        (int Count, string? For) tradeNeed,
        (int Target, int Have)? watch,
        bool foundInRaid,
        int otherQuests,
        int otherBarters)
    {
        var lines = new List<VerdictLine>();

        if (questNeed.Count > 0)
        {
            lines.Add(new VerdictLine(
                Line("QUEST", questNeed, foundInRaid ? " · found in raid" : ""),
                foundInRaid ? VerdictWeight.Critical : VerdictWeight.Wanted));
        }

        if (hideoutNeed.Count > 0)
        {
            lines.Add(new VerdictLine(
                Line("HIDEOUT", hideoutNeed, ""), VerdictWeight.Wanted));
        }

        if (tradeNeed.Count > 0)
        {
            lines.Add(new VerdictLine(
                Line("TRADE", tradeNeed, ""), VerdictWeight.Wanted));
        }

        if (watch is { } w)
        {
            // Your own number, said as progress rather than as a target: what you want to know
            // holding one is whether you still need it.
            var left = Math.Max(0, w.Target - w.Have);

            lines.Add(new VerdictLine(
                left > 0
                    ? $"WATCHLIST  {left} more ({w.Have} of {w.Target})"
                    : $"WATCHLIST  you have all {w.Target}",
                left > 0 ? VerdictWeight.Wanted : VerdictWeight.Background));
        }

        // Everything else, counted rather than listed. "Four other quests want this eventually" is
        // worth a line; naming all four is worth a screen you have no time to read.
        var other = new List<string>();

        if (otherQuests > 0)
            other.Add($"{otherQuests} quest{(otherQuests == 1 ? "" : "s")} you have not started");

        if (otherBarters > 0)
            other.Add($"{otherBarters} barter{(otherBarters == 1 ? "" : "s")}");

        if (other.Count > 0)
            lines.Add(new VerdictLine($"ALSO  {string.Join(", ", other)}", VerdictWeight.Background));

        var wanted = lines.FirstOrDefault(l => l.Weight is VerdictWeight.Critical or VerdictWeight.Wanted);

        if (wanted is null)
        {
            // Said plainly. "Nothing wants this" is a complete answer and the most common one, and
            // a card that goes blank instead makes you wonder whether it read the item at all.
            lines.Insert(0, new VerdictLine(
                "Nothing you are working on wants this.", VerdictWeight.Ignore));

            return new ItemVerdict
            {
                Headline = other.Count > 0 ? "Not now" : "Leave it",
                Weight = VerdictWeight.Ignore,
                Lines = lines,
            };
        }

        return new ItemVerdict
        {
            Headline = wanted.Weight == VerdictWeight.Critical ? "Keep — found in raid" : "Keep",
            Weight = wanted.Weight,
            Lines = lines,
        };
    }

    private static string Line(string label, (int Count, string? For) need, string suffix) =>
        need.For is { Length: > 0 } what
            ? $"{label}  {need.Count} for {what}{suffix}"
            : $"{label}  {need.Count}{suffix}";
}
