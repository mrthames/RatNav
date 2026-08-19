using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Sharing;

namespace RatNav.Core.Tests;

public class PlanSharingTests
{
    private static readonly MapDef Customs = new() { Id = "customs", Name = "Customs" };

    private static Waypoint At(string id, double x, double z, params string[] keys) => new()
    {
        ObjectiveId = id,
        TaskId = $"task-{id}",
        TaskName = $"Task {id}",
        Description = $"Objective {id}",
        Position = new GamePosition(x, 0, z),
        NeededKeyItemIds = keys,
    };

    private static PlanDocument DocumentFor(string owner, params Waypoint[] stops) =>
        PlanDocument.From(RaidPlanner.Plan(Customs, stops), owner);

    [Fact]
    public void A_plan_survives_the_trip_to_a_file_and_back()
    {
        var plan = RaidPlanner.Plan(Customs, [At("a", 0, 0, "dorm-114"), At("b", 100, 50)]);
        var document = PlanDocument.From(plan, "justin", ["salewa"]);

        var restored = PlanDocument.FromJson(document.ToJson(), out var problem);

        Assert.Null(problem);
        Assert.NotNull(restored);
        Assert.Equal("justin", restored.Owner);
        Assert.Equal("customs", restored.MapId);
        Assert.Equal(2, restored.Stops.Count);
        Assert.Contains("dorm-114", restored.RequiredKeyItemIds);
        Assert.Contains("salewa", restored.ShoppingListItemIds);
    }

    [Fact]
    public void A_plan_from_a_newer_RatNav_is_refused_with_an_explanation()
    {
        // Half-reading a format we do not understand would produce a plan that looks fine and is
        // wrong, which is worse than declining.
        var plan = RaidPlanner.Plan(Customs, [At("a", 0, 0)]);
        var json = (PlanDocument.From(plan, "someone") with { Version = PlanDocument.CurrentVersion + 5 }).ToJson();

        var restored = PlanDocument.FromJson(json, out var problem);

        Assert.Null(restored);
        Assert.NotNull(problem);
        Assert.Contains("newer version", problem);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void Unreadable_files_are_reported_rather_than_thrown(string json)
    {
        Assert.Null(PlanDocument.FromJson(json, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void Merging_keeps_every_objective_and_says_whose_it_is()
    {
        var mine = DocumentFor("justin", At("a", 0, 0), At("b", 100, 0));
        var theirs = DocumentFor("the tester", At("c", 200, 0), At("d", 300, 0));

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        // Nothing dropped: the whole point is that neither player gives up their own raid.
        Assert.Equal(4, squad.Plan.Waypoints.Count);
        Assert.Equal(["justin", "the tester"], squad.Owners);
        Assert.All(squad.Plan.Waypoints, w => Assert.False(string.IsNullOrEmpty(w.Owner)));
    }

    [Fact]
    public void An_objective_you_both_picked_becomes_one_shared_stop()
    {
        var mine = DocumentFor("justin", At("shared", 50, 0), At("mine", 0, 0));
        var theirs = DocumentFor("the tester", At("shared", 50, 0), At("theirs", 200, 0));

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        // Three stops, not four: one pin, not two stacked on the same spot.
        Assert.Equal(3, squad.Plan.Waypoints.Count);

        var shared = Assert.Single(squad.Overlap.Shared);
        Assert.Equal("shared", shared.ObjectiveId);
        Assert.Equal(2, shared.Owners.Count);

        var stop = squad.Plan.Waypoints.Single(w => w.ObjectiveId == "shared");
        Assert.Contains("justin", stop.Owner);
        Assert.Contains("the tester", stop.Owner);
    }

    [Fact]
    public void Items_you_are_both_hunting_are_flagged()
    {
        var mine = PlanDocument.From(RaidPlanner.Plan(Customs, [At("a", 0, 0)]), "justin", ["salewa", "watch"]);
        var theirs = PlanDocument.From(RaidPlanner.Plan(Customs, [At("b", 100, 0)]), "the tester", ["salewa", "bolts"]);

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        var contested = Assert.Single(squad.Overlap.ContestedItems);
        Assert.Equal("salewa", contested.ItemId);
        Assert.Equal(["the tester", "justin"], contested.Owners);
    }

    [Fact]
    public void A_key_you_would_both_have_carried_names_one_carrier()
    {
        var mine = DocumentFor("justin", At("a", 0, 0, "dorm-114", "dorm-220"));
        var theirs = DocumentFor("the tester", At("b", 100, 0, "dorm-114"));

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        var redundant = Assert.Single(squad.Overlap.RedundantKeys);
        Assert.Equal("dorm-114", redundant.ItemId);
        Assert.Equal("the tester", redundant.Carrier);

        // The key only one player needed is not redundant and stays their problem.
        Assert.DoesNotContain(squad.Overlap.RedundantKeys, k => k.ItemId == "dorm-220");
    }

    [Fact]
    public void An_updated_plan_from_the_same_player_replaces_the_old_one()
    {
        var first = DocumentFor("the tester", At("old", 0, 0)) with { CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        var second = DocumentFor("the tester", At("new", 100, 0)) with { CreatedAt = DateTimeOffset.UtcNow };
        var mine = DocumentFor("justin", At("mine", 200, 0));

        var squad = PlanMerger.Merge(Customs, [mine, first, second]);

        // Re-merging after a buddy sends an update must be a merge, not a duplication.
        Assert.Equal(2, squad.Plan.Waypoints.Count);
        Assert.Contains(squad.Plan.Waypoints, w => w.ObjectiveId == "new");
        Assert.DoesNotContain(squad.Plan.Waypoints, w => w.ObjectiveId == "old");
    }

    [Fact]
    public void Merging_leaves_the_original_plans_untouched()
    {
        var mine = DocumentFor("justin", At("a", 0, 0));
        var theirs = DocumentFor("the tester", At("b", 100, 0));
        var before = mine.ToJson();

        PlanMerger.Merge(Customs, [mine, theirs]);

        Assert.Equal(before, mine.ToJson());
    }

    [Fact]
    public void A_merge_keeps_both_orders_with_yours_first()
    {
        // Your arrangement survives, and theirs follows in the order they arranged it. Re-sorting
        // the pair into a shorter route would throw away two people's decisions to save a walk.
        var mine = DocumentFor("justin", At("c", 200, 0), At("a", 0, 0));
        var theirs = DocumentFor("the tester", At("d", 300, 0), At("b", 100, 0));

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        Assert.Equal(["c", "a", "d", "b"], squad.Plan.Waypoints.Select(w => w.ObjectiveId));
    }

    [Fact]
    public void An_objective_you_both_picked_keeps_your_place_for_it()
    {
        // The same stop, not two — and it stays where you put it. Moving it to where they put it
        // would rearrange your plan on their say-so.
        var mine = DocumentFor("justin", At("a", 0, 0), At("shared", 50, 0));
        var theirs = DocumentFor("the tester", At("shared", 50, 0), At("b", 100, 0));

        var squad = PlanMerger.Merge(Customs, [mine, theirs]);

        Assert.Equal(["a", "shared", "b"], squad.Plan.Waypoints.Select(w => w.ObjectiveId));
    }

    [Fact]
    public void Merging_nothing_is_an_error_rather_than_an_empty_plan()
    {
        Assert.Throws<ArgumentException>(() => PlanMerger.Merge(Customs, []));
    }
}
