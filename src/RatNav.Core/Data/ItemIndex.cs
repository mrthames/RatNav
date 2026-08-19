using RatNav.Core.Model;

namespace RatNav.Core.Data;

/// <summary>
/// The reverse index behind "do I need this?" — the question a Tarkov player asks a hundred
/// times a raid.
///
/// tarkov.dev gives us quests-that-need-items and hideout-levels-that-need-items. This flips
/// both around so an item id answers in one lookup: which quests want it, whether they want it
/// found-in-raid, which hideout modules want it, and whether it is a key that opens something
/// you're questing for.
///
/// Built once per data refresh and treated as immutable. It reflects the whole game, not your
/// progress — filtering to *your* active quests is <c>ItemTracker</c>'s job, so this stays
/// cacheable and does not need rebuilding every time you complete something.
/// </summary>
public sealed class ItemIndex
{
    private readonly Dictionary<string, ItemDef> _itemsById;
    private readonly Dictionary<string, ItemNeeds> _needsByItemId;
    private readonly List<ItemDef> _searchable;

    public ItemIndex(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _itemsById = data.Items
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var quests = new Dictionary<string, List<QuestNeed>>(StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<string, List<QuestNeed>>(StringComparer.OrdinalIgnoreCase);
        var hideout = new Dictionary<string, List<HideoutNeed>>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in data.Tasks)
        {
            foreach (var objective in task.Objectives)
            {
                foreach (var item in objective.Items)
                {
                    Add(quests, item.ItemId, new QuestNeed
                    {
                        TaskId = task.Id,
                        TaskName = task.Name,
                        ObjectiveId = objective.Id,
                        Count = item.Count,
                        FoundInRaid = item.FoundInRaid,
                        TraderName = task.TraderName,
                    });
                }

                foreach (var keyId in objective.NeededKeyItemIds)
                {
                    Add(keys, keyId, new QuestNeed
                    {
                        TaskId = task.Id,
                        TaskName = task.Name,
                        ObjectiveId = objective.Id,
                        Count = 1,
                        FoundInRaid = false,
                        TraderName = task.TraderName,
                    });
                }
            }
        }

        foreach (var station in data.HideoutStations)
        {
            foreach (var level in station.Levels)
            {
                foreach (var requirement in level.ItemRequirements)
                {
                    Add(hideout, requirement.ItemId, new HideoutNeed
                    {
                        StationId = station.Id,
                        StationName = station.Name,
                        Level = level.Level,
                        Count = requirement.Count,
                    });
                }
            }
        }

        // Barters are indexed by what they cost, because the question being answered is "should I
        // keep this?" — not "what can I buy?". What the trade hands back is carried along so the
        // answer is judgeable rather than just true.
        var barters = new Dictionary<string, List<BarterNeed>>(StringComparer.OrdinalIgnoreCase);

        foreach (var barter in data.Barters)
        {
            foreach (var required in barter.RequiredItems)
            {
                Add(barters, required.ItemId, new BarterNeed
                {
                    BarterId = barter.Id,
                    TraderName = barter.TraderName ?? "Trader",
                    TraderLevel = barter.MinTraderLevel,
                    Count = required.Count,
                    OfferedItemName = barter.OfferedItem is { } offered
                        ? _itemsById.GetValueOrDefault(offered.ItemId)?.Name
                        : null,
                    OfferedCount = barter.OfferedItem?.Count ?? 0,
                });
            }
        }

        var touched = quests.Keys.Concat(keys.Keys).Concat(hideout.Keys).Concat(barters.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        _needsByItemId = new Dictionary<string, ItemNeeds>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemId in touched)
        {
            // An item referenced by a quest we can't resolve to an item definition still gets an
            // entry, with a placeholder name. Better a row reading "Unknown item" than a silently
            // missing quest requirement.
            _needsByItemId[itemId] = new ItemNeeds
            {
                Item = _itemsById.TryGetValue(itemId, out var def) ? def : Placeholder(itemId),
                Quests = quests.GetValueOrDefault(itemId, []),
                Hideout = hideout.GetValueOrDefault(itemId, []),
                AsKey = keys.GetValueOrDefault(itemId, []),
                Barters = barters.GetValueOrDefault(itemId, []),
            };
        }

        _searchable = [.. data.Items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public int ItemCount => _itemsById.Count;

    /// <summary>Number of distinct items wanted by any quest or hideout module.</summary>
    public int NeededItemCount => _needsByItemId.Count;

    public ItemDef? GetItem(string itemId) => _itemsById.GetValueOrDefault(itemId);

    /// <summary>Why this item matters, or null if nothing in the game wants it.</summary>
    public ItemNeeds? GetNeeds(string itemId) => _needsByItemId.GetValueOrDefault(itemId);

    /// <summary>Everything anything wants, for the Items view's default listing.</summary>
    /// <summary>
    /// Everything anything wants, for the Items view's default listing.
    ///
    /// <para>Money is left out. Quests and hideout upgrades genuinely cost roubles, but that is
    /// not something you go looking for in a raid — it comes from selling what you found — and a
    /// line reading "2,857,000 Roubles" beside three bolts buries the things you can act on.</para>
    /// </summary>
    public IEnumerable<ItemNeeds> AllNeeded() =>
        _needsByItemId.Values.Where(n => !Currency.Is(n.Item.Id));

    /// <summary>
    /// Name search, ranked so the obvious answer comes first: exact short-name match (how
    /// players actually refer to items), then prefix, then substring.
    /// </summary>
    public IEnumerable<ItemDef> Search(string query, int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var q = query.Trim();

        return _searchable
            .Select(item => (item, score: Score(item, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.item.Name.Length)
            .Take(limit)
            .Select(x => x.item);
    }

    private static int Score(ItemDef item, string query)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;

        if (item.ShortName is { } shortName && shortName.Equals(query, ci)) return 100;
        if (item.Name.Equals(query, ci)) return 90;
        if (item.Name.StartsWith(query, ci)) return 70;
        if (item.ShortName?.StartsWith(query, ci) == true) return 60;
        if (item.Name.Contains(query, ci)) return 40;
        if (item.NormalizedName?.Contains(query, ci) == true) return 20;
        return 0;
    }

    private static ItemDef Placeholder(string itemId) => new()
    {
        Id = itemId,
        Name = "Unknown item",
        ShortName = "?",
    };

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(value);
    }
}
