namespace RatNav.Core.Model;

/// <summary>
/// Everything RatNav knows about the game, as of one fetch from tarkov.dev.
/// Cached to disk keyed by game version so a patch produces a new file rather than
/// overwriting a working one.
/// </summary>
public sealed record GameData
{
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>Game version this data was fetched against, if known (from the EFT log directory name).</summary>
    public string? GameVersion { get; init; }

    public IReadOnlyList<TaskDef> Tasks { get; init; } = [];
    public IReadOnlyList<ItemDef> Items { get; init; } = [];
    public IReadOnlyList<HideoutStation> HideoutStations { get; init; } = [];
    public IReadOnlyList<MapDef> Maps { get; init; } = [];
}

/// <summary>A quest, as tarkov.dev models it.</summary>
public sealed record TaskDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NormalizedName { get; init; }
    public string? TraderName { get; init; }
    public int? MinPlayerLevel { get; init; }
    public bool Kappa { get; init; }

    /// <summary>Task ids that must be completed before this one unlocks.</summary>
    public IReadOnlyList<string> PrerequisiteTaskIds { get; init; } = [];

    public IReadOnlyList<TaskObjective> Objectives { get; init; } = [];

    /// <summary>Wiki page for this quest. Deep-linked rather than scraped, so guidance stays current.</summary>
    public string? WikiUrl { get; init; }
}

/// <summary>
/// One objective within a quest. Objectives of every tarkov.dev type collapse into this
/// shape — unrecognized types still render, they just carry less detail.
/// </summary>
public sealed record TaskObjective
{
    public required string Id { get; init; }
    public required string Description { get; init; }

    /// <summary>tarkov.dev objective type ("giveItem", "mark", "shoot", ...). Kept raw so new types don't break us.</summary>
    public string? Type { get; init; }

    public bool Optional { get; init; }

    /// <summary>Map ids this objective applies to. Empty means "anywhere" or "not map-specific".</summary>
    public IReadOnlyList<string> MapIds { get; init; } = [];

    /// <summary>Where on the map this objective is, in game world coordinates. Null when tarkov.dev has no position for it.</summary>
    public GamePosition? Position { get; init; }

    /// <summary>Items this objective consumes, if any.</summary>
    public IReadOnlyList<ObjectiveItem> Items { get; init; } = [];

    /// <summary>Keys needed to reach this objective. Aggregated into the raid plan's "bring these" list.</summary>
    public IReadOnlyList<string> NeededKeyItemIds { get; init; } = [];
}

public sealed record ObjectiveItem
{
    public required string ItemId { get; init; }
    public required int Count { get; init; }
    public bool FoundInRaid { get; init; }
}

public sealed record ItemDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public string? NormalizedName { get; init; }
    public string? IconUrl { get; init; }
    public string? WikiUrl { get; init; }
    public int? BasePrice { get; init; }
    public int? Avg24hPrice { get; init; }
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
}

public sealed record HideoutStation
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<HideoutLevel> Levels { get; init; } = [];
}

public sealed record HideoutLevel
{
    public required string Id { get; init; }
    public required int Level { get; init; }
    public IReadOnlyList<ObjectiveItem> ItemRequirements { get; init; } = [];
}

public sealed record MapDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NormalizedName { get; init; }

    /// <summary>Names this map goes by in EFT's log files, so a raid start can be matched to a map.</summary>
    public IReadOnlyList<string> LogAliases { get; init; } = [];

    /// <summary>Image + coordinate calibration. Null when we have no calibrated image for the map yet.</summary>
    public MapImage? Image { get; init; }

    public IReadOnlyList<MapExtract> Extracts { get; init; } = [];
}

/// <summary>
/// Calibration for turning game coordinates into pixels on a map image.
/// Mirrors the shape TarkovTracker/tarkovdata publishes in maps.json.
/// </summary>
public sealed record MapImage
{
    /// <summary>Remote URL the image is downloaded from. Images are never committed to the repo.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Degrees the map image is rotated relative to game world axes (0, 90, 180, 270).</summary>
    public required int CoordinateRotation { get; init; }

    /// <summary>Game-world bounds of the image, as [[x1, z1], [x2, z2]].</summary>
    public required double[][] Bounds { get; init; }

    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
}

public sealed record MapExtract
{
    public required string Name { get; init; }
    public GamePosition? Position { get; init; }
    /// <summary>"pmc", "scav", "shared" — kept raw.</summary>
    public string? Faction { get; init; }
}

/// <summary>A point in EFT's world space. Y is vertical (floor), X/Z are the ground plane.</summary>
public readonly record struct GamePosition(double X, double Y, double Z);
