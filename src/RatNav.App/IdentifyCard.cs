using System.Windows.Media;
using RatNav.Core.Tracking;
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
/// <para>The service works out <i>what</i> to say — which of the reasons are things you are
/// actually working on, and which are background — because that is domain logic and all three
/// surfaces have to agree about it. This decides what colour each line is and nothing else.</para>
///
/// <para>The card used to list every quest that has ever wanted the item, every hideout level, and
/// every barter it appears in. All of that is true and almost none of it is the question: you are
/// reading this over a pile of loot with footsteps nearby, and it was answering by listing.</para>
/// </summary>
public static class IdentifyCard
{
    private static readonly Brush Need = Frozen(0xff, 0x5a, 0x3c);
    private static readonly Brush Route = Frozen(0xe0, 0xc9, 0x8a);
    private static readonly Brush Accent = Frozen(0x8e, 0xc8, 0xff);
    private static readonly Brush Muted = Frozen(0x7b, 0x8c, 0x9b);

    public static IReadOnlyList<IdentifyReason> Reasons(ItemDetail detail)
    {
        // Falls back to the full lists when the service did not reach a verdict — an item it has
        // no needs for at all. Saying something is better than an empty card.
        if (detail.Verdict is not { } verdict)
        {
            return
            [
                new IdentifyReason
                {
                    Text = detail.Item.Avg24hPrice is { } price and > 0
                        ? $"Nothing needs this. Flea around {price:N0} ₽."
                        : "Nothing needs this.",
                    Colour = Muted,
                },
            ];
        }

        var rows = new List<IdentifyReason>
        {
            new()
            {
                Text = verdict.Headline,
                Colour = Colour(verdict.Weight),
            },
        };

        rows.AddRange(verdict.Lines.Select(line => new IdentifyReason
        {
            Text = line.Text,
            Colour = Colour(line.Weight),
        }));

        // What you hold, last. It qualifies everything above it rather than competing with it.
        if (detail.Have > 0)
        {
            rows.Add(new IdentifyReason { Text = $"You have {detail.Have}.", Colour = Muted });
        }

        return rows;
    }

    private static Brush Colour(VerdictWeight weight) => weight switch
    {
        VerdictWeight.Critical => Need,
        VerdictWeight.Wanted => Route,
        VerdictWeight.Background => Accent,
        _ => Muted,
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
