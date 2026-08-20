namespace RatNav.Core.Tracking;

/// <summary>
/// One item a goal needs, how many, and how many of them you have found for <i>this</i> goal.
///
/// <para><c>Found</c> is counted per goal rather than against a single stash total on purpose.
/// The question a collection answers is "how many more for this", and two goals wanting the same
/// item are two separate answers — three plugs set aside for the document case are not also
/// available for the workbench.</para>
/// </summary>
public readonly record struct GoalItem(string ItemId, int Count, int Found = 0);

/// <summary>
/// Something you have decided to collect for, named by you.
///
/// <para>This replaced a searchable catalogue of every barter and craft in the game. Picking one
/// out of 789 meant knowing which of Therapist's four Dorm 303 trades you meant, and the answer to
/// "what am I collecting for" was never "the one tarkov.dev calls 6a70c20e". It was "the document
/// case". So you name it and list what it takes.</para>
///
/// <para>Nothing checks it against the game's own trades on purpose. A goal can be a barter, a
/// craft, a kit you build for yourself, or a promise to a friend, and RatNav has no business
/// having an opinion about which.</para>
/// </summary>
public sealed record Goal
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public IReadOnlyList<GoalItem> Items { get; init; } = [];

    /// <summary>How many times over. Two of a goal wants twice its items.</summary>
    public int Times { get; init; } = 1;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>What one item is wanted for, across every goal.</summary>
public sealed record GoalNeed
{
    public required string ItemId { get; init; }
    public required int Count { get; init; }

    /// <summary>The goals wanting it, by name.</summary>
    public required IReadOnlyList<string> For { get; init; }
}

/// <summary>Turns the goals you are collecting for into what to pick up.</summary>
public static class GoalDemands
{
    public static IReadOnlyDictionary<string, GoalNeed> From(IEnumerable<Goal> goals)
    {
        var wants = new Dictionary<string, (int Count, List<string> For)>(StringComparer.Ordinal);

        foreach (var goal in goals)
        {
            var times = Math.Max(1, goal.Times);

            foreach (var item in goal.Items)
            {
                // Nothing to collect is not a line on a shopping list. The store filters these on
                // the way in; this filters them on the way out, because a goal can also arrive
                // from a file somebody edited by hand.
                if (item.ItemId is not { Length: > 0 } || item.Count <= 0) continue;

                // What is left, not what the goal asked for. Found four of six and the list should
                // say two — otherwise it keeps asking for things sitting in your stash.
                var left = Math.Max(0, item.Count - item.Found);
                if (left == 0) continue;

                var entry = wants.TryGetValue(item.ItemId, out var found) ? found : (0, []);

                entry.Count += left * times;

                if (!entry.For.Contains(goal.Name)) entry.For.Add(goal.Name);

                wants[item.ItemId] = entry;
            }
        }

        return wants.ToDictionary(
            pair => pair.Key,
            pair => new GoalNeed
            {
                ItemId = pair.Key,
                Count = pair.Value.Count,
                For = pair.Value.For,
            },
            StringComparer.Ordinal);
    }
}
