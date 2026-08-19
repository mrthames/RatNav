namespace RatNav.Core.Tracking;

using RatNav.Core.Model;

/// <summary>Whether a tracked trade is a trader barter or a hideout craft.</summary>
public enum TradeKind
{
    Barter,
    Craft,
}

/// <summary>
/// A barter or craft the player has chosen to work towards.
///
/// <para>Choosing is the whole point. There are 789 barters and 214 crafts; a list of everything
/// they collectively want is a list of most of the game. What earns a place on a shopping list is
/// the handful someone has actually decided to do.</para>
/// </summary>
public sealed record TrackedTrade
{
    public required string Id { get; init; }
    public required TradeKind Kind { get; init; }

    /// <summary>How many times over. Two of a barter wants twice its inputs.</summary>
    public int Times { get; init; } = 1;
}

/// <summary>What one item is wanted for, across every trade being worked towards.</summary>
public sealed record TradeNeed
{
    public required string ItemId { get; init; }

    /// <summary>How many, across every tracked trade that wants it.</summary>
    public required int Count { get; init; }

    /// <summary>The trades wanting it, named the way a player would say them.</summary>
    public required IReadOnlyList<string> For { get; init; }

    /// <summary>True when every trade wanting it is a craft, which decides which subsection it sits in.</summary>
    public required bool CraftOnly { get; init; }
}

/// <summary>
/// Turns the trades someone is working towards into what to pick up.
///
/// <para>Kept apart from quest and hideout demand on purpose. An item wanted three times for a
/// quest and seven for a barter is two reasons and not a single ten — the numbers add up to the
/// same total either way, but only the split tells you that finishing the quest leaves seven still
/// to find.</para>
/// </summary>
public static class TradeDemands
{
    public static IReadOnlyDictionary<string, TradeNeed> From(
        IEnumerable<TrackedTrade> tracked,
        IReadOnlyList<BarterDef> barters,
        IReadOnlyList<CraftDef> crafts,
        Func<string, string?>? itemName = null)
    {
        var byBarter = barters.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var byCraft = crafts.ToDictionary(c => c.Id, StringComparer.Ordinal);

        var wants = new Dictionary<string, (double Count, List<string> For, bool AnyBarter)>(
            StringComparer.Ordinal);

        foreach (var trade in tracked)
        {
            var times = Math.Max(1, trade.Times);

            var (needs, label) = trade.Kind switch
            {
                TradeKind.Barter when byBarter.TryGetValue(trade.Id, out var barter) =>
                    (barter.RequiredItems, Name(barter, itemName)),

                TradeKind.Craft when byCraft.TryGetValue(trade.Id, out var craft) =>
                    (craft.RequiredItems, Name(craft, itemName)),

                // A trade that no longer exists — a patch removed it, or the cache predates it.
                // Skipped rather than throwing: a stale selection should cost you a line on a
                // list, not the list.
                _ => (null, null),
            };

            if (needs is null || label is null) continue;

            foreach (var item in needs)
            {
                var entry = wants.TryGetValue(item.ItemId, out var found)
                    ? found
                    : (0d, [], false);

                entry.Count += item.Count * times;
                entry.AnyBarter |= trade.Kind == TradeKind.Barter;

                if (!entry.For.Contains(label)) entry.For.Add(label);

                wants[item.ItemId] = entry;
            }
        }

        return wants.ToDictionary(
            pair => pair.Key,
            pair => new TradeNeed
            {
                ItemId = pair.Key,

                // Rounded up. Barter counts are fractional where the trade is priced in currency,
                // and you cannot carry four-fifths of a rouble stack — what you need is the next
                // whole one.
                Count = (int)Math.Ceiling(pair.Value.Count),
                For = pair.Value.For,
                CraftOnly = !pair.Value.AnyBarter,
            },
            StringComparer.Ordinal);
    }

    /// <summary>"Prapor LL2 · Dorm room 303 key" — the trade said the way someone would say it.</summary>
    private static string Name(BarterDef barter, Func<string, string?>? itemName)
    {
        var trader = barter.TraderName ?? "a trader";
        var level = barter.MinTraderLevel > 0 ? $" LL{barter.MinTraderLevel}" : "";
        var gives = barter.OfferedItem is { } offer ? itemName?.Invoke(offer.ItemId) : null;

        return gives is { Length: > 0 } ? $"{trader}{level} · {gives}" : $"{trader}{level} barter";
    }

    /// <summary>"Workbench 2 · Toolset".</summary>
    private static string Name(CraftDef craft, Func<string, string?>? itemName)
    {
        var station = craft.StationName ?? "a station";
        var makes = craft.ProducedItem is { } made ? itemName?.Invoke(made.ItemId) : null;

        return makes is { Length: > 0 }
            ? $"{station} {craft.StationLevel} · {makes}"
            : $"{station} {craft.StationLevel} craft";
    }
}
