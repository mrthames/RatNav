using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RatNav.Core.Data;
using RatNav.Core.Maps;
using RatNav.Core.Model;
using RatNav.Core.Progress;
using RatNav.Core.Tracking;

namespace RatNav.Service;

public static class ApiEndpoints
{
    public static void MapRatNavApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---- status and refresh

        api.MapGet("/status", async (RatNavState state) =>
        {
            var refresh = await state.Cache.EnsureFreshAsync();
            return Results.Ok(state.Status(refresh));
        });

        api.MapPost("/refresh", async (RatNavState state) =>
        {
            var refresh = await state.Cache.RefreshAsync();

            // A failed refresh is not an HTTP error: we still have data, and the UI needs to
            // render it alongside an honest note about why it is old.
            return Results.Ok(state.Status(refresh));
        });

        // ---- items

        // Search returns the same shape as the needed and watchlist views, so one table
        // component renders all three and a row behaves identically wherever you found it.
        api.MapGet("/items/search", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, string q, int? limit) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var results = index.Search(q, limit ?? 25)
                .Select(item => index.GetNeeds(item.Id) ?? new ItemNeeds { Item = item })
                .Select(needs => TrackedItemView.From(tracker.Track(needs, progress)));

            return Results.Ok(results);
        });

        // What to actually pick up: only what active quests and un-built modules want, minus
        // what you already have. The unfiltered version is every item the game will ever ask
        // for, which is not a shopping list.
        api.MapGet("/items/needed", (RatNavState state, ItemTracker tracker, ProgressStore progress) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var results = index.AllNeeded()
                .Select(n => tracker.Track(n, progress))
                .Where(t => t.Remaining > 0)
                .OrderByDescending(t => t.FoundInRaid)
                .ThenByDescending(t => t.Remaining)
                .Select(TrackedItemView.From);

            return Results.Ok(results);
        });

        api.MapGet("/items/watchlist", (RatNavState state, ItemTracker tracker, ProgressStore progress) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var results = tracker.Watchlist
                .Select(w => index.GetNeeds(w.ItemId)
                    ?? new ItemNeeds { Item = index.GetItem(w.ItemId) ?? Unknown(w.ItemId) })
                .Select(n => TrackedItemView.From(tracker.Track(n, progress)));

            return Results.Ok(results);
        });

        api.MapPost("/items/{id}/have", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, string id, HaveRequest request) =>
        {
            if (request.Delta is { } delta) tracker.AdjustHave(id, delta);
            else if (request.Count is { } count) tracker.SetHave(id, count);
            else return Results.BadRequest(new { error = "Send either a count or a delta." });

            return Results.Ok(Track(state, tracker, progress, id));
        });

        api.MapPost("/items/{id}/watch", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, string id, WatchRequest request) =>
        {
            if (request.Watch) tracker.Watch(id, request.Note, request.Target);
            else tracker.Unwatch(id);

            return Results.Ok(Track(state, tracker, progress, id));
        });

        api.MapGet("/items/{id}", (RatNavState state, string id) =>
        {
            if (state.Index is not { } index) return Results.NotFound();

            var item = index.GetItem(id);
            return item is null
                ? Results.NotFound()
                : Results.Ok(ItemDetail.From(item, index.GetNeeds(id)));
        });

        // ---- progress

        api.MapGet("/progress", (RatNavState state, ProgressStore progress) =>
        {
            var tasks = state.Cache.Current?.Tasks ?? [];
            var summary = progress.Summarize(tasks);

            return Results.Ok(new
            {
                notStarted = summary[QuestState.NotStarted],
                active = summary[QuestState.Active],
                completed = summary[QuestState.Completed],
                failed = summary[QuestState.Failed],
                availableNow = progress.AvailableNow(tasks).Count(),
            });
        });

        api.MapPost("/progress/tasks/{id}", (ProgressStore progress, string id, TaskStateRequest request) =>
        {
            if (!Enum.TryParse<QuestState>(request.State, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Unknown quest state '{request.State}'." });

            progress.SetManual(id, parsed);
            return Results.Ok(new { id, state = parsed.ToString() });
        });

        api.MapPost("/progress/hideout/{id}", (ProgressStore progress, string id, HideoutLevelRequest request) =>
        {
            progress.SetHideoutLevel(id, request.Level);
            return Results.Ok(new { id, level = progress.HideoutLevelOf(id) });
        });

        // ---- tasks

        api.MapGet("/tasks", (RatNavState state) =>
            Results.Ok((state.Cache.Current?.Tasks ?? []).Select(TaskSummary.From)));

        api.MapGet("/tasks/{id}", (RatNavState state, string id) =>
        {
            var task = state.Cache.Current?.Tasks.FirstOrDefault(t => t.Id == id);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        // ---- maps

        api.MapGet("/maps", (RatNavState state) =>
            Results.Ok((state.Cache.Current?.Maps ?? []).Select(MapSummary.From)));

        // The map image, restyled for wherever it is about to be drawn.
        //
        // `ink` is the Diablo-style dial: `full` on a second monitor, `structure` or `outline`
        // over the game, where a solid map would bury the thing you are trying to see. The
        // restyle happens here rather than in each surface so the overlay and the web app can
        // never disagree about what "outline at 40%" looks like.
        api.MapGet("/maps/{id}/image", async (
            RatNavState state, MapAssets assets, string id, string? ink, double? opacity, string? accent) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            var path = await assets.EnsureImageAsync(map.Image);
            if (path is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var markup = await File.ReadAllTextAsync(path);

            var options = new MapInkOptions
            {
                Level = ParseInk(ink),
                Opacity = opacity ?? 1.0,
                Accent = string.IsNullOrWhiteSpace(accent) ? new MapInkOptions().Accent : accent,
            };

            return Results.Text(MapInk.Apply(markup, options), "image/svg+xml");
        });

        // Every objective that can be pinned on this map, already converted to image
        // coordinates. Doing the transform server-side is what keeps the WPF overlay and the
        // web app from drifting apart: there is one implementation, and both surfaces call it.
        api.MapGet("/maps/{id}/objectives", (RatNavState state, string id) =>
        {
            var data = state.Cache.Current;
            if (data is null) return Results.NotFound();

            var map = FindMap(state, id);
            if (map is null) return Results.NotFound();
            if (map.Image is null) return Results.Ok(Array.Empty<object>());

            var transform = new CoordinateTransform(map.Image);

            var pins =
                from task in data.Tasks
                from objective in task.Objectives
                // Match on the resolved map's own id, not the URL segment — the route accepts a
                // normalized name too, and comparing that against tarkov.dev ids silently
                // matched nothing.
                where objective.Position is not null && objective.MapIds.Contains(map.Id)
                let position = objective.Position.GetValueOrDefault()
                let point = transform.ToNormalized(position)
                select new ObjectivePin
                {
                    TaskId = task.Id,
                    TaskName = task.Name,
                    TraderName = task.TraderName,
                    ObjectiveId = objective.Id,
                    Description = objective.Description,
                    Type = objective.Type,
                    Optional = objective.Optional,
                    X = point.X,
                    Y = point.Y,
                    Elevation = position.Y,
                    NeededKeyItemIds = objective.NeededKeyItemIds,
                };

            return Results.Ok(pins);
        });

        // Converting a raw screenshot filename into a map position. This is the Pass 1
        // checkpoint's endpoint, and it stays useful afterwards as the overlay's fix path.
        api.MapPost("/maps/{id}/locate", (RatNavState state, string id, LocateRequest request) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            if (!ScreenshotFilename.TryParse(request.Filename, out var fix))
                return Results.BadRequest(new { error = "That filename carries no coordinates." });

            var transform = new CoordinateTransform(map.Image);
            var point = transform.ToNormalized(fix.Position);

            return Results.Ok(new LocateResponse
            {
                X = point.X,
                Y = point.Y,
                HeadingDegrees = fix.HeadingDegrees,
                ImageHeadingDegrees = transform.ToImageHeading(fix.HeadingDegrees),
                Position = fix.Position,
                TakenAt = fix.TakenAt,
            });
        });
    }

    /// <summary>
    /// Looks a map up by tarkov.dev id or by its normalized name. Both work, because when
    /// tarkov.dev is down the only id a map has is its tarkovdata key — and a URL that stops
    /// working during someone else's outage is a URL that was wrong to begin with.
    /// </summary>
    private static MapDef? FindMap(RatNavState state, string id)
    {
        var maps = state.Cache.Current?.Maps;
        if (maps is null) return null;

        return maps.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? maps.FirstOrDefault(m => string.Equals(m.NormalizedName, id, StringComparison.OrdinalIgnoreCase));
    }

    private static ItemDef Unknown(string itemId) => new() { Id = itemId, Name = "Unknown item" };

    private static object? Track(RatNavState state, ItemTracker tracker, ProgressStore progress, string itemId)
    {
        if (state.Index is not { } index) return null;

        var needs = index.GetNeeds(itemId)
            ?? new ItemNeeds { Item = index.GetItem(itemId) ?? Unknown(itemId) };

        return TrackedItemView.From(tracker.Track(needs, progress));
    }

    private static MapInkLevel ParseInk(string? ink) => ink?.ToLowerInvariant() switch
    {
        "outline" => MapInkLevel.Outline,
        "structure" => MapInkLevel.Structure,
        _ => MapInkLevel.Full,
    };
}

// ---- Wire shapes. Deliberately flatter than the domain model: these exist to be read by a
// ---- React table and an XAML canvas, not to round-trip.

public sealed record LocateRequest(string Filename);

/// <summary>Either an absolute count or a nudge. The +/- buttons send a delta.</summary>
public sealed record HaveRequest(int? Count, int? Delta);

public sealed record WatchRequest(bool Watch, string? Note, int? Target);
public sealed record TaskStateRequest(string State);
public sealed record HideoutLevelRequest(int Level);

/// <summary>An item row with progress folded in — what the Items view renders.</summary>
public sealed record TrackedItemView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public string? IconUrl { get; init; }
    public string? WikiUrl { get; init; }
    public int? Avg24hPrice { get; init; }
    public int QuestNeeded { get; init; }
    public int HideoutNeeded { get; init; }
    public int Needed { get; init; }
    public int Have { get; init; }
    public int Remaining { get; init; }
    public bool FoundInRaid { get; init; }
    public bool IsKey { get; init; }
    public bool Watched { get; init; }
    public string? WatchNote { get; init; }
    public int? WatchTarget { get; init; }

    public static TrackedItemView From(TrackedItem t) => new()
    {
        Id = t.Item.Id,
        Name = t.Item.Name,
        ShortName = t.Item.ShortName,
        IconUrl = t.Item.IconUrl,
        WikiUrl = t.Item.WikiUrl,
        Avg24hPrice = t.Item.Avg24hPrice,
        QuestNeeded = t.QuestNeeded,
        HideoutNeeded = t.HideoutNeeded,
        Needed = t.Needed,
        Have = t.Have,
        Remaining = t.Remaining,
        FoundInRaid = t.FoundInRaid,
        IsKey = t.IsKey,
        Watched = t.Watched,
        WatchNote = t.WatchNote,
        WatchTarget = t.WatchTarget,
    };
}

