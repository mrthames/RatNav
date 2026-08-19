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
public sealed class ProgressStore(string dataDirectory) : IProgressView
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private ProgressState _state = new();

    private string StatePath => Path.Combine(dataDirectory, "progress.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return;

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
    public IEnumerable<TaskDef> AvailableNow(IEnumerable<TaskDef> tasks) =>
        tasks.Where(t =>
            StateOf(t.Id) == QuestState.NotStarted &&
            t.PrerequisiteTaskIds.All(p => StateOf(p) == QuestState.Completed));

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);

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
        /// <summary>Corrections made by hand. Always wins.</summary>
        public Dictionary<string, QuestState> Manual { get; init; } = [];

        /// <summary>What the game's logs reported.</summary>
        public Dictionary<string, QuestState> FromLogs { get; init; } = [];

        /// <summary>Highest built level per hideout station.</summary>
        public Dictionary<string, int> HideoutLevels { get; init; } = [];
    }
}
