using System.Windows.Media;
using RatNav.Service;

// WinForms is referenced for the tray icon and brings a clashing Brush with it.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace RatNav.App;

/// <summary>
/// One line of the overlay's items list: what it is, how many more you want, and why.
///
/// <para>Deliberately thin. This is read at a glance while something is shooting at you, so it
/// carries a name, a number, and a colour — everything else lives in the tooltip, where it costs
/// nothing until asked for.</para>
/// </summary>
public sealed record ItemRow
{
    public required string Name { get; init; }

    /// <summary>How many more to find. "3" reads faster than "1/4" and answers the same question.</summary>
    public required string Count { get; init; }

    /// <summary>Why you want it, on hover.</summary>
    public required string Reason { get; init; }

    public required Brush Colour { get; init; }

    public static ItemRow From(TrackedItemView view)
    {
        var reasons = new List<string>();

        if (view.QuestNeeded > 0) reasons.Add($"quests {view.QuestNeeded}");
        if (view.HideoutNeeded > 0) reasons.Add($"hideout {view.HideoutNeeded}");
        if (view.IsKey) reasons.Add("key");
        if (view.Watched) reasons.Add(view.WatchNote is { Length: > 0 } note ? $"watching — {note}" : "watching");
        if (view.FoundInRaid) reasons.Add("found in raid");

        return new ItemRow
        {
            // The short name is what players call it and what fits the column.
            Name = view.ShortName is { Length: > 0 } and not "?" ? view.ShortName : view.Name,
            Count = view.Remaining > 0 ? view.Remaining.ToString() : "✓",
            Reason = $"{view.Name}{(reasons.Count > 0 ? " · " + string.Join(" · ", reasons) : "")}",

            // Found-in-raid items are the ones you cannot buy your way out of, so they carry the
            // one colour that means "this is the reason you are here".
            Colour = new SolidColorBrush(
                view.Remaining <= 0 ? Color.FromRgb(0x7b, 0x8c, 0x9b)
                : view.FoundInRaid ? Color.FromRgb(0xff, 0x5a, 0x3c)
                : Color.FromRgb(0xe0, 0xc9, 0x8a)),
        };
    }
}