public sealed record LocateResponse
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double HeadingDegrees { get; init; }
    public required double ImageHeadingDegrees { get; init; }
    public required GamePosition Position { get; init; }
    public required DateTimeOffset TakenAt { get; init; }
}

public sealed record ObjectivePin
{
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public string? TraderName { get; init; }
    public required string ObjectiveId { get; init; }
    public required string Description { get; init; }
    public string? Type { get; init; }
    public bool Optional { get; init; }

    /// <summary>Position on the map image as a fraction of its size, so any render scale works.</summary>
    public required double X { get; init; }
    public required double Y { get; init; }

    /// <summary>World height, for maps with floors.</summary>
    public double Elevation { get; init; }

    public IReadOnlyList<string> NeededKeyItemIds { get; init; } = [];
}

public sealed record ItemSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public string? IconUrl { get; init; }
    public int? Avg24hPrice { get; init; }
    public int Needed { get; init; }
    public bool FoundInRaid { get; init; }
    public int QuestCount { get; init; }
    public int HideoutCount { get; init; }
    public bool IsKey { get; init; }

    public static ItemSummary From(ItemDef item, ItemNeeds? needs) => new()
    {
        Id = item.Id,
        Name = item.Name,
        ShortName = item.ShortName,
        IconUrl = item.IconUrl,
        Avg24hPrice = item.Avg24hPrice,
        Needed = needs?.TotalNeeded ?? 0,
        FoundInRaid = needs?.AnyFoundInRaid ?? false,
        QuestCount = needs?.Quests.Count ?? 0,
        HideoutCount = needs?.Hideout.Count ?? 0,
        IsKey = needs?.AsKey.Count > 0,
    };
}

