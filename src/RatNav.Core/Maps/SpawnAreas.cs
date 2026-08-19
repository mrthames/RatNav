namespace RatNav.Core.Maps;

using RatNav.Core.Model;

/// <summary>
/// Turns tarkov.dev's raw spawn points into a handful of areas worth drawing.
///
/// <para>The source lists every individual spawn: 140 PMC points on Woods, 196 on Streets. Drawn
/// literally that is a rash of dots covering the map, which answers no question — nobody wants to
/// know that a spawn exists at one particular tree. What a player wants before a raid is "roughly
/// where did the others start", and the answer to that is a dozen regions, not four hundred
/// points.</para>
///
/// <para>So points are clustered greedily: repeatedly take the point with the most neighbours
/// within <see cref="Radius"/>, absorb them, and move on. Greedy rather than k-means because the
/// number of areas is not known in advance and should not have to be guessed — a map with three
/// spawn regions should produce three, and one with twenty should produce twenty.</para>
/// </summary>
public static class SpawnAreas
{
    /// <summary>
    /// How far apart two spawns can be and still belong to the same area, in metres.
    ///
    /// <para>Chosen against the real data: at 100 metres every map lands between 12 and 26 areas
    /// per faction, which is few enough to read and many enough to still say something. At 60 it
    /// climbs past 50 on Woods, which is back to a rash of dots.</para>
    /// </summary>
    public const double Radius = 100;

    /// <summary>
    /// Only spawns a player can appear at, grouped by faction.
    ///
    /// <para>Bot-only points are dropped. They are most of the list, and knowing where a Scav AI
    /// wanders in tells you nothing about who is coming for you.</para>
    ///
    /// <para>A side of <c>all</c> means either faction spawns there — Streets and Factory record
    /// their player spawns that way — so it counts as PMC. That is the reading that matters: what
    /// the toggle is for is the humans who loaded in with you.</para>
    /// </summary>
    public static IReadOnlyList<MapSpawnArea> From(IEnumerable<RawSpawn> spawns)
    {
        var player = spawns.Where(s => s.Categories.Contains("player", StringComparer.OrdinalIgnoreCase)).ToList();

        return
        [
            .. Cluster(player.Where(IsPmc), SpawnFaction.Pmc),
            .. Cluster(player.Where(s => !IsPmc(s) && Has(s, "scav")), SpawnFaction.Scav),
        ];
    }

    private static bool IsPmc(RawSpawn s) => Has(s, "pmc") || Has(s, "all");

    private static bool Has(RawSpawn s, string side) =>
        s.Sides.Contains(side, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<MapSpawnArea> Cluster(IEnumerable<RawSpawn> spawns, SpawnFaction faction)
    {
        var left = spawns.ToList();

        while (left.Count > 0)
        {
            // Seed on the densest point rather than the first one. Starting anywhere would let a
            // lone outlier claim the neighbours of a real cluster and split it in two.
            var seed = left.MaxBy(a => left.Count(b => Distance(a, b) <= Radius))!;
            var members = left.Where(b => Distance(seed, b) <= Radius).ToList();

            var x = members.Average(m => m.Position.X);
            var z = members.Average(m => m.Position.Z);
            var y = members.Average(m => m.Position.Y);

            yield return new MapSpawnArea
            {
                Centre = new GamePosition(x, y, z),
                Faction = faction,

                // How far the area actually reaches, not the clustering radius. A tight cluster
                // should draw tight; drawing every area the same size would say the map is more
                // uniformly dangerous than it is.
                Spread = members.Max(m => Math.Sqrt(
                    ((m.Position.X - x) * (m.Position.X - x)) + ((m.Position.Z - z) * (m.Position.Z - z)))),

                Points = members.Count,

                // The zone the most members share. Tarkov's own zone names ("ZoneBigRocks") are
                // internal, so this is a hint for a tooltip rather than something to draw.
                Zone = members
                    .Where(m => m.Zone is { Length: > 0 })
                    .GroupBy(m => m.Zone!)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key,
            };

            left = left.Except(members).ToList();
        }
    }

    private static double Distance(RawSpawn a, RawSpawn b)
    {
        var dx = a.Position.X - b.Position.X;
        var dz = a.Position.Z - b.Position.Z;

        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>One spawn point as tarkov.dev records it, before any grouping.</summary>
    public readonly record struct RawSpawn(
        GamePosition Position,
        IReadOnlyList<string> Sides,
        IReadOnlyList<string> Categories,
        string? Zone);
}
