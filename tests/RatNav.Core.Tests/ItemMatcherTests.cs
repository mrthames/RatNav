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
