using RatNav.Core.Maps;
using RatNav.Core.Model;
using RatNav.Core.Planning;

namespace RatNav.Core.Sharing;

/// <summary>What running a merged plan together actually changes.</summary>
public sealed record SquadOverlap
{
    /// <summary>Objectives more than one player picked — do them once, together, both tick off.</summary>
    public IReadOnlyList<SharedObjective> Shared { get; init; } = [];

    /// <summary>
    /// Items more than one player needs. The map only spawns so many, so this is the argument
    /// worth having before the raid rather than in Dorms.
    /// </summary>
    public IReadOnlyList<ContestedItem> ContestedItems { get; init; } = [];

    /// <summary>Keys more than one player was going to bring. Only one of you needs to.</summary>
    public IReadOnlyList<RedundantKey> RedundantKeys { get; init; } = [];
}

public sealed record SharedObjective
{
    public required string ObjectiveId { get; init; }
    public required IReadOnlyList<string> Owners { get; init; }
}

public sealed record ContestedItem
{
    public required string ItemId { get; init; }
    public required IReadOnlyList<string> Owners { get; init; }
}

public sealed record RedundantKey
{
    public required string ItemId { get; init; }
    public required IReadOnlyList<string> Owners { get; init; }

    /// <summary>Who should carry it — the first owner, so the answer is stable rather than arbitrary.</summary>
    public string Carrier => Owners[0];
}

/// <summary>A merged plan: everyone's objectives, attributed, on one route.</summary>
public sealed record SquadPlan
{
    public required RaidPlan Plan { get; init; }
    public required IReadOnlyList<string> Owners { get; init; }
    public required SquadOverlap Overlap { get; init; }

    /// <summary>The documents this was merged from, so an updated one can replace its predecessor.</summary>
    public required IReadOnlyList<PlanDocument> Sources { get; init; }
}

/// <summary>
/// Combines raid plans from several players into one.
///
/// <para><b>Nothing is dropped.</b> A merge that quietly discarded someone's objective would be
/// worse than no merge at all — the entire reason to share plans is that neither player has to
/// give up their own raid. Every objective survives, carrying its owner.</para>
///
/// <para>What merging adds beyond a longer list is the overlap: which objectives you can do
/// together, which items you are about to compete for, and which keys only one of you needs to
/// bring. That is the part that changes how the raid is run.</para>
/// </summary>
public static class PlanMerger
{
    public static SquadPlan Merge(
        MapDef map,
        IReadOnlyList<PlanDocument> documents,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? itemsByTask = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
            throw new ArgumentException("Nothing to merge.", nameof(documents));

        // A later document from the same owner replaces an earlier one, so re-merging after a
        // buddy sends an update is a merge rather than a duplication.
        var latest = documents
            .GroupBy(d => d.Owner ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .ToList();

        var owners = latest.Select(d => d.Owner ?? "(unnamed)").ToList();

        // One waypoint per objective. Where several players picked the same one it collapses into
        // a single shared stop rather than two pins on top of each other.
        var byObjective = new Dictionary<string, List<(PlanDocument Doc, PlanStop Stop)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in latest)
        {
            foreach (var stop in document.Stops)
            {
                if (!byObjective.TryGetValue(stop.ObjectiveId, out var list))
                    byObjective[stop.ObjectiveId] = list = [];
                list.Add((document, stop));
            }
        }

        var waypoints = new List<Waypoint>();
        var shared = new List<SharedObjective>();

        foreach (var (objectiveId, entries) in byObjective)
        {
            var stopOwners = entries
                .Select(e => e.Stop.Owner ?? e.Doc.Owner ?? "(unnamed)")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var first = entries[0].Stop;

            waypoints.Add(new Waypoint
            {
                ObjectiveId = objectiveId,
                TaskId = first.TaskId,
                TaskName = first.TaskId,
                Description = "",
                Position = new GamePosition(first.X, first.Y, first.Z),
                Owner = string.Join(" + ", stopOwners),
                NeededKeyItemIds = [.. entries.SelectMany(e => e.Stop.NeededKeyItemIds).Distinct(StringComparer.OrdinalIgnoreCase)],
            });

            if (stopOwners.Count > 1)
                shared.Add(new SharedObjective { ObjectiveId = objectiveId, Owners = stopOwners });
        }

        var plan = RaidPlanner.Plan(map, waypoints);

        return new SquadPlan
        {
            Plan = plan,
            Owners = owners,
            Sources = latest,
            Overlap = new SquadOverlap
            {
                Shared = shared,
                ContestedItems = FindContested(latest, itemsByTask),
                RedundantKeys = FindRedundantKeys(latest),
            },
        };
    }

    /// <summary>
    /// Items more than one player is hunting. Uses each plan's own shopping list, falling back to
    /// the items its tasks require when a plan did not carry one.
    /// </summary>
    private static IReadOnlyList<ContestedItem> FindContested(
        IReadOnlyList<PlanDocument> documents,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? itemsByTask)
    {
        var byItem = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var owner = document.Owner ?? "(unnamed)";

            var items = document.ShoppingListItemIds.Count > 0
                ? document.ShoppingListItemIds
                : [.. document.Stops
                    .SelectMany(s => itemsByTask?.GetValueOrDefault(s.TaskId) ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)];

            foreach (var itemId in items)
            {
                if (!byItem.TryGetValue(itemId, out var owners))
                    byItem[itemId] = owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                owners.Add(owner);
            }
        }

        return
        [
            .. byItem
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => new ContestedItem { ItemId = pair.Key, Owners = [.. pair.Value.Order()] })
                .OrderBy(c => c.ItemId, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static IReadOnlyList<RedundantKey> FindRedundantKeys(IReadOnlyList<PlanDocument> documents)
    {
        var byKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var owner = document.Owner ?? "(unnamed)";

            var keys = document.RequiredKeyItemIds.Count > 0
                ? document.RequiredKeyItemIds
                : [.. document.Stops.SelectMany(s => s.NeededKeyItemIds).Distinct(StringComparer.OrdinalIgnoreCase)];

            foreach (var keyId in keys)
            {
                if (!byKey.TryGetValue(keyId, out var owners))
                    byKey[keyId] = owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                owners.Add(owner);
            }
        }

        return
        [
            .. byKey
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => new RedundantKey { ItemId = pair.Key, Owners = [.. pair.Value.Order()] })
                .OrderBy(k => k.ItemId, StringComparer.OrdinalIgnoreCase)
        ];
    }
}
