using Microsoft.Extensions.Hosting;
using RatNav.Core;
using RatNav.Core.Watchers;

namespace RatNav.Service;

/// <summary>
/// Runs the watchers for as long as the service is up, and connects them to the raid session.
///
/// <para>This is the only place anything in RatNav observes the game, and all it does is read
/// files the game already wrote: log lines, and the names of screenshots. Both watchers are
/// event-driven or on a slow poll, so a raid costs no measurable CPU between fixes.</para>
/// </summary>
public sealed class RaidHost(RaidSession session, RatNavSettings settings) : IHostedService, IDisposable
{
    private ScreenshotWatcher? _screenshots;
    private LogWatcher? _logs;

    /// <summary>Whether the game was found, so the UI can explain itself rather than look broken.</summary>
    public bool GameFound => _logs?.Available ?? false;

    public string? GameVersion => _logs?.GameVersion;

    public Task StartAsync(CancellationToken ct)
    {
        _screenshots = new ScreenshotWatcher(settings.ScreenshotDirectory)
        {
            Disposal = settings.ScreenshotDisposal,
        };

        _screenshots.PositionFixed += (_, fix) => session.OnPositionFixed(fix);
        _screenshots.Start();

        // A fix taken before RatNav started is still the player's current position, and the first
        // screenshot of a raid is usually taken before anyone thinks about the app.
        _screenshots.ReadLatestExisting();

        _logs = new LogWatcher(settings.GameDirectory);
        _logs.RaidStarted += (_, raid) => session.OnRaidStarted(raid.LocationId);
        _logs.QuestChanged += (_, change) => session.OnQuestChanged(change);
        _logs.Start();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _screenshots?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        (_screenshots as IDisposable)?.Dispose();
        _logs?.Dispose();
    }
}

/// <summary>
/// Where RatNav should look for things, and what it should do with what it finds.
///
/// Nothing here is hardcoded to one machine: an unset path means "work it out", and the answers
/// are found rather than assumed. A stale second install of the game on another drive is a real
/// situation, which is why detection prefers the one that has been played most recently.
/// </summary>
public sealed record RatNavSettings
{
    /// <summary>Game install. Null means detect it.</summary>
    public string? GameDirectory { get; init; }

    /// <summary>Screenshot folder. Null means the default under Documents.</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>
    /// What to do with a screenshot once its position has been read. Archiving by default:
    /// leaving them to accumulate is what makes this technique feel slow.
    /// </summary>
    public ScreenshotDisposal ScreenshotDisposal { get; init; } = ScreenshotDisposal.Archive;

    /// <summary>The handle put on plans you share.</summary>
    public string? Owner { get; init; }

    public static RatNavSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "settings.json");

        try
        {
            if (File.Exists(path))
            {
                return System.Text.Json.JsonSerializer.Deserialize<RatNavSettings>(
                    File.ReadAllText(path),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                    {
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                    }) ?? new RatNavSettings();
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            // Defaults are all detectable, so a broken settings file costs nothing but the
            // customisations in it.
        }

        return new RatNavSettings();
    }

    public void Save(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);

        var json = System.Text.Json.JsonSerializer.Serialize(this,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        var path = Path.Combine(dataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
