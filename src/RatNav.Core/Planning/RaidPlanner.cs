using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Planning;

/// <summary>One stop on a raid plan: an objective, where it is, and what it needs.</summary>
public sealed record Waypoint
{
    public required string ObjectiveId { get; init; }
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public required string Description { get; init; }
    public required GamePosition Position { get; init; }

    public string? TraderName { get; init; }
    public bool Optional { get; init; }

    /// <summary>Keys that open the way here. Any one of them will do.</summary>
    public IReadOnlyList<string> NeededKeyItemIds { get; init; } = [];

    /// <summary>Whose objective this is, once plans are merged. Null while planning alone.</summary>
    public string? Owner { get; init; }
}

/// <summary>A route through the objectives you chose, plus what to bring.</summary>
public sealed record RaidPlan
{
    public required string MapId { get; init; }
    public required string MapName { get; init; }

    /// <summary>Stops in the order to walk them.</summary>
    public required IReadOnlyList<Waypoint> Waypoints { get; init; }

    /// <summary>Every key any stop needs, so it can be checked against your stash before queueing.</summary>
    public IReadOnlyList<string> RequiredKeyItemIds { get; init; } = [];

    /// <summary>Total ground distance of the route in metres — the honest cost of the plan.</summary>
    public double DistanceMetres { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Orders the objectives you picked into a route worth walking.
///
/// <para>This is the travelling salesman problem, and a raid plan is small — a dozen stops at
/// most — so it is solved by nearest-neighbour followed by 2-opt. Nearest-neighbour alone
/// produces routes with an obvious crossing in them, which reads as broken even when the total
/// distance is respectable; 2-opt removes exactly those crossings, which is why it earns its
/// place here rather than being over-engineering.</para>
///
/// <para>Distance is straight-line on the ground plane. Tarkov has walls, so this underestimates
/// real walking distance — but the ordering it produces is right far more often than not, and a
/// route the player can drag to reorder beats a pathfinder that needs navmesh data nobody
/// publishes.</para>
/// </summary>
public static class RaidPlanner
{
    /// <summary>
    /// Builds a plan. When <paramref name="start"/> is known — the first position fix of a raid —
    /// the route begins from there; otherwise it starts at whichever objective makes the shortest
    /// overall loop.
    /// </summary>
    public static RaidPlan Plan(
        MapDef map,
        IReadOnlyList<Waypoint> chosen,
        GamePosition? start = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(chosen);

        var ordered = Order(chosen, start);

        return new RaidPlan
        {
            MapId = map.Id,
            MapName = map.Name,
            Waypoints = ordered,
            RequiredKeyItemIds =
            [
                .. ordered.SelectMany(w => w.NeededKeyItemIds).Distinct(StringComparer.OrdinalIgnoreCase)
            ],
            DistanceMetres = Length(ordered, start),
        };
    }

    /// <summary>
    /// Re-orders the stops you have not reached yet, starting from where you actually are.
    ///
    /// This is what runs on every position fix in a raid: the plan made before queueing assumed
    /// nothing about your spawn, and the first fix is the moment it can stop assuming.
    /// </summary>
    public static RaidPlan Reroute(RaidPlan plan, GamePosition from, IReadOnlySet<string> completed)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var remaining = plan.Waypoints.Where(w => !completed.Contains(w.ObjectiveId)).ToArray();
        var ordered = Order(remaining, from);

        return plan with
        {
            Waypoints = ordered,
            DistanceMetres = Length(ordered, from),
        };
    }

    private static IReadOnlyList<Waypoint> Order(IReadOnlyList<Waypoint> waypoints, GamePosition? start)
    {
        // Only a single stop is order-free. Two stops still need deciding when the start is
        // known — walking to the far one first and doubling back is exactly the mistake a plan
        // exists to prevent.
        if (waypoints.Count <= 1) return [.. waypoints];

        var route = NearestNeighbour(waypoints, start);
        return TwoOpt(route, start);
    }

    /// <summary>Greedy: from wherever you are, walk to the closest stop you have not made.</summary>
    private static List<Waypoint> NearestNeighbour(IReadOnlyList<Waypoint> waypoints, GamePosition? start)
    {
        var remaining = waypoints.ToList();
        var route = new List<Waypoint>(remaining.Count);

        // With no known start, begin at the stop furthest from the centre of the cluster. Starting
        // in the middle strands an outlier at the end and drags the route back across itself.
        var current = start ?? FurthestFromCentre(remaining);

        while (remaining.Count > 0)
        {
            var nextIndex = 0;
            var best = double.MaxValue;

            for (var i = 0; i < remaining.Count; i++)
            {
                var distance = CoordinateTransform.GroundDistance(current, remaining[i].Position);
                if (distance < best)
                {
                    best = distance;
                    nextIndex = i;
                }
            }

            current = remaining[nextIndex].Position;
            route.Add(remaining[nextIndex]);
            remaining.RemoveAt(nextIndex);
        }

        return route;
    }

    /// <summary>
    /// Repeatedly reverses any stretch of the route that crosses itself, until nothing improves.
    /// Bounded by a pass limit so a pathological set of stops cannot spin.
    /// </summary>
    private static List<Waypoint> TwoOpt(List<Waypoint> route, GamePosition? start)
    {
        const int maxPasses = 50;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var improved = false;

            for (var i = 0; i < route.Count - 1; i++)
            {
                for (var j = i + 1; j < route.Count; j++)
                {
                    var before = SegmentLength(route, start, i, j);
                    route.Reverse(i, j - i + 1);
                    var after = SegmentLength(route, start, i, j);

                    if (after < before - 1e-9) improved = true;
                    else route.Reverse(i, j - i + 1);   // no better: put it back
                }
            }

            if (!improved) return route;
        }

        return route;
    }

    /// <summary>Length of the two edges a reversal would change, which is all that can differ.</summary>
    private static double SegmentLength(List<Waypoint> route, GamePosition? start, int i, int j)
    {
        var total = 0.0;

        if (i == 0)
        {
            if (start is { } from) total += CoordinateTransform.GroundDistance(from, route[0].Position);
        }
        else
        {
            total += CoordinateTransform.GroundDistance(route[i - 1].Position, route[i].Position);
        }

        if (j + 1 < route.Count)
            total += CoordinateTransform.GroundDistance(route[j].Position, route[j + 1].Position);

        return total;
    }

    private static GamePosition FurthestFromCentre(IReadOnlyList<Waypoint> waypoints)
    {
        var centre = new GamePosition(
            waypoints.Average(w => w.Position.X), 0, waypoints.Average(w => w.Position.Z));

        return waypoints
            .OrderByDescending(w => CoordinateTransform.GroundDistance(centre, w.Position))
            .First().Position;
    }

    private static double Length(IReadOnlyList<Waypoint> route, GamePosition? start)
    {
        if (route.Count == 0) return 0;

        var total = 0.0;
        var previous = start ?? route[0].Position;

        foreach (var waypoint in route)
        {
            total += CoordinateTransform.GroundDistance(previous, waypoint.Position);
            previous = waypoint.Position;
        }

        return total;
    }
}
