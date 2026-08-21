using System.Diagnostics;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Game;

namespace RatNav.Service;

/// <summary>
/// Whether RatNav can actually see the game, and what to do when it cannot.
///
/// <para>Setup here is four things that each fail silently: the game found, the screenshot folder
/// found, a screenshot key bound in game, and the game running in borderless. Every one of them
/// looks identical from the player's side — an overlay that shows nothing — so this reports each
/// separately rather than leaving someone to guess which is wrong.</para>
/// </summary>
public sealed record Diagnostics
{
    public required IReadOnlyList<Check> Checks { get; init; }
    public required string OpenInBrowserUrl { get; init; }

    /// <summary>Every install found, so a second stale copy explains itself rather than confusing.</summary>
    public required IReadOnlyList<InstallView> Installs { get; init; }

    public bool Ready => Checks.All(c => c.Ok || !c.Required);

    public static Diagnostics Build(RatNavSettings settings, int port, DataStatus? data = null)
    {
        var installs = GameInstallFinder.FindAll();
        var chosen = settings.GameDirectory is { Length: > 0 } configured
            ? GameInstallFinder.Describe(configured)
            : installs.FirstOrDefault();

        var screenshots = settings.ScreenshotDirectory ?? RatNavPaths.DefaultScreenshotDirectory;
        var shotCount = CountScreenshots(screenshots);
        var running = Process.GetProcessesByName("EscapeFromTarkov").Length > 0;

        return new Diagnostics
        {
            OpenInBrowserUrl = $"http://localhost:{port}/",

            Installs =
            [
                .. installs.Select(i => new InstallView
                {
                    Directory = i.Directory,
                    Version = i.Version,
                    LastPlayed = i.LastPlayed,
                    Chosen = string.Equals(i.Directory, chosen?.Directory, StringComparison.OrdinalIgnoreCase),
                })
            ],

            Checks =
            [
                new Check
                {
                    Name = "Game found",
                    Ok = chosen is not null,
                    Detail = chosen?.Directory ?? "No Escape from Tarkov install found.",
                    Fix = "Set the Escape from Tarkov folder below — the one containing "
                          + "EscapeFromTarkov.exe.",
                    Required = true,
                },
                new Check
                {
                    Name = "Reading the game's logs",
                    Ok = chosen?.HasLogs ?? false,
                    Detail = chosen?.Version is { } v
                        ? $"version {v}, last played {Ago(chosen.LastPlayed)}"
                        : "No log sessions yet — launch the game once.",
                    Fix = "Launch Escape from Tarkov. RatNav reads the logs it writes.",
                    Required = true,
                },
                new Check
                {
                    Name = "Screenshot folder",
                    Ok = shotCount is not null,
                    // Just the folder. This check answers one question — can RatNav find the
                    // place your screenshots land — and a count of files sitting in it answered a
                    // different one nobody asked, in raid or out of it. Whether RatNav has ever
                    // actually read one is the next check along, which is where that belongs.
                    Detail = shotCount is null ? $"{screenshots} — not created yet" : screenshots,
                    Fix = "It appears the first time you take a screenshot in game. If your Documents "
                          + "folder has moved — OneDrive does this — set the folder below.",
                    Required = true,
                },
                new Check
                {
                    Name = "Screenshot key bound",
                    Ok = shotCount is > 0 || HasArchive(screenshots),
                    Detail = shotCount is > 0 || HasArchive(screenshots)
                        ? $"RatNav has seen screenshots. Tap {settings.ScreenshotKey} in raid for a position fix."
                        : "No screenshots seen yet.",
                    Fix = $"Bind one in Tarkov: Settings, Controls, Screenshot. RatNav expects "
                        + $"{settings.ScreenshotKey} — change either to match the other.",
                    Required = false,
                },
                new Check
                {
                    Name = "Game data",

                    // A source that failed counts as not OK even when the refresh as a whole
                    // succeeded — planning around data that quietly came back empty is worse
                    // than being told a source is down.
                    Ok = data is { Loaded: true, ServingStale: false } && data.BrokenSources.Count == 0,
                    Detail = data is null
                        ? "Not loaded."
                        : data.ServingStale
                            ? $"Serving cached data — {data.LastError}"
                            : data.BrokenSources.Count > 0
                                ? $"{string.Join(", ", data.BrokenSources.Keys)} unavailable — "
                                  + string.Join("; ", data.BrokenSources.Values)
                                : $"{data.TaskCount} quests, {data.ItemCount} items, "
                                  + $"{data.BarterCount} barters, {data.CalibratedMapCount} maps, "
                                  + $"checked {Ago(data.FetchedAt)}",
                    Fix = "RatNav checks at launch and every six hours. Refresh forces it now.",
                    Required = false,
                },
                new Check
                {
                    Name = "Game running",
                    Ok = running,
                    Detail = running ? "Escape from Tarkov is running." : "Not running.",

                    // The one thing no overlay can work around, so it is worth saying plainly
                    // before someone concludes RatNav is broken.
                    Fix = "Run the game in Borderless or Windowed. Exclusive fullscreen draws above every overlay.",
                    Required = false,
                },
            ],
        };
    }

    private static int? CountScreenshots(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.png").Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Screenshots are archived once read, so an empty folder does not mean none were seen.</summary>
    private static bool HasArchive(string directory)
    {
        try
        {
            var archive = Path.Combine(directory, "RatNav archive");
            return Directory.Exists(archive) && Directory.GetFiles(archive, "*.png").Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Ago(DateTimeOffset? at)
    {
        if (at is null) return "never";

        var since = DateTimeOffset.UtcNow - at.Value;
        if (since.TotalHours < 1) return $"{since.TotalMinutes:F0} minutes ago";
        if (since.TotalDays < 1) return $"{since.TotalHours:F0} hours ago";
        return $"{since.TotalDays:F0} days ago";
    }
}

public sealed record Check
{
    public required string Name { get; init; }
    public required bool Ok { get; init; }
    public required string Detail { get; init; }
    public required string Fix { get; init; }

    /// <summary>Required checks must pass; the rest are advice.</summary>
    public bool Required { get; init; }
}

public sealed record InstallView
{
    public required string Directory { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>Which one RatNav is watching — the most recently played, unless configured.</summary>
    public bool Chosen { get; init; }
}
