namespace RatNav.Core.Maps;

using RatNav.Core.Model;

/// <summary>
/// The spawns a player can appear at, from tarkov.dev's list of every spawn on a map.
///
/// <para>Bot-only points are dropped. They are most of the list, and where a Scav AI wanders in
/// says nothing about where the people hunting you started.</para>
///
/// <para>A side of <c>all</c> counts as PMC: Streets and Factory record their player spawns that
/// way, and the humans are what this layer is for.</para>
/// </summary>
public static class SpawnPoints
{
    public static IReadOnlyList<MapSpawnPoint> From(IEnumerable<RawSpawn> spawns)
    {
        var points = new List<MapSpawnPoint>();

        foreach (var spawn in spawns)
        {
            if (!Has(spawn.Categories, "player")) continue;

            var faction =
                Has(spawn.Sides, "pmc") || Has(spawn.Sides, "all") ? SpawnFaction.Pmc
                : Has(spawn.Sides, "scav") ? SpawnFaction.Scav

                // A side we do not recognise. Dropped rather than guessed at: an unexplained dot
                // on a map is worse than a missing one.
                : (SpawnFaction?)null;

            if (faction is not { } side) continue;

            points.Add(new MapSpawnPoint { Position = spawn.Position, Faction = side });
        }

        return points;
    }

    private static bool Has(IReadOnlyList<string> values, string wanted) =>
        values.Contains(wanted, StringComparer.OrdinalIgnoreCase);

    /// <summary>One spawn as tarkov.dev records it.</summary>
    public readonly record struct RawSpawn(
        GamePosition Position,
        IReadOnlyList<string> Sides,
        IReadOnlyList<string> Categories,
        string? Zone);
}
