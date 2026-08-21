using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Data;

/// <summary>Raised when tarkov.dev is unreachable, erroring, or returns something we can't read.</summary>
public sealed class TarkovDevException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reads game data from tarkov.dev's JSON API at <c>json.tarkov.dev</c>.
///
/// <para>This deliberately does not use the GraphQL API at <c>api.tarkov.dev</c>. That endpoint
/// returned <c>"GraphQL server unavailable"</c> for every query across an entire day of
/// development while <c>json.tarkov.dev</c> served the same data in under half a second — and
/// tarkov.dev's own website reads from the JSON endpoints, which is the strongest available
/// signal about which one is actually maintained. The JSON API is also plain cacheable documents
/// rather than a query language, so there is no schema to drift out from under us.</para>
///
/// <para><b>Shape.</b> The JSON API is normalized where GraphQL was not: collections are keyed by
/// id rather than listed, references are bare id strings, and human-readable text is replaced by
/// translation keys. Names come from a parallel document at <c>{path}_{lang}</c> mapping key to
/// text. So every fetch here is a pair of requests, and the mapping resolves names as it goes.</para>
///
/// <para>Everything fails loudly by throwing. Deciding what to do about a failure is
/// <see cref="GameDataCache"/>'s job, and its answer is always "keep serving the last good
/// data" — a refresh that fails must never leave a player without a planner mid-raid.</para>
/// </summary>
public sealed class TarkovDevClient(HttpClient http)
{
    public const string BaseUrl = "https://json.tarkov.dev/";

    /// <summary>"regular", "pve", or "pvp-season". Regular is the standard wipe.</summary>
    public string GameMode { get; set; } = "regular";

    public string Language { get; set; } = "en";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TaskDef>> GetTasksAsync(CancellationToken ct = default)
    {
        var (payload, text) = await FetchAsync<TasksPayload>($"{GameMode}/tasks", ct);
        var traders = await GetTraderNamesAsync(ct);

        return
        [
            .. (payload.Tasks ?? [])
                .Where(pair => pair.Value is not null)
                .Select(pair => MapTask(pair.Key, pair.Value!, text, traders))
        ];
    }

    public async Task<IReadOnlyList<ItemDef>> GetItemsAsync(CancellationToken ct = default)
    {
        var (payload, text) = await FetchAsync<ItemsPayload>($"{GameMode}/items", ct);

        return
        [
            .. (payload.Items ?? [])
                .Where(pair => pair.Value is not null)
                .Select(pair => new ItemDef
                {
                    Id = pair.Key,
                    Name = text.Of(pair.Value!.Name) ?? "(unnamed)",
                    ShortName = text.Of(pair.Value.ShortName),
                    NormalizedName = pair.Value.NormalizedName,
                    BasePrice = pair.Value.BasePrice,
                    Avg24hPrice = pair.Value.Avg24hPrice,
                    Width = pair.Value.Width ?? 1,
                    Height = pair.Value.Height ?? 1,
                    IconUrl = pair.Value.IconLink,
                    WikiUrl = pair.Value.WikiLink,
                })
        ];
    }

