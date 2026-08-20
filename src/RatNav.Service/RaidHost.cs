using Microsoft.Extensions.Hosting;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Sharing;
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
    GameDataCache cache,
    PlanStore plans) : IHostedService, IDisposable
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
        //
        // The plan is restored after this rather than before it, because activating one needs the
        // map data the refresh loads.
        _ = RefreshAsync(ct).ContinueWith(_ => RestoreActivePlan(), TaskScheduler.Default);
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

        // A map somebody settled by marking where they stood stays settled across a refresh. The
        // answer lives in settings rather than in the cache, so re-fetching the data would
        // otherwise quietly un-settle it.
        cache.Reapply(settings.ConfirmedMaps);

        LastRefresh = result;
        return result;
    }

    /// <summary>
    /// Puts back the plan that was active when RatNav last closed.
    ///
    /// <para>A plan outlives its raid, so it has to outlive the app too. Closing RatNav between
    /// sessions and losing what you were working towards would make the planner something you
    /// rebuild every evening.</para>
    /// </summary>
    private void RestoreActivePlan()
    {
        if (settings.ActivePlanId is not { Length: > 0 } id) return;

        var saved = plans.Get(id);
        var data = cache.Current;

        var map = data?.Maps.FirstOrDefault(m =>
            string.Equals(m.Id, saved?.Document.MapId, StringComparison.OrdinalIgnoreCase));

        if (saved is null || map is null) return;

        session.UsePlan(PlanConversion.ToPlan(saved.Document, map, data), map);
    }

    /// <summary>
    /// Points the watchers at wherever the game now is.
    ///
    /// <para>Changing the install folder in Setup and then being told to restart the app would be
    /// a poor answer to "RatNav cannot see my game" — that is the moment someone is least willing
    /// to be patient with it.</para>
    /// </summary>
    public void Rewatch()
    {
        _screenshots?.Stop();
        _logs?.Dispose();

        _screenshots = new ScreenshotWatcher(settings.ScreenshotDirectory)
        {
            Disposal = settings.ScreenshotDisposal,
        };

        _screenshots.PositionFixed += (_, fix) => session.OnPositionFixed(fix);
        _screenshots.Start();
        _screenshots.ReadLatestExisting();

        _logs = new LogWatcher(settings.GameDirectory);
        _logs.RaidStarted += (_, raid) => session.OnRaidStarted(raid.LocationId);
        _logs.RaidEnded += (_, _) => session.OnRaidEnded();
        _logs.QuestChanged += (_, change) => session.OnQuestChanged(change);
        _logs.Start();
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
    /// <summary>
    /// Where Escape from Tarkov is installed. Null means detect it.
    ///
    /// <para>Detection is a convenience, not a guarantee: the launcher can be pointed anywhere,
    /// an old copy can sit on another drive, and a wrong guess looks exactly like RatNav being
    /// broken. So this is editable, and Setup says which install it settled on and why.</para>
    /// </summary>
    public string? GameDirectory { get; set; }

    /// <summary>
    /// Screenshot folder. Null means the default under Documents — which OneDrive moves, so this
    /// has to be settable rather than assumed.
    /// </summary>
    public string? ScreenshotDirectory { get; set; }

    /// <summary>
    /// What to do with a screenshot once its position has been read. Archiving by default:
    /// leaving them to accumulate is what makes this technique feel slow.
    /// </summary>
    public ScreenshotDisposal ScreenshotDisposal { get; set; } = ScreenshotDisposal.Archive;

    /// <summary>The handle put on plans you share.</summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Your character level. Most quests gate on it, so without this the planner offers quests you
    /// cannot accept yet.
    ///
    /// <para>Set by hand: nothing Escape from Tarkov writes to disk reports your level, and the
    /// only endpoint that does needs your account credentials. Setup suggests a floor from the
    /// quests you have marked complete, which is the best that can be known from here.</para>
    /// </summary>
    public int? PlayerLevel { get; set; }

    /// <summary>
    /// Which edition of the game you own. It decides the stash you start with — Edge of Darkness
    /// begins at Stash 4 — which otherwise looks like an upgrade you have not built.
    /// </summary>
    public string GameEdition { get; set; } = "standard";

    /// <summary>
    /// The key bound to Screenshot <i>inside Escape from Tarkov</i> — middle mouse by default,
    /// because it is reachable without letting go of movement.
    ///
    /// <para>RatNav does not and cannot bind this: sending or intercepting input to the game is
    /// the one technique here that carries real ban risk, so the game keeps ownership of the key
    /// and RatNav only reads the file that pressing it produces. This setting exists so every
    /// prompt in the UI names the key you actually use instead of guessing.</para>
    /// </summary>
    public string ScreenshotKey { get; set; } = "Middle Mouse";

    /// <summary>
    /// Hotkeys, written as text so settings.json stays editable by hand. Defaults are function
    /// keys because they are unmodified, easy to reach mid-raid, and rarely bound by the game.
    /// </summary>
    public HotKeySettings Hotkeys { get; set; } = new();

    /// <summary>Where the overlay sits and how big it is. Remembered so it is arranged once.</summary>
    /// <summary>
    /// Map layouts confirmed by standing somewhere and marking it, keyed by normalized map name.
    ///
    /// <para>Four maps cannot be settled from published data alone — their extracts all sit inside
    /// the border, so mirroring the layout moves nothing off the edge and nothing distinguishes it
    /// from the truth. One marked position settles any of them outright, and this is where that
    /// answer lives so it is never asked for twice.</para>
    ///
    /// <para>Values are the mapping's own short form: <c>(x, z)</c>, <c>(-x, z)</c>, <c>(z, -x)</c>
    /// and so on.</para>
    /// </summary>
    public Dictionary<string, string> ConfirmedMaps { get; set; } = [];

    public OverlayBounds Overlay { get; set; } = new();

    /// <summary>Bindable hotkeys. Anything <see cref="string"/> here is parsed at startup.</summary>
    /// <summary>
    /// The plan that was active when RatNav last closed, put back on the next start. A plan
    /// outlives its raid, so it has to outlive the app too.
    ///
    /// <para>Settable rather than init-only: this one is changed by the service while running,
    /// every time a plan is activated, and everything else here is set by a person in Setup.</para>
    /// </summary>
    public string? ActivePlanId { get; set; }

    /// <summary>
    /// How many hideout upgrades deep the items list reaches. 1 is only what can be built right
    /// now; higher is what to stop vendoring. Set from the Hideout view and remembered.
    /// </summary>
    public int HideoutLookAhead { get; set; } = Core.Planning.HideoutPlanner.DefaultLookAhead;

    /// <summary>
    /// Where these settings were read from, so a change made while running can be written back.
    /// Not serialised — it describes the file rather than being part of it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Origin { get; set; }

    /// <summary>
    /// Changes a setting and saves it, in one step, so no caller has to remember the second half.
    /// </summary>
    public void Remember(Action<RatNavSettings> change)
    {
        change(this);
        if (Origin is { Length: > 0 } directory) Save(directory);
    }

    public sealed record HotKeySettings
    {
        /// <summary>Show or hide the overlay.</summary>
        public string ToggleOverlay { get; set; } = "F5";

        /// <summary>
        /// Let the mouse reach the overlay, so it can be moved, resized and zoomed. Off by
        /// default in raid: an overlay that swallows a click is worse than one you cannot drag.
        /// </summary>
        public string ToggleInteract { get; set; } = "F6";

        /// <summary>Open the full management panel over the game.</summary>
        public string ExpandPanel { get; set; } = "F7";

        /// <summary>Tick the current objective off without leaving the game.</summary>
        public string CompleteObjective { get; set; } = "F8";

        /// <summary>Switch between the corner panel and the centred wireframe map.</summary>
        public string ToggleMode { get; set; } = "F9";

        /// <summary>
        /// Identify whatever the mouse is hovering, by reading the tooltip off the screen.
        ///
        /// <para>A key rather than shift-click, deliberately. Catching a mouse click over another
        /// application needs a system-wide mouse hook, which is the same machinery RatNav refuses
        /// to use for the keyboard and for the same reason. A hotkey is registered with Windows
        /// the ordinary way and touches nothing.</para>
        /// </summary>
        public string IdentifyItem { get; set; } = "F10";

        /// <summary>
        /// Read the extract list the game is showing, and keep only those on the map.
        ///
        /// <para>Pressed while the game's own list is up — the one double-tapping <c>O</c> opens.
        /// RatNav cannot know you pressed <c>O</c> without watching the keyboard, which it will
        /// not do, so it asks you to tell it.</para>
        /// </summary>
        public string ReadExtracts { get; set; } = "F11";
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
    /// <summary>
    /// How one presentation of the overlay is arranged: where it sits, how big it is, and where
    /// the map is inside it.
    ///
    /// <para>Kept per presentation rather than shared. The corner box and the centred map are
    /// used for different things at different sizes, and arranging one then switching to the
    /// other used to overwrite the first — so setting up the big map cost you the small one.</para>
    /// </summary>
    public sealed record OverlayPlacement
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }

        /// <summary>Map zoom, 1 = the whole map.</summary>
        public double Zoom { get; init; } = 1;

        /// <summary>How far the map has been dragged, as a fraction of the map image.</summary>
        public double PanX { get; init; }
        public double PanY { get; init; }

        /// <summary>Whether the map keeps you centred.</summary>
        public bool Follow { get; init; }

        /// <summary>
        /// How much of the map to draw, and in whose colours: "graphical", "full", "structure",
        /// or "outline".
        ///
        /// <para>Per presentation, because the two are used for different things. The centred map
        /// is for crossing ground — roads and structures, nothing else competing. The corner map
        /// is for when you have arrived somewhere and want to read the place.</para>
        /// </summary>
        public string Ink { get; init; } = "graphical";

        /// <summary>
        /// How solid the whole panel is, 0.2 to 1.
        ///
        /// <para>Different from the map's own fade, which decides how heavily the drawing is inked.
        /// This is the window: the corner view is a bordered panel sitting on the screen, and how
        /// much of the game it blocks while you run around is a separate question from how dense
        /// the map inside it looks.</para>
        /// </summary>
        public double WindowOpacity { get; init; } = 1.0;

        /// <summary>True before anyone has arranged this presentation, so a sensible default can be used.</summary>
        public bool Unplaced => Width <= 0 || Height <= 0;
    }

    public sealed record OverlayBounds
    {
        public OverlayMode Mode { get; init; } = OverlayMode.Box;

        /// <summary>
        /// The small corner panel. Follows you by default: it is too small to hold a whole map
        /// usefully, so its job is to show where you are and what is around you.
        /// </summary>
        public OverlayPlacement Box { get; init; } = new()
        {
            Left = 40,
            Top = 40,
            Width = 360,
            Height = 240,
            Follow = true,
        };

        /// <summary>
        /// The large centred map. Holds still by default: it is big enough to read as a map, and
        /// one that re-centres on every fix puts the same building somewhere new each time you
        /// look. Drawn as outlines, because crossing ground wants roads and structures and not
        /// much else.
        /// </summary>
        public OverlayPlacement Wireframe { get; init; } = new() { Ink = "outline" };

        /// <summary>Fraction of the screen the centred map covers before it is arranged by hand.</summary>
        public double WireframeScale { get; init; } = 0.7;

        /// <summary>
        /// How strongly the map is drawn, 0 to 1. Lower is more of the game showing through; this
        /// is the dial that decides whether an overlay helps or gets in the way.
        /// </summary>
        public double MapOpacity { get; init; } = 0.55;

        /// <summary>
        /// Draw the floors below the one you are on, faintly, underneath it.
        ///
        /// <para>A single floor in isolation is hard to place — walking off a street into a
        /// building, the room you are in means nothing without the street it came off. Ghosting
        /// keeps that context without letting it compete with the floor you are actually on.</para>
        /// </summary>
        public bool GhostOtherFloors { get; init; } = true;

        /// <summary>Draw the map's named places — "Old Gas", "Dorms" — on the map itself.</summary>
        public bool ShowPlaceNames { get; init; } = true;

        /// <summary>Which extracts to draw: "pmc", "scav", or "off".</summary>
        public string Extracts { get; init; } = "pmc";

        /// <summary>
        /// Which spawn areas to draw: "off", "pmc", "scav", or "both".
        ///
        /// <para>Off by default. It answers a question you ask before the raid — where might the
        /// others have come from — and having it always on puts a dozen circles over a map you
        /// are trying to navigate.</para>
        /// </summary>
        public string Spawns { get; init; } = "off";

        /// <summary>
        /// How large the overlay's own furniture is drawn — buttons, labels, lists, headings.
        ///
        /// <para>One at 1080p, which is what the defaults are sized for. A 4K screen has four
        /// times the pixels and the same physical size, so everything on it lands at a quarter of
        /// the area unless it is told otherwise; two is about right there.</para>
        ///
        /// <para>Separate from the marker and text dials, which size what is drawn on the map. This
        /// is the app's chrome.</para>
        /// </summary>
        public double UiScale { get; init; } = 1.0;

        /// <summary>
        /// Extract names read off the game's own list, when you have asked RatNav to read it.
        ///
        /// <para>Empty means "not read this raid", which is a different thing from "none are
        /// open" — so an unread map keeps showing every extract rather than going blank.</para>
        /// </summary>
        public IReadOnlyList<string> OfferedExtracts { get; init; } = [];

        /// <summary>Whether to draw only what was read off the screen, once something has been.</summary>
        public bool OnlyOfferedExtracts { get; init; } = true;

        /// <summary>Whether the items panel is open. Collapsed by default — the map comes first.</summary>
        public bool ShowItems { get; init; }

        /// <summary>
        /// Whether the map controls are showing while the overlay takes the mouse. Folding them
        /// away leaves one button; how you like to work is worth remembering.
        /// </summary>
        public bool ShowControls { get; init; } = true;

        /// <summary>Which side of the map the items list sits on — "left" or "right".</summary>
        public string ItemsSide { get; init; } = "right";

        /// <summary>
        /// Whether the quest log is open: the started stops in the plan, numbered as they are on
        /// the map, with what each one wants.
        /// </summary>
        public bool ShowQuests { get; init; }

        /// <summary>Which side the quest log sits on. Its own, so the two can face each other.</summary>
        public string QuestsSide { get; init; } = "left";

        /// <summary>
        /// How wide the items list is, in pixels. Dragged by the divider between it and the map,
        /// and remembered — item names vary enough in length that one fixed width suits nobody.
        /// </summary>
        /// <para>Wide enough for a name like "Chekannaya 15 apartment key" without trimming it,
        /// because a list of truncated names is a list you have to hover to read.</para>
        public double ItemsWidth { get; init; } = 235;

        /// <summary>
        /// Sections of the items list that are folded away, by title. Remembered, because the
        /// list is rebuilt on every position fix and folding would otherwise undo itself.
        /// </summary>
        public IReadOnlyList<string> CollapsedSections { get; init; } = ["LATER"];

        /// <summary>
        /// Draw a dark halo behind map lines. Outline ink over a bright snowfield is otherwise
        /// close to invisible, and the halo is what makes one set of colours work on every map.
        /// </summary>
        public bool Halo { get; init; } = true;

        /// <summary>Line weight multiplier, for making the map heavier or lighter over the game.</summary>
        public double LineWeight { get; init; } = 1.0;

        /// <summary>
        /// Size of everything placed on the map — waypoints, extracts, the player marker.
        ///
        /// <para>Scalable rather than fixed because players run wildly different resolutions, and
        /// a marker sized for 1080p is a speck on a 4K screen.</para>
        /// </summary>
        public double MarkerScale { get; init; } = 3.0;

        /// <summary>
        /// Size of text drawn on the map — place names, extract names. Separate from
        /// <see cref="MarkerScale"/>: a map can want big markers and small labels, or the reverse.
        /// </summary>
        public double TextScale { get; init; } = 2.0;

        /// <summary>
        /// How much markers and text shrink as the map is zoomed out, 0 to 1.
        ///
        /// <para>Markers sized for a zoomed-in view cover a zoomed-out one — pins and captions
        /// tuned for reading a building become a mess of overlapping furniture across a whole map.
        /// At 0 they stay one fixed size however far out you go; at 1 they scale with the map and
        /// vanish. In between they ease off, which is what keeps both views readable.</para>
        /// </summary>
        public double ScaleWithZoom { get; init; } = 0.55;

        /// <summary>
        /// Size of your own marker and the cone showing which way you are facing.
        ///
        /// <para>Its own setting, and deliberately not subject to the zoom shrink. Where you are
        /// and which way you are pointing is the one thing that matters at every zoom — pull back
        /// to see the whole map and that is exactly when a marker that has shrunk with everything
        /// else stops answering the question you pulled back to ask.</para>
        /// </summary>
        public double PlayerScale { get; init; } = 3.0;

        /// <summary>
        /// Draw the floor above the ground together with it, rather than ghosted.
        ///
        /// <para>On Streets the ground level holds only building footprints — the interiors are one
        /// level up. Standing inside a building at street level resolves to the ground floor, which
        /// has nothing indoors to draw, so the two have to be read together or going through a door
        /// shows you nothing at all.</para>
        /// </summary>
        public bool MergeGroundFloor { get; init; } = true;

        /// <summary>The arrangement of whichever presentation is on screen.</summary>
        public OverlayPlacement Current => Mode == OverlayMode.Wireframe ? Wireframe : Box;

        /// <summary>Replaces the arrangement of whichever presentation is on screen.</summary>
        public OverlayBounds WithCurrent(OverlayPlacement placement) =>
            Mode == OverlayMode.Wireframe ? this with { Wireframe = placement } : this with { Box = placement };
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
                    }) is { } loaded
                    ? Stamp(loaded, dataDirectory)
                    : Stamp(new RatNavSettings(), dataDirectory);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            // Defaults are all detectable, so a broken settings file costs nothing but the
            // customisations in it.
        }

        return Stamp(new RatNavSettings(), dataDirectory);
    }

    private static RatNavSettings Stamp(RatNavSettings settings, string dataDirectory)
    {
        settings.Origin = dataDirectory;
        return settings;
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
