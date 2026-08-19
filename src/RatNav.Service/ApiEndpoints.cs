using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RatNav.Core.Data;
using RatNav.Core.Maps;
using RatNav.Core.Model;

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

        api.MapGet("/items/search", (RatNavState state, string q, int? limit) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var results = index.Search(q, limit ?? 25)
                .Select(item => ItemSummary.From(item, index.GetNeeds(item.Id)));

            return Results.Ok(results);
        });

        api.MapGet("/items/needed", (RatNavState state) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var results = index.AllNeeded()
                .OrderByDescending(n => n.TotalNeeded)
                .Select(n => ItemSummary.From(n.Item, n));

            return Results.Ok(results);
        });

        api.MapGet("/items/{id}", (RatNavState state, string id) =>
        {
            if (state.Index is not { } index) return Results.NotFound();

            var item = index.GetItem(id);
            return item is null
                ? Results.NotFound()
                : Results.Ok(ItemDetail.From(item, index.GetNeeds(id)));
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
