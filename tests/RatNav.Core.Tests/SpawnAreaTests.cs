namespace RatNav.Core.Tests;

using RatNav.Core.Maps;
using RatNav.Core.Model;

public class SpawnAreaTests
{
    private static SpawnAreas.RawSpawn At(
        double x, double z, string side = "pmc", string category = "player", string? zone = null) =>
        new(new GamePosition(x, 0, z), [side], [category], zone);

    [Fact]
    public void Points_within_the_radius_become_one_area()
    {
        var areas = SpawnAreas.From([At(0, 0), At(30, 0), At(0, 40)]);

        var area = Assert.Single(areas);

        Assert.Equal(3, area.Points);
        Assert.Equal(SpawnFaction.Pmc, area.Faction);
    }

    [Fact]
    public void Points_beyond_the_radius_stay_apart()
    {
        var areas = SpawnAreas.From([At(0, 0), At(500, 0)]);

        Assert.Equal(2, areas.Count);
    }

    [Fact]
    public void The_centre_is_the_average_of_the_members()
    {
        var area = Assert.Single(SpawnAreas.From([At(0, 0), At(40, 0)]));

        Assert.Equal(20, area.Centre.X, 3);
        Assert.Equal(0, area.Centre.Z, 3);
    }

    [Fact]
    public void The_spread_is_how_far_the_furthest_member_sits_from_the_centre()
    {
        var area = Assert.Single(SpawnAreas.From([At(0, 0), At(40, 0)]));

        Assert.Equal(20, area.Spread, 3);
    }

    /// <summary>The whole point of the layer: bot spawns are most of the list and none of them
    /// tell you where a player is coming from.</summary>
    [Fact]
    public void Bot_only_spawns_are_dropped()
    {
        Assert.Empty(SpawnAreas.From([At(0, 0, category: "bot"), At(10, 0, category: "boss")]));
    }

    [Fact]
    public void A_side_of_all_counts_as_pmc()
    {
        var area = Assert.Single(SpawnAreas.From([At(0, 0, side: "all")]));

        Assert.Equal(SpawnFaction.Pmc, area.Faction);
    }

    [Fact]
    public void Pmc_and_scav_areas_are_clustered_separately()
    {
        var areas = SpawnAreas.From([At(0, 0), At(10, 0, side: "scav")]);

        Assert.Equal(2, areas.Count);
        Assert.Contains(areas, a => a.Faction == SpawnFaction.Pmc);
        Assert.Contains(areas, a => a.Faction == SpawnFaction.Scav);
    }

    /// <summary>
    /// Seeding on the densest point rather than the first one. Four points evenly spaced across
    /// more than one radius: seeded from the front they split two and two, seeded from the middle
    /// three of them are recognised as the group they are.
    /// </summary>
    [Fact]
    public void The_densest_point_seeds_the_area()
    {
        var areas = SpawnAreas.From([At(0, 0), At(60, 0), At(120, 0), At(180, 0)]);

        Assert.Equal(3, areas.Max(a => a.Points));
    }

    [Fact]
    public void The_zone_the_members_mostly_agree_on_is_kept()
    {
        var area = Assert.Single(SpawnAreas.From([
            At(0, 0, zone: "ZoneBigRocks"),
            At(10, 0, zone: "ZoneBigRocks"),
            At(20, 0, zone: "ZoneScavBase"),
        ]));

        Assert.Equal("ZoneBigRocks", area.Zone);
    }

    [Fact]
    public void No_spawns_is_no_areas_rather_than_a_throw()
    {
        Assert.Empty(SpawnAreas.From([]));
    }
}
