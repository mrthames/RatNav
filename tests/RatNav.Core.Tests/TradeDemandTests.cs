namespace RatNav.Core.Tests;

using RatNav.Core.Model;
using RatNav.Core.Tracking;

public class TradeDemandTests
{
    private static readonly BarterDef Dorm303 = new()
    {
        Id = "barter-303",
        TraderId = "therapist",
        TraderName = "Therapist",
        MinTraderLevel = 2,
        RequiredItems = [new BarterItem("plug", 7), new BarterItem("tape", 3)],
        OfferedItem = new BarterItem("key-303", 1),
    };

    private static readonly CraftDef Toolset = new()
    {
        Id = "craft-toolset",
        StationId = "workbench",
        StationName = "Workbench",
        StationLevel = 2,
        RequiredItems = [new BarterItem("plug", 2), new BarterItem("wrench", 1)],
        ProducedItem = new BarterItem("toolset", 1),
    };

    private static string? Names(string id) => id switch
    {
        "key-303" => "Dorm room 303 key",
        "toolset" => "Toolset",
        _ => null,
    };

    [Fact]
    public void A_chosen_barter_puts_its_inputs_on_the_list()
    {
        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter }],
            [Dorm303], [], Names);

        Assert.Equal(7, demand["plug"].Count);
        Assert.Equal(3, demand["tape"].Count);
    }

    [Fact]
    public void Nothing_chosen_wants_nothing()
    {
        Assert.Empty(TradeDemands.From([], [Dorm303], [Toolset], Names));
    }

    [Fact]
    public void Doing_a_trade_twice_wants_twice_the_inputs()
    {
        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter, Times = 2 }],
            [Dorm303], [], Names);

        Assert.Equal(14, demand["plug"].Count);
    }

    [Fact]
    public void Two_trades_wanting_the_same_item_add_up_and_both_say_why()
    {
        var demand = TradeDemands.From(
            [
                new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter },
                new TrackedTrade { Id = "craft-toolset", Kind = TradeKind.Craft },
            ],
            [Dorm303], [Toolset], Names);

        Assert.Equal(9, demand["plug"].Count);
        Assert.Equal(2, demand["plug"].For.Count);
    }

    [Fact]
    public void A_barter_is_named_by_its_trader_loyalty_and_what_it_hands_back()
    {
        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter }],
            [Dorm303], [], Names);

        Assert.Equal("Therapist LL2 · Dorm room 303 key", Assert.Single(demand["tape"].For));
    }

    [Fact]
    public void A_craft_is_named_by_its_station_level_and_what_it_makes()
    {
        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "craft-toolset", Kind = TradeKind.Craft }],
            [], [Toolset], Names);

        Assert.Equal("Workbench 2 · Toolset", Assert.Single(demand["wrench"].For));
    }

    /// <summary>Which subsection the item sits under. A barter anywhere in the reasons wins,
    /// because "you can buy this from a trader" is the more useful of the two things to know.</summary>
    [Fact]
    public void Craft_only_is_flagged_so_the_list_can_split_barter_from_crafting()
    {
        var crafting = TradeDemands.From(
            [new TrackedTrade { Id = "craft-toolset", Kind = TradeKind.Craft }],
            [], [Toolset], Names);

        var both = TradeDemands.From(
            [
                new TrackedTrade { Id = "craft-toolset", Kind = TradeKind.Craft },
                new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter },
            ],
            [Dorm303], [Toolset], Names);

        Assert.True(crafting["wrench"].CraftOnly);
        Assert.False(both["plug"].CraftOnly);
    }

    /// <summary>A patch removes a trade, or the cache predates one. It should cost a line on a
    /// list rather than the list.</summary>
    [Fact]
    public void A_trade_that_no_longer_exists_is_skipped_rather_than_throwing()
    {
        var demand = TradeDemands.From(
            [
                new TrackedTrade { Id = "gone", Kind = TradeKind.Barter },
                new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter },
            ],
            [Dorm303], [], Names);

        Assert.Equal(7, demand["plug"].Count);
    }

    /// <summary>Fractional counts are real data — tarkov.dev records currency-priced trades that
    /// way. You cannot carry four fifths of a stack, so what you need is the next whole one.</summary>
    [Fact]
    public void Fractional_counts_round_up()
    {
        var priced = Dorm303 with { RequiredItems = [new BarterItem("roubles", 155.1)] };

        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter }],
            [priced], [], Names);

        Assert.Equal(156, demand["roubles"].Count);
    }

    [Fact]
    public void An_unnamed_trade_still_reads_as_something()
    {
        var demand = TradeDemands.From(
            [new TrackedTrade { Id = "barter-303", Kind = TradeKind.Barter }],
            [Dorm303], [], _ => null);

        Assert.Equal("Therapist LL2 barter", Assert.Single(demand["plug"].For));
    }
}
