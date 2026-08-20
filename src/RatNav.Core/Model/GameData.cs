namespace RatNav.Core.Model;

/// <summary>
/// Everything RatNav knows about the game, as of one fetch from tarkov.dev.
/// Cached to disk keyed by game version so a patch produces a new file rather than
/// overwriting a working one.
/// </summary>
public sealed record GameData
{
    /// <summary>
    /// What shape this document is in. Bumped whenever a field is added that a cached copy would
    /// not have.
    ///
    /// <para>Without it, adding a field means every existing install serves a cache missing it
    /// until the six-hour age check happens to fire — so a new layer looks broken rather than
    /// absent, and the person who has it worst is whoever just updated.</para>
    /// </summary>
    public const int CurrentSchema = 8;

    /// <summary>The schema this copy was written with. Zero on anything written before schemas.</summary>
    public int Schema { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>Game version this data was fetched against, if known (from the EFT log directory name).</summary>
    public string? GameVersion { get; init; }

    public IReadOnlyList<TaskDef> Tasks { get; init; } = [];
    public IReadOnlyList<ItemDef> Items { get; init; } = [];
    public IReadOnlyList<HideoutStation> HideoutStations { get; init; } = [];
    public IReadOnlyList<MapDef> Maps { get; init; } = [];
    public IReadOnlyList<BarterDef> Barters { get; init; } = [];

    public IReadOnlyList<CraftDef> Crafts { get; init; } = [];
    public IReadOnlyList<TraderDef> Traders { get; init; } = [];
}

/// <summary>
/// A trader, and what each of their loyalty levels costs.
///
/// <para>Loyalty gates quests — 109 of them — so without this RatNav offers work you cannot take
/// yet. It cannot know your reputation or how much you have spent, but it can know that Prapor
/// level 3 needs player level 21, which is enough to stop pretending a quest is reachable.</para>
/// </summary>
public sealed record TraderDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NormalizedName { get; init; }

    /// <summary>The trader's portrait, so a list of traders looks like the ones in the game.</summary>
    public string? ImageUrl { get; init; }

    public IReadOnlyList<TraderLevel> Levels { get; init; } = [];
}

public readonly record struct TraderLevel(int Level, int RequiredPlayerLevel, double RequiredReputation);

/// <summary>
/// One trader barter: what you hand over, and what you get for it.
///
/// <para>This is the answer to "is this junk worth keeping?" for a large part of the loot table.
/// Plenty of items are worthless on the flea and worth a great deal to a barter, and that is not
/// something a price tag can tell you.</para>
/// </summary>
public sealed record BarterDef
{
    public required string Id { get; init; }
    public required string TraderId { get; init; }
    public string? TraderName { get; init; }

    /// <summary>Loyalty level the trader must be at before this trade appears.</summary>
    public int MinTraderLevel { get; init; }

    /// <summary>Task that unlocks this barter, when one does.</summary>
    public string? TaskUnlockId { get; init; }

    public IReadOnlyList<BarterItem> RequiredItems { get; init; } = [];
    public BarterItem? OfferedItem { get; init; }
}

/// <summary>
/// One side of a trade. The count is fractional because tarkov.dev records currency-priced trades
/// that way — "155.1" is real data, not a mistake.
/// </summary>
public readonly record struct BarterItem(string ItemId, double Count);

/// <summary>
/// A hideout craft: what it consumes, what it produces, and the station level that can run it.
/// </summary>
public sealed record CraftDef
{
    public required string Id { get; init; }
    public required string StationId { get; init; }
    public string? StationName { get; init; }

    /// <summary>The station level needed. A craft you cannot run is not one to be hoarding for.</summary>
    public int StationLevel { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<BarterItem> RequiredItems { get; init; } = [];
    public BarterItem? ProducedItem { get; init; }
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

    /// <summary>
    /// Trader loyalty this quest needs before it is offered. Real, and previously unmodelled —
    /// which is why quests you cannot yet take were being listed as ready.
    /// </summary>
    public IReadOnlyList<TraderLevelRequirement> TraderRequirements { get; init; } = [];

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
    public string? NormalizedName { get; init; }
    public IReadOnlyList<HideoutLevel> Levels { get; init; } = [];
}

public sealed record HideoutLevel
{
    public required string Id { get; init; }
    public required int Level { get; init; }
    public IReadOnlyList<ObjectiveItem> ItemRequirements { get; init; } = [];

    /// <summary>
    /// Other stations that must already be built, and to what level.
    ///
    /// <para>This is what makes a hideout a build order rather than a shopping list. Without it
    /// every un-built level looks equally reachable, and a list of everything the hideout will
    /// ever want is not something anyone can act on.</para>
    /// </summary>
    public IReadOnlyList<StationLevelRequirement> StationRequirements { get; init; } = [];

    /// <summary>Trader loyalty needed before this level can be built.</summary>
    public IReadOnlyList<TraderLevelRequirement> TraderRequirements { get; init; } = [];

    /// <summary>Character skills needed. Rare, and no reason to hide it when it applies.</summary>
    public IReadOnlyList<SkillRequirement> SkillRequirements { get; init; } = [];

    /// <summary>Build time in seconds, for judging whether to start it before a raid.</summary>
    public int ConstructionTimeSeconds { get; init; }

