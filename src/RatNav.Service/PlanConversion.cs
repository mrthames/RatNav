using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Sharing;

namespace RatNav.Service;

/// <summary>
/// Turns a stored plan document into a routed plan.
///
/// <para>Shared rather than private to the API, because a plan is activated from two places: when
/// you press Start raid, and when RatNav starts and puts back the one you had open. Two copies of
/// this would eventually disagree about what a restored plan looks like.</para>
/// </summary>
public static class PlanConversion
{
    /// <summary>
    /// Rebuilds a route from a shared document.
    ///
    /// Positions travel with the document so a route can be drawn even for an objective this copy
    /// of the game data no longer knows about — but names are resolved from local data, so a plan
    /// shows what the quests are called <i>now</i> rather than what they were called when it was
    /// made, and a plan from a friend reads in your own language.
    /// </summary>
    public static RaidPlan ToPlan(PlanDocument document, MapDef map, GameData? data)
    {
        var tasks = (data?.Tasks ?? []).ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        var objectives = (data?.Tasks ?? [])
            .SelectMany(t => t.Objectives)
            .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return RaidPlanner.Plan(map,
        [
            .. document.Stops.Select(s =>
            {
                var task = tasks.GetValueOrDefault(s.TaskId);
                var objective = objectives.GetValueOrDefault(s.ObjectiveId);

                return new Waypoint
                {
                    ObjectiveId = s.ObjectiveId,
                    TaskId = s.TaskId,
                    TaskName = task?.Name ?? "(unknown quest)",
                    Description = objective?.Description ?? "",
                    Position = new GamePosition(s.X, s.Y, s.Z),
                    TraderName = task?.TraderName,
                    Owner = s.Owner,
                    NeededKeyItemIds = s.NeededKeyItemIds,
                };
            })
        ]);
    }
}
