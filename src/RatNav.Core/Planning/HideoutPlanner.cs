using RatNav.Core.Model;
using RatNav.Core.Tracking;

namespace RatNav.Core.Planning;

/// <summary>Why a level cannot be built yet, in words a player can act on.</summary>
public sealed record HideoutBlocker
{
    /// <summary>"station", "trader", or "skill".</summary>
    public required string Kind { get; init; }
    public required string Text { get; init; }

    /// <summary>The station that has to come first, when that is what is missing.</summary>
    public string? StationId { get; init; }
}

/// <summary>How much of one item the hideout wants, and which upgrade wants it first.</summary>
public sealed record HideoutDemand
{
    public required int Count { get; init; }

    /// <summary>How far out the nearest upgrade wanting this sits. 1 is buildable now.</summary>
    public required int Wave { get; init; }

    /// <summary>The upgrade that wants it first — "Medstation 3".</summary>
    public required string UpgradeName { get; init; }

    public required string StationId { get; init; }

    /// <summary>True when any of those upgrades wants it found in raid.</summary>
    public bool FoundInRaid { get; init; }
}

/// <summary>One upgrade: a station's next level, or one further out.</summary>
public sealed record HideoutUpgrade
{
    public required string StationId { get; init; }
    public required string StationName { get; init; }
    public required int Level { get; init; }

    /// <summary>
    /// How many upgrades deep this sits.
    ///
    /// <para>1 means buildable right now. 2 means buildable once everything at 1 is done, and so
    /// on. This is the number the look-ahead control moves: it is a real distance through the
    /// build order rather than an arbitrary count of rows.</para>
    /// </summary>
    public required int Wave { get; init; }

    /// <summary>What is standing in the way. Empty when the level can be started today.</summary>
    public IReadOnlyList<HideoutBlocker> Blockers { get; init; } = [];

    public IReadOnlyList<ObjectiveItem> ItemRequirements { get; init; } = [];
    public int ConstructionTimeSeconds { get; init; }
    public string? Description { get; init; }

    /// <summary>Picked out by the player as something they are working towards.</summary>
    public bool Targeted { get; init; }
}

/// <summary>
/// Works out what the hideout can actually be built next, and in what order.
///
/// <para><b>The problem this solves.</b> Every un-built level wants items, so the naive list is
/// everything the hideout will ever need — hundreds of items, most of them for upgrades gated
/// behind three others you have not started. That list cannot be shopped from. What a player needs
/// is the handful of things standing between them and the next upgrade they will actually
/// complete.</para>
///
/// <para><b>How.</b> Levels are sorted into waves. Wave 1 is everything buildable with the
/// hideout as it stands. Wave 2 is what opens up once all of wave 1 is done, and so on. A
/// look-ahead of 1 is "what can I build tonight"; 3 is "what should I stop vendoring". Because the
/// waves come from the game's own station prerequisites, the number means something concrete
/// rather than being a slider that just makes the list longer.</para>
///
/// <para>Trader and skill requirements are reported but never gate a wave: RatNav does not know
/// your loyalty levels or your skills, and inventing them would hide upgrades you can in fact
/// start. They are shown so you can see why the game is refusing.</para>
/// </summary>
public static class HideoutPlanner
{
    /// <summary>How far past what is buildable now to look, when nothing else is said.</summary>
    public const int DefaultLookAhead = 2;

    /// <summary>
    /// The upgrades ahead of you, nearest first.
    /// </summary>
    /// <param name="stations">Every hideout station.</param>
    /// <param name="builtLevel">The level each station is currently at, by station id.</param>
    /// <param name="lookAhead">How many waves deep to go. 1 is only what can be built right now.</param>
    /// <param name="targeted">Levels the player has picked out, as "stationId:level".</param>
    public static IReadOnlyList<HideoutUpgrade> Upcoming(
        IReadOnlyList<HideoutStation> stations,
        IReadOnlyDictionary<string, int> builtLevel,
        int lookAhead = DefaultLookAhead,
        IReadOnlySet<string>? targeted = null)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(builtLevel);

        lookAhead = Math.Max(1, lookAhead);

