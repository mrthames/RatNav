namespace RatNav.Core.Model;

/// <summary>
/// One map as TarkovTracker/tarkovdata describes it: an image plus the numbers needed to place
/// a world coordinate on it.
///
/// This is deliberately usable on its own. tarkov.dev supplies quests and items, tarkovdata
/// supplies maps, and they are different hosts — when one is down the other should still work,
/// so a map can be opened and read even with no quest data to pin on it.
/// </summary>
public sealed record MapCalibration
{
    /// <summary>tarkovdata's own key, e.g. "customs". Stable, and what RatNav routes on.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The matching tarkov.dev map id, when known. Now usually null: the map metadata and the game
    /// data come from the same project, so they join on normalized name instead.
    /// </summary>
    public string? TarkovDevId { get; init; }

    public required MapImage Image { get; init; }

    /// <summary>Named places on this map.</summary>
    public IReadOnlyList<MapLabel> Labels { get; init; } = [];
}
