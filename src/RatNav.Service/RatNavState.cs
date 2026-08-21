using RatNav.Core.Data;
using RatNav.Core.Planning;
using RatNav.Core.Progress;
using RatNav.Core.Tracking;
using RatNav.Core.Model;

namespace RatNav.Service;

/// <summary>
/// The single source of truth all three surfaces read from — compact overlay, expanded
/// overlay, and the app. Holding it in one place is what makes a change in one
/// surface appear in the others rather than each keeping its own drifting copy.
/// </summary>
public sealed class RatNavState(GameDataCache cache)
{
    private ItemIndex? _index;
    private GameData? _indexedFrom;
    private readonly object _gate = new();

    public GameDataCache Cache => cache;

    /// <summary>
    /// The item index for the current data, rebuilt only when the underlying data actually
    /// changes. Building it walks every task and hideout level, so doing it per request would
    /// be wasteful; doing it never would serve stale answers after a patch.
    /// </summary>
    public ItemIndex? Index
    {
        get
        {
            var data = cache.Current;
            if (data is null) return null;

            lock (_gate)
            {
                if (!ReferenceEquals(_indexedFrom, data))
                {
                    _index = new ItemIndex(data);
                    _indexedFrom = data;
                }
                return _index;
            }
        }
    }

    /// <summary>Freshness, in the form the UI shows the player.</summary>
    /// <summary>
    /// The hideout upgrades in view, given how far ahead the player has asked to look.
    ///
    /// <para>Single source for this: the Hideout view, the items list, and the overlay's panel all
    /// have to agree about what is next, and three separate walks of the build order would
    /// eventually not.</para>
    /// </summary>
    /// <param name="lookAhead">
    /// How many waves to look <em>beyond</em> what can be built right now. Zero is only what you
    /// could start today.
    ///
    /// <para>The planner counts waves, where one wave is what is buildable now — so "look ahead 1"
    /// is two waves. Everyone reading a dial said the opposite: it showed 1 above the words "only
    /// what you can finish now", which is a 1 that means none. Translated once, here, rather than
    /// at each of the seven places that ask.</para>
    /// </param>
    public IReadOnlyList<HideoutUpgrade> Upcoming(ProgressStore progress, int lookAhead) =>
        HideoutPlanner.Upcoming(
            cache.Current?.HideoutStations ?? [],
            progress.HideoutLevels,
            Math.Max(0, lookAhead) + 1,
            progress.HideoutTargets);

    /// <summary>
    /// What the goals you are collecting for want, keyed by item.
    ///
    /// <para>Single source for the same reason <see cref="Upcoming"/> is: the items list, the
    /// overlay panel and a search result all have to agree about why an item is wanted.</para>
    /// </summary>
    public IReadOnlyDictionary<string, GoalNeed> GoalDemand(ItemTracker tracker) =>
        GoalDemands.From(tracker.Goals);

    public DataStatus Status(RefreshResult? lastRefresh = null)
    {
        var data = cache.Current;

        return new DataStatus
        {
            Loaded = data is not null,
            FetchedAt = data?.FetchedAt == DateTimeOffset.MinValue ? null : data?.FetchedAt,
            GameVersion = data?.GameVersion,
            TaskCount = data?.Tasks.Count ?? 0,
            ItemCount = data?.Items.Count ?? 0,
            MapCount = data?.Maps.Count ?? 0,
            CalibratedMapCount = data?.Maps.Count(m => m.Image is not null) ?? 0,
            BarterCount = data?.Barters.Count ?? 0,
            ServingStale = lastRefresh?.ServingStale ?? false,
            LastError = lastRefresh?.Error,

            // A refresh can succeed overall while one source is dead. Reporting only the overall
            // result is how barters were empty for a release with nothing anywhere saying so.
            BrokenSources = cache.Problems,
        };
    }
}

public sealed record DataStatus
{
    public required bool Loaded { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public string? GameVersion { get; init; }
    public int TaskCount { get; init; }
    public int ItemCount { get; init; }
    public int MapCount { get; init; }
    public int BarterCount { get; init; }

    /// <summary>How many maps we can actually plot pins on — the rest have no calibration yet.</summary>
    public int CalibratedMapCount { get; init; }

    /// <summary>True when the last refresh failed and we are serving what we had.</summary>
    public bool ServingStale { get; init; }

    public string? LastError { get; init; }

    /// <summary>Sources that failed on the last refresh, keyed by name. Empty when all is well.</summary>
    public IReadOnlyDictionary<string, string> BrokenSources { get; init; } =
        new Dictionary<string, string>();
}
