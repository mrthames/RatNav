namespace RatNav.Core.Tests;

using RatNav.Core.Maps;

/// <summary>
/// Reading the game's own extraction list.
///
/// <para>The rows here are what a real Customs raid showed, in the order it showed them, so what
/// is asserted is the shape the game actually produces rather than the shape it was assumed to
/// produce.</para>
/// </summary>
public class ExtractListTests
{
    /// <summary>The panel as read off a 4K screenshot, heading and raid clock included.</summary>
    private static readonly string[] Customs =
    [
        "Find an extraction point",
        "0:31:19",
        "EXFIL01 ZB-013",
        "EXFIL02 Dorms V-Ex ??:??:??",
        "EXFIL03 ZB-1011",
        "EXFIL04 Old Gas Station ??:??:??",
        "EXFIL05 Railroad Passage (Flare)",
        "TRANSIT01 Transit to Reserve",
        "TRANSIT02 Transit to Factory",
        "TRANSIT03 Transit to Interchange",
        "TRANSIT03 Transit to Shoreline",
    ];

    [Fact]
    public void The_heading_and_the_raid_clock_are_not_rows()
    {
        var rows = ExtractList.Read(Customs);

        Assert.DoesNotContain(rows, r => r.Name.Contains("extraction point"));
        Assert.DoesNotContain(rows, r => r.Name.Contains("31"));
    }

    [Fact]
    public void The_id_is_stripped_and_the_name_kept()
    {
        var rows = ExtractList.Read(Customs);

        Assert.Contains(rows, r => r.Name == "ZB-013");
        Assert.Contains(rows, r => r.Name == "Railroad Passage (Flare)");
    }

    [Fact]
    public void A_row_with_no_time_is_open()
    {
        var rows = ExtractList.Read(Customs);

        Assert.Equal(ExtractRowKind.Open, rows.Single(r => r.Name == "ZB-013").Kind);
        Assert.Equal(ExtractRowKind.Open, rows.Single(r => r.Name == "ZB-1011").Kind);
    }

    /// <summary>Still a way out — the condition might be one you can meet.</summary>
    [Fact]
    public void Question_marks_mean_conditional_rather_than_gone()
    {
        var rows = ExtractList.Read(Customs);

        Assert.Equal(ExtractRowKind.Conditional, rows.Single(r => r.Name == "Dorms V-Ex").Kind);
        Assert.Equal(ExtractRowKind.Conditional, rows.Single(r => r.Name == "Old Gas Station").Kind);
    }

    /// <summary>A transit leaves for another map. It is not a way to end the raid.</summary>
    [Fact]
    public void Transits_are_marked_as_transits()
    {
        var rows = ExtractList.Read(Customs);
        var transits = rows.Where(r => r.Kind == ExtractRowKind.Transit).ToList();

        Assert.Equal(4, transits.Count);
        Assert.All(transits, t => Assert.StartsWith("Transit to", t.Name));
    }

    /// <summary>The game repeats TRANSIT03, so the id cannot be treated as unique.</summary>
    [Fact]
    public void A_repeated_id_still_yields_both_rows()
    {
        var rows = ExtractList.Read(Customs);

        Assert.Contains(rows, r => r.Name == "Transit to Interchange");
        Assert.Contains(rows, r => r.Name == "Transit to Shoreline");
    }

    [Fact]
    public void A_real_countdown_is_a_time_and_not_a_name()
    {
        var rows = ExtractList.Read(["EXFIL01 Smugglers' Boat 04:12"]);

        Assert.Equal("Smugglers' Boat", rows.Single().Name);
        Assert.Equal(ExtractRowKind.Open, rows.Single().Kind);
    }

    /// <summary>OCR trades zero for O and one for I, and a row is worth keeping either way.</summary>
    [Theory]
    [InlineData("EXFILO1 ZB-013")]
    [InlineData("EXF1L01 ZB-013")]
    [InlineData("EXFIL01: ZB-013")]
    public void A_misread_id_still_reads_as_a_row(string line) =>
        Assert.Equal("ZB-013", ExtractList.Read([line]).Single().Name);

    [Fact]
    public void Nothing_readable_is_no_rows_rather_than_an_error()
    {
        Assert.Empty(ExtractList.Read([]));
        Assert.Empty(ExtractList.Read(["", "   ", "some unrelated text"]));
    }
}
