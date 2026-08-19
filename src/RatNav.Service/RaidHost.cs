using Microsoft.Extensions.Hosting;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Watchers;

namespace RatNav.Service;

/// <summary>
/// Runs the watchers for as long as the service is up, and connects them to the raid session.
///
/// <para>This is the only place anything in RatNav observes the game, and all it does is read
/// files the game already wrote: log lines, and the names of screenshots. Both watchers are
/// event-driven or on a slow poll, so a raid costs no measurable CPU between fixes.</para>
/// </summary>
public sealed class RaidHost(
    RaidSession session,
    RatNavSettings settings,
    GameDataCache cache) : IHostedService, IDisposable
{
    private ScreenshotWatcher? _screenshots;
    private LogWatcher? _logs;
    private Timer? _refresh;

    /// <summary>How the last data refresh went, for the UI to report honestly.</summary>
    public RefreshResult? LastRefresh { get; private set; }

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
        _logs.RaidEnded += (_, _) => session.OnRaidEnded();
        _logs.QuestChanged += (_, change) => session.OnQuestChanged(change);
        _logs.Start();

        // Quests, items and maps all change with a patch, and a plan built on last wipe's data is
        // worse than no plan. So: check at launch, then every six hours while running.
        _ = RefreshAsync(ct);
        _refresh = new Timer(_ => _ = RefreshAsync(CancellationToken.None), null,
            TimeSpan.FromHours(6), TimeSpan.FromHours(6));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Brings game data up to date, passing the running game's version so a patch forces a full
    /// refresh however recently the cache was written.
    ///
    /// <para>Nothing blocks on this. The app is usable from the cached copy while it runs, and a
    /// failure leaves the last good data in place with the UI saying it is stale — being offline
    /// should cost you the newest prices, not the planner.</para>
    /// </summary>
    public async Task<RefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        var result = await cache.EnsureFreshAsync(_logs?.GameVersion, ct);
        LastRefresh = result;
        return result;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _screenshots?.Stop();
        _refresh?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        (_screenshots as IDisposable)?.Dispose();
        _logs?.Dispose();
        _refresh?.Dispose();
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

    /// <summary>
    /// The key bound to Screenshot <i>inside Escape from Tarkov</i> — middle mouse by default,
    /// because it is reachable without letting go of movement.
    ///
    /// <para>RatNav does not and cannot bind this: sending or intercepting input to the game is
    /// the one technique here that carries real ban risk, so the game keeps ownership of the key
    /// and RatNav only reads the file that pressing it produces. This setting exists so every
    /// prompt in the UI names the key you actually use instead of guessing.</para>
    /// </summary>
    public string ScreenshotKey { get; init; } = "Middle Mouse";

    /// <summary>
    /// Hotkeys, written as text so settings.json stays editable by hand. Defaults are function
    /// keys because they are unmodified, easy to reach mid-raid, and rarely bound by the game.
    /// </summary>
    public HotKeySettings Hotkeys { get; init; } = new();

    /// <summary>Where the overlay sits and how big it is. Remembered so it is arranged once.</summary>
    public OverlayBounds Overlay { get; init; } = new();

    /// <summary>Bindable hotkeys. Anything <see cref="string"/> here is parsed at startup.</summary>
    public sealed record HotKeySettings
    {
        /// <summary>Show or hide the overlay.</summary>
        public string ToggleOverlay { get; init; } = "F5";

        /// <summary>
        /// Let the mouse reach the overlay, so it can be moved, resized and zoomed. Off by
        /// default in raid: an overlay that swallows a click is worse than one you cannot drag.
        /// </summary>
        public string ToggleInteract { get; init; } = "F6";

        /// <summary>Open the full management panel over the game.</summary>
        public string ExpandPanel { get; init; } = "F7";

        /// <summary>Tick the current objective off without leaving the game.</summary>
        public string CompleteObjective { get; init; } = "F8";

        /// <summary>Switch between the corner panel and the centred wireframe map.</summary>
        public string ToggleMode { get; init; } = "F9";
    }

    /// <summary>How the overlay presents itself.</summary>
    public enum OverlayMode
    {
        /// <summary>
        /// A small panel in a corner. Out of the way, and the right default: it never covers the
        /// middle of the screen, which is where the game is.
        /// </summary>
        Box,

        /// <summary>
        /// The map itself, drawn large and translucent over the centre of the screen — the
        /// dungeon map from Diablo. Terrain drops away and buildings and roads carry it, so you
        /// can still see through it. Best on a hotkey you tap rather than leave on.
        /// </summary>
        Wireframe,
    }

    /// <summary>Overlay geometry, in device-independent pixels.</summary>
    public sealed record OverlayBounds
    {
        public double Left { get; init; } = 40;
        public double Top { get; init; } = 40;
        public double Width { get; init; } = 360;
        public double Height { get; init; } = 240;

        /// <summary>Map zoom, 1 = the whole map.</summary>
        public double Zoom { get; init; } = 1;

        public OverlayMode Mode { get; init; } = OverlayMode.Box;

        /// <summary>
        /// How strongly the map is drawn, 0 to 1. Lower is more of the game showing through; this
        /// is the dial that decides whether an overlay helps or gets in the way.
        /// </summary>
        public double MapOpacity { get; init; } = 0.55;

        /// <summary>Wireframe covers this fraction of the screen when centred.</summary>
        public double WireframeScale { get; init; } = 0.7;

        /// <summary>
        /// How much of the map to draw at all: "full", "structure", or "outline". Distinct from
        /// <see cref="MapOpacity"/> — this drops whole categories of detail rather than fading
        /// everything equally, which is what keeps a dense map readable over a dark scene.
        /// </summary>
        public string Ink { get; init; } = "structure";
    }

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
