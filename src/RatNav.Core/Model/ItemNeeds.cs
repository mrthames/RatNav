
namespace RatNav.Core.Model;

/// <summary>Why an item matters: one quest that wants it.</summary>
public sealed record QuestNeed
{
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public required string ObjectiveId { get; init; }
    public required int Count { get; init; }
    public required bool FoundInRaid { get; init; }
    public string? TraderName { get; init; }
}

/// <summary>Why an item matters: one hideout module that wants it.</summary>
public sealed record HideoutNeed
{
    public required string StationId { get; init; }
    public required string StationName { get; init; }
    public required int Level { get; init; }
    public required int Count { get; init; }
}

/// <summary>Why an item matters: a trader will take it in trade.</summary>
public sealed record BarterNeed
{
    public required string BarterId { get; init; }
    public required string TraderName { get; init; }

    /// <summary>Loyalty level the trade appears at — "Prapor LL2".</summary>
    public required int TraderLevel { get; init; }

    /// <summary>How many of this item the trade costs.</summary>
    public required int Count { get; init; }

    /// <summary>What the trade hands back, named. This is what makes the trade worth judging.</summary>
    public string? OfferedItemName { get; init; }
    public int OfferedCount { get; init; }
}

/// <summary>Everything RatNav knows about why you'd keep an item.</summary>
public sealed record ItemNeeds
{
    public required ItemDef Item { get; init; }
    public IReadOnlyList<QuestNeed> Quests { get; init; } = [];
    public IReadOnlyList<HideoutNeed> Hideout { get; init; } = [];

    /// <summary>Quests that need this item as a key to reach an objective.</summary>
    public IReadOnlyList<QuestNeed> AsKey { get; init; } = [];

    /// <summary>Trades that will take this item. Not a "need" you can finish — a reason not to sell.</summary>
    public IReadOnlyList<BarterNeed> Barters { get; init; } = [];

    /// <summary>Total wanted across quests and hideout, ignoring anything you already have.</summary>
    public int TotalNeeded => Quests.Sum(q => q.Count) + Hideout.Sum(h => h.Count);

    /// <summary>True when at least one need requires the item to be found in raid.</summary>
    public bool AnyFoundInRaid => Quests.Any(q => q.FoundInRaid);

    /// <summary>
    /// Barters are deliberately left out. A trade is a standing offer, not a quantity to collect,
    /// and counting it would make the shopping list demand items you need no more of.
    /// </summary>
    public bool IsNeeded => Quests.Count > 0 || Hideout.Count > 0 || AsKey.Count > 0;

    /// <summary>True when anything at all wants this item, including a trader.</summary>
    public bool IsWanted => IsNeeded || Barters.Count > 0;
}
