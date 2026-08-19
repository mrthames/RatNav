using System.Windows.Media;
using RatNav.Service;

// WinForms is referenced for the tray icon and brings clashing drawing types with it.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace RatNav.App;

/// <summary>One reason an item is worth keeping, ready to draw.</summary>
public sealed record IdentifyReason
{
    public required string Text { get; init; }
    public required Brush Colour { get; init; }
}

/// <summary>
/// Turns an item into the handful of lines worth reading while standing over a pile of loot.
///
/// <para>Ordered by how much it changes what you do. A quest that wants the item found in raid is
/// the strongest reason not to sell it; a hideout module is next, because it names the station and
/// level; barters last, because they are a standing offer rather than something you can finish.
/// Anything nothing wants says so plainly, which is just as useful an answer.</para>
/// </summary>
public static class IdentifyCard
{
    private static readonly Brush Need = Frozen(0xff, 0x5a, 0x3c);
    private static readonly Brush Route = Frozen(0xe0, 0xc9, 0x8a);
    private static readonly Brush Accent = Frozen(0x8e, 0xc8, 0xff);
    private static readonly Brush Muted = Frozen(0x7b, 0x8c, 0x9b);

    public static IReadOnlyList<IdentifyReason> Reasons(ItemDetail detail)
    {
        var rows = new List<IdentifyReason>();

        foreach (var quest in detail.Quests)
        {
            var fir = quest.FoundInRaid ? " (found in raid)" : "";
            var trader = quest.TraderName is { Length: > 0 } who ? $"{who} · " : "";

            rows.Add(new IdentifyReason
            {
                Text = $"QUEST  {trader}{quest.TaskName} ×{quest.Count}{fir}",
                Colour = quest.FoundInRaid ? Need : Route,
            });
        }

        // The specific thing the player wants to know: which station, and which level of it.
        foreach (var hideout in detail.Hideout)
        {
            rows.Add(new IdentifyReason
            {
                Text = $"HIDEOUT  {hideout.StationName} level {hideout.Level} ×{hideout.Count}",
                Colour = Route,
            });
        }

        foreach (var key in detail.AsKey)
        {
            rows.Add(new IdentifyReason
            {
                Text = $"KEY  opens the way for {key.TaskName}",
                Colour = Accent,
            });
        }

        // Barters are capped rather than listed in full: a common item can appear in a dozen, and
        // a card that runs off the screen answers nothing.
        foreach (var barter in detail.Barters.Take(4))
        {
            var gets = barter.OfferedItemName is { Length: > 0 } offered
                ? $" → {offered}{(barter.OfferedCount > 1 ? $" ×{barter.OfferedCount}" : "")}"
                : "";

            rows.Add(new IdentifyReason
            {
                Text = $"BARTER  {barter.TraderName} LL{barter.TraderLevel} ×{barter.Count}{gets}",
                Colour = Accent,
            });
        }

        if (detail.Barters.Count > 4)
        {
            rows.Add(new IdentifyReason
            {
                Text = $"BARTER  and {detail.Barters.Count - 4} more",
                Colour = Muted,
            });
        }

        if (rows.Count == 0)
        {
            // "Nothing wants this" is a real answer and worth saying, not an empty card.
            rows.Add(new IdentifyReason
            {
                Text = detail.Item.Avg24hPrice is { } price and > 0
                    ? $"Nothing needs this. Flea around {price:N0} ₽."
                    : "Nothing needs this.",
                Colour = Muted,
            });
        }
        else if (detail.Have > 0)
        {
            rows.Add(new IdentifyReason
            {
                Text = $"You have {detail.Have}.",
                Colour = Muted,
            });
        }

        return rows;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