public sealed record ItemDetail
{
    public required ItemDef Item { get; init; }
    public IReadOnlyList<QuestNeed> Quests { get; init; } = [];
    public IReadOnlyList<HideoutNeed> Hideout { get; init; } = [];
    public IReadOnlyList<QuestNeed> AsKey { get; init; } = [];
    public int TotalNeeded { get; init; }
    public bool AnyFoundInRaid { get; init; }

    public static ItemDetail From(ItemDef item, ItemNeeds? needs) => new()
    {
        Item = item,
        Quests = needs?.Quests ?? [],
        Hideout = needs?.Hideout ?? [],
        AsKey = needs?.AsKey ?? [],
        TotalNeeded = needs?.TotalNeeded ?? 0,
        AnyFoundInRaid = needs?.AnyFoundInRaid ?? false,
    };
}

public sealed record TaskSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TraderName { get; init; }
    public int? MinPlayerLevel { get; init; }
    public bool Kappa { get; init; }
    public string? WikiUrl { get; init; }
    public int ObjectiveCount { get; init; }
    public IReadOnlyList<string> MapIds { get; init; } = [];

    public static TaskSummary From(TaskDef task) => new()
    {
        Id = task.Id,
        Name = task.Name,
        TraderName = task.TraderName,
        MinPlayerLevel = task.MinPlayerLevel,
        Kappa = task.Kappa,
        WikiUrl = task.WikiUrl,
        ObjectiveCount = task.Objectives.Count,
        MapIds = [.. task.Objectives.SelectMany(o => o.MapIds).Distinct()],
    };
}

