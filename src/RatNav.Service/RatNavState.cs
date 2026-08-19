using RatNav.Core.Data;
using RatNav.Core.Model;

namespace RatNav.Service;

/// <summary>
/// The single source of truth all three surfaces read from — compact overlay, expanded
/// overlay, and the buddy web app. Holding it in one place is what makes a change in one
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
