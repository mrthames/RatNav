using System.Text.Json;
using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Data;

/// <summary>How a refresh went, so the UI can say something honest about data freshness.</summary>
public sealed record RefreshResult
{
    public required bool Succeeded { get; init; }
    public required GameData Data { get; init; }

    /// <summary>Why the refresh failed, when it did. The old data is still in <see cref="Data"/>.</summary>
    public string? Error { get; init; }

    /// <summary>True when we are serving data fetched before this session, because the refresh failed.</summary>
    public bool ServingStale => !Succeeded;
}

/// <summary>
/// Owns RatNav's copy of the game data: loads it from disk, refreshes it from tarkov.dev, and
/// above all keeps serving whatever it last had.
///
/// The governing rule is <b>fail soft, never fail closed</b>. tarkov.dev goes down (it was down
/// while this was written), schemas drift, laptops go offline mid-session. None of that is
/// allowed to leave a player staring at an empty planner, so every failure path here ends in
/// "return the last good data and say so" rather than throwing.
///
/// Cache files are keyed by game version, so a patch writes a new file instead of overwriting
/// one that works.
/// </summary>
public sealed class GameDataCache(TarkovDevClient client, MapAssets mapAssets, string cacheDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameData? _current;

    /// <summary>How old data may get before <see cref="EnsureFreshAsync"/> refreshes it.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(6);

    /// <summary>The data currently being served, or null before the first load.</summary>
    public GameData? Current => _current;

    /// <summary>
    /// Loads from disk if we have nothing, then refreshes if what we have is older than
    /// <see cref="MaxAge"/>. Safe to call on every request — it is cheap when the data is fresh.
    /// </summary>
    public async Task<RefreshResult> EnsureFreshAsync(string? gameVersion = null, CancellationToken ct = default)
    {
        var loaded = _current ?? LoadFromDisk(gameVersion);

        var patched = loaded is not null && gameVersion is not null && loaded.GameVersion != gameVersion;
        var stale = loaded is null || DateTimeOffset.UtcNow - loaded.FetchedAt > MaxAge;

        if (loaded is not null && !stale && !patched)
        {
            _current = loaded;
            return new RefreshResult { Succeeded = true, Data = loaded };
        }

        return await RefreshAsync(gameVersion, ct);
    }

    /// <summary>
    /// Fetches everything from tarkov.dev and writes it to disk. On failure, returns the last
    /// good data with <see cref="RefreshResult.Succeeded"/> false rather than throwing.
    /// </summary>
    public async Task<RefreshResult> RefreshAsync(string? gameVersion = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var fresh = await FetchAsync(gameVersion, ct);
            Save(fresh);
            _current = fresh;
            return new RefreshResult { Succeeded = true, Data = fresh };
        }
        catch (Exception ex) when (ex is TarkovDevException or HttpRequestException or IOException)
        {
            var fallback = _current ?? LoadFromDisk(gameVersion) ?? Empty(gameVersion);
            _current = fallback;

            return new RefreshResult
            {
                Succeeded = false,
                Data = fallback,
                Error = ex.Message,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GameData> FetchAsync(string? gameVersion, CancellationToken ct)
    {
        // Quests, items and maps come from tarkov.dev; map calibration comes from tarkovdata on
        // GitHub. Two independent hosts, so one being down should not blank what the other
        // returned — each result is captured separately and missing pieces fall back to what we
        // already had. Only a total washout is treated as a failed refresh.
        var previous = _current ?? LoadFromDisk(gameVersion);

        var tasksQuery = Try(() => client.GetTasksAsync(ct));
        var itemsQuery = Try(() => client.GetItemsAsync(ct));
        var hideoutQuery = Try(() => client.GetHideoutStationsAsync(ct));
        var mapsQuery = Try(() => client.GetMapsAsync(ct));
        var bartersQuery = Try(() => client.GetBartersAsync(ct));
        var calibrationQuery = Try(() => mapAssets.GetCalibrationAsync(ct));

        await Task.WhenAll(tasksQuery, itemsQuery, hideoutQuery, mapsQuery, bartersQuery, calibrationQuery);

        var fetchedTasks = await tasksQuery;
        var fetchedItems = await itemsQuery;
        var fetchedHideout = await hideoutQuery;
        var fetchedMaps = await mapsQuery;
        var fetchedBarters = await bartersQuery;
        var calibration = await calibrationQuery;

        // Decide success on what we actually fetched, before any fallback — otherwise a total
        // outage merges the old data forward, stamps it with a new timestamp, and reports a
        // refresh that never happened.
        if (fetchedTasks is null && fetchedItems is null && fetchedHideout is null
            && fetchedMaps is null && calibration is null)
        {
            throw new TarkovDevException("Every data source was unreachable.");
        }

        var tasks = fetchedTasks ?? previous?.Tasks;
        var items = fetchedItems ?? previous?.Items;
        var hideout = fetchedHideout ?? previous?.HideoutStations;
        var barters = fetchedBarters ?? previous?.Barters;
        var mapsQueryResult = fetchedMaps ?? [];
        var calibrations = calibration ?? [];

        // Both sources are tarkov.dev now, so they join on normalized name — no id cross-walk,
        // and no chance of the map metadata describing a different map from the game data.
        var byKey = calibrations.ToDictionary(c => c.Key, c => c, StringComparer.OrdinalIgnoreCase);

        var fromTarkovDev = fetchedMaps ?? [];

        var maps = fromTarkovDev
            .Select(m => m.NormalizedName is { Length: > 0 } key && byKey.TryGetValue(key, out var c)
                ? m with { Image = c.Image, Labels = c.Labels }
                : m)
            .ToList();

        // Maps the calibration knows about that the game data did not return — including the case
        // where it returned nothing at all. Better a map with no objectives than no map.
        var known = maps
            .Select(m => m.NormalizedName)
            .Where(n => n is { Length: > 0 })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in calibrations.Where(c => !known.Contains(c.Key)))
        {
            maps.Add(new MapDef
            {
                Id = c.Key,
                Name = c.Name,
                NormalizedName = c.Key,
                Image = c.Image,
                Labels = c.Labels,
            });
        }

        // Calibration is solved here because it needs both halves: the image's proportions and the
        // extract positions. In practice every map now resolves to a plain (x, z) mapping — the
        // earlier rotation rules were compensating for a different source's bad bounds.
        for (var i = 0; i < maps.Count; i++)
        {
            if (maps[i].Image is not { } image) continue;

            var size = await mapAssets.GetImageSizeAsync(image, ct);

            var solved = CalibrationSolver.Solve(
                maps[i].NormalizedName,
                image.Bounds,
                size?.Width ?? 0,
                size?.Height ?? 0,
                [.. maps[i].Extracts.Where(e => e.Position is not null).Select(e => e.Position!.Value)]);

            maps[i] = maps[i] with
            {
                Image = image with
                {
                    PixelWidth = size?.Width ?? 0,
                    PixelHeight = size?.Height ?? 0,
                    Mapping = solved.Mapping,
                    Confidence = solved.Confidence,
                    CalibrationReason = solved.Reason,
                },
            };
        }

        return new GameData
        {
            FetchedAt = DateTimeOffset.UtcNow,
            GameVersion = gameVersion,
            Tasks = tasks ?? [],
            Items = items ?? [],
            HideoutStations = hideout ?? [],
            Maps = maps,
            Barters = barters ?? [],
        };
    }

    /// <summary>Runs a fetch, turning failure into null so one dead source cannot fail the rest.</summary>
    private static async Task<T?> Try<T>(Func<Task<T>> fetch) where T : class
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex) when (ex is TarkovDevException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the newest cache file, preferring one matching the running game version.
    /// Returns null when there is nothing cached or the file is unreadable.
    /// </summary>
    public GameData? LoadFromDisk(string? gameVersion = null)
    {
        if (!Directory.Exists(cacheDirectory)) return null;

        var candidates = Directory.GetFiles(cacheDirectory, "gamedata-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (gameVersion is not null)
        {
            var exact = PathFor(gameVersion);
            if (File.Exists(exact)) candidates.Insert(0, exact);
        }

        foreach (var path in candidates.Distinct())
        {
            try
            {
                var data = JsonSerializer.Deserialize<GameData>(File.ReadAllText(path), Json);
                if (data is not null) return data;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt or half-written cache file is not worth failing over — try the next one.
            }
        }

        return null;
    }

    private void Save(GameData data)
    {
        Directory.CreateDirectory(cacheDirectory);
        var path = PathFor(data.GameVersion);

        // Write to a temp file and move it into place, so a crash mid-write cannot leave a
        // corrupt cache behind for the next launch to trip over.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string? gameVersion) =>
        Path.Combine(cacheDirectory, $"gamedata-{Sanitize(gameVersion ?? "unknown")}.json");

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static GameData Empty(string? gameVersion) => new()
    {
        FetchedAt = DateTimeOffset.MinValue,
        GameVersion = gameVersion,
    };
}