public sealed record MapSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NormalizedName { get; init; }

    /// <summary>False when we have no calibrated image, so the UI can say why pins are missing.</summary>
    public bool Calibrated { get; init; }

    public string? ImageUrl { get; init; }
    public int CoordinateRotation { get; init; }
    public int ExtractCount { get; init; }

    /// <summary>False where the mapping has not been established with confidence.</summary>
    public bool CalibrationVerified { get; init; }

    /// <summary>"Verified", "Derived", "Weak" or "Unknown".</summary>
    public string Confidence { get; init; } = "Unknown";

    /// <summary>How the mapping was arrived at, in words, for the UI to show.</summary>
    public string? CalibrationReason { get; init; }

    /// <summary>The solved mapping itself, e.g. "(-z, x)".</summary>
    public string? Mapping { get; init; }

    public static MapSummary From(MapDef map) => new()
    {
        Id = map.Id,
        Name = map.Name,
        NormalizedName = map.NormalizedName,
        Calibrated = map.Image is not null,
        CalibrationVerified = map.Image?.CalibrationVerified ?? false,
        Confidence = map.Image?.Confidence.ToString() ?? "Unknown",
        CalibrationReason = map.Image?.CalibrationReason,
        Mapping = map.Image?.Mapping.ToString(),
        ImageUrl = map.Image?.SourceUrl,
        CoordinateRotation = map.Image?.CoordinateRotation ?? 0,
        ExtractCount = map.Extracts.Count,
    };
}