    public async Task<IReadOnlyList<HideoutStation>> GetHideoutStationsAsync(CancellationToken ct = default)
    {
        // Hideout is the one endpoint whose payload is the station map itself rather than a
        // named collection inside it.
        var (stations, text) = await FetchAsync<Dictionary<string, StationDto?>>($"{GameMode}/hideout", ct);
        var traders = await GetTraderNamesAsync(ct);

        return
        [
            .. stations
                .Where(pair => pair.Value is not null)
                .Select(pair => new HideoutStation
                {
                    Id = pair.Key,
                    Name = text.Of(pair.Value!.Name) ?? "(unnamed station)",
                    NormalizedName = pair.Value.NormalizedName,
                    ImageUrl = pair.Value.ImageLink,
                    Levels =
                    [
                        .. (pair.Value.Levels ?? [])
                            .Select(level => new HideoutLevel
                            {
                                Id = level.Id ?? $"{pair.Key}-{level.Level}",
                                Level = level.Level,
                                ConstructionTimeSeconds = level.ConstructionTime,
                                Description = text.Of(level.Description),
                                ItemRequirements =
                                [
                                    .. (level.ItemRequirements ?? [])
                                        .Where(r => r.Item is { Length: > 0 })
                                        .Select(r => new ObjectiveItem
                                        {
                                            ItemId = r.Item!,
                                            Count = r.Count,

                                            // Hideout items are found-in-raid more often than
                                            // people expect, and it decides what you can buy your
                                            // way out of.
                                            FoundInRaid = r.Attributes?.FoundInRaid ?? false,
                                        })
                                ],
                                StationRequirements =
                                [
                                    .. (level.StationLevelRequirements ?? [])
                                        .Where(r => r.Station is { Length: > 0 })
                                        .Select(r => new StationLevelRequirement(r.Station!, r.Level))
                                ],
                                TraderRequirements =
                                [
                                    .. (level.TraderRequirements ?? [])
                                        .Where(r => r.Trader is { Length: > 0 })
                                        .Select(r => new TraderLevelRequirement(
                                            r.Trader!, traders.GetValueOrDefault(r.Trader!), r.Level))
                                ],
                                SkillRequirements =
                                [
                                    .. (level.SkillRequirements ?? [])
                                        .Where(r => r.Name is { Length: > 0 })
                                        .Select(r => new SkillRequirement(r.Name!, r.Level))
                                ],
                            })
                    ],
                })
        ];
    }

    /// <summary>
    /// Trader barters — what each trade costs and what it hands back.
    ///
    /// <para>Barters answer a question prices cannot: plenty of loot is near worthless on the flea
    /// and worth carrying anyway because a trader wants it. Trader names are resolved here so the
    /// answer reads "Prapor LL2" rather than a hex id.</para>
    /// </summary>
    public async Task<IReadOnlyList<BarterDef>> GetBartersAsync(CancellationToken ct = default)
    {
        // No translation document for this one — it is all ids and numbers.
        var payload = await GetAsync<Envelope<List<BarterDto>>>($"{GameMode}/barters", ct);
        var barters = payload.Data ?? [];

        var traders = await GetTraderNamesAsync(ct);

        return
        [
            .. barters
                .Where(b => b.Id is { Length: > 0 } && b.Trader is { Length: > 0 })
                .Select(b => new BarterDef
                {
                    Id = b.Id!,
                    TraderId = b.Trader!,
                    TraderName = traders.GetValueOrDefault(b.Trader!),
                    MinTraderLevel = b.MinTraderLevel,
                    TaskUnlockId = b.TaskUnlock,
                    RequiredItems =
                    [
                        .. (b.RequiredItems ?? [])
                            .Where(r => r.Item is { Length: > 0 })
                            .Select(r => new BarterItem(r.Item!, r.Count))
                    ],
                    OfferedItem = b.OfferedItem?.Item is { Length: > 0 } offered
                        ? new BarterItem(offered, b.OfferedItem.Count)
                        : null,
                })
        ];
    }

    /// <summary>
    /// Map metadata. Carries no image or coordinate calibration — tarkov.dev keeps its map
    /// drawings private — so <see cref="MapAssets"/> joins these against TarkovTracker/tarkovdata
    /// for bounds and imagery.
    /// </summary>
    public async Task<IReadOnlyList<MapDef>> GetMapsAsync(CancellationToken ct = default)
    {
        var (payload, text) = await FetchAsync<MapsPayload>($"{GameMode}/maps", ct);

        return
        [
            .. (payload.Maps ?? [])
                .Where(pair => pair.Value is not null)
                .Select(pair => new MapDef
                {
                    Id = pair.Key,
                    Name = text.Of(pair.Value!.Name) ?? pair.Value.NormalizedName ?? "(unnamed map)",
                    NormalizedName = pair.Value.NormalizedName,

                    // nameId is what the game writes into its own logs — "bigmap" for Customs,
                    // "factory4_day" for Factory — so this is what turns a raid start into a map.
                    LogAliases = pair.Value.NameId is { Length: > 0 } id ? [id] : [],

                    Extracts =
                    [
                        .. (pair.Value.Extracts ?? [])
                            .Where(e => e.Name is not null)
                            .Select(e => new MapExtract
                            {
                                Name = text.Of(e.Name) ?? e.Name!,
                                Faction = e.Faction,
                                Position = e.Position?.ToGamePosition(),
                            })
                    ],
                })
        ];
    }

