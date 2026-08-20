using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

/// <summary>
/// Matching text read off the screen to an item.
///
/// <para>The cases here are the ones OCR actually produces: a name surrounded by tooltip
/// furniture, characters misread for lookalikes, and a screen with nothing on it. Exact matching
/// handles none of them, which is why this is fuzzy and reports how sure it is.</para>
/// </summary>
public class ItemMatcherTests
{
    private static readonly ItemDef[] Items =
    [
        new() { Id = "watch", Name = "Bronze pocket watch", ShortName = "Watch" },
        new() { Id = "ledx", Name = "LEDX Skin Transilluminator", ShortName = "LEDX" },
        new() { Id = "bolts", Name = "Bolts", ShortName = "Bolts" },
        new() { Id = "wires", Name = "Piece of plexiglass", ShortName = "Plexi" },
        new() { Id = "gpu", Name = "Graphics card", ShortName = "GPU" },
    ];

    [Fact]
    public void Reads_an_exact_name()
    {
        var matches = ItemMatcher.Identify(["Bronze pocket watch"], Items);

        Assert.Equal("watch", matches[0].Item.Id);
        Assert.Equal(1.0, matches[0].Confidence, 3);
    }

    [Fact]
    public void Survives_the_mistakes_ocr_actually_makes()
    {
        // "rn" read as "m", "1" for "l" — the classic confusions.
        var matches = ItemMatcher.Identify(["Bronze pocket watcn"], Items);

        Assert.Equal("watch", matches[0].Item.Id);
        Assert.True(matches[0].Confidence > 0.9);
    }

    [Fact]
    public void Finds_the_name_among_the_rest_of_the_tooltip()
    {
        // A real capture is the name plus whatever else was on screen near it.
        var matches = ItemMatcher.Identify(
            ["Barter item", "LEDX Skin Transilluminator", "1x1", "Weight: 0.1 kg"],
            Items);

        Assert.Equal("ledx", matches[0].Item.Id);
    }

    [Fact]
    public void Returns_nothing_for_text_that_is_not_an_item()
    {
        var matches = ItemMatcher.Identify(
            ["Health", "Energy", "Hydration", "00:34:12"],
            Items);

        Assert.Empty(matches);
    }

    [Fact]
    public void Offers_alternatives_rather_than_one_answer()
    {
        // OCR is wrong often enough that "no, the one below it" has to be possible.
        var matches = ItemMatcher.Identify(["Bolts", "Piece of plexiglass"], Items);

        Assert.True(matches.Count >= 2);
        Assert.Contains(matches, m => m.Item.Id == "bolts");
        Assert.Contains(matches, m => m.Item.Id == "wires");
    }

    [Fact]
    public void Says_which_line_it_matched()
    {
        var matches = ItemMatcher.Identify(["some junk", "Graphics card"], Items);

        // A wrong answer has to be explainable, or the fix is guesswork.
        Assert.Equal("graphics card", matches[0].MatchedText);
    }

    [Fact]
    public void A_two_letter_fragment_does_not_match_everything()
    {
        // Short strings are close to every name by edit distance. Left unchecked, a stray "GP"
        // in the corner of the screen identifies as a graphics card.
        var matches = ItemMatcher.Identify(["GP"], Items);

        Assert.Empty(matches);
    }

    [Fact]
    public void Punctuation_and_case_do_not_matter()
    {
        var matches = ItemMatcher.Identify(["LEDX SKIN TRANSILLUMINATOR."], Items);

        Assert.Equal("ledx", matches[0].Item.Id);
    }
}

/// <summary>
/// Finding things by typing what you can see.
///
/// <para>Escape from Tarkov's names are typeset rather than typed: a typographic apostrophe where
/// anyone searching uses a straight one. Matching literally meant a quest sitting in the list
/// could not be found by its own name, which reads as the quest being missing.</para>
/// </summary>
public class SearchTextTests
{
    [Theory]
    [InlineData("What's on the Flash Drive?", "What\u2019s on the Flash Drive?")]
    [InlineData("whats on the flash drive", "What\u2019s on the Flash Drive?")]
    [InlineData("You've Got Mail", "You\u2019ve Got Mail")]
    [InlineData("gunsmith part 1", "Gunsmith — Part 1")]
    public void A_typed_apostrophe_finds_a_typeset_one(string typed, string actual)
    {
        Assert.True(SearchText.Contains(actual, typed));
    }

    [Fact]
    public void Accents_do_not_have_to_be_typed()
    {
        Assert.True(SearchText.Contains("Kübel", "kubel"));
    }

    [Fact]
    public void An_empty_query_matches_nothing_rather_than_everything()
    {
        // Contains("") is true for every string, which would quietly return the whole list.
        Assert.False(SearchText.Contains("Debut", ""));
        Assert.False(SearchText.Contains("Debut", "   "));
    }

    [Fact]
    public void Unrelated_text_still_does_not_match()
    {
        Assert.False(SearchText.Contains("What\u2019s on the Flash Drive?", "shoreline"));
    }

    /// <summary>
    /// The reported case, verbatim: a compass in a backpack read as a golden neck chain.
    ///
    /// <para>Six cells beside it were labelled "GoldChain" and its own cell was truncated to
    /// "Compa". The one thing in the picture naming what the cursor was on was the game's
    /// tooltip.</para>
    /// </summary>
    [Fact]
    public void A_neighbouring_cells_abbreviation_does_not_beat_the_tooltip()
    {
        ItemDef[] items =
        [
            new() { Id = "compass", Name = "EYE MK.2 professional hand-held compass", ShortName = "Compass" },
            new() { Id = "chain", Name = "Golden neck chain", ShortName = "GoldChain" },
        ];

        var read = new[]
        {
            "GoldChain", "GoldChain", "GoldChain", "GoldChain", "GoldChain", "GoldChain",
            "Compa",
            "EYE MK.2 professional hand-held compass",
        };

        Assert.Equal("compass", ItemMatcher.Identify(read, items)[0].Item.Id);
    }

    /// <summary>An abbreviation on its own names nothing — it is what the cell says, and the cells
    /// around it say the same sort of thing.</summary>
    [Fact]
    public void An_abbreviation_alone_is_not_enough_to_identify_an_item()
    {
        ItemDef[] items =
            [new() { Id = "chain", Name = "Golden neck chain", ShortName = "GoldChain" }];

        Assert.Empty(ItemMatcher.Identify(["GoldChain"], items));
    }
}
