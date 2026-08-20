namespace RatNav.Core.Tracking;

using System.Text.Json;

/// <summary>What kind of thing a mark is.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum MarkKind
{
    /// <summary>Somewhere worth remembering — a stash, an angle, a way through.</summary>
    Place,

    /// <summary>Something to pick up when you are there.</summary>
    Item,
}

/// <summary>
/// A spot someone marked on a map, with a short name for it.
///
/// <para>Coordinates are normalised — 0 to 1 across the map image — rather than game world units,
/// because these are placed by clicking a map rather than by standing somewhere. That also means
/// they survive a change to a map's calibration, which world coordinates would not.</para>
/// </summary>
public sealed record CustomWaypoint
{
    public required string Id { get; init; }
    public required string MapId { get; init; }

    /// <summary>Short, because it is drawn on a map over a game — "car batteries", not a sentence.</summary>
    public required string Label { get; init; }

    public required double X { get; init; }
    public required double Y { get; init; }

    /// <summary>The floor it belongs to, when the map has floors and one was chosen.</summary>
    public string? Floor { get; init; }

    /// <summary>
    /// Whether this is a place or a thing to pick up.
    ///
    /// <para>Drawn as a different shape rather than a different colour. Colour already carries
    /// something — that this is yours rather than a quest's — and a second meaning stacked onto it
    /// would need both to be read at once.</para>
    /// </summary>
    public MarkKind Kind { get; init; } = MarkKind.Place;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The marks you have made on maps, kept between sessions.
///
/// <para>Separate from plans on purpose. A plan is for one raid and gets cleared; "the car
/// batteries spawn behind the garage" is true every raid, and having to re-add it each time is how
/// a feature stops being used.</para>
/// </summary>
public sealed class CustomWaypointStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly object _gate = new();
    private List<CustomWaypoint> _marks = [];

    private string StatePath => Path.Combine(dataDirectory, "waypoints.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return;

            var loaded = JsonSerializer.Deserialize<List<CustomWaypoint>>(File.ReadAllText(StatePath), Json);
            lock (_gate) _marks = loaded ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A corrupt file costs you your marks, not the app. Starting empty is recoverable;
            // refusing to start is not.
            lock (_gate) _marks = [];
        }
    }

    public IReadOnlyList<CustomWaypoint> All
    {
        get { lock (_gate) return [.. _marks]; }
    }

    public IReadOnlyList<CustomWaypoint> For(string mapId)
    {
        lock (_gate)
            return [.. _marks.Where(m => string.Equals(m.MapId, mapId, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Marks a spot. Returns what was stored, id and all.</summary>
    public CustomWaypoint Add(
        string mapId, string label, double x, double y,
        string? floor = null, MarkKind kind = MarkKind.Place)
    {
        var mark = new CustomWaypoint
        {
            Kind = kind,
            Id = Guid.NewGuid().ToString("n"),
            MapId = mapId,

            // A blank label is worse than a default one: an unnamed dot on a map is a puzzle.
            Label = label is { Length: > 0 } ? Trim(label) : "mark",
            X = Math.Clamp(x, 0, 1),
            Y = Math.Clamp(y, 0, 1),
            Floor = floor,
        };

        lock (_gate) _marks.Add(mark);
        Save();

        return mark;
    }

    /// <summary>Renames a mark, keeping where it is. Returns false when there is no such mark.</summary>
    public bool Rename(string id, string label)
    {
        lock (_gate)
        {
            var at = _marks.FindIndex(m => m.Id == id);
            if (at < 0) return false;

            _marks[at] = _marks[at] with { Label = Trim(label) };
        }
        Save();

        return true;
    }

    /// <summary>Changes what kind of thing a mark is, without moving or renaming it.</summary>
    public bool SetKind(string id, MarkKind kind)
    {
        lock (_gate)
        {
            var at = _marks.FindIndex(m => m.Id == id);
            if (at < 0) return false;

            _marks[at] = _marks[at] with { Kind = kind };
        }
        Save();

        return true;
    }

    /// <summary>One mark by id, or null.</summary>
    public CustomWaypoint? Get(string id)
    {
        lock (_gate) return _marks.FirstOrDefault(m => m.Id == id);
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate) removed = _marks.RemoveAll(m => m.Id == id) > 0;

        if (removed) Save();

        return removed;
    }

    /// <summary>
    /// Short enough to draw. Anything past this is a note rather than a label, and a label that
    /// runs across a quarter of the map is worse than a truncated one.
    /// </summary>
    private static string Trim(string label) =>
        label.Trim() is { Length: > 24 } long_ ? long_[..24] : label.Trim();

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);

            string json;
            lock (_gate) json = JsonSerializer.Serialize(_marks, Json);

            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (IOException)
        {
            // Kept in memory for this session; the next change tries again.
        }
    }
}
