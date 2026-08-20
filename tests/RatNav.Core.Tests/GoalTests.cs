namespace RatNav.Core.Tests;

using RatNav.Core.Tracking;

/// <summary>
/// Goals you name yourself, which replaced a searchable catalogue of every barter in the game.
/// Picking one out of 789 meant knowing which of four Dorm 303 trades you meant; naming it "the
/// document case" does not.
/// </summary>
public class GoalTests
{
    private static Goal Goal(string name, params (string Item, int Count)[] items) => new()
    {
        Id = name,
        Name = name,
        Items = [.. items.Select(i => new GoalItem(i.Item, i.Count))],
    };

    [Fact]
    public void A_goal_puts_its_items_on_the_list()
    {
        var demand = GoalDemands.From([Goal("Document case", ("plug", 7), ("tape", 3))]);

        Assert.Equal(7, demand["plug"].Count);
        Assert.Equal(3, demand["tape"].Count);
    }

    [Fact]
    public void No_goals_want_nothing()
    {
        Assert.Empty(GoalDemands.From([]));
    }

    [Fact]
    public void Doing_a_goal_twice_wants_twice_the_items()
    {
        var demand = GoalDemands.From([Goal("Document case", ("plug", 7)) with { Times = 2 }]);

        Assert.Equal(14, demand["plug"].Count);
    }

    [Fact]
    public void Two_goals_wanting_the_same_item_add_up_and_both_say_why()
    {
        var demand = GoalDemands.From([
            Goal("Document case", ("plug", 7)),
            Goal("Toolset", ("plug", 2)),
        ]);

        Assert.Equal(9, demand["plug"].Count);
        Assert.Equal(["Document case", "Toolset"], demand["plug"].For);
    }

    [Fact]
    public void An_item_with_no_id_or_no_count_is_ignored()
    {
        Assert.Empty(GoalDemands.From([Goal("Nonsense", ("", 4), ("plug", 0))]));
    }
}

public class GoalStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ratnav-goals-" + Guid.NewGuid().ToString("n"));

    private ItemTracker New()
    {
        var tracker = new ItemTracker(_directory);
        tracker.Load();

        return tracker;
    }

    [Fact]
    public void A_goal_survives_a_restart()
    {
        New().SaveGoal(null, "Document case", [new GoalItem("plug", 7)]);

        var goal = Assert.Single(New().Goals);

        Assert.Equal("Document case", goal.Name);
        Assert.Equal(7, Assert.Single(goal.Items).Count);
    }

    [Fact]
    public void Saving_with_the_same_id_replaces_rather_than_duplicating()
    {
        var tracker = New();
        var first = tracker.SaveGoal(null, "Document case", [new GoalItem("plug", 7)]);

        tracker.SaveGoal(first.Id, "Document case", [new GoalItem("plug", 9)]);

        var goal = Assert.Single(tracker.Goals);

        Assert.Equal(9, Assert.Single(goal.Items).Count);
    }

    /// <summary>An unnamed goal is a list of items with no reason attached, which is the one thing
    /// this exists to avoid.</summary>
    [Fact]
    public void A_blank_name_becomes_something_readable()
    {
        Assert.Equal("unnamed", New().SaveGoal(null, "  ", [new GoalItem("plug", 1)]).Name);
    }

    [Fact]
    public void Removing_takes_it_off_the_list_and_off_disk()
    {
        var tracker = New();
        var goal = tracker.SaveGoal(null, "Document case", [new GoalItem("plug", 7)]);

        Assert.True(tracker.RemoveGoal(goal.Id));
        Assert.Empty(New().Goals);
    }

    [Fact]
    public void Removing_something_that_is_not_there_says_so_rather_than_throwing()
    {
        Assert.False(New().RemoveGoal("nothing"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
