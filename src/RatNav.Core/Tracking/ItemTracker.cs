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
public sealed class ItemTracker(RatNavProfile profile)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private TrackingState _state = new();

    private string StatePath => Path.Combine(profile.Directory, "tracking.json");

    /// <summary>Loads saved counts and watchlist. Missing or corrupt state starts empty rather than throwing.</summary>
    public void Load()
    {
        try
        {
            // An empty state rather than the one already in memory. Load runs again on every
            // character switch, and a profile with no file yet has to read as a fresh character —
            // keeping the last one's data would show it under the new name and then save it there.
            if (!File.Exists(StatePath))
            {
                lock (_gate) _state = new TrackingState();
                return;
            }

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
    public void Watch(string itemId, string? note = null, int? target = null, int? have = null)
    {
        lock (_gate)
        {
            var existing = _state.Watchlist.FindIndex(w => w.ItemId == itemId);

            // Editing one field must not blank the others: the UI sends whichever the player just
            // changed, and a target set yesterday should survive a note typed today.
            var current = existing >= 0 ? _state.Watchlist[existing] : null;

            var entry = new WatchlistEntry
            {
                ItemId = itemId,
                Note = note ?? current?.Note,
                Target = target ?? current?.Target,
                Have = Math.Max(0, have ?? current?.Have ?? 0),
            };

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

    /// <summary>The goals you are collecting for.</summary>
    public IReadOnlyList<Goal> Goals
    {
        get { lock (_gate) return [.. _state.Goals]; }
    }

    /// <summary>
    /// Adds a goal, or replaces one by id. Returns what was stored, id and all.
    /// </summary>
    public Goal SaveGoal(string? id, string name, IReadOnlyList<GoalItem> items, int times = 1)
    {
        var goal = new Goal
        {
            Id = id is { Length: > 0 } ? id : Guid.NewGuid().ToString("n"),

            // An unnamed goal is a list of items with no reason attached, which is the one thing
            // this exists to avoid. Trimmed before the check, or a name of three spaces passes it
            // and lands as an empty string.
            Name = name?.Trim() is { Length: > 0 } trimmed ? trimmed : "unnamed",
            Items = [.. items.Where(i => i.ItemId is { Length: > 0 } && i.Count > 0)],
            Times = Math.Max(1, times),
        };

        lock (_gate)
        {
            var at = _state.Goals.FindIndex(g => g.Id == goal.Id);

            if (at >= 0) _state.Goals[at] = goal with { CreatedAt = _state.Goals[at].CreatedAt };
            else _state.Goals.Add(goal);
        }
        Save();

        return goal;
    }

    /// <summary>
    /// Records finding one of a goal's items, or un-finding it.
    ///
    /// <para>Per goal rather than against one stash total: two collections wanting the same item
    /// are two separate counts, and items set aside for one are not also available for the other.
    /// Clamped to what the goal asks for, so a stuck finger cannot claim you have forty.</para>
    /// </summary>
    public Goal? AdjustGoalItem(string goalId, string itemId, int by)
    {
        lock (_gate)
        {
            var at = _state.Goals.FindIndex(g => g.Id == goalId);
            if (at < 0) return null;

            var goal = _state.Goals[at];
            var index = goal.Items.ToList().FindIndex(i => i.ItemId == itemId);

            if (index < 0) return null;

            var item = goal.Items[index];
            var items = goal.Items.ToList();

            items[index] = item with { Found = Math.Clamp(item.Found + by, 0, item.Count) };
            _state.Goals[at] = goal with { Items = items };

            Save();
            return _state.Goals[at];
        }
    }

    public bool RemoveGoal(string id)
    {
        bool removed;
        lock (_gate) removed = _state.Goals.RemoveAll(g => g.Id == id) > 0;

        if (removed) Save();

        return removed;
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
        IReadOnlyDictionary<string, HideoutDemand>? hideout = null,
        IReadOnlyDictionary<string, GoalNeed>? goals = null)
    {
        var active = needs.Quests.Where(q => progress.IsActive(q.TaskId)).ToList();
        var questNeeded = active.Sum(q => q.Count);

        // The hideout's demand is decided by the planner, not here.
        //
        // Counting every un-built level produces a list of everything the hideout will ever want —
        // hundreds of items, most for upgrades gated behind three others you have not started.
        // With no planner supplied, nothing is claimed rather than claiming all of it.
        var demand = hideout is null ? null : hideout.GetValueOrDefault(needs.Item.Id);
        var hideoutNeeded = demand?.Count ?? 0;

        // Goals you named and are collecting for. Counted apart from the two above, because an
        // item wanted three times for a quest and seven for a goal is two reasons rather than one
        // ten — and only the split says that finishing the quest leaves seven still to find.
        var goal = goals?.GetValueOrDefault(needs.Item.Id);
        var goalNeeded = goal?.Count ?? 0;

        var watch = Watchlist.FirstOrDefault(w => w.ItemId == needs.Item.Id);
        var have = GetHave(needs.Item.Id);
        var total = questNeeded + hideoutNeeded + goalNeeded + (watch?.Target ?? 0);

        return new TrackedItem
        {
            Item = needs.Item,
            QuestNeeded = questNeeded,

            // Which quests, not just how many. The hideout half of the same row has said
            // "4 for Medstation 3" for as long as it has existed, while the quest half said
            // "30 for quests" — the same sentence with the useful word left out.
            QuestFor = [.. active.Select(q => q.TaskName).Where(n => n is { Length: > 0 }).Distinct()],
            HideoutNeeded = hideoutNeeded,
            HideoutUpgrade = demand?.UpgradeName,
            HideoutWave = demand?.Wave,
            GoalNeeded = goalNeeded,
            GoalFor = goal?.For ?? [],
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
            Directory.CreateDirectory(profile.Directory);

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
        public List<Goal> Goals { get; init; } = [];
    }
}

public sealed record WatchlistEntry
{
    public required string ItemId { get; init; }
    public string? Note { get; init; }

    /// <summary>How many you want. Null means "just remind me", with no quantity attached.</summary>
    public int? Target { get; init; }

    /// <summary>
    /// How many you have set aside <b>for this</b>, counted separately from the stash total.
    ///
    /// <para>Two pools rather than one on purpose. Twenty bundles of wires with fifteen earmarked
    /// for the hideout is not twenty available for a barter, and a single shared count says it
    /// is — which is how you end up spending something you had already promised elsewhere.</para>
    /// </summary>
    public int Have { get; init; }
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

    /// <summary>The active quests wanting it, by name.</summary>
    public IReadOnlyList<string> QuestFor { get; init; } = [];
    public int HideoutNeeded { get; init; }

    /// <summary>The nearest hideout upgrade wanting this — "Medstation 3". Null when none does.</summary>
    public string? HideoutUpgrade { get; init; }

    /// <summary>How far out that upgrade is. 1 means you could build it today.</summary>
    public int? HideoutWave { get; init; }
    /// <summary>How many are wanted by the goals you are collecting for.</summary>
    public int GoalNeeded { get; init; }

    /// <summary>Which goals want it, by the names you gave them.</summary>
    public IReadOnlyList<string> GoalFor { get; init; } = [];

    public int? WatchTarget { get; init; }
    public string? WatchNote { get; init; }
    public bool Watched { get; init; }
    public int Have { get; init; }
    public int Remaining { get; init; }

    /// <summary>True when an active quest wants it found in raid — the difference between keeping and buying.</summary>
    public bool FoundInRaid { get; init; }

    public bool IsKey { get; init; }

    public int Needed => QuestNeeded + HideoutNeeded + GoalNeeded + (WatchTarget ?? 0);
    public bool Done => Needed > 0 && Remaining == 0;
}
