namespace RatNav.Core.Tests;

using RatNav.Core.Data;
using RatNav.Core.Model;

/// <summary>
/// Folding the locations that are the same ground into one map.
///
/// <para>The game splits some places into several locations that share every building and street:
/// Ground Zero has one for under level 21 and one for 21+, Factory has a night version. Offered
/// separately they put two identical maps in the picker with nothing to choose between them.</para>
/// </summary>
public class MapVariantTests
{
    private static MapDef Map(string id, string name, string normalized, params string[] logAliases) =>
        new() { Id = id, Name = name, NormalizedName = normalized, LogAliases = logAliases };

    [Fact]
    public void A_variant_stops_being_its_own_map()
    {
        var folded = GameDataCache.FoldVariants(
        [
            Map("gz", "Ground Zero", "ground-zero"),
            Map("gz21", "Ground Zero 21+", "ground-zero-21"),
        ]);

        Assert.Equal(["Ground Zero"], folded.Select(m => m.Name));
    }

    /// <summary>Quests are attached to a location, so the id has to survive the fold or every
    /// quest on Ground Zero 21+ quietly stops having a map.</summary>
    [Fact]
    public void The_map_that_absorbs_a_variant_still_answers_to_its_id()
    {
        var folded = GameDataCache.FoldVariants(
        [
            Map("gz", "Ground Zero", "ground-zero"),
            Map("gz21", "Ground Zero 21+", "ground-zero-21"),
        ]);

        var groundZero = folded.Single();

        Assert.True(groundZero.Covers("gz21"));
        Assert.True(groundZero.Covers("gz"));
        Assert.False(groundZero.Covers("customs"));
    }

    /// <summary>A raid started on the variant has to keep being recognised.</summary>
    [Fact]
    public void Log_names_come_along_with_the_fold()
    {
        var folded = GameDataCache.FoldVariants(
        [
            Map("factory", "Factory", "factory", "factory4_day"),
            Map("night", "Night Factory", "night-factory", "factory4_night"),
        ]);

        Assert.Contains("factory4_night", folded.Single().LogAliases);
        Assert.Contains("factory4_day", folded.Single().LogAliases);
    }

    /// <summary>Dropping it would lose the map rather than tidy the list.</summary>
    [Fact]
    public void A_variant_whose_parent_is_missing_keeps_its_place()
    {
        var folded = GameDataCache.FoldVariants([Map("gz21", "Ground Zero 21+", "ground-zero-21")]);

        Assert.Equal(["Ground Zero 21+"], folded.Select(m => m.Name));
    }

    [Fact]
    public void Maps_that_are_their_own_place_are_left_alone()
    {
        var maps = new List<MapDef> { Map("customs", "Customs", "customs"), Map("woods", "Woods", "woods") };

        Assert.Equal(["Customs", "Woods"], GameDataCache.FoldVariants(maps).Select(m => m.Name));
    }

    /// <summary>A map on its own covers itself and nothing else.</summary>
    [Fact]
    public void An_unfolded_map_covers_only_itself()
    {
        Assert.True(Map("customs", "Customs", "customs").Covers("CUSTOMS"));
        Assert.False(Map("customs", "Customs", "customs").Covers("woods"));
    }
}
