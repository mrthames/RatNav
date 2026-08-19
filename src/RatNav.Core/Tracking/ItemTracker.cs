using System.Text.Json;
using RatNav.Core.Data;
using RatNav.Core.Model;
using RatNav.Core.Planning;

namespace RatNav.Core.Tracking;

/// <summary>
/// Answers "do I need this, and how many?" — the question a Tarkov player asks a hundred times
/// a raid, and the reason this app exists.
///
/// Three layers, deliberately separate:
///
/// <list type="bullet">
///   <item><b>Auto needs</b> come from active quests and un-built hideout modules. Derived, never
///   hand-edited, recomputed whenever quest progress or game data changes.</item>
///   <item><b>Have counts</b> are entered by hand. Escape from Tarkov puts stash contents in no
///   file on disk, so there is no honest way to know them; the app asks rather than guesses.</item>
///   <item><b>The watchlist</b> is free-form — items worth remembering that no quest or module
///   requires, with a note and an optional target.</item>
/// </list>
///
/// Keeping them apart is what lets quest data refresh under a player's own counts without
/// clobbering them.
/// </summary>
public sealed class ItemTracker(string dataDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private TrackingState _state = new();

    private string StatePath => Path.Combine(dataDirectory, "tracking.json");

    /// <summary>Loads saved counts and watchlist. Missing or corrupt state starts empty rather than throwing.</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return;

            var loaded = JsonSerializer.Deserialize<TrackingState>(File.ReadAllText(StatePath), Json);
            if (loaded is not null)
            {
                lock (_gate) _state = loaded;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Losing hand-entered counts is bad, but refusing to start is worse. The file is
            // left alone so it can be recovered by hand if it mattered.
        }
    }

    public int GetHave(string itemId)
    {
        lock (_gate) return _state.Have.GetValueOrDefault(itemId);
    }

    /// <summary>Sets how many of an item you have. Negative counts are treated as zero.</summary>
    public void SetHave(string itemId, int count)
    {
        lock (_gate)
        {
            if (count <= 0) _state.Have.Remove(itemId);
            else _state.Have[itemId] = count;
        }
        Save();
    }

    /// <summary>Nudges a count by one or more, never below zero. What the +/− buttons call.</summary>
    public int AdjustHave(string itemId, int delta)
    {
        int updated;
        lock (_gate)
        {
            updated = Math.Max(0, _state.Have.GetValueOrDefault(itemId) + delta);
            if (updated == 0) _state.Have.Remove(itemId);
            else _state.Have[itemId] = updated;
        }
        Save();
        return updated;
    }

    public IReadOnlyList<WatchlistEntry> Watchlist
    {
        get { lock (_gate) return [.. _state.Watchlist]; }
    }

    /// <summary>Adds or updates a watchlist entry. Re-adding an item edits it rather than duplicating.</summary>
    public void Watch(string itemId, string? note = null, int? target = null)
    {
        lock (_gate)
        {
            var existing = _state.Watchlist.FindIndex(w => w.ItemId == itemId);
            var entry = new WatchlistEntry { ItemId = itemId, Note = note, Target = target };

            if (existing >= 0) _state.Watchlist[existing] = entry;
            else _state.Watchlist.Add(entry);
        }
        Save();
    }

    public void Unwatch(string itemId)
    {
        lock (_gate) _state.Watchlist.RemoveAll(w => w.ItemId == itemId);
        Save();
    }

    /// <summary>
    /// What you still need of an item, given your progress.
    ///
    /// Only <b>active</b> quests count: a quest you have finished no longer needs its items, and
    /// one you have not unlocked is not something to be hoarding for yet. That filtering is the
    /// difference between a list of everything the game will ever ask for and a list of what to
    /// pick up tonight.
    /// </summary>
    public TrackedItem Track(
        ItemNeeds needs,
        IProgressView progress,
        IReadOnlyDictionary<string, HideoutDemand>? hideout = null)
    {
        var questNeeded = needs.Quests
            .Where(q => progress.IsActive(q.TaskId))
            .Sum(q => q.Count);

        // The hideout's demand is decided by the planner, not here.
        //
        // Counting every un-built level produces a list of everything the hideout will ever want —
        // hundreds of items, most for upgrades gated behind three others you have not started.
        // With no planner supplied, nothing is claimed rather than claiming all of it.
        var demand = hideout is null ? null : hideout.GetValueOrDefault(needs.Item.Id);
        var hideoutNeeded = demand?.Count ?? 0;

        var watch = Watchlist.FirstOrDefault(w => w.ItemId == needs.Item.Id);
        var have = GetHave(needs.Item.Id);
        var total = questNeeded + hideoutNeeded + (watch?.Target ?? 0);

        return new TrackedItem
        {
            Item = needs.Item,
            QuestNeeded = questNeeded,
            HideoutNeeded = hideoutNeeded,
            HideoutUpgrade = demand?.UpgradeName,
            HideoutWave = demand?.Wave,
            WatchTarget = watch?.Target,
            WatchNote = watch?.Note,
            Watched = watch is not null,
            Have = have,
            Remaining = Math.Max(0, total - have),

            // Found-in-raid is the difference between keeping something and buying it, and the
            // hideout asks for it as often as quests do.
            FoundInRaid = needs.Quests.Any(q => q.FoundInRaid && progress.IsActive(q.TaskId))
                || (demand?.FoundInRaid ?? false),

            IsKey = needs.AsKey.Count > 0,
        };
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);

            string json;
            lock (_gate) json = JsonSerializer.Serialize(_state, Json);

            // Write then move, so a crash mid-write cannot destroy counts that took a player
            // weeks of raids to accumulate.
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (IOException)
        {
            // Kept in memory for this session; the next change tries again.
        }
    }

    private sealed record TrackingState
    {
        public Dictionary<string, int> Have { get; init; } = [];
        public List<WatchlistEntry> Watchlist { get; init; } = [];
    }
}

public sealed record WatchlistEntry
{
    public required string ItemId { get; init; }
    public string? Note { get; init; }

    /// <summary>How many you want. Null means "just remind me", with no quantity attached.</summary>
    public int? Target { get; init; }
}

/// <summary>
/// What the tracker needs to know about progress. An interface so item tracking does not depend
/// on how progress is discovered — logs, manual ticks, or an import.
/// </summary>
public interface IProgressView
{
    /// <summary>True when a quest is accepted and unfinished, so its items are worth collecting.</summary>
    bool IsActive(string taskId);

    bool IsHideoutLevelBuilt(string stationId, int level);
}

/// <summary>An item with your progress folded in — the row the Items view renders.</summary>
public sealed record TrackedItem
{
    public required ItemDef Item { get; init; }
    public int QuestNeeded { get; init; }
    public int HideoutNeeded { get; init; }

    /// <summary>The nearest hideout upgrade wanting this — "Medstation 3". Null when none does.</summary>
    public string? HideoutUpgrade { get; init; }

    /// <summary>How far out that upgrade is. 1 means you could build it today.</summary>
    public int? HideoutWave { get; init; }
    public int? WatchTarget { get; init; }
    public string? WatchNote { get; init; }
    public bool Watched { get; init; }
    public int Have { get; init; }
    public int Remaining { get; init; }

    /// <summary>True when an active quest wants it found in raid — the difference between keeping and buying.</summary>
    public bool FoundInRaid { get; init; }

    public bool IsKey { get; init; }

    public int Needed => QuestNeeded + HideoutNeeded + (WatchTarget ?? 0);
    public bool Done => Needed > 0 && Remaining == 0;
}