    /// <summary>
    /// Hideout crafts — what each one consumes, and the station level that can run it.
    ///
    /// <para>The level matters as much as the recipe. Two hundred crafts exist and most of them
    /// need a station past where any given hideout is, so a list that ignores level is a list of
    /// things to collect for a machine you do not own.</para>
    /// </summary>
    public async Task<IReadOnlyList<CraftDef>> GetCraftsAsync(CancellationToken ct = default)
    {
        var payload = await GetAsync<Envelope<List<CraftDto>>>($"{GameMode}/crafts", ct);
        var crafts = payload.Data ?? [];

        // Station names come from the hideout document, so a craft reads "Workbench 2" rather
        // than a hex id and a number.
        var (stations, text) = await FetchAsync<Dictionary<string, StationDto?>>($"{GameMode}/hideout", ct);

        var names = stations
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => text.Of(pair.Value!.Name) ?? pair.Key);

        return
        [
            .. crafts
                .Where(c => c.Id is { Length: > 0 } && c.Station is { Length: > 0 })
                .Select(c => new CraftDef
                {
                    Id = c.Id!,
                    StationId = c.Station!,
                    StationName = names.GetValueOrDefault(c.Station!),
                    StationLevel = c.Level,
                    Duration = TimeSpan.FromSeconds(c.Duration),
                    RequiredItems =
                    [
                        .. (c.RequiredItems ?? [])
                            .Where(r => r.Item is { Length: > 0 })
                            .Select(r => new BarterItem(r.Item!, r.Count))
                    ],
                    ProducedItem = c.ProductItem?.Item is { Length: > 0 } made
                        ? new BarterItem(made, c.ProductItem.Count)
                        : null,
                })
        ];
    }

    /// <summary>
    /// Traders, with what each loyalty level costs.
    ///
    /// <para>Loyalty gates quests, so without this RatNav offers work you cannot take yet. It
    /// cannot see your reputation or your spending, but it can see that Prapor level 3 wants
    /// player level 21.</para>
    /// </summary>
    public async Task<IReadOnlyList<TraderDef>> GetTradersAsync(CancellationToken ct = default)
    {
        var (traders, text) = await FetchAsync<Dictionary<string, TraderDto?>>($"{GameMode}/traders", ct);

        return
        [
            .. traders
                .Where(pair => pair.Value is not null)
                .Select(pair => new TraderDef
                {
                    Id = pair.Key,
                    Name = text.Of(pair.Value!.Name) ?? pair.Value.NormalizedName ?? pair.Key,
                    NormalizedName = pair.Value.NormalizedName,
                    ImageUrl = pair.Value.ImageLink,
                    Levels =
                    [
                        .. (pair.Value.Levels ?? [])
                            .Select(l => new TraderLevel(l.Level, l.RequiredPlayerLevel, l.RequiredReputation))
                            .OrderBy(l => l.Level)
                    ],
                })
        ];
    }

    private async Task<Dictionary<string, string>> GetTraderNamesAsync(CancellationToken ct)
    {
        // Tasks reference traders by id only, and "which trader" is the first thing a player
        // sorts quests by, so this small extra fetch earns its place.
        try
        {
            // Traders sit directly under `data`, like hideout stations, rather than nested in a
            // named collection the way tasks and items are.
            var (traders, text) = await FetchAsync<Dictionary<string, TraderDto?>>($"{GameMode}/traders", ct);

            return traders
                .Where(pair => pair.Value is not null)
                .ToDictionary(
                    pair => pair.Key,
                    pair => text.Of(pair.Value!.Name) ?? pair.Key,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (TarkovDevException)
        {
            // Quests without a trader name are still perfectly usable.
            return [];
        }
    }

    /// <summary>Fetches a document and its translation table together.</summary>
    private async Task<(T Data, Translations Text)> FetchAsync<T>(string path, CancellationToken ct)
    {
        var dataTask = GetAsync<Envelope<T>>(path, ct);
        var textTask = GetAsync<Envelope<Dictionary<string, string>>>($"{path}_{Language}", ct);

        await Task.WhenAll(dataTask, textTask);

        var data = (await dataTask).Data
            ?? throw new TarkovDevException($"tarkov.dev returned no data for {path}.");

        return (data, new Translations((await textTask).Data));
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(BaseUrl + path, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TarkovDevException($"Could not reach tarkov.dev for {path}.", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new TarkovDevException($"tarkov.dev returned HTTP {(int)response.StatusCode} for {path}.");

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                ?? throw new TarkovDevException($"tarkov.dev returned an empty body for {path}.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TarkovDevException($"tarkov.dev returned unreadable JSON for {path}.", ex);
        }
    }

    private static TaskDef MapTask(
        string id, TaskDto task, Translations text, IReadOnlyDictionary<string, string> traders) => new()
    {
        Id = id,
        Name = text.Of(task.Name) ?? "(unnamed task)",
        NormalizedName = task.NormalizedName,
        TraderName = task.Trader is { Length: > 0 } t ? traders.GetValueOrDefault(t, t) : null,
        MinPlayerLevel = task.MinPlayerLevel,
        Kappa = task.KappaRequired ?? false,
        WikiUrl = task.WikiLink,
        TraderRequirements =
        [
            .. (task.TraderRequirements ?? [])
                .Where(r => r.Trader is { Length: > 0 }
                    && string.Equals(r.RequirementType, "level", StringComparison.OrdinalIgnoreCase))
                .Select(r => new TraderLevelRequirement(
                    r.Trader!, traders.GetValueOrDefault(r.Trader!), r.Value))
        ],

        PrerequisiteTaskIds =
        [
            .. (task.TaskRequirements ?? [])
                .Select(r => r.Task)
                .Where(t => t is { Length: > 0 })
                .Select(t => t!)
        ],
        Objectives = [.. (task.Objectives ?? []).Where(o => o.Id is not null).Select(o => MapObjective(o, text))],

        // The quest's own key list, which is the only complete one.
        //
        // Keys are also recorded against individual objectives, and that is where RatNav used to
        // read them — but only an objective with a zone becomes a waypoint, and the plan's "bring
        // these" list is aggregated from waypoints. So every key belonging to an objective without
        // coordinates was dropped: 29 of the 57 quests that need one.
        //
        // Flattened across maps. A plan is for one raid on one map, and it knows which.
        NeededKeyItemIds =
        [
            .. (task.NeededKeys ?? [])
                .SelectMany(k => k.Keys ?? [])
                .Where(k => k is { Length: > 0 })
                .Select(k => k!)
                .Distinct()
        ],
    };

    private static TaskObjective MapObjective(ObjectiveDto o, Translations text)
    {
        // An objective can name several zones; the first positioned one is the pin we plot.
        var zone = (o.Zones ?? []).FirstOrDefault(z => z.Position is not null);

        // Maps come from the objective's own list, falling back to whatever its zones name —
        // some objectives carry a positioned zone but leave `maps` empty.
        var mapIds = (o.Maps ?? [])
            .Concat((o.Zones ?? []).Select(z => z.Map))
            .Where(m => m is { Length: > 0 })
            .Select(m => m!)
            .Distinct()
            .ToArray();

        var items = new List<ObjectiveItem>();
        foreach (var itemId in o.Items ?? [])
        {
            if (itemId is { Length: > 0 })
                items.Add(new ObjectiveItem { ItemId = itemId, Count = o.Count ?? 1, FoundInRaid = o.FoundInRaid ?? false });
        }
        if (o.MarkerItem is { Length: > 0 })
            items.Add(new ObjectiveItem { ItemId = o.MarkerItem, Count = 1 });

        return new TaskObjective
        {
            Id = o.Id!,
            Description = text.Of(o.Description) ?? "",
            Type = o.Type,
            Optional = o.Optional ?? false,
            MapIds = mapIds,
            Position = zone?.Position?.ToGamePosition(),
            Items = items,

            // requiredKeys is a list of alternatives: any one key in a group opens the way.
            // Flattened here; the raid plan groups them again when it says what to bring.
            NeededKeyItemIds =
            [
                .. (o.RequiredKeys ?? [])
                    .SelectMany(group => group ?? [])
                    .Where(k => k is { Length: > 0 })
                    .Select(k => k!)
                    .Distinct()
            ],
        };
    }

    /// <summary>Resolves translation keys, passing anything unrecognised straight through.</summary>
    private readonly record struct Translations(Dictionary<string, string>? Table)
    {
        public string? Of(string? key) =>
            key is null ? null : Table?.GetValueOrDefault(key) ?? key;
    }

    // ---- Wire shapes. Deliberately permissive: everything nullable, so a field going missing
    // ---- upstream degrades one entry instead of throwing out the whole response.

    private sealed record Envelope<T>([property: JsonPropertyName("data")] T? Data);

    private sealed record TasksPayload(Dictionary<string, TaskDto?>? Tasks);
    private sealed record ItemsPayload(Dictionary<string, ItemDto?>? Items);
    private sealed record MapsPayload(Dictionary<string, MapDto?>? Maps);

    private sealed record TraderLevelDto(int Level, int RequiredPlayerLevel, double RequiredReputation);
    private sealed record TaskTraderRequirementDto(string? Trader, string? RequirementType, int Value);

    private sealed record TaskDto(
        string? Name, string? NormalizedName, string? WikiLink, int? MinPlayerLevel,
        bool? KappaRequired, string? Trader, List<TaskRequirementDto>? TaskRequirements,
        List<TaskTraderRequirementDto>? TraderRequirements,
        List<ObjectiveDto>? Objectives, List<NeededKeyDto>? NeededKeys);

    /// <summary>The keys a quest needs, listed per map.</summary>
    private sealed record NeededKeyDto(string? Map, List<string?>? Keys);

    private sealed record TaskRequirementDto(string? Task);

    private sealed record ObjectiveDto(
        string? Id, string? Type, string? Description, bool? Optional,
        List<string?>? Maps, List<ZoneDto>? Zones, List<string?>? Items, string? MarkerItem,
        int? Count, bool? FoundInRaid, List<List<string?>?>? RequiredKeys);

    private sealed record ZoneDto(string? Map, PositionDto? Position);

    private sealed record PositionDto(double X, double Y, double Z)
    {
        public GamePosition ToGamePosition() => new(X, Y, Z);
    }

    private sealed record ItemDto(
        string? Name, string? ShortName, string? NormalizedName,
        int? BasePrice, int? Avg24hPrice, int? Width, int? Height, string? IconLink, string? WikiLink);

    private sealed record StationDto(
        string? Name, string? NormalizedName, string? ImageLink, List<StationLevelDto>? Levels);

    private sealed record StationLevelDto(
        string? Id,
        int Level,
        int ConstructionTime,
        string? Description,
        List<RequirementItemDto>? ItemRequirements,
        List<StationRequirementDto>? StationLevelRequirements,
        List<TraderRequirementDto>? TraderRequirements,
        List<SkillRequirementDto>? SkillRequirements);

    private sealed record RequirementItemDto(string? Item, int Count, RequirementAttributesDto? Attributes);
    private sealed record RequirementAttributesDto(bool FoundInRaid);
    private sealed record StationRequirementDto(string? Station, int Level);
    private sealed record TraderRequirementDto(string? Trader, int Level);
    private sealed record SkillRequirementDto(string? Name, int Level);

    private sealed record TraderDto(
        string? Name, string? NormalizedName, string? ImageLink, List<TraderLevelDto>? Levels);

    private sealed record BarterDto(
        string? Id,
        string? Trader,
        string? TaskUnlock,
        int MinTraderLevel,
        List<BarterItemDto>? RequiredItems,
        BarterItemDto? OfferedItem);

    // Counts are fractional. A barter that costs "155.1" of something is not a rounding error in
    // the source — it is how tarkov.dev records trades priced in currency, and 313 of the 789
    // barters have one. Reading them as integers failed the whole document.
    private sealed record BarterItemDto(string? Item, double Count);

    private sealed record CraftDto(
        string? Id, string? Station, int Level, double Duration,
        List<BarterItemDto>? RequiredItems, BarterItemDto? ProductItem);

    private sealed record MapDto(
        string? Name, string? NormalizedName, string? NameId, List<ExtractDto>? Extracts);

    private sealed record ExtractDto(string? Name, string? Faction, PositionDto? Position);
}
