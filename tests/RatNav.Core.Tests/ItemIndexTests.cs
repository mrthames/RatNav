using RatNav.Core.Data;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

public class ItemIndexTests
{
    private static GameData Sample() => new()
    {
        FetchedAt = DateTimeOffset.UtcNow,
        Items =
        [
            new ItemDef { Id = "watch", Name = "Bronze pocket watch", ShortName = "Watch" },
            new ItemDef { Id = "bolts", Name = "Bolts", ShortName = "Bolts" },
            new ItemDef { Id = "key-dorm", Name = "Dorm room 114 key", ShortName = "Dorm 114" },
            new ItemDef { Id = "unloved", Name = "Chocolate bar", ShortName = "Choco" },
        ],
        Tasks =
        [
            new TaskDef
            {
                Id = "debut",
                Name = "Debut",
                TraderName = "Prapor",
                Objectives =
                [
                    new TaskObjective
                    {
                        Id = "debut-1",
                        Description = "Hand over the watch",
                        Items = [new ObjectiveItem { ItemId = "watch", Count = 1, FoundInRaid = true }],
                        NeededKeyItemIds = ["key-dorm"],
                    },
                ],
            },
            new TaskDef
            {
                Id = "checking",
                Name = "Checking",
                TraderName = "Prapor",
                Objectives =
                [
                    new TaskObjective
                    {
                        Id = "checking-1",
                        Description = "Hand over another watch",
                        Items = [new ObjectiveItem { ItemId = "watch", Count = 2, FoundInRaid = false }],
                    },
                ],
            },
        ],
        HideoutStations =
        [
            new HideoutStation
            {
                Id = "workbench",
                Name = "Workbench",
                Levels =
                [
                    new HideoutLevel
                    {
                        Id = "workbench-1",
                        Level = 1,
                        ItemRequirements = [new ObjectiveItem { ItemId = "bolts", Count = 4 }],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void Collects_every_quest_that_wants_an_item()
    {
        var needs = new ItemIndex(Sample()).GetNeeds("watch");

        Assert.NotNull(needs);
        Assert.Equal(2, needs.Quests.Count);
        Assert.Equal(3, needs.TotalNeeded);
        Assert.True(needs.AnyFoundInRaid);
        Assert.Contains(needs.Quests, q => q.TaskName == "Debut" && q.Count == 1);
    }

    [Fact]
    public void Tracks_hideout_requirements_separately_from_quests()
    {
        var needs = new ItemIndex(Sample()).GetNeeds("bolts");

        Assert.NotNull(needs);
        Assert.Empty(needs.Quests);
        Assert.Single(needs.Hideout);
        Assert.Equal("Workbench", needs.Hideout[0].StationName);
        Assert.Equal(4, needs.TotalNeeded);
    }

    [Fact]
    public void Keys_are_needed_without_being_turned_in()
    {
        var needs = new ItemIndex(Sample()).GetNeeds("key-dorm");

        Assert.NotNull(needs);
        Assert.Single(needs.AsKey);
        Assert.Empty(needs.Quests);

        // A key you must carry is not a key you must hand over, so it adds nothing to the
        // quantity you need to collect.
        Assert.Equal(0, needs.TotalNeeded);
        Assert.True(needs.IsNeeded);
    }

    [Fact]
    public void Items_nothing_wants_are_absent_from_the_index()
    {
        var index = new ItemIndex(Sample());

        Assert.Null(index.GetNeeds("unloved"));
        Assert.NotNull(index.GetItem("unloved"));
        Assert.Equal(3, index.NeededItemCount);
    }

    [Fact]
    public void An_item_a_quest_wants_but_we_cannot_resolve_still_gets_a_row()
    {
        var data = Sample() with
        {
            Tasks =
            [
                new TaskDef
                {
                    Id = "mystery",
                    Name = "Mystery",
                    Objectives =
                    [
                        new TaskObjective
                        {
                            Id = "mystery-1",
                            Description = "Hand over something we have no definition for",
                            Items = [new ObjectiveItem { ItemId = "ghost", Count = 1 }],
                        },
                    ],
                },
            ],
        };

        var needs = new ItemIndex(data).GetNeeds("ghost");

        Assert.NotNull(needs);
        Assert.Equal("Unknown item", needs.Item.Name);
        Assert.Single(needs.Quests);
    }

    [Theory]
    [InlineData("Watch", "watch")]         // exact short name — how players actually say it
    [InlineData("Bronze", "watch")]        // prefix of the full name
    [InlineData("pocket", "watch")]        // substring
    [InlineData("bolts", "bolts")]
    public void Search_puts_the_obvious_answer_first(string query, string expectedId)
    {
        var first = new ItemIndex(Sample()).Search(query).FirstOrDefault();

        Assert.NotNull(first);
        Assert.Equal(expectedId, first.Id);
    }

    [Fact]
    public void Search_returns_nothing_for_an_empty_query()
    {
        Assert.Empty(new ItemIndex(Sample()).Search("   "));
    }
}
