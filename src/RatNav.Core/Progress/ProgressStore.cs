using System.Text.Json;
using System.Text.Json.Serialization;
using RatNav.Core.Model;
using RatNav.Core.Tracking;

namespace RatNav.Core.Progress;

public enum QuestState
{
    /// <summary>Not accepted, or not yet unlocked.</summary>
    NotStarted,

    /// <summary>Accepted and unfinished — the state that makes its items worth collecting.</summary>
    Active,

    Completed,
    Failed,
}

/// <summary>
/// Where a player is in the game: which quests are accepted, which are done, and what the hideout
/// has been built to.
///
/// <para><b>Two layers, and the order matters.</b> Events read from the game's own logs form the
/// base, and anything the player corrects by hand sits on top and wins. The game does not reliably
/// write in-raid quest state changes, so log parsing alone will miss progress — and a design where
/// a later log replay silently reverts a correction would be worse than no automation at all.</para>
///
/// <para>Log watching is not wired in yet; this is the store it will write into. Everything here
/// works standalone in the meantime, which is why the manual layer came first.</para>
/// </summary>
public sealed class ProgressStore(RatNavProfile profile) : IProgressView
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private ProgressState _state = new();

    private string StatePath => Path.Combine(profile.Directory, "progress.json");

    public void Load()
    {
        try
        {
            // An empty state rather than the one already in memory. Load runs again on every
            // character switch, and a profile with no file yet has to read as a fresh character —
            // keeping the last one's data would show it under the new name and then save it there.
            if (!File.Exists(StatePath))
            {
                lock (_gate) _state = new ProgressState();
                return;
            }

            var loaded = JsonSerializer.Deserialize<ProgressState>(File.ReadAllText(StatePath), Json);
            if (loaded is not null)
            {
                lock (_gate) _state = loaded;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Start empty rather than refuse to run.
        }
    }

    /// <summary>The effective state of a quest: what the player said, else what the logs said.</summary>
    public QuestState StateOf(string taskId)
    {
        lock (_gate)
        {
            if (_state.Manual.TryGetValue(taskId, out var manual)) return manual;
            return _state.FromLogs.GetValueOrDefault(taskId, QuestState.NotStarted);
        }
    }

    public bool IsActive(string taskId) => StateOf(taskId) == QuestState.Active;

    /// <summary>Records a correction made by hand. Beats anything the logs say, now or later.</summary>
    public void SetManual(string taskId, QuestState state)
    {
        lock (_gate) _state.Manual[taskId] = state;
        Save();
    }

    /// <summary>Drops a correction, handing the quest back to whatever the logs report.</summary>
    public void ClearManual(string taskId)
    {
        lock (_gate) _state.Manual.Remove(taskId);
        Save();
    }

    /// <summary>
    /// Records what the game's logs reported. Never touches the manual layer, so replaying an
    /// entire log history cannot undo a correction.
    /// </summary>
    public void RecordFromLogs(string taskId, QuestState state)
    {
        lock (_gate) _state.FromLogs[taskId] = state;
        Save();
    }

    /// <summary>
    /// Records an objective as done, permanently.
    ///
    /// <para>Objectives are tracked separately from quests on purpose. Ticking one off in a raid
    /// is a real fact worth keeping — you cleared it, you should not walk there again — but it is
    /// not the same as finishing the quest, which may have objectives you never selected. Marking
    /// the quest complete off a partial plan would retire item needs you still have.</para>
    /// </summary>
    public void CompleteObjective(string objectiveId, bool done = true)
    {
        lock (_gate)
        {
            if (done) _state.CompletedObjectives.Add(objectiveId);
            else _state.CompletedObjectives.Remove(objectiveId);
        }
        Save();
    }

    public bool IsObjectiveComplete(string objectiveId)
    {
        lock (_gate) return _state.CompletedObjectives.Contains(objectiveId);
    }

    /// <summary>Every objective cleared so far, for the planner to leave out of the next route.</summary>
    public IReadOnlySet<string> CompletedObjectives
    {
        get { lock (_gate) return _state.CompletedObjectives.ToHashSet(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Forgets objective progress for a quest, so a re-taken or reset quest starts clean rather
    /// than looking half-done forever.
    /// </summary>
    public void ClearObjectives(IEnumerable<string> objectiveIds)
    {
        lock (_gate)
        {
            foreach (var id in objectiveIds) _state.CompletedObjectives.Remove(id);
        }
        Save();
    }

    public bool IsHideoutLevelBuilt(string stationId, int level)
    {
        lock (_gate) return _state.HideoutLevels.GetValueOrDefault(stationId, 0) >= level;
    }

    /// <summary>Sets how far a hideout station has been built, which retires its item needs.</summary>
    public void SetHideoutLevel(string stationId, int level)
    {
        lock (_gate)
        {
            if (level <= 0) _state.HideoutLevels.Remove(stationId);
            else _state.HideoutLevels[stationId] = level;
        }
        Save();
    }

    /// <summary>
    /// Marks a hideout upgrade as one you are working towards.
    ///
    /// <para>Targets are how a player says "these three, not the other eight". Without them the
    /// look-ahead can only widen the list; with them it narrows to what someone actually
    /// decided.</para>
    /// </summary>
    public void TargetHideoutLevel(string stationId, int level, bool wanted = true)
    {
        var key = Planning.HideoutPlanner.Key(stationId, level);

        lock (_gate)
        {
            if (wanted) _state.HideoutTargets.Add(key);
            else _state.HideoutTargets.Remove(key);
        }

        Save();
    }

    /// <summary>Upgrades picked out as wanted, as "stationId:level".</summary>
    public IReadOnlySet<string> HideoutTargets
    {
        get { lock (_gate) return _state.HideoutTargets.ToHashSet(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>Every station's built level, which is what the planner walks forward from.</summary>
    public IReadOnlyDictionary<string, int> HideoutLevels
    {
        get { lock (_gate) return new Dictionary<string, int>(_state.HideoutLevels, StringComparer.OrdinalIgnoreCase); }
    }

    public int HideoutLevelOf(string stationId)
    {
        lock (_gate) return _state.HideoutLevels.GetValueOrDefault(stationId, 0);
    }

    /// <summary>Counts by state, for the summary line above the quest list.</summary>
    public IReadOnlyDictionary<QuestState, int> Summarize(IEnumerable<TaskDef> tasks)
    {
        var counts = new Dictionary<QuestState, int>
        {
            [QuestState.NotStarted] = 0,
            [QuestState.Active] = 0,
            [QuestState.Completed] = 0,
            [QuestState.Failed] = 0,
        };

        foreach (var task in tasks) counts[StateOf(task.Id)]++;
        return counts;
    }

    /// <summary>
    /// Quests worth showing as "available now": not started, but with every prerequisite done.
    /// Prerequisites come from the data rather than being assumed, so a reworked quest chain
    /// follows the game rather than a hardcoded tree.
    /// </summary>
    /// <param name="playerLevel">
    /// Your character level, which most quests gate on. Null means "do not filter by level" —
    /// better to over-report than to hide a quest because RatNav was never told.
    /// </param>
    public IEnumerable<TaskDef> AvailableNow(IEnumerable<TaskDef> tasks, int? playerLevel = null) =>
        tasks.Where(t => StateOf(t.Id) == QuestState.NotStarted && Reachable(t, playerLevel));

    /// <summary>
    /// The quests within <paramref name="depth"/> steps of being available.
    ///
    /// <para>Depth 1 is what you could accept today — every gate met. Past that it follows the
    /// prerequisite chain: depth 2 adds what finishing one of today's quests would unlock, depth 3
    /// what that would unlock, and so on.</para>
    ///
    /// <para>Beyond depth 1 the level and loyalty gates are deliberately ignored. Both rise as you
    /// play, and holding a quest back because today's loyalty is one short would hide exactly the
    /// work that raises it — which is the opposite of what looking ahead is for.</para>
    /// </summary>
    public IReadOnlyList<TaskDef> ReachableWithin(
        IEnumerable<TaskDef> tasks, int depth, int? playerLevel = null)
    {
        var all = tasks.ToList();
        var reached = AvailableNow(all, playerLevel).ToList();
        var ids = reached.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var step = 2; step <= depth; step++)
        {
            var next = all
                .Where(t => !ids.Contains(t.Id))
                .Where(t => StateOf(t.Id) == QuestState.NotStarted)
                .Where(t => t.PrerequisiteTaskIds.All(
                    p => StateOf(p) == QuestState.Completed || ids.Contains(p)))
                .ToList();

            if (next.Count == 0) break;

            reached.AddRange(next);
            foreach (var task in next) ids.Add(task.Id);
        }

        return reached;
    }

    /// <summary>
    /// Whether every gate on a quest is met: prerequisites, character level, and trader loyalty.
    ///
    /// <para>Loyalty was the one missing, and it is not a small omission — a hundred and nine
    /// quests carry one, so leaving it out listed work as ready that the game will not offer.</para>
    /// </summary>
    public bool Reachable(TaskDef task, int? playerLevel)
    {
        if (!task.PrerequisiteTaskIds.All(p => StateOf(p) == QuestState.Completed)) return false;

        // Not knowing your level means not filtering by it. Hiding a quest because RatNav was
        // never told is worse than listing one you cannot quite take.
        if (playerLevel is { } level && task.MinPlayerLevel is { } needed && needed > level) return false;

        return task.TraderRequirements.All(r => TraderLevelOf(r.TraderName ?? r.TraderId) >= r.Level);
    }

    /// <summary>
    /// The lowest character level consistent with the quests marked complete.
    ///
    /// <para>Not your real level — RatNav cannot see that, and nothing the game writes to disk
    /// says it. But a quest that needs level 15 cannot have been finished below it, so this is a
    /// floor worth offering as a suggestion rather than leaving the field blank.</para>
    /// </summary>
    public int LevelImpliedBy(IEnumerable<TaskDef> tasks) =>
        tasks.Where(t => StateOf(t.Id) == QuestState.Completed)
            .Select(t => t.MinPlayerLevel ?? 0)
            .DefaultIfEmpty(1)
            .Max();

    /// <summary>Trader loyalty, by trader id. Set by hand; nothing on disk reports it.</summary>
    public int TraderLevelOf(string traderId)
    {
        lock (_gate) return _state.TraderLevels.GetValueOrDefault(traderId, 1);
    }

    /// <summary>Character level, per profile.</summary>
    public int? PlayerLevel
    {
        get { lock (_gate) return _state.PlayerLevel; }
    }

    public void SetPlayerLevel(int? level)
    {
        lock (_gate) _state.PlayerLevel = level is { } l ? Math.Clamp(l, 1, 79) : null;
        Save();
    }

    public void SetTraderLevel(string traderId, int level)
    {
        lock (_gate) _state.TraderLevels[traderId] = Math.Clamp(level, 1, 4);
        Save();
    }

    public IReadOnlyDictionary<string, int> TraderLevels
    {
        get { lock (_gate) return new Dictionary<string, int>(_state.TraderLevels, StringComparer.OrdinalIgnoreCase); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(profile.Directory);

            string json;
            lock (_gate) json = JsonSerializer.Serialize(_state, Json);

            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (IOException)
        {
            // Held in memory for this session.
        }
    }

    private sealed record ProgressState
    {
        /// <summary>
        /// Character level.
        ///
        /// <para>Here rather than in settings because it belongs to a character, not a machine.
        /// Settings are shared across profiles — your game's install path and your hotkeys do not
        /// change when you switch to PvE — and a level that did the same would gate the wrong
        /// quests the moment you switched.</para>
        /// </summary>
        public int? PlayerLevel { get; set; }

        /// <summary>Corrections made by hand. Always wins.</summary>
        public Dictionary<string, QuestState> Manual { get; init; } = [];

        /// <summary>What the game's logs reported.</summary>
        public Dictionary<string, QuestState> FromLogs { get; init; } = [];

        /// <summary>Highest built level per hideout station.</summary>
        public Dictionary<string, int> HideoutLevels { get; init; } = [];

        /// <summary>Objectives cleared in a raid, kept once the raid is over.</summary>
        public HashSet<string> CompletedObjectives { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Hideout upgrades being worked towards, as "stationId:level".</summary>
        public HashSet<string> HideoutTargets { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Trader loyalty levels, by trader id.</summary>
        public Dictionary<string, int> TraderLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
