namespace RatNav.Core.Tests;

using RatNav.Core.Tracking;

/// <summary>
/// The card you read standing over loot. Every one of these is about what it refuses to say as
/// much as what it says.
/// </summary>
public class LootVerdictTests
{
    private static ItemVerdict For(
        (int, string?)? quest = null,
        (int, string?)? hideout = null,
        (int, string?)? trade = null,
        (int Target, int Have)? watch = null,
        bool foundInRaid = false,
        int otherQuests = 0,
        int otherBarters = 0) =>
        LootVerdict.For(
            quest ?? (0, null),
            hideout ?? (0, null),
            trade ?? (0, null),
            watch,
            foundInRaid,
            otherQuests,
            otherBarters);

    [Fact]
    public void Nothing_wanting_it_says_so_rather_than_going_blank()
    {
        var verdict = For();

        Assert.Equal("Leave it", verdict.Headline);
        Assert.Equal(VerdictWeight.Ignore, verdict.Weight);
        Assert.Contains(verdict.Lines, l => l.Text.Contains("Nothing you are working on"));
    }

    [Fact]
    public void An_active_quest_is_a_reason_to_keep_it()
    {
        var verdict = For(quest: (3, "Gunsmith Part 4"));

        Assert.Equal("Keep", verdict.Headline);
        Assert.Contains(verdict.Lines, l => l.Text == "QUEST  3 for Gunsmith Part 4");
    }

    /// <summary>Found-in-raid is the one thing you cannot buy your way out of later, so it leads.</summary>
    [Fact]
    public void Found_in_raid_is_called_out_above_everything_else()
    {
        var verdict = For(quest: (1, "Shortage"), foundInRaid: true);

        Assert.Equal("Keep — found in raid", verdict.Headline);
        Assert.Equal(VerdictWeight.Critical, verdict.Weight);
        Assert.Contains("found in raid", verdict.Lines[0].Text);
    }

    [Fact]
    public void The_hideout_names_the_station_and_level()
    {
        var verdict = For(hideout: (4, "Medstation 3"));

        Assert.Contains(verdict.Lines, l => l.Text == "HIDEOUT  4 for Medstation 3");
    }

    [Fact]
    public void A_tracked_trade_counts_as_something_you_are_working_on()
    {
        var verdict = For(trade: (7, "Therapist LL2 · Dorm room 303 key"));

        Assert.Equal("Keep", verdict.Headline);
        Assert.Contains(verdict.Lines, l => l.Text.StartsWith("TRADE  7 for Therapist"));
    }

    /// <summary>Holding one, what you want to know is whether you still need it.</summary>
    [Fact]
    public void The_watchlist_is_said_as_progress_not_as_a_target()
    {
        Assert.Contains(
            For(watch: (60, 11)).Lines,
            l => l.Text == "WATCHLIST  49 more (11 of 60)");
    }

    [Fact]
    public void A_finished_watchlist_entry_stops_arguing_for_keeping_it()
    {
        var verdict = For(watch: (5, 5));

        Assert.Equal("Leave it", verdict.Headline);
        Assert.Contains(verdict.Lines, l => l.Text == "WATCHLIST  you have all 5");
    }

    /// <summary>
    /// The whole point. A common item appears in a dozen barters and half the quest tree, and
    /// reciting them is how the old card answered — by listing, when the question was one glance.
    /// </summary>
    [Fact]
    public void Everything_else_is_counted_rather_than_listed()
    {
        var verdict = For(otherQuests: 4, otherBarters: 11);

        var also = Assert.Single(verdict.Lines, l => l.Text.StartsWith("ALSO"));

        Assert.Equal("ALSO  4 quests you have not started, 11 barters", also.Text);
    }

    [Fact]
    public void One_of_something_reads_as_one_of_something()
    {
        var verdict = For(otherQuests: 1, otherBarters: 1);

        Assert.Contains(verdict.Lines, l => l.Text == "ALSO  1 quest you have not started, 1 barter");
    }

    /// <summary>
    /// Background reasons alone are not a reason to carry something out. They change the headline,
    /// because "nothing at all" and "nothing yet" are different answers.
    /// </summary>
    [Fact]
    public void Background_reasons_alone_do_not_make_it_worth_keeping()
    {
        var verdict = For(otherQuests: 3);

        Assert.Equal("Not now", verdict.Headline);
        Assert.Equal(VerdictWeight.Ignore, verdict.Weight);
    }

    [Fact]
    public void Reasons_come_strongest_first()
    {
        var verdict = For(
            quest: (2, "Debut"),
            hideout: (1, "Workbench 2"),
            trade: (3, "Skier LL1 · a thing"),
            watch: (9, 0),
            otherBarters: 5);

        Assert.Collection(
            verdict.Lines.Select(l => l.Text.Split("  ")[0]),
            first => Assert.Equal("QUEST", first),
            second => Assert.Equal("HIDEOUT", second),
            third => Assert.Equal("TRADE", third),
            fourth => Assert.Equal("WATCHLIST", fourth),
            fifth => Assert.Equal("ALSO", fifth));
    }
}
