namespace RatNav.Core.Tests;

using RatNav.Core.Maps;
using RatNav.Core.Model;

public class SpawnPointTests
{
    private static SpawnPoints.RawSpawn At(
        double x, double z, string side = "pmc", string category = "player") =>
        new(new GamePosition(x, 0, z), [side], [category], null);

    [Fact]
    public void Every_player_spawn_comes_back_as_its_own_point()
    {
        var points = SpawnPoints.From([At(0, 0), At(30, 0), At(500, 900)]);

        Assert.Equal(3, points.Count);
        Assert.Equal(500, points[2].Position.X);
    }

    /// <summary>Most of the list, and none of it says where a person started.</summary>
    [Fact]
    public void Bot_only_spawns_are_dropped()
    {
        Assert.Empty(SpawnPoints.From([At(0, 0, category: "bot"), At(10, 0, category: "boss")]));
    }

    [Fact]
    public void A_side_of_all_counts_as_pmc()
    {
        Assert.Equal(SpawnFaction.Pmc, Assert.Single(SpawnPoints.From([At(0, 0, side: "all")])).Faction);
    }

    [Fact]
    public void Scav_spawns_keep_their_own_faction()
    {
        Assert.Equal(SpawnFaction.Scav, Assert.Single(SpawnPoints.From([At(0, 0, side: "scav")])).Faction);
    }

    /// <summary>An unexplained dot on a map is worse than a missing one.</summary>
    [Fact]
    public void A_side_we_do_not_recognise_is_dropped_rather_than_guessed_at()
    {
        Assert.Empty(SpawnPoints.From([At(0, 0, side: "none")]));
    }

    [Fact]
    public void No_spawns_is_no_points_rather_than_a_throw()
    {
        Assert.Empty(SpawnPoints.From([]));
    }
}
