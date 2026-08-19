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

    /// <summary>True when a plan is loaded, whether or not you are in the raid it was built for.</summary>
    public bool HasPlan { get; init; }

    /// <summary>
    /// True when there is a map to draw, raid or no raid.
    ///
    /// <para>Distinct from <see cref="InRaid"/> on purpose: a map picked by hand is worth drawing,
    /// and a raid is worth reporting, and conflating them left the overlay blank whenever you had
    /// one without the other.</para>
    /// </summary>
    public bool HasMap { get; init; }

    /// <summary>Set when the active plan is for a different map than the one on screen.</summary>
    public string? PlanMapName { get; init; }

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

    /// <summary>The quest this serves, so finished stops can be grouped into "ready to turn in".</summary>
    public required string TaskId { get; init; }

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
    private PositionFix? _fix;

    /// <summary>
    /// The game's own name for the map — "TarkovStreets". Kept rather than the resolved map,
    /// because a raid can start before the game data finishes loading: the watchers begin
    /// immediately and the first refresh takes a moment. Resolving on demand means the map appears
    /// as soon as the data does, instead of the session being permanently stuck on "no raid".
    /// </summary>
    private string? _locationId;

    /// <summary>The map a plan was activated for, when one was chosen by hand.</summary>
    private MapDef? _chosenMap;

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
        lock (_gate)
        {
            // Re-entering the same map is not a new raid — the log is scanned on startup, and
            // treating that as a fresh raid would wipe the fix the player just took.
            if (string.Equals(locationId, _locationId, StringComparison.OrdinalIgnoreCase)) return;

            _locationId = locationId;
            _chosenMap = null;
            _fix = null;
            _trail.Clear();
            _completed.Clear();
        }

        Publish();
    }

    /// <summary>
    /// The raid is over — extracted, killed, or backed out of.
    ///
    /// <para><b>What you finished is banked before anything is cleared.</b> Objectives ticked off
    /// mid-raid are the whole record of what the raid was for, and losing them on the way back to
    /// the menu would mean re-walking to a stop you already cleared. They go into the progress
    /// store, which outlives the session.</para>
    ///
    /// <para>Everything else raid-scoped goes: the map, the fix, the trail, and the plan. The
    /// overlay returns to idle rather than showing a route through a raid that has ended.</para>
    /// </summary>
    public void OnRaidEnded()
    {
        string[] finished;

        lock (_gate)
        {
            finished = [.. _completed];

            // Only what belonged to the raid: which map the game had loaded, where you were, and
            // the trail behind you.
            _locationId = null;
            _fix = null;
            _trail.Clear();
        }

        foreach (var objectiveId in finished)
            _progress.CompleteObjective(objectiveId);

        Publish();
    }

    /// <summary>The map the game currently has loaded, or null when not in a raid.</summary>
    private MapDef? RaidMap =>
        _locationId is null
            ? null
            : _state.Cache.Current?.Maps.FirstOrDefault(m =>
                m.LogAliases.Contains(_locationId, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The map to draw.
    ///
    /// <para>In a raid the game wins, always — if you planned Customs and queued Streets, the
    /// overlay must show Streets, and the plan simply does not apply. Out of a raid it falls back
    /// to whatever the active plan is for, so the plan is still there to look at and edit between
    /// raids rather than vanishing the moment you extract.</para>
    /// </summary>
    private MapDef? Map => RaidMap ?? _chosenMap;

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

            if (Map?.Image is { } image)
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

    /// <summary>
    /// Shows a map without a plan.
    ///
    /// <para>The overlay used to need a raid or a plan before it would draw anything, which left
    /// nothing on screen in the two places you most want a map: loading into a raid before the
    /// game has written which map it is, and looking one over between raids. Neither needs
    /// objectives — sometimes you just want to see the map.</para>
    /// </summary>
    public void ShowMap(MapDef map)
    {
        lock (_gate) _chosenMap = map;
        Publish();
    }

    /// <summary>Activates a plan, which the overlay then follows.</summary>
    public void UsePlan(RaidPlan plan, MapDef map)
    {
        lock (_gate)
        {
            _plan = plan;
            _chosenMap = map;

            // Anything cleared in an earlier raid starts ticked, so re-running a plan does not
            // route you back through stops you already finished.
            _completed.Clear();
            foreach (var waypoint in plan.Waypoints)
            {
                if (_progress.IsObjectiveComplete(waypoint.ObjectiveId))
                    _completed.Add(waypoint.ObjectiveId);
            }
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

        // Written through immediately rather than at raid end: the game can close on you, and a
        // stop you walked to should not depend on RatNav seeing a clean exit.
        _progress.CompleteObjective(objectiveId, done);

        Publish();
    }

    public RaidView View()
    {
        lock (_gate)
        {
            var map = Map;

            if (map?.Image is not { } image)
                return new RaidView { InRaid = false, HasPlan = _plan is not null };

            // A plan for somewhere else is not this raid's plan. Its stops are kept — you will
            // want them next time you queue that map — but nothing from them is drawn here.
            var applies = _plan is not null && string.Equals(_plan.MapId, map.Id, StringComparison.OrdinalIgnoreCase);

            var transform = new CoordinateTransform(image);
            var stops = new List<RaidStop>();

            foreach (var waypoint in (applies ? _plan?.Waypoints : null) ?? [])
            {
                var point = transform.ToNormalized(waypoint.Position);

                stops.Add(new RaidStop
                {
                    ObjectiveId = waypoint.ObjectiveId,
                    TaskId = waypoint.TaskId,
                    TaskName = waypoint.TaskName,
                    Description = waypoint.Description,
                    X = point.X,
                    Y = point.Y,
                    Owner = waypoint.Owner,
                    Place = map.NearestLabel(waypoint.Position)?.Text,
                    Done = _completed.Contains(waypoint.ObjectiveId),
                });
            }

            var next = applies
                ? _plan?.Waypoints.FirstOrDefault(w => !_completed.Contains(w.ObjectiveId))
                : null;
            var here = _fix is null ? (MapPoint?)null : transform.ToNormalized(_fix.Position);

            return new RaidView
            {
                HasMap = true,
                // In a raid means the game says so, not that there is a map on screen. Between
                // raids the plan still draws, and calling that "in raid" would have the overlay
                // reporting a fix age for a raid that ended an hour ago.
                InRaid = RaidMap is not null,
                HasPlan = _plan is not null,
                PlanMapName = _plan is not null && !applies ? PlanMapName() : null,
                MapId = map.Id,
                MapName = map.Name,
                X = here?.X,
                Y = here?.Y,
                HeadingDegrees = _fix is null ? null : transform.ToImageHeading(_fix.HeadingDegrees),
                FixedAt = _fix?.TakenAt,
                Floor = _fix is null ? image.DefaultFloor : map.FloorAt(_fix.Position.Y)?.Layer ?? image.DefaultFloor,
                Stops = stops,
                CompletedObjectiveIds = [.. _completed],
                Trail = [.. _trail],
                NextStopName = next is null ? null : map.NearestLabel(next.Position)?.Text ?? next.TaskName,
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

    /// <summary>The name of the map the active plan is for, when it is not the one being drawn.</summary>
    private string? PlanMapName() =>
        _chosenMap?.Name
        ?? _state.Cache.Current?.Maps.FirstOrDefault(m =>
            string.Equals(m.Id, _plan?.MapId, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>
    /// Drops one stop from the active plan.
    ///
    /// <para>A plan outlives the raid it was built for, so it has to be editable outside one —
    /// the usual next move after extracting is to strike off what is no longer worth doing and
    /// keep the rest.</para>
    /// </summary>
    public void RemoveStop(string objectiveId)
    {
        lock (_gate)
        {
            if (_plan is null) return;

            var kept = _plan.Waypoints.Where(w => !string.Equals(w.ObjectiveId, objectiveId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (kept.Count == _plan.Waypoints.Count) return;

            _plan = kept.Count == 0 ? null : _plan with { Waypoints = kept };
            _completed.Remove(objectiveId);
        }

        Publish();
    }

    /// <summary>Puts the plan away entirely, for when the next raid is nothing like the last.</summary>
    public void ClearPlan()
    {
        lock (_gate)
        {
            _plan = null;
            _chosenMap = null;
            _completed.Clear();
        }

        Publish();
    }

    /// <summary>The plan currently active, for saving it across a restart.</summary>
    public RaidPlan? ActivePlan
    {
        get { lock (_gate) return _plan; }
    }

    private void Publish() => Changed?.Invoke(this, View());
}
