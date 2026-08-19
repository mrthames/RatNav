using RatNav.Core.Maps;
using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Progress;
using RatNav.Core.Watchers;

namespace RatNav.Service;

/// <summary>What the overlay shows while you are in a raid.</summary>
public sealed record RaidView
{
    public bool InRaid { get; init; }
    public string? MapId { get; init; }
    public string? MapName { get; init; }

    /// <summary>Where you were at your last position fix, as a fraction of the map image.</summary>
    public double? X { get; init; }
    public double? Y { get; init; }

    /// <summary>Facing, already turned into image space so a cone can be drawn without maths.</summary>
    public double? HeadingDegrees { get; init; }

    /// <summary>When the fix was taken, so the UI can say how much to trust it.</summary>
    public DateTimeOffset? FixedAt { get; init; }

    /// <summary>The floor the fix puts you on, chosen from the map's height bands.</summary>
    public string? Floor { get; init; }

    public IReadOnlyList<RaidStop> Stops { get; init; } = [];
    public IReadOnlyList<string> CompletedObjectiveIds { get; init; } = [];

    /// <summary>Distance and direction to the next stop — what the HUD actually reads out.</summary>
    public string? NextStopName { get; init; }
    public double? NextStopMetres { get; init; }
    public double? NextStopRelativeBearing { get; init; }

    /// <summary>Past fixes, for a breadcrumb trail.</summary>
    public IReadOnlyList<Breadcrumb> Trail { get; init; } = [];
}

public sealed record RaidStop
{
    public required string ObjectiveId { get; init; }
    public required string TaskName { get; init; }
    public required string Description { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public string? Owner { get; init; }
    public string? Place { get; init; }
    public bool Done { get; init; }
}

public readonly record struct Breadcrumb(double X, double Y);

/// <summary>
/// The live state of a raid: which map, where you are, and what is left of the plan.
///
/// <para>This is where the watchers meet the plan. A raid start names the map and loads its plan;
/// a position fix snaps the marker and re-orders the stops you have not reached from where you
/// actually are. Everything else — the compact overlay, the expanded panel, the browser — reads
/// this one object, which is what keeps the three surfaces from disagreeing.</para>
///
/// <para>Nothing here polls or animates. State changes when the game says something happened or
/// when the player takes a fix, and not otherwise.</para>
/// </summary>
public sealed class RaidSession
{
    private readonly RatNavState _state;
    private readonly ProgressStore _progress;
    private readonly object _gate = new();

    private readonly List<Breadcrumb> _trail = [];
    private readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);

    private RaidPlan? _plan;
    private MapDef? _map;
    private PositionFix? _fix;

    /// <summary>Raised whenever the view changes, so the surfaces can be pushed rather than poll.</summary>
    public event EventHandler<RaidView>? Changed;

    public RaidSession(RatNavState state, ProgressStore progress)
    {
        _state = state;
        _progress = progress;
    }

    /// <summary>The game loaded a map. Its own name for it — "bigmap" — is what we match on.</summary>
    public void OnRaidStarted(string locationId)
    {
        var map = _state.Cache.Current?.Maps.FirstOrDefault(m =>
            m.LogAliases.Contains(locationId, StringComparer.OrdinalIgnoreCase));

        lock (_gate)
        {
            _map = map;
            _fix = null;
            _trail.Clear();
            _completed.Clear();
        }

        Publish();
    }

    /// <summary>A quest changed according to the game. Recorded under any manual correction.</summary>
    public void OnQuestChanged(QuestEvent change)
    {
        _progress.RecordFromLogs(change.TaskId, change.State);
        Publish();
    }

    /// <summary>
    /// A new position fix. This is the whole navigation loop: snap the marker, then re-order the
    /// stops you have not reached from where you now are — which is what makes the first fix after
    /// spawn rebuild the plan around where you actually spawned.
    /// </summary>
    public void OnPositionFixed(PositionFix fix)
    {
        lock (_gate)
        {
            _fix = fix;

            if (_map?.Image is { } image)
            {
                var point = new CoordinateTransform(image).ToNormalized(fix.Position);
                _trail.Add(new Breadcrumb(point.X, point.Y));

                // A trail is context, not history. Older marks stop helping and start crowding.
                if (_trail.Count > 24) _trail.RemoveAt(0);
            }

            if (_plan is not null)
                _plan = RaidPlanner.Reroute(_plan, fix.Position, _completed);
        }

        Publish();
    }

    /// <summary>Activates a plan, which the overlay then follows.</summary>
    public void UsePlan(RaidPlan plan, MapDef map)
    {
        lock (_gate)
        {
            _plan = plan;
            _map = map;
            _completed.Clear();
        }

        Publish();
    }

    /// <summary>Ticks a stop off, and re-plans what is left around it.</summary>
    public void Complete(string objectiveId, bool done = true)
    {
        lock (_gate)
        {
            if (done) _completed.Add(objectiveId);
            else _completed.Remove(objectiveId);

            if (_plan is not null && _fix is not null)
                _plan = RaidPlanner.Reroute(_plan, _fix.Position, _completed);
        }

        Publish();
    }

    public RaidView View()
    {
        lock (_gate)
        {
            if (_map?.Image is not { } image)
                return new RaidView { InRaid = false };

            var transform = new CoordinateTransform(image);
            var stops = new List<RaidStop>();

            foreach (var waypoint in _plan?.Waypoints ?? [])
            {
                var point = transform.ToNormalized(waypoint.Position);

                stops.Add(new RaidStop
                {
                    ObjectiveId = waypoint.ObjectiveId,
                    TaskName = waypoint.TaskName,
                    Description = waypoint.Description,
                    X = point.X,
                    Y = point.Y,
                    Owner = waypoint.Owner,
                    Place = _map.NearestLabel(waypoint.Position)?.Text,
                    Done = _completed.Contains(waypoint.ObjectiveId),
                });
            }

            var next = _plan?.Waypoints.FirstOrDefault(w => !_completed.Contains(w.ObjectiveId));
            var here = _fix is null ? (MapPoint?)null : transform.ToNormalized(_fix.Position);

            return new RaidView
            {
                InRaid = true,
                MapId = _map.Id,
                MapName = _map.Name,
                X = here?.X,
                Y = here?.Y,
                HeadingDegrees = _fix is null ? null : transform.ToImageHeading(_fix.HeadingDegrees),
                FixedAt = _fix?.TakenAt,
                Floor = _fix is null ? image.DefaultFloor : _map.FloorAt(_fix.Position.Y)?.Layer ?? image.DefaultFloor,
                Stops = stops,
                CompletedObjectiveIds = [.. _completed],
                Trail = [.. _trail],
                NextStopName = next is null ? null : _map.NearestLabel(next.Position)?.Text ?? next.TaskName,
                NextStopMetres = next is null || _fix is null
                    ? null
                    : CoordinateTransform.GroundDistance(_fix.Position, next.Position),
                NextStopRelativeBearing = next is null || _fix is null
                    ? null
                    : CoordinateTransform.RelativeBearing(
                        _fix.HeadingDegrees,
                        CoordinateTransform.BearingTo(_fix.Position, next.Position)),
            };
        }
    }

    private void Publish() => Changed?.Invoke(this, View());
}
