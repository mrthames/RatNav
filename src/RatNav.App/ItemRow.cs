using System.Windows.Media;
using RatNav.Service;

// WinForms is referenced for the tray icon and brings clashing drawing types with it.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace RatNav.App;

/// <summary>
/// One line of the overlay's items list: how many more, what it is, and why.
///
/// <para>Deliberately thin. This is read at a glance while something is shooting at you, so it
/// carries a count, a name, and a colour — the reason lives in the tooltip, where it costs
/// nothing until asked for.</para>
/// </summary>
public sealed record ItemRow
{
    /// <summary>The item's full name, ellipsised by the column and whole again in the tooltip.</summary>
    public required string Name { get; init; }

    /// <summary>How many more to find.</summary>
    public required string Count { get; init; }

    /// <summary>The full name and why you want it, on hover.</summary>
    public required string Reason { get; init; }

    public required Brush Colour { get; init; }

    public static ItemRow From(PanelRow row) => new()
    {
        Name = row.Name,
        // A tick means "you have enough of something you were counting". Without a target there
        // was never anything to count, so it gets a dash instead of a false sense of completion.
        Count = row.Count > 0 ? row.Count.ToString() : row.Tracked ? "\u2713" : "\u2013",
        Reason = row.Reason is { Length: > 0 }
            ? $"{row.FullName} — {row.Reason}"
            : row.FullName,

        // Found-in-raid items are the ones you cannot buy your way out of later, so they carry
        // the one colour that means "this is why you are here".
        Colour = row.Count <= 0 ? Muted : row.FoundInRaid ? Need : Route,
    };

    private static readonly Brush Muted = Frozen(0x7b, 0x8c, 0x9b);
    private static readonly Brush Need = Frozen(0xff, 0x5a, 0x3c);
    private static readonly Brush Route = Frozen(0xe0, 0xc9, 0x8a);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