    /// <summary>What the level gives you, in the game's own words.</summary>
    public string? Description { get; init; }
}

public readonly record struct StationLevelRequirement(string StationId, int Level);
public readonly record struct TraderLevelRequirement(string TraderId, string? TraderName, int Level);
public readonly record struct SkillRequirement(string Name, int Level);

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

    /// <summary>Named places on this map, for naming route stops the way a player would.</summary>
    public IReadOnlyList<MapLabel> Labels { get; init; } = [];

    /// <summary>
    /// The floor a given elevation falls on, or null when the map has no levels or none matches.
    /// This is what lets a position fix choose the floor without anyone touching a control.
    /// </summary>
    public MapFloor? FloorAt(double height) =>
        Image?.Floors.LastOrDefault(f => f.Covers(height));

    /// <summary>The nearest named place to a position, for describing a stop in words.</summary>
    public MapLabel? NearestLabel(GamePosition position, double withinMetres = 120)
    {
        MapLabel? best = null;
        var bestDistance = withinMetres;

        foreach (var label in Labels)
        {
            var dx = label.Position.X - position.X;
            var dz = label.Position.Z - position.Z;
            var distance = Math.Sqrt(dx * dx + dz * dz);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = label;
            }
        }

        return best;
    }
}

/// <summary>
/// Calibration for turning game coordinates into pixels on a map image.
/// Mirrors the shape TarkovTracker/tarkovdata publishes in maps.json.
/// </summary>
public sealed record MapImage
{
    /// <summary>Remote URL the image is downloaded from. Images are never committed to the repo.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Raster tiles for this map, as a <c>{z}/{x}/{y}</c> template, when the source has them.
    ///
    /// <para>Drawn beneath the vector to give a map that looks like a place rather than a diagram.
    /// From the same project as the SVG and by the same authors, who are credited per map — which
    /// is what makes using them reasonable where a commercial mapmaker's tiles would not be.</para>
    /// </summary>
    public string? TilePath { get; init; }

    /// <summary>
    /// The Leaflet transformation placing game coordinates in the tile grid: <c>[a, b, c, d]</c>
    /// giving <c>(a·x + b, c·z + d)</c>. Without it the tiles cannot be lined up with anything.
    /// </summary>
    public double[]? TileTransform { get; init; }

    public int MinZoom { get; init; }
    public int MaxZoom { get; init; }

    /// <summary>
    /// What tarkovdata declares as the map's rotation. Kept for reference and display only —
    /// it does not determine the coordinate mapping. See <see cref="Mapping"/>.
    /// </summary>
    public required int CoordinateRotation { get; init; }

    /// <summary>How world axes lay onto this image. Solved per map; never computed from the rotation.</summary>
    public AxisMapping Mapping { get; init; } = AxisMapping.Direct;

    /// <summary>How far the mapping can be trusted, and why.</summary>
    public CalibrationConfidence Confidence { get; init; } = CalibrationConfidence.Unknown;

    /// <summary>Plain-language account of how the mapping was arrived at.</summary>
    public string? CalibrationReason { get; init; }

    /// <summary>Game-world bounds of the image, as [[x1, z1], [x2, z2]].</summary>
    public required double[][] Bounds { get; init; }

    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }

    /// <summary>The SVG group drawn by default — the ground level on most maps.</summary>
    public string? DefaultFloor { get; init; }

    /// <summary>
    /// Levels of a multi-storey map, bottom to top, each with the world height band it covers.
    /// Seven of ten maps have more than one, and Streets has seven.
    /// </summary>
    public IReadOnlyList<MapFloor> Floors { get; init; } = [];

    /// <summary>Who drew this map. Shown in the credits — these are community mapmakers' work.</summary>
    public string? Author { get; init; }
    public string? AuthorLink { get; init; }

    /// <summary>
    /// Whether pins on this map can be trusted. Surfaced rather than hidden: a pin that might be
    /// wrong should say so, and a map nobody has confirmed is exactly where a silent error hides.
    /// </summary>
    public bool CalibrationVerified =>
        Confidence is CalibrationConfidence.Verified or CalibrationConfidence.Derived;
}

/// <summary>One level of a multi-storey map.</summary>
public sealed record MapFloor
{
    public required string Name { get; init; }

    /// <summary>Id of the group inside the map's SVG that draws this level.</summary>
    public required string Layer { get; init; }

    /// <summary>World height band this level covers, when known.</summary>
    public double? MinHeight { get; init; }
    public double? MaxHeight { get; init; }

    /// <summary>Whether a player at this elevation is on this floor.</summary>
    public bool Covers(double height) =>
        (MinHeight is null || height >= MinHeight) && (MaxHeight is null || height < MaxHeight);
}

/// <summary>
/// A named place on a map — "Big Red", "Dorms", "Resort". What players actually call parts of a
/// map, so a route can read as somewhere to go rather than a pair of coordinates.
/// </summary>
public sealed record MapLabel
{
    public required string Text { get; init; }
    public required GamePosition Position { get; init; }

    /// <summary>Height band this label belongs to, for maps whose levels have different names.</summary>
    public double? MinHeight { get; init; }
    public double? MaxHeight { get; init; }
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
