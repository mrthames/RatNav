using System.Text.Json;
using System.Text.Json.Serialization;
using RatNav.Core.Planning;

namespace RatNav.Core.Sharing;

/// <summary>
/// A raid plan in a form that can leave this machine.
///
/// <para>Versioned and self-describing, because the whole point is that someone else's copy of
/// RatNav — possibly an older or newer one — has to read it. A document from the future is
/// reported as such rather than half-parsed into something that looks fine and is wrong.</para>
///
/// <para>Everything is stored by id: task ids, objective ids, item ids. Those are stable across
/// game patches and identical for everyone, whereas names are localized and change with reworks.
/// The importing copy resolves them against its own game data, which also means a plan naturally
/// picks up renamed quests instead of preserving whatever they were called when it was made.</para>
/// </summary>
public sealed record PlanDocument
{
    /// <summary>Bumped only when a change would confuse an older reader.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Who made this. Free text — a handle, not an account.</summary>
    public string? Owner { get; init; }

    public required string MapId { get; init; }
    public required string MapName { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public required IReadOnlyList<PlanStop> Stops { get; init; }

    /// <summary>Item ids to bring, gathered from the stops. Stored so a reader need not recompute.</summary>
    public IReadOnlyList<string> RequiredKeyItemIds { get; init; } = [];

    /// <summary>Items to look for this raid, by id.</summary>
    public IReadOnlyList<string> ShoppingListItemIds { get; init; } = [];

    public string? Notes { get; init; }

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reads a shared plan. Returns null for anything unreadable — a corrupt or future document is
    /// a thing to report to the player, not an exception to crash an import on.
    /// </summary>
    public static PlanDocument? FromJson(string json, out string? problem)
    {
        problem = null;

        PlanDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PlanDocument>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // The parser's own message names a byte offset and a JSON path, which is developer
            // text: "Expected end of string, but instead reached end of data. Path: $.stops[0] |
            // LineNumber: 9". True, and no help at all to somebody who pasted half a code out of a
            // chat window. What they need is what to do, which is ask for it again.
            problem = "That plan is incomplete or damaged — ask for it again, in full.";
            return null;
        }

        if (document is null)
        {
            problem = "That file was empty.";
            return null;
        }

        if (document.Version > CurrentVersion)
        {
            problem = $"That plan was made by a newer version of RatNav (format {document.Version}, " +
                      $"this copy reads {CurrentVersion}). Update and try again.";
            return null;
        }

        if (document.Stops.Count == 0)
        {
            problem = "That plan has no stops in it.";
            return null;
        }

        return document;
    }

    /// <summary>Turns a built plan into something shareable.</summary>
    public static PlanDocument From(RaidPlan plan, string? owner = null, IEnumerable<string>? shoppingList = null) => new()
    {
        Owner = owner,
        MapId = plan.MapId,
        MapName = plan.MapName,
        Stops =
        [
            .. plan.Waypoints.Select(w => new PlanStop
            {
                ObjectiveId = w.ObjectiveId,
                TaskId = w.TaskId,
                Owner = w.Owner ?? owner,

                // Only carried when nothing else can supply it. A quest objective's name comes
                // back from game data on import, and shipping a copy would mean a plan showing an
                // old name after a patch renamed the quest.
                Label = w.TaskId is { Length: > 0 } ? null : w.TaskName,
                X = w.Position.X,
                Y = w.Position.Y,
                Z = w.Position.Z,
                NeededKeyItemIds = w.NeededKeyItemIds,
            })
        ],
        RequiredKeyItemIds = plan.RequiredKeyItemIds,
        ShoppingListItemIds = [.. shoppingList ?? []],
        Notes = plan.Notes,
    };
}

/// <summary>One stop in a shared plan. Positions travel with it so a reader can draw the route
/// even for an objective its own game data no longer knows about.</summary>
public sealed record PlanStop
{
    public required string ObjectiveId { get; init; }
    public required string TaskId { get; init; }

    /// <summary>Whose objective this is. Set on every stop once plans are merged.</summary>
    public string? Owner { get; init; }

    /// <summary>
    /// What to call this stop when no quest can supply the name.
    ///
    /// <para>A stop for a quest objective needs no label: the name is re-derived from game data on
    /// import, which is what lets a plan survive a patch renaming something. A mark of your own has
    /// no game data behind it, so its name has to travel with it or it arrives as an unnamed dot.</para>
    /// </summary>
    public string? Label { get; init; }

    public required double X { get; init; }
    public double Y { get; init; }
    public required double Z { get; init; }

    public IReadOnlyList<string> NeededKeyItemIds { get; init; } = [];
}
