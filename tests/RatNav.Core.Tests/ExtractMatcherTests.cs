namespace RatNav.Core.Tests;

using RatNav.Core.Maps;
using RatNav.Core.Model;

/// <summary>
/// Turning what the game put on screen back into extracts the map knows about.
///
/// <para>The lines here are what OCR over a game scene actually produces: dropped characters,
/// letters read as digits, names split across lines, and the surrounding interface swept up with
/// them.</para>
/// </summary>
public class ExtractMatcherTests
{
    private static readonly MapExtract[] Customs =
    [
        new() { Name = "ZB-1011", Faction = "shared" },
        new() { Name = "Crossroads", Faction = "pmc" },
        new() { Name = "Old Gas Station", Faction = "pmc" },
        new() { Name = "Trailer Park", Faction = "scav" },
        new() { Name = "RUAF Roadblock", Faction = "pmc" },
        new() { Name = "Factory Shacks", Faction = "scav" },
        new() { Name = "Smugglers' Boat", Faction = "scav" },
    ];

    [Fact]
    public void A_name_on_screen_matches_the_map_s_own_spelling()
    {
        Assert.Equal(["Crossroads"], ExtractMatcher.Match(["Crossroads"], Customs));
    }

    [Fact]
    public void Surrounding_interface_text_does_not_stop_a_match()
    {
        var found = ExtractMatcher.Match(
            ["EXFILTRATION POINTS", "Old Gas Station  02:14", "Trailer Park  available"], Customs);

        Assert.Equal(["Old Gas Station", "Trailer Park"], found);
    }

    /// <summary>The name comes back as the map spells it, which is what everything downstream keys on.</summary>
    [Fact]
    public void Punctuation_and_case_are_not_load_bearing()
    {
        Assert.Equal(["Smugglers' Boat"], ExtractMatcher.Match(["smugglers boat"], Customs));
    }

    [Fact]
    public void A_name_read_with_a_word_missing_still_matches_on_the_distinctive_one()
    {
        Assert.Equal(["RUAF Roadblock"], ExtractMatcher.Match(["RUAF"], Customs));
    }

    /// <summary>
    /// The reason distinctive words exist. Four extracts on a real map end in the same generic
    /// word, and a line reading only that word should claim none of them rather than all of them.
    /// </summary>
    [Fact]
    public void A_word_shared_by_several_extracts_claims_none_of_them()
    {
        MapExtract[] gates = [new() { Name = "Factory Gate" }, new() { Name = "North Gate" }];

        Assert.Empty(ExtractMatcher.Match(["Gate"], gates));
    }

    /// <summary>
    /// The normal case for OCR over a game scene: one word of a long name comes back wrong. Losing
    /// the whole extract over it would make the feature useless exactly where it is needed.
    /// </summary>
    [Fact]
    public void One_misread_word_in_a_long_name_does_not_lose_it()
    {
        MapExtract[] streets = [new() { Name = "Klimov Shopping Mall Exfil" }];

        Assert.Equal(
            ["Klimov Shopping Mall Exfil"],
            ExtractMatcher.Match(["Klimov Shopping Mall Exit  03:22"], streets));
    }

    /// <summary>
    /// What stops the leniency above turning into noise. Half of two short words is one short
    /// word, and no extract should be identifiable by a fragment that size.
    /// </summary>
    [Fact]
    public void A_short_fragment_alone_is_not_enough()
    {
        MapExtract[] streets = [new() { Name = "Primorsky Ave Taxi V-Ex" }];

        Assert.Empty(ExtractMatcher.Match(["Taxi"], streets));
        Assert.Equal(
            ["Primorsky Ave Taxi V-Ex"], ExtractMatcher.Match(["Primorsky Ave."], streets));
    }

    [Fact]
    public void Nothing_recognisable_matches_nothing()
    {
        Assert.Empty(ExtractMatcher.Match(["SURVIVED", "00:41:12", "Scav Boss"], Customs));
    }

    [Fact]
    public void A_line_too_short_to_mean_anything_is_ignored()
    {
        Assert.Empty(ExtractMatcher.Match(["ZB", "x", ""], Customs));
    }

    [Fact]
    public void An_extract_named_twice_on_screen_comes_back_once()
    {
        var found = ExtractMatcher.Match(["Crossroads", "Crossroads  01:00"], Customs);

        Assert.Single(found);
    }

    [Fact]
    public void Reading_nothing_is_not_an_error()
    {
        Assert.Empty(ExtractMatcher.Match([], Customs));
    }

    [Fact]
    public void A_map_with_no_extracts_matches_nothing_rather_than_throwing()
    {
        Assert.Empty(ExtractMatcher.Match(["Crossroads"], []));
    }
}