        // Walked forward rather than solved: as each wave is accepted, pretend it is built and see
        // what that opens up. Simulated separately so the player's real hideout is never touched.
        var assumed = new Dictionary<string, int>(builtLevel, StringComparer.OrdinalIgnoreCase);
        var byId = stations
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var found = new List<HideoutUpgrade>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var wave = 1; wave <= lookAhead; wave++)
        {
            var thisWave = new List<HideoutUpgrade>();

            foreach (var station in stations)
            {
                var at = assumed.GetValueOrDefault(station.Id, 0);
                var next = station.Levels.FirstOrDefault(l => l.Level == at + 1);

                if (next is null) continue;

                var key = Key(station.Id, next.Level);
                if (!taken.Add(key)) continue;

                var blockers = BlockersFor(next, assumed, byId);

                // Only station prerequisites hold a level back to a later wave. A trader or skill
                // gate is real, but RatNav cannot see whether you have met it, so treating it as a
                // blocker would hide upgrades that are ready to start.
                if (blockers.Any(b => b.Kind == "station"))
                {
                    taken.Remove(key);
                    continue;
                }

                thisWave.Add(new HideoutUpgrade
                {
                    StationId = station.Id,
                    StationName = station.Name,
                    Level = next.Level,
                    Wave = wave,
                    Blockers = blockers,
                    ItemRequirements = next.ItemRequirements,
                    ConstructionTimeSeconds = next.ConstructionTimeSeconds,
                    Description = next.Description,
                    Targeted = targeted?.Contains(key) ?? false,
                });
            }

            // Nothing new opened up, so no later wave will either.
            if (thisWave.Count == 0) break;

            found.AddRange(thisWave);
            foreach (var upgrade in thisWave) assumed[upgrade.StationId] = upgrade.Level;
        }

        return
        [
            .. found
                .OrderBy(u => u.Wave)
                .ThenByDescending(u => u.Targeted)
                .ThenBy(u => u.StationName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// What the hideout wants, per item, across the upgrades in view.
    ///
    /// <para>When anything is targeted, only targeted upgrades count. Picking three things you
    /// actually want and still being shown the shopping list for all eleven would defeat the
    /// point of picking.</para>
    ///
    /// <para>Each entry carries the nearest upgrade that wants the item, so a row in the items
    /// list can say <i>why</i> — "Medstation 3" beats a bare number, and the wave it came from is
    /// what lets the list be ordered by what you will finish first.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, HideoutDemand> Demand(IEnumerable<HideoutUpgrade> upgrades)
    {
        ArgumentNullException.ThrowIfNull(upgrades);

        var all = upgrades.ToList();
        var counted = all.Any(u => u.Targeted) ? all.Where(u => u.Targeted) : all;

        var demands = new Dictionary<string, HideoutDemand>(StringComparer.OrdinalIgnoreCase);

        foreach (var upgrade in counted.OrderBy(u => u.Wave))
        {
            foreach (var requirement in upgrade.ItemRequirements)
            {
                if (demands.TryGetValue(requirement.ItemId, out var existing))
                {
                    // Ordered by wave, so the first upgrade to want an item is the nearest one and
                    // stays the one named. Later ones only add to the count.
                    demands[requirement.ItemId] = existing with
                    {
                        Count = existing.Count + requirement.Count,
                        FoundInRaid = existing.FoundInRaid || requirement.FoundInRaid,
                    };

                    continue;
                }

                demands[requirement.ItemId] = new HideoutDemand
                {
                    Count = requirement.Count,
                    Wave = upgrade.Wave,
                    UpgradeName = $"{upgrade.StationName} {upgrade.Level}",
                    StationId = upgrade.StationId,
                    FoundInRaid = requirement.FoundInRaid,
                };
            }
        }

        return demands;
    }

    private static IReadOnlyList<HideoutBlocker> BlockersFor(
        HideoutLevel level,
        IReadOnlyDictionary<string, int> assumed,
        IReadOnlyDictionary<string, HideoutStation> byId)
    {
        var blockers = new List<HideoutBlocker>();

        foreach (var requirement in level.StationRequirements)
        {
            if (assumed.GetValueOrDefault(requirement.StationId, 0) >= requirement.Level) continue;

            var name = byId.GetValueOrDefault(requirement.StationId)?.Name ?? "another station";

            blockers.Add(new HideoutBlocker
            {
                Kind = "station",
                Text = $"{name} level {requirement.Level}",
                StationId = requirement.StationId,
            });
        }

        foreach (var requirement in level.TraderRequirements)
        {
            blockers.Add(new HideoutBlocker
            {
                Kind = "trader",
                Text = $"{requirement.TraderName ?? "Trader"} LL{requirement.Level}",
            });
        }

        foreach (var requirement in level.SkillRequirements)
        {
            blockers.Add(new HideoutBlocker
            {
                Kind = "skill",
                Text = $"{requirement.Name} {requirement.Level}",
            });
        }

        return blockers;
    }

    /// <summary>How a targeted level is named, in settings and in the API.</summary>
    public static string Key(string stationId, int level) => $"{stationId}:{level}";
}
