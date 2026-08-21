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
    private Timer? _sweep;

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
            LogPath = settings.Origin is { Length: > 0 } data
                ? Path.Combine(data, "screenshots-read.log")
                : null,
        };

        _screenshots.PositionFixed += (_, fix) => session.OnPositionFixed(fix);
        _screenshots.Start();

        // Sweep at launch, then daily. These are thirteen-megabyte pictures of a 4K screen, and
        // one development machine had put away 3.9 GB of them before anybody looked.
        Sweep();
        _sweep = new Timer(_ => Sweep(), null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));

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
    /// Clears out screenshots that have already been read.
    ///
    /// <para>Two days, and half a gigabyte at the outside. The age cap is what somebody would ask
    /// for; the size cap is what actually saves them, because three hundred files in two evenings
    /// of playing is four gigabytes and every one of them is inside the age limit.</para>
    ///
    /// <para>What is lost is only pixels. Every position fix's filename is written to a log first,
    /// and the filename is the whole of what a fix knows.</para>
    /// </summary>
    private void Sweep()
    {
        // The configured folder or the default one. ScreenshotDirectory is null until somebody
        // sets it by hand, which is the ordinary case — reading it directly took RatNav down at
        // startup, because tidying up threw out of the code path that starts everything else.
        var folder = settings.ScreenshotDirectory ?? RatNavPaths.DefaultScreenshotDirectory;

        if (folder is not { Length: > 0 }) return;

        try
        {
            var result = ArchiveSweeper.Sweep(folder, TimeSpan.FromDays(2), 512L * 1024 * 1024);

            if (result.Removed > 0) LastSweep = result;
        }
        catch (Exception ex)
        {
            // Anything at all. Tidying is never worth interrupting anything for, least of all the
            // thing that starts the app.
            _ = ex;
        }
    }

    /// <summary>What the last sweep cleared, for saying so in Setup.</summary>
    public SweepResult? LastSweep { get; private set; }

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
        _sweep?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        (_screenshots as IDisposable)?.Dispose();
        _logs?.Dispose();
        _refresh?.Dispose();
        _sweep?.Dispose();
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

    /// <summary>
    /// Whether to open the app in a browser when RatNav starts.
    ///
    /// <para>On, because the panel hotkey that used to do this is gone and something has to. Off
    /// for anyone who would rather open it themselves.</para>
    /// </summary>
    public bool OpenAppAtStart { get; set; } = true;

    /// <summary>
    /// The name this had before the app stopped being called the buddy app.
    ///
    /// <para>Kept only so a settings file written by an older RatNav still says what it meant.
    /// Renaming the property renamed the JSON key, and a key nothing reads is a choice silently
    /// discarded — which for this one means the browser opening on launch after somebody turned
    /// it off.</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("openBuddyAppAtStart")]
    public bool? OpenBuddyAppAtStartLegacy { get; set; }

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
    /// <summary>
    /// How many hideout waves to count <em>beyond</em> the ones you could build today.
    ///
    /// <para>Zero means only what is buildable now. It read as a wave count before — where one
    /// meant none — which put "1" on a dial above the words "only what you can finish now".</para>
    /// </summary>
    public int HideoutLookAhead { get; set; } = 1;

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

        /// <summary>Switch between the corner panel and the centred wireframe map.</summary>
        public string ToggleMode { get; set; } = "F7";

        /// <summary>
        /// Let the mouse reach the overlay, so it can be moved, resized and zoomed. Off by
        /// default in raid: an overlay that swallows a click is worse than one you cannot drag.
        /// </summary>
        public string ToggleInteract { get; set; } = "F6";

        /// <summary>
        /// Identify whatever the mouse is hovering, by reading the tooltip off the screen.
        ///
        /// <para>A key rather than shift-click, deliberately. Catching a mouse click over another
        /// application needs a system-wide mouse hook, which is the same machinery RatNav refuses
        /// to use for the keyboard and for the same reason. A hotkey is registered with Windows
        /// the ordinary way and touches nothing.</para>
        /// </summary>
        public string IdentifyItem { get; set; } = "F11";

        /// <summary>
        /// Read the extract list the game is showing, and keep only those on the map.
        ///
        /// <para>Pressed while the game's own list is up — the one double-tapping <c>O</c> opens.
        /// RatNav cannot know you pressed <c>O</c> without watching the keyboard, which it will
        /// not do, so it asks you to tell it.</para>
        /// </summary>
        public string ReadExtracts { get; set; } = "F10";

        /// <summary>
        /// Put the map back on you, without starting to follow.
        ///
        /// <para>With follow off the map holds still, which is the point of it — a big map that
        /// re-centres on every fix puts the same building somewhere new each time you look. The
        /// cost is that there is then no quick way to ask "where am I now" without turning follow
        /// on and losing the framing you chose. This is that question, asked once.</para>
        /// </summary>
        public string CenterMap { get; set; } = "F9";

        /// <summary>Hold the map still, or have it keep you centred.</summary>
        public string ToggleFollow { get; set; } = "F8";
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
        ///
        /// <para>Zero means "not placed yet", and the app works the real numbers out from the
        /// screen on first run — see <see cref="OverlayPlacement.Unplaced"/>. A fixed pixel
        /// rectangle is a guess about somebody else's monitor: 25,494 sits under the game's health
        /// tracker and above the toolbars on a 4K screen and lands somewhere else entirely on
        /// 1080p, so the position is kept as a share of the screen and turned into pixels once
        /// there is a screen to measure.</para>
        /// </summary>
        public OverlayPlacement Box { get; init; } = new() { Follow = true, Zoom = 3.46 };

        /// <summary>
        /// Where the corner panel starts, as a share of the screen.
        ///
        /// <para>Measured from a real arrangement rather than chosen: low on the left, clear of
        /// the health tracker above it and the toolbars below, which is where it stays out of the
        /// way while you are playing.</para>
        /// </summary>
        public static (double Left, double Top, double Width, double Height) BoxShare =>
            (0.0065, 0.229, 0.216, 0.279);

        /// <summary>
        /// The large centred map. Follows you by default — a centred map that does not is a
        /// centred map of somewhere you were. Drawn as outlines, because crossing ground wants
        /// roads and structures and not much else.
        ///
        /// <para>It said "holds still by default" here for as long as it has actually followed,
        /// which is the kind of thing that leaves everyone sure of a default nobody ships.</para>
        /// </summary>
        /// <para>The zoom and follow are measured from a real arrangement rather than left at
        /// their neutral values. Fully zoomed out is the whole map at once, which is not a useful
        /// place to open a view whose job is showing the ground around you; and a centred map that
        /// does not follow you is a centred map of somewhere you were.</para>
        public OverlayPlacement Wireframe { get; init; } = new()
        {
            Ink = "outline",
            Zoom = 3.0,
            Follow = true,
        };

        /// <summary>
        /// How much of the screen the centred view covers, as a fraction.
        ///
        /// <para>This is the dial that turns the centred map into a HUD. At 1.0 it takes the whole
        /// screen, the drawing fades out toward the edges, and everything off-view is an arrow
        /// pointing at it. Below 1.0 it is a window in the middle of the screen with a border,
        /// which is what it has always been. One continuum rather than two modes, because
        /// <c>Box</c> and <c>Wireframe</c> already carry separate settings and a third would be a
        /// third of everything.</para>
        ///
        /// <para>Unlike the corner panel, this is not overridden by dragging: the centred view is
        /// centred, so the only thing to decide about its rectangle is how big it is.</para>
        /// </summary>
        public double WireframeScale { get; init; } = 0.7;

        /// <summary>
        /// Where the drawing starts fading toward the edges of the full-screen HUD, as a fraction
        /// of the way out from the centre.
        ///
        /// <para>At 1.0 nothing fades and the map ends at a hard edge, which is the thing the HUD
        /// is trying not to look like. Lower starts the fade sooner, so the map dissolves into the
        /// game instead of stopping at a border.</para>
        ///
        /// <para>Only used at full coverage. A windowed centred map has a border already, and
        /// fading out inside one would just look like a fault.</para>
        /// </summary>
        public double EdgeFade { get; init; } = 0.55;

        /// <summary>
        /// Whether the centred map turns so your heading points up the screen.
        ///
        /// <para>On, because it is the reason to have a map in the middle of the screen: what is
        /// drawn at the top is what is in front of you. A setting rather than a rule because
        /// north-up is what a map normally means, and somebody who thinks in compass directions
        /// should be able to keep it.</para>
        ///
        /// <para>Has no effect on the corner panel, which is deliberately still.</para>
        /// </summary>
        public bool TurnWithYou { get; init; } = true;

        /// <summary>
        /// How strongly map lines glow in the full-screen HUD.
        ///
        /// <para>Drawn as a wide, dim stroke under the real one rather than as a blur effect. A
        /// map runs to several hundred paths and a <c>DropShadowEffect</c> on each is a frame
        /// budget spent on decoration.</para>
        /// </summary>
        public double Glow { get; init; } = 1.6;

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
        /// Which quest waypoints to draw — "active", "all" or "off".
        ///
        /// <para>Named to match the app's Maps page, because what you can turn on and off should
        /// read the same in both places. What the three mean is the overlay's own, since the
        /// overlay has a plan and the Maps page does not: <b>active</b> is the plan's stops,
        /// <b>all</b> adds every other started quest's objectives on this map, and <b>off</b>
        /// leaves the map clean.</para>
        /// </summary>
        public string Quests { get; init; } = "active";


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
        /// <summary>
        /// Null until somebody chooses one, which is what lets the first run pick a scale from
        /// the screen rather than from a guess about who is running it.
        /// </summary>
        public double? UiScale { get; init; }

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
        /// Whether the map is drawn in the panel view, or folded away leaving just the lists.
        ///
        /// <para>For the part of a raid spent standing still reading what you still need, where
        /// the map is the biggest thing on screen and the least useful. Nothing about how the map
        /// is set up changes while it is folded — the zoom, the ink, the floor and the rest are
        /// where they were when it comes back.</para>
        /// </summary>
        public bool ShowMap { get; init; } = true;

        /// <summary>
        /// How wide the overlay is with the map folded away. Null until it has been folded once.
        ///
        /// <para>Its own width rather than the placement's, because both are worth remembering:
        /// the strip of two lists wants to be narrow, the map wants room, and a single width would
        /// mean re-dragging the overlay every time it was folded or unfolded. The placement keeps
        /// the width with the map; this keeps the width without it.</para>
        /// </summary>
        public double? FoldedWidth { get; init; }

        /// <summary>
        /// Whether the map settings stack is open while the overlay takes the mouse.
        ///
        /// <para><b>Closed by default.</b> The interact key hands over the mouse and shows the
        /// handles — the grab bar, the drawer chips, the gear. A stack of map settings is a thing
        /// you go and get, not a thing you are handed every time you reach for the mouse.</para>
        ///
        /// <para>Remembered once opened, because how you like to work is worth keeping.</para>
        /// </summary>
        public bool ShowControls { get; init; }

        /// <summary>
        /// The strip of key reminders under the map.
        ///
        /// <para>On by default, because the moment you need reminding which key does what is the
        /// moment you are looking at the plain overlay. Off is for anyone who has learned them —
        /// at which point it is a row of text over a raid, and on a 1080p overlay it is a row the
        /// map could have had.</para>
        /// </summary>
        public bool ShowHotkeyHints { get; init; } = true;

        /// <summary>Which side of the map the items list sits on — "left" or "right".</summary>
        public string ItemsSide { get; init; } = "left";

        /// <summary>
        /// Whether the quest log is open: the started stops in the plan, numbered as they are on
        /// the map, with what each one wants.
        /// </summary>
        public bool ShowQuests { get; init; }

        /// <summary>Which side the quest log sits on. Its own, so the two can face each other.</summary>
        public string QuestsSide { get; init; } = "left";

        /// <summary>
        /// How wide a side panel starts out, in pixels, before either side has been dragged.
        ///
        /// <para>Wide enough for a name like "Chekannaya 15 apartment key" without trimming it,
        /// because a list of truncated names is a list you have to hover to read.</para>
        /// </summary>
        public double ItemsWidth { get; init; } = 223;

        /// <summary>
        /// How wide each side of the map is, once its divider has been dragged. Null means that
        /// side has not been touched and follows <see cref="ItemsWidth"/>.
        ///
        /// <para>Per side rather than per panel, because a column is the thing being sized: two
        /// panels can share a side, and they cannot have two widths between them. Independent,
        /// because dragging one edge of the map and watching the opposite edge move is not a
        /// thing anybody wants.</para>
        /// </summary>
        public double? LeftWidth { get; init; }

        /// <inheritdoc cref="LeftWidth"/>
        public double? RightWidth { get; init; }

        /// <summary>
        /// When the quest log and the items list share a side, how much of the height the quest
        /// log takes — as a fraction, so it survives the overlay being resized.
        ///
        /// <para>Under half by default. The quest log is the shorter of the two and the one you
        /// read rather than scan, so it needs less room than the list of everything you still
        /// need.</para>
        /// </summary>
        public double QuestShare { get; init; } = 0.42;

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
        /// How big the map's own place names are.
        ///
        /// <para>Its own dial rather than sharing one with the waypoint captions, because the two
        /// are doing opposite jobs. Place names are the backdrop you read at a glance to know
        /// which end of the map you are looking at; a waypoint's caption is the thing you are
        /// navigating to. One control for both meant neither was ever the right size.</para>
        /// </summary>
        public double PlaceNameScale { get; init; } = 2.0;

        /// <summary>
        /// How much markers and text shrink as the map is zoomed out, 0 to 1.
        ///
        /// <para>Markers sized for a zoomed-in view cover a zoomed-out one — pins and captions
        /// tuned for reading a building become a mess of overlapping furniture across a whole map.
        /// At 0 they stay one fixed size however far out you go; at 1 they scale with the map and
        /// vanish. In between they ease off, which is what keeps both views readable.</para>
        /// </summary>
        public double ScaleWithZoom { get; init; } = 0.6;

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

    /// <summary>Reaching RatNav from another device on the same network.</summary>
    public LanSettings Lan { get; set; } = new();

    public sealed record LanSettings
    {
        /// <summary>
        /// Whether the service answers on the machine's network address as well as loopback.
        ///
        /// <para><b>Off by default and it stays that way.</b> Binding to the network is a
        /// deliberate act, not something a tool should do because it can — and the person turning
        /// it on is the one who knows what network they are on.</para>
        /// </summary>
        public bool Enabled { get; init; }

        /// <summary>
        /// The port to listen on. Zero means the built-in one.
        ///
        /// <para>Configurable because 8722 can already be taken, and a tool that cannot start
        /// because something else got there first with no way to move is a tool you uninstall.</para>
        /// </summary>
        public int Port { get; init; }
    }

    /// <summary>
    /// Which round of settings migrations this file has already been through.
    ///
    /// <para>A file written before this existed reads as 0 and gets every migration, which is
    /// exactly right — that is what those files need.</para>
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Bumped whenever a migration is added below, so it runs once and then stops.
    ///
    /// <para>1 — the hotkeys renumbered down from F9–F11, and the F6/F7 pair swapped.</para>
    /// <para>2 — the map settings stack starts folded.</para>
    /// <para>3 — the F6/F7 pair swapped back, putting edit mode beside show/hide.</para>
    /// <para>4 — F5 to F11 run in the order the keys are actually used.</para>
    /// <para>5 — the hideout look-ahead counts from zero rather than from one.</para>
    /// </summary>
    private const int CurrentRevision = 5;

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

        if (settings.Revision >= CurrentRevision) return settings;

        // Each migration is gated on its own round, not on the current one.
        //
        // Gated on the current round together, adding a second migration re-runs the first — so
        // somebody who deliberately bound the old F6/F7 pair back after round 1 would have it
        // taken off them again the day round 2 shipped. That is exactly the stickiness the
        // revision stamp exists to prevent, arriving by the back door.
        if (settings.Revision < 1) Renumber(settings.Hotkeys);
        if (settings.Revision < 2) FoldControls(settings);
        if (settings.Revision < 3) PairEditModeWithToggle(settings.Hotkeys);
        if (settings.Revision < 4) RunKeysInUseOrder(settings.Hotkeys);
        if (settings.Revision < 5) CountLookAheadFromZero(settings);

        settings.Revision = CurrentRevision;

        // Written back so a migration runs once rather than on every launch. That is what stops
        // it from being sticky: somebody who deliberately wants the arrangement a migration
        // moved them off can set it and keep it, because the file already says it has been
        // through this round.
        //
        // A settings file that cannot be written is not worth failing to start over — the
        // migrated values are correct in memory either way, and the only cost is doing it again
        // next launch.
        try
        {
            settings.Save(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return settings;
    }

    /// <summary>
    /// Closes the gaps left by the two hotkeys that were removed.
    ///
    /// <para>Opening the panel and ticking an objective off are gone — the app opens with
    /// RatNav, and ticking is better done by hand where you can see the list — which left F7 and
    /// F8 empty and the rest stranded at F9 to F11.</para>
    ///
    /// <para>Only bindings still sitting on their old defaults are moved. Somebody who chose their
    /// own keys chose them, and a settings file quietly rewriting itself is worse than a gap.</para>
    /// </summary>
    private static void Renumber(HotKeySettings keys)
    {
        if (keys.ToggleMode == "F9") keys.ToggleMode = "F7";
        if (keys.IdentifyItem == "F10") keys.IdentifyItem = "F8";
        if (keys.ReadExtracts == "F11") keys.ReadExtracts = "F9";

        SwapModeAndInteract(keys);
    }

    /// <summary>
    /// Moves an existing settings file onto the new F6/F7 pairing.
    ///
    /// <para>The view you flip between constantly now sits next to show/hide, and the key that
    /// hands the overlay the mouse moved out one place. That is a change to the shipped pair
    /// rather than to anybody's own choice, so a file still carrying the old pair comes with it —
    /// otherwise everyone who installed before today keeps the old arrangement forever and the
    /// documentation is wrong for them.</para>
    ///
    /// <para>It fires only on the exact old pair, both keys together. One of them moved by hand
    /// is a choice, and a half-match is somebody's arrangement that happens to share a key.</para>
    /// </summary>
    private static void SwapModeAndInteract(HotKeySettings keys)
    {
        if (keys.ToggleInteract != "F6" || keys.ToggleMode != "F7") return;

        keys.ToggleMode = "F6";
        keys.ToggleInteract = "F7";
    }

    /// <summary>
    /// Puts edit mode back on F6, beside show/hide, and the view switcher on F7.
    ///
    /// <para>Round 1 moved these the other way, on the reasoning that the view you flip between
    /// constantly belongs next to show/hide. Four hours of watching somebody actually use it says
    /// otherwise: he reached for edit mode far more often than for the view, called F7 "edit mode"
    /// unprompted for the whole session, and asked for the toggle and the edit key to sit next to
    /// each other. Reversing a shipped default is worth doing when the evidence is that direct.</para>
    ///
    /// <para>A file old enough to predate round 1 is swapped by <see cref="SwapModeAndInteract"/>
    /// and swapped straight back by this, which nets out at the arrangement it already had — the
    /// correct answer, and the reason each migration is gated on its own round rather than being
    /// collapsed into one.</para>
    ///
    /// <para>Fires only on the exact pair this shipped with. One key moved by hand is a choice.</para>
    /// </summary>
    private static void PairEditModeWithToggle(HotKeySettings keys)
    {
        if (keys.ToggleMode != "F6" || keys.ToggleInteract != "F7") return;

        keys.ToggleMode = "F7";
        keys.ToggleInteract = "F6";
    }

    /// <summary>
    /// Lays F5 to F11 out in the order the keys are used rather than the order they were added.
    ///
    /// <para>They accumulated: show/hide, edit mode and the view switcher took F5 to F7, then
    /// identify and extracts took F8 and F9 because those were next, and centre-on-me and
    /// follow arrived after that and took what was left. So the two that read the screen sat in
    /// the middle of the run and the two that move the map sat at the end of it.</para>
    ///
    /// <para>The order now is: show/hide, edit mode, centre or panel, follow or still, centre on
    /// me, update extracts, check item. Arrange the overlay, then move the map, then read the
    /// screen.</para>
    ///
    /// <para>Fires only when every one of the seven is still where RatNav put it. One key moved
    /// by hand makes the whole set somebody's own arrangement, and shuffling it underneath them
    /// would be worse than an order that reads oddly.</para>
    /// </summary>
    private static void RunKeysInUseOrder(HotKeySettings keys)
    {
        var shipped =
            keys is { ToggleOverlay: "F5", ToggleInteract: "F6", ToggleMode: "F7" }
            && keys is { IdentifyItem: "F8", ReadExtracts: "F9", CenterMap: "F10", ToggleFollow: "F11" };

        if (!shipped) return;

        keys.ToggleFollow = "F8";
        keys.CenterMap = "F9";
        keys.ReadExtracts = "F10";
        keys.IdentifyItem = "F11";
    }

    /// <summary>
    /// Moves a saved look-ahead onto the scale that counts from zero.
    ///
    /// <para>It stored a wave count, where one meant "only what is buildable now". It stores the
    /// number of waves beyond that instead, so the dial and the sentence under it agree. Every
    /// existing file is one higher than it should now be, and a file already at the old floor
    /// lands on the new one.</para>
    /// </summary>
    private static void CountLookAheadFromZero(RatNavSettings settings) =>
        settings.HideoutLookAhead = Math.Max(0, settings.HideoutLookAhead - 1);

    /// <summary>
    /// Closes the map settings stack on files that never chose to have it open.
    ///
    /// <para>It shipped open, so every existing file says open whether or not anyone decided
    /// that — and what it actually did was cover the map with settings every time you reached for
    /// the mouse. Same reasoning as the hotkey swap: the shipped default is changing, so files
    /// still carrying it come along, and the revision stamp means anyone who opens it afterwards
    /// keeps it open for good.</para>
    /// </summary>
    private static void FoldControls(RatNavSettings settings)
    {
        if (settings.Overlay.ShowControls) settings.Overlay = settings.Overlay with { ShowControls = false };
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
        var temporary = path + ".tmp";

        File.WriteAllText(temporary, json);

        // Write-then-move, so a settings file is never half-written — and retried, because the
        // move is the step that fails. A virus scanner opening the brand-new .tmp is enough to
        // make Windows refuse to move it, for a few milliseconds, with "access is denied". That
        // is transient and invisible, and it took down whatever was saving at the time: dragging
        // the divider between two panels raised it on a real machine.
        //
        // Six attempts over about a third of a second. Longer than any scanner needs and shorter
        // than anyone notices.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 5)
                {
                    // Out of retries. Writing straight over the destination gives up the
                    // half-written-file guarantee, which is worth less than not losing the
                    // settings at all — and it fails differently, so it is worth trying.
                    File.WriteAllText(path, json);
                    File.Delete(temporary);
                    return;
                }

                Thread.Sleep(50);
            }
        }
    }
}
