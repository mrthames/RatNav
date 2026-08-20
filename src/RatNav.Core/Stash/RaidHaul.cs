namespace RatNav.Core.Stash;

/// <summary>What one item's count did over a raid.</summary>
public sealed record HaulLine
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }

    /// <summary>How many you were carrying when you queued.</summary>
    public required int Before { get; init; }

    /// <summary>How many you were carrying when you got out.</summary>
    public required int After { get; init; }

    /// <summary>What changed. Positive is what you found.</summary>
    public int Change => After - Before;
}

/// <summary>
/// What a raid actually produced, from what you were carrying at each end of it.
///
/// <para>Two screenshots of the inventory screen: one before you queue, one when you are out.
/// Whatever is there at the end and was not there at the start, you found. It is the only way to
/// count a raid's haul without reading the game's memory, and it needs nothing from the game that
/// a screenshot does not already contain.</para>
///
/// <para>Only what you were <b>carrying</b> counts — a backpack, a rig, pockets, a secure
/// container. Never what you were wearing. That is not a rule applied afterwards: worn equipment
/// does not sit in a grid of uniform cells, so it never reaches this in the first place.</para>
///
/// <para>Losses are reported as well as gains. Dying with three of something is worth knowing
/// about, and a tracker that only ever counted upwards would drift away from your stash one raid
/// at a time.</para>
/// </summary>
public static class RaidHaul
{
    /// <param name="before">What was carried at the start, by item id.</param>
    /// <param name="after">What was carried at the end, by item id.</param>
    /// <param name="nameOf">How to say an item, for a list somebody has to read.</param>
    public static IReadOnlyList<HaulLine> Compare(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after,
        Func<string, string?>? nameOf = null)
    {
        var ids = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal);

        return
        [
            .. ids
                .Select(id => new HaulLine
                {
                    ItemId = id,
                    Name = nameOf?.Invoke(id) ?? id,
                    Before = before.GetValueOrDefault(id),
                    After = after.GetValueOrDefault(id),
                })
                .Where(line => line.Change != 0)

                // Found first and biggest first, because that is what the screen is for. Losses
                // come after, in the same order.
                .OrderByDescending(line => line.Change > 0)
                .ThenByDescending(line => Math.Abs(line.Change))
                .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Adds up what several containers hold, since an inventory screen is four of them at once.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Total(
        IEnumerable<IReadOnlyDictionary<string, int>> containers)
    {
        var all = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var container in containers)
        {
            foreach (var (id, count) in container)
                all[id] = all.GetValueOrDefault(id) + count;
        }

        return all;
    }
}
