using RatNav.Core.Maps;
using RatNav.Core.Model;
using RatNav.Core.Planning;

namespace RatNav.Core.Tests;

public class RaidPlannerTests
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

    private static double Length(RaidPlan plan, GamePosition? start = null)
    {
        var total = 0.0;
        var previous = start ?? plan.Waypoints[0].Position;
        foreach (var w in plan.Waypoints)
        {
            total += CoordinateTransform.GroundDistance(previous, w.Position);
            previous = w.Position;
        }
        return total;
    }

    [Fact]
    public void A_route_along_a_line_comes_out_in_order()
    {
        // Handed to the planner shuffled; there is only one sensible answer.
        var plan = RaidPlanner.Plan(Customs, [At("c", 200, 0), At("a", 0, 0), At("d", 300, 0), At("b", 100, 0)]);

        Assert.Equal(["a", "b", "c", "d"], plan.Waypoints.Select(w => w.ObjectiveId));
    }

    [Fact]
    public void A_known_start_position_anchors_the_route()
    {
        var stops = new[] { At("far", 300, 0), At("near", 10, 0), At("middle", 150, 0) };

        var fromWest = RaidPlanner.Plan(Customs, stops, new GamePosition(0, 0, 0));
        Assert.Equal("near", fromWest.Waypoints[0].ObjectiveId);

        // Spawn at the other end and the whole plan flips, which is the point of re-planning on
        // the first position fix rather than assuming a spawn.
        var fromEast = RaidPlanner.Plan(Customs, stops, new GamePosition(320, 0, 0));
        Assert.Equal("far", fromEast.Waypoints[0].ObjectiveId);
    }

    [Fact]
    public void Two_opt_removes_a_crossing_that_nearest_neighbour_leaves_behind()
    {
        // A square. Greedy nearest-neighbour from a corner produces a bow-tie across the
        // diagonal; the perimeter is shorter and is what a player would actually walk.
        var square = new[] { At("nw", 0, 100), At("ne", 100, 100), At("se", 100, 0), At("sw", 0, 0) };

        var plan = RaidPlanner.Plan(Customs, square, new GamePosition(0, 0, 0));

        // Perimeter of three sides is 300; any route with a crossing exceeds that.
        Assert.True(Length(plan, new GamePosition(0, 0, 0)) <= 300.0001,
            $"route was {Length(plan, new GamePosition(0, 0, 0)):F1}m, expected no more than 300m");
    }

    [Fact]
    public void The_reported_distance_matches_the_route_it_describes()
    {
        var start = new GamePosition(0, 0, 0);
        var plan = RaidPlanner.Plan(Customs, [At("a", 0, 30), At("b", 40, 30)], start);

        Assert.Equal(70, plan.DistanceMetres, 3);
    }

    [Fact]
    public void Keys_are_gathered_from_every_stop_without_duplicates()
    {
        var plan = RaidPlanner.Plan(Customs,
        [
            At("a", 0, 0, "dorm-114"),
            At("b", 50, 0, "dorm-114", "dorm-220"),
            At("c", 100, 0),
        ]);

        Assert.Equal(2, plan.RequiredKeyItemIds.Count);
        Assert.Contains("dorm-114", plan.RequiredKeyItemIds);
        Assert.Contains("dorm-220", plan.RequiredKeyItemIds);
    }

    [Fact]
    public void Rerouting_drops_what_you_have_done_and_starts_from_where_you_are()
    {
        var plan = RaidPlanner.Plan(Customs, [At("a", 0, 0), At("b", 100, 0), At("c", 200, 0)]);

        // Cleared "a", and standing nearer "c" than "b".
        var rerouted = RaidPlanner.Reroute(plan, new GamePosition(190, 0, 0), new HashSet<string> { "a" });

        Assert.Equal(["c", "b"], rerouted.Waypoints.Select(w => w.ObjectiveId));
        Assert.Equal("customs", rerouted.MapId);
    }

    [Fact]
    public void Rerouting_after_the_last_stop_leaves_an_empty_route_rather_than_failing()
    {
        var plan = RaidPlanner.Plan(Customs, [At("a", 0, 0), At("b", 100, 0)]);

        var done = RaidPlanner.Reroute(plan, new GamePosition(100, 0, 0), new HashSet<string> { "a", "b" });

        Assert.Empty(done.Waypoints);
        Assert.Equal(0, done.DistanceMetres);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Small_plans_are_handled_without_special_cases(int count)
    {
        var stops = Enumerable.Range(0, count).Select(i => At($"s{i}", i * 100, 0)).ToArray();

        var plan = RaidPlanner.Plan(Customs, stops);

        Assert.Equal(count, plan.Waypoints.Count);
    }

    [Fact]
    public void Stops_at_the_same_spot_do_not_confuse_the_route()
    {
        // Common in the real data: several objectives share one zone, so several waypoints sit
        // on identical coordinates. Customs really does return three objectives at 20.3%, 42.3%.
        var plan = RaidPlanner.Plan(Customs,
        [
            At("a", 20.3, 42.3), At("b", 20.3, 42.3), At("c", 20.3, 42.3), At("d", 200, 200),
        ]);

        Assert.Equal(4, plan.Waypoints.Count);

        // The three co-located stops must be walked together rather than being revisited.
        var together = plan.Waypoints.Select(w => w.ObjectiveId).ToList();
        var indexes = new[] { "a", "b", "c" }.Select(id => together.IndexOf(id)).OrderBy(i => i).ToArray();
        Assert.Equal(indexes[0] + 1, indexes[1]);
        Assert.Equal(indexes[1] + 1, indexes[2]);

        // And the route costs one crossing, not several.
        Assert.Equal(CoordinateTransform.GroundDistance(
            new GamePosition(20.3, 0, 42.3), new GamePosition(200, 0, 200)), plan.DistanceMetres, 3);
    }

    [Fact]
    public void A_route_never_visits_a_stop_twice_or_loses_one()
    {
        var stops = new[]
        {
            At("a", 0, 0), At("b", 130, 20), At("c", 40, 90), At("d", 200, 10),
            At("e", 70, 160), At("f", 10, 45), At("g", 180, 140),
        };

        var plan = RaidPlanner.Plan(Customs, stops, new GamePosition(0, 0, 0));

        Assert.Equal(stops.Length, plan.Waypoints.Count);
        Assert.Equal(
            stops.Select(s => s.ObjectiveId).OrderBy(id => id),
            plan.Waypoints.Select(w => w.ObjectiveId).OrderBy(id => id));
    }

    [Fact]
    public void Optimisation_beats_the_order_it_was_given()
    {
        // Deliberately awful input order: alternating ends of a line.
        var stops = new[] { At("a", 0, 0), At("f", 500, 0), At("b", 100, 0), At("e", 400, 0), At("c", 200, 0) };
        var start = new GamePosition(0, 0, 0);

        var plan = RaidPlanner.Plan(Customs, stops, start);

        var asGiven = 0.0;
        var previous = start;
        foreach (var s in stops)
        {
            asGiven += CoordinateTransform.GroundDistance(previous, s.Position);
            previous = s.Position;
        }

        Assert.True(plan.DistanceMetres < asGiven,
            $"planned {plan.DistanceMetres:F0}m was not better than the given order's {asGiven:F0}m");
        Assert.Equal(500, plan.DistanceMetres, 3);
    }
}
