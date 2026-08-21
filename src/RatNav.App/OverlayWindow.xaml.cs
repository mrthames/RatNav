using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// System.IO is here for one exception filter; Path must keep meaning the WPF shape.
using IOException = System.IO.IOException;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Model;
using RatNav.Core.Tracking;
using RatNav.App.Interop;
using RatNav.Service;

// WinForms comes in for the tray icon and brings clashing drawing types with it.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Panel = System.Windows.Controls.Panel;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System.Windows.Threading;

namespace RatNav.App;

/// <summary>
/// The overlay that sits over the game.
///
/// <para>Two presentations of one state. <b>Box</b> is a small panel in a corner, out of the way.
/// <b>Wireframe</b> draws the map itself, large and translucent over the centre — terrain dropped
/// away so buildings and roads carry it and the game still shows through.</para>
///
/// <para><b>Nothing animates or polls.</b> The scene is redrawn when the raid state changes — a
/// position fix, a stop ticked, a new plan — and at no other time. A marker sliding between fixes
/// would be inventing movement it cannot know about, and would cost frames in a firefight to do it.</para>
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly HttpClient _http = new();

    private GlobalHotKey? _hotkeys;
    private RaidView? _view;
    private RatNavSettings _settings;
    private readonly Action<RatNavSettings> _saveSettings;

    private bool _clickThrough = true;
    private string? _mapShapesFor;
    private IReadOnlyList<MapShape> _mapShapes = [];

    /// <summary>The floors below the one in view, drawn faintly under it for context.</summary>
    private IReadOnlyList<MapShape> _ghostShapes = [];

    /// <summary>
    /// The floor immediately above ground, kept apart from it.
    ///
    /// <para>Separate from both the active floor and the floors below because it is neither: it is
    /// the level whose interiors you can walk into from the street, and it has to be visible
    /// without pretending to be where you are standing.</para>
    /// </summary>
    private IReadOnlyList<MapShape> _aboveShapes = [];

    /// <summary>Where the floor you are on sits, so other floors are only faded where they clash.</summary>
    private FloorOverlap? _overlap;
    private Size _mapViewBox = new(1000, 1000);

    /// <summary>
    /// The floor to draw alongside this one, or null when there is nothing to pair it with.
    ///
    /// <para>Only the ground floor pairs, and only upward, by one. Higher floors keep hiding what
    /// is below them — the problem being solved is specifically that you cannot see indoors at
    /// street level, not that floors are hard to tell apart generally.</para>
    ///
    /// <para>Which floor sits above ground differs by map, so it comes from the map's own ordered
    /// floor list rather than from a hardcoded name.</para>
    /// </summary>
    private string? Merged(string? floor)
    {
        if (floor is null || _floors.Count == 0) return null;

        var at = _floors.ToList().FindIndex(f => f.Layer == floor);
        if (at < 0 || at + 1 >= _floors.Count) return null;

        // "Ground" is where the pairing applies. Anywhere else, drawing two floors at once would
        // be adding confusion rather than removing it.
        return _floors[at].Name.StartsWith("Ground", StringComparison.OrdinalIgnoreCase)
            ? _floors[at + 1].Layer
            : null;
    }

    /// <summary>The map's levels, so the floor control knows what there is to look at.</summary>
    private IReadOnlyList<MapFloorSummary> _floors = [];
    private string? _floorsFor;

    /// <summary>
    /// A level chosen by hand, which outranks the one the fix implies — but only until the next
    /// fix. Looking at the floor above is a question you ask once; walking somewhere is an answer,
    /// and the map should follow you rather than stay where you last poked it.
    /// </summary>
    private string? _floorOverride;
    private DateTimeOffset? _overrodeAt;

    /// <summary>
    /// How much of the map to draw, and in whose colours.
    ///
    /// <para><c>graphical</c> uses the map's own palette — the fifteen colours it was drawn with,
    /// saying what is forest, water, rock and road. The others recolour by role, which reads
    /// better over a firefight but throws away everything that makes a map look like a place.</para>
    /// </summary>
    private static readonly string[] InkLevels = ["graphical", "full", "structure", "outline"];

    /// <summary>The current map's own stylesheet, for the graphical ink level.</summary>
    private IReadOnlyDictionary<string, MapStyle> _palette =
        new Dictionary<string, MapStyle>(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ExtractModes = ["pmc", "scav", "both", "off"];

    /// <summary>Named to match the app's Maps page, so one control set reads two places.</summary>
    private static readonly string[] QuestModes = ["active", "all", "off"];

    /// <summary>
    /// A setting value as the app writes it — "PMC", "Graphical", "Active".
    ///
    /// <para>The values are stored lowercase and were shown that way, next to an app that titles
    /// them. Two spellings of the same four choices reads as two different sets of choices.</para>
    /// </summary>
    private static string Titled(string value) => value switch
    {
        "pmc" => "PMC",
        "scav" => "Scav",
        { Length: > 0 } => char.ToUpperInvariant(value[0]) + value[1..],
        _ => value,
    };

    /// <summary>Extracts for the current map, fetched once per map alongside its floors.</summary>
    private IReadOnlyList<ExtractPin> _extracts = [];

    /// <summary>Spots marked by hand. Not part of any plan, so they outlive every plan.</summary>
    private IReadOnlyList<CustomWaypoint> _marks = [];

    /// <summary>
    /// Every started quest's objectives on this map, for the "all" setting.
    ///
    /// <para>Fetched with the rest of a map's furniture rather than when the setting is switched
    /// on, because the switch is flicked mid-raid and a map that fills in a second later reads as
    /// the control not working.</para>
    /// </summary>
    private IReadOnlyList<ObjectivePin> _objectives = [];

    /// <summary>
    /// What each thing on the map is, and where it is, so hovering can say so.
    ///
    /// <para>Recorded while drawing rather than derived after: the scene already knows where every
    /// marker went, and working it out a second time is how the label and the marker end up
    /// disagreeing.</para>
    ///
    /// <para>Drawn last is on top, so this is searched from the end.</para>
    /// </summary>
    private readonly List<(Rect Bounds, string Text, RaidStop? Stop)> _hoverTargets = [];

    /// <summary>Named places on the current map, fetched once per map.</summary>
    private IReadOnlyList<PlaceLabel> _places = [];

    /// <summary>The torn-off items window, while one is open.</summary>
    private ItemsWindow? _itemsWindow;

    /// <summary>The torn-off quest log, while one is open.</summary>
    private ItemsWindow? _questWindow;

    public OverlayWindow(RatNavSettings settings, Action<RatNavSettings> saveSettings)
    {
        InitializeComponent();

        _settings = settings;
        _saveSettings = saveSettings;

        ApplyBounds();
        ApplyMapDrawer();
        ApplyItemsPanel();

        SourceInitialized += (_, _) =>
        {
            OverlayWindowStyles.Apply(this, _clickThrough);

            if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(OnHitTest);
        };
        SizeChanged += (_, _) => Draw();

        // The quest log's ceiling is a fraction of the side's height, so it has to be worked out
        // again whenever that height changes — the overlay is resized, or a panel opens beside it.
        LeftDrawers.SizeChanged += (_, _) => ApplyQuestCeiling();
        RightDrawers.SizeChanged += (_, _) => ApplyQuestCeiling();

        // The stack is placed from the measured height of its neighbours, and both of them change
        // size — the footer wraps, the quick bar grows with the size dial.
        QuickBar.SizeChanged += (_, _) => ClearNeighbours();
        StatusRow.SizeChanged += (_, _) => ClearNeighbours();

        DragHandle.MouseLeftButtonDown += (_, _) => DragMove();
        ResizeGrip.DragDelta += OnResize;
        // On the map, not on the window. Handled at window level it beat the items list to every
        // scroll, so the wheel zoomed the map while the pointer was over a list you were trying
        // to read.
        MapFrame.MouseWheel += OnWheel;

        // The lists scroll on their own wheel handler rather than relying on the one WPF gives a
        // ScrollViewer for free.
        //
        // Both sit inside a window that is click-through most of the time and never takes focus,
        // and the free behaviour was not surviving that — a wheel over a full items list did
        // nothing at all. Handling it here works regardless, and marking it handled stops it
        // bubbling on to anything that would rather zoom the map with it.
        ScrollOnWheel(QuestScroll);
        ScrollOnWheel(ItemsScroll);

        _identifyCardTimer.Tick += (_, _) => HideIdentifyCard();

        _ = LoadHotkeyHintsAsync();

        // The canvas is a bare drawing surface, so WPF leaves the cursor to whatever was last
        // set — which over a transparent window is nothing at all. Stating it keeps the pointer
        // visible while arranging, which is the one time it has to be.
        MapCanvas.Cursor = Cursors.Arrow;

        MouseRightButtonDown += OnPanStart;
        MouseRightButtonUp += OnPanEnd;
        MouseMove += OnPanMove;
        MapCanvas.MouseMove += OnHoverMove;
        MapCanvas.MouseLeftButtonUp += OnMapClick;

        QuestBriefClose.Click += (_, _) => HideQuestBrief();

        // Clicking away puts it down. Handled rather than left to bubble, so the click that
        // dismisses the brief does not also land on the map underneath it and open another one.
        QuestBriefScrim.MouseLeftButtonDown += (_, e) => { HideQuestBrief(); e.Handled = true; };

        // Resizing the overlay with the brief open would otherwise leave it at the height the map
        // used to be — too tall for a smaller map, and no longer nine tenths of a bigger one.
        MapFrame.SizeChanged += (_, _) =>
        {
            if (QuestBrief.Visibility == Visibility.Visible) SizeQuestBrief();

            // Wrapping only helps if something tells it where the edge is.
            QuickBar.MaxWidth = Math.Max(80, MapFrame.ActualWidth - 32);
        };
        QuestBriefBack.Click += (_, _) => StepQuestImage(-1);
        QuestBriefNext.Click += (_, _) => StepQuestImage(+1);
        QuestBriefWiki.Click += (_, _) => OpenWiki();

        // Leaving the map takes the label with it. Without this it stays showing whatever was last
        // under the cursor, which reads as the map claiming something about wherever you moved to.
        MapCanvas.MouseLeave += (_, _) => HoverCard.Visibility = Visibility.Collapsed;

        FloorUp.Click += (_, _) => StepFloor(+1);
        FloorDown.Click += (_, _) => StepFloor(-1);
        QuickFloor.SelectionChanged += (_, _) => OnFloorPicked();
        InkBack.Click += (_, _) => CycleInk(-1);
        InkNext.Click += (_, _) => CycleInk(+1);
        FadeUp.Click += (_, _) => StepFade(+0.1);
        FadeDown.Click += (_, _) => StepFade(-0.1);
        ZoomReset.Click += (_, _) => SetZoom(1);
        FollowButton.Click += (_, _) => ToggleFollowing();
        RecentreButton.Click += (_, _) => Recentre();
        ExtractButton.Click += (_, _) => CycleExtracts();

        TurnButton.Click += (_, _) =>
        {
            Remember(_settings.Overlay with { TurnWithYou = !_settings.Overlay.TurnWithYou });
            Draw();
        };

        CoverageUp.Click += (_, _) => StepCoverage(+0.05);
        CoverageDown.Click += (_, _) => StepCoverage(-0.05);
        FadeEdgeUp.Click += (_, _) => StepEdgeFade(+0.05);
        FadeEdgeDown.Click += (_, _) => StepEdgeFade(-0.05);
        GlowUp.Click += (_, _) => StepGlow(+0.2);
        GlowDown.Click += (_, _) => StepGlow(-0.2);
        QuestVisibility.Click += (_, _) => CycleQuests();
        OfferedButton.Click += (_, _) => ForgetOfferedExtracts();

        // Both directions, on the quick bar. Turning the filter off does not throw the reading
        // away — you can put it back without going into a raid to take another one.
        QuickOffered.Click += (_, _) =>
        {
            Remember(_settings.Overlay with
            {
                OnlyOfferedExtracts = !_settings.Overlay.OnlyOfferedExtracts,
            });

            Redraw();
            UpdateControls(_view);
        };
        OpenAppButton.Click += (_, _) => OpenAppRequested?.Invoke(this, EventArgs.Empty);
        HaloButton.Click += (_, _) => ToggleHalo();
        GhostButton.Click += (_, _) => ToggleGhost();
        PlacesButton.Click += (_, _) => TogglePlaces();
        MarkerUp.Click += (_, _) => StepMarker(+0.5);
        MarkerDown.Click += (_, _) => StepMarker(-0.5);
        TextUp.Click += (_, _) => StepText(+0.5);
        TextDown.Click += (_, _) => StepText(-0.5);
        PlaceNameUp.Click += (_, _) => StepPlaceNames(+0.5);
        PlaceNameDown.Click += (_, _) => StepPlaceNames(-0.5);
        ShrinkUp.Click += (_, _) => StepShrink(+0.1);
        ShrinkDown.Click += (_, _) => StepShrink(-0.1);
        YouUp.Click += (_, _) => StepPlayer(+0.5);
        YouDown.Click += (_, _) => StepPlayer(-0.5);
        WeightUp.Click += (_, _) => StepWeight(+0.25);
        WeightDown.Click += (_, _) => StepWeight(-0.25);
        DetachItems.Click += (_, _) => DetachItemsPanel();
        SwapSide.Click += (_, _) => SwapItemsSide();
        LeftSplitter.DragDelta += (_, e) => OnSideResize(left: true, e);
        RightSplitter.DragDelta += (_, e) => OnSideResize(left: false, e);

        LeftStack.DragDelta += (_, e) => OnStackResize(LeftDrawers, e);
        RightStack.DragDelta += (_, e) => OnStackResize(RightDrawers, e);

        QuickFadeDown.Click += (_, _) => StepWindowOpacity(-0.1);
        QuickFadeUp.Click += (_, _) => StepWindowOpacity(+0.1);
        QuickZoomDown.Click += (_, _) => SetZoom(Placement.Zoom / 1.25);
        QuickZoomUp.Click += (_, _) => SetZoom(Placement.Zoom * 1.25);
        QuickScaleDown.Click += (_, _) => StepUiScale(-0.1);
        QuickScaleUp.Click += (_, _) => StepUiScale(+0.1);
        QuickFollow.Click += (_, _) => ToggleFollowing();
        CollapseItems.Click += (_, _) => ToggleItems();
        ItemsDrawer.Click += (_, _) => ToggleItems();
        QuestDrawer.Click += (_, _) => ToggleQuests();
        MapDrawer.Click += (_, _) => ToggleMapDrawer();
        CollapseQuests.Click += (_, _) => ToggleQuests();
        SwapQuestsSide.Click += (_, _) => SwapQuestsToOtherSide();
        DetachQuests.Click += (_, _) => DetachQuestPanel();
        CollapseControls.Click += (_, _) => ShowControls(false);
        ExpandControls.Click += (_, _) => ShowControls(!_settings.Overlay.ShowControls);
        CentredControls.Click += (_, _) => ShowControls(!_settings.Overlay.ShowControls);
    }

    public event EventHandler? ExpandRequested;

    /// <summary>Raised when someone asks for the app, from the control drawer.</summary>
    public event EventHandler? OpenAppRequested;

    /// <summary>
    /// Binds the configured hotkeys, reporting any that could not be set. Safe to call again
    /// after they are changed in Setup — the previous bindings are released first, because
    /// Windows keeps a combination reserved until it is.
    /// </summary>
    public void BindHotKeys(Action<string> onProblem)
    {
        _hotkeys ??= new GlobalHotKey(this);
        _hotkeys.UnregisterAll();

        var keys = _settings.Hotkeys;

        Bind(keys.ToggleOverlay, "Show/hide overlay", ToggleVisible);
        Bind(keys.ToggleInteract, "Edit mode", ToggleInteractive);
        Bind(keys.ToggleMode, "Center or panel view", ToggleMode);
        Bind(keys.IdentifyItem, "Identify item under cursor", IdentifyUnderCursor);
        Bind(keys.ReadExtracts, "Read the game's extract list", ReadOfferedExtracts);
        Bind(keys.CenterMap, "Center the map on me", CenterOnPlayer);

        void Bind(string text, string what, Action action)
        {
            if (!HotKeySpec.TryParse(text, out var spec, out var problem))
            {
                onProblem($"{what}: {problem}");
                return;
            }

            // Another application may already own a combination. Saying so beats leaving someone
            // pressing a key that does nothing.
            if (!_hotkeys!.Register(spec.Modifiers, spec.Key, action))
                onProblem($"{what}: {spec} is already taken by another app.");
        }
    }

    public void ToggleVisible()
    {
        // Hidden means hidden: the window draws nothing at all, rather than sitting at zero
        // opacity and still costing compositing work.
        //
        // Everything, including a list torn off into its own window — that is still an overlay
        // component, and leaving it on screen after the overlay is hidden is not what "hide the
        // overlay" means to anyone.
        if (IsVisible)
        {
            if (!_clickThrough) SetInteractive(false);

            _itemsWindow?.Hide();
            _questWindow?.Hide();
            Hide();
        }
        else
        {
            Show();
            _itemsWindow?.Show();
            _questWindow?.Show();
        }
    }

    /// <summary>
    /// Hands the mouse to the overlay so it can be moved, resized and zoomed — then takes it away
    /// again. Off during a raid by design: an overlay that swallows a click is worse than one you
    /// cannot drag.
    /// </summary>
    public void ToggleInteractive() => SetInteractive(_clickThrough);

    private void SetInteractive(bool interactive)
    {
        _clickThrough = !interactive;
        OverlayWindowStyles.Apply(this, _clickThrough);

        EditChrome.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        Cursor = interactive ? Cursors.Arrow : Cursors.None;

        ApplyControlStack();
        ApplyItemsPanel();

        if (interactive)
        {
            Show();
            Activate();
        }
        else
        {
            // Where it ended up is where it should be next time — for this presentation only.
            // The list width saves itself as it is dragged, so there is nothing to read back here.
            //
            // With the map folded, the width on screen is the strip's, not the map's. Writing it
            // into the placement would lose the width the map wants, and unfolding would open onto
            // a window the size of two lists.
            if (Folded) Remember(_settings.Overlay with { FoldedWidth = Width });

            // The centred view has no arrangement to save: it is centred, and how big it is comes
            // from the coverage dial. Writing the rectangle it happens to occupy would be storing
            // a value nothing reads.
            if (_settings.Overlay.Mode != RatNavSettings.OverlayMode.Wireframe)
            {
                Place(p => p with
                {
                    Left = Left,
                    Top = Top,
                    Width = Folded ? p.Width : Width,
                    Height = Height,
                });
            }
        }
    }

    /// <summary>True when the panel view is showing its lists with the map folded away.</summary>
    private bool Folded =>
        !_settings.Overlay.ShowMap && _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box;

    /// <summary>
    /// Folds the map away, leaving the lists — or brings it back.
    ///
    /// <para>Nothing about the map is given up on the way. Zoom, ink, floor, follow and which side
    /// the panels are on are all settings, and folding does not touch any of them; the map that
    /// comes back is the one that went away.</para>
    ///
    /// <para>The window's width is the exception, because it has to change: the map was most of
    /// it. Both widths are kept — the placement's with the map, <c>FoldedWidth</c> without — so
    /// neither has to be dragged back into shape.</para>
    /// </summary>
    private void ToggleMapDrawer()
    {
        // The centred view *is* the map. There would be nothing left of it to fold into.
        if (_settings.Overlay.Mode != RatNavSettings.OverlayMode.Box) return;

        var folding = _settings.Overlay.ShowMap;

        // Folding the map with both lists already away would leave a strip of nothing — on
        // screen, not foldable any further, and with no visible handle to bring anything back.
        // The quest log opens instead, so there is always something in the window.
        var lists = _settings.Overlay.ShowItems || _settings.Overlay.ShowQuests;

        Remember(_settings.Overlay with
        {
            ShowMap = !folding,
            ShowQuests = folding && !lists || _settings.Overlay.ShowQuests,

            // The width being left behind is the one worth keeping.
            FoldedWidth = folding ? _settings.Overlay.FoldedWidth : Width,
        });

        if (folding) Place(p => p with { Width = Width });

        ApplyMapDrawer();
        ApplyItemsPanel();
        RefreshItems();
        Draw();
    }

    /// <summary>Gives the map its column, or takes it away.</summary>
    private void ApplyMapDrawer()
    {
        var folded = Folded;

        MapFrame.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;

        // The column has to give up its minimum as well as its width. Left at 80 it holds the
        // window open by that much with nothing in it.
        MapSlot.Width = folded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MapSlot.MinWidth = folded ? 0 : 80;

        // The map controls act on something that is not there. The gear and the stack are handled
        // by ApplyControlStack, which reads this.
        QuickBar.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;

        MapDrawer.Content = folded ? "map ▸" : "map ▾";
        MapDrawer.ToolTip = folded
            ? "Bring the map back, the size it was"
            : "Fold the map away, leaving the lists";

        // An unplaced presentation has no width yet — ApplyBounds has just worked one out from
        // the screen, and reading the stored zero back over it would collapse the window on a
        // fresh install.
        if (folded) Width = _settings.Overlay.FoldedWidth ?? StripWidth();
        else if (!Placement.Unplaced) Width = Placement.Width;

        ApplyControlStack();
    }

    /// <summary>
    /// How wide the strip of lists should open the first time the map is folded.
    ///
    /// <para>Measured from the panels rather than picked, so it lands right whatever they have
    /// been dragged to, and clamped because a fold that opens a window too narrow to read is not
    /// obviously a fold at all.</para>
    /// </summary>
    private double StripWidth()
    {
        var sides = (Occupied(LeftDrawers) ? SideWidth(left: true) : 0)
            + (Occupied(RightDrawers) ? SideWidth(left: false) : 0);

        // The frame, the padding either side, and the gutter each panel keeps against the map.
        return Math.Clamp(sides + 32, 160, Math.Max(160, SystemParameters.WorkArea.Width));
    }

    /// <summary>Switches between the corner panel and the centred wireframe map.</summary>
    public void ToggleMode()
    {
        var mode = _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box
            ? RatNavSettings.OverlayMode.Wireframe
            : RatNavSettings.OverlayMode.Box;

        Remember(_settings.Overlay with { Mode = mode });

        ApplyBounds();
        ApplyMapDrawer();
        ApplyItemsPanel();
        ApplyControlStack();
        Draw();
    }

    /// <summary>Takes settings changed elsewhere — Setup, in practice — and applies them.</summary>
    public void Apply(RatNavSettings settings, Action<string> onProblem)
    {
        Dispatcher.Invoke(() =>
        {
            _settings = settings;

            BindHotKeys(onProblem);
            ApplyBounds();
            Draw();

            // Rebinding a key changes what the reminder strip should say.
            _ = LoadHotkeyHintsAsync();
        });
    }

    public void Update(RaidView view)
    {
        var had = PlanApplies(_view);
        var lastFix = _view?.FixedAt;

        _view = view;
        Dispatcher.Invoke(async () =>
        {
            FollowPlan(had, PlanApplies(view));
            RevealOnFix(lastFix, view.FixedAt);

            await EnsureMapAsync(view);
            Draw();
            RefreshItems();
        });
    }

    /// <summary>
    /// Brings the overlay back when a screenshot puts a new fix on the map.
    ///
    /// <para>Taking a screenshot is how you tell RatNav where you are — the game writes the
    /// coordinates into the filename and the watcher reads them. So a screenshot taken with the
    /// overlay hidden produced a better map that nobody could see, and getting to it meant a
    /// second key on the other side of the keyboard. You press the shot because you want to know
    /// where you are; this is that.</para>
    ///
    /// <para>On a <em>new</em> fix, never on the state. Every publish carries the last fix's
    /// timestamp, and reopening whenever one exists would make the overlay impossible to put away
    /// mid-raid — it would come back on the next quest ticked off.</para>
    /// </summary>
    private void RevealOnFix(DateTimeOffset? before, DateTimeOffset? now)
    {
        if (now is null || now == before || IsVisible) return;

        Show();
        _itemsWindow?.Show();
        _questWindow?.Show();
    }

    /// <summary>
    /// Opens the quest log with a new plan, and closes it when the plan goes.
    ///
    /// <para>The log is a list of a plan's stops. Building a plan and then having to go and turn on
    /// the panel that shows it is a step that should not exist; clearing one and leaving an empty
    /// panel holding a side of the overlay open is the same mistake backwards.</para>
    ///
    /// <para><b>On the change, never on the state.</b> Following the state would reopen the panel
    /// on every position fix for anyone who had deliberately folded it away mid-raid — which is a
    /// reasonable thing to do with a plan you have memorised.</para>
    /// </summary>
    private void FollowPlan(bool had, bool has)
    {
        if (had == has) return;

        Remember(_settings.Overlay with { ShowQuests = has });
        ApplyItemsPanel();
    }

    /// <summary>
    /// Whether there is a plan <em>for the map on screen</em>.
    ///
    /// <para>Not the same question as <see cref="RaidView.HasPlan"/>, which is true for a plan
    /// built for anywhere. Queue into Interchange carrying a plan for Customs and the overlay
    /// opened the quest log for it — a log belonging to a map you are not on, over a raid you have
    /// not planned. The session already declines to draw those stops; this is the panel that
    /// announces them agreeing with it.</para>
    ///
    /// <para>The session says so by naming the other map: <see cref="RaidView.PlanMapName"/> is
    /// set only when a plan is loaded and is for somewhere else, so its absence is the signal.</para>
    /// </summary>
    private static bool PlanApplies(RaidView? view) =>
        view is { HasPlan: true, PlanMapName: null };

    /// <summary>
    /// Puts the overlay back where it starts, and makes sure it is visible.
    ///
    /// <para>The recovery for a window dragged onto a monitor that is no longer there: there is
    /// nothing on screen to grab, so no amount of dragging brings it back.</para>
    /// </summary>
    public void ReturnToDefaultPlace() => Dispatcher.Invoke(() =>
    {
        ApplyBounds();

        // Shown as well as moved. Someone asking for this has lost the window, and putting it
        // somewhere correct but still hidden would look exactly like the button doing nothing.
        Show();
        Draw();
    });

    private void ApplyBounds()
    {
        var bounds = _settings.Overlay;
        var placement = bounds.Current;

        // The centred view is centred, always. Its rectangle has one thing to decide — how much of
        // the screen it covers — and dragging it somewhere is not a thing that makes sense for a
        // view whose whole idea is that you are in the middle of it. So it is placed from the dial
        // every time rather than from a saved rectangle, which also means turning the dial to 1.0
        // takes effect immediately instead of on the next fresh install.
        if (bounds.Mode == RatNavSettings.OverlayMode.Wireframe)
        {
            // The whole screen at full coverage, not the work area: the HUD is meant to reach the
            // edges, and the taskbar is not something to leave a gap for when the game is over it.
            var full = Coverage >= 1;

            // The whole primary screen, not the virtual desktop: a second monitor to the left
            // puts the virtual origin at a negative x, which would centre the HUD across both and
            // leave half of it on the wrong one. WorkArea is the primary monitor too, so this
            // stays on the same screen the centred map has always used.
            var screen = full
                ? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight)
                : SystemParameters.WorkArea;

            Width = screen.Width * Coverage;
            Height = screen.Height * Coverage;
            Left = screen.Left + (screen.Width - Width) / 2;
            Top = screen.Top + (screen.Height - Height) / 2;
        }
        else if (placement.Unplaced)
        {
            // Never arranged. The corner panel gets a corner; once it is moved, that arrangement
            // is what comes back.
            var screen = SystemParameters.WorkArea;

            {
                // Kept as a share of the screen rather than a pixel rectangle. These numbers come
                // from a real arrangement — low on the left, clear of the game's health tracker
                // above and its toolbars below, which is where it stays out of the way while you
                // are playing — and that position only survives a change of monitor if it is
                // expressed relative to one.
                var (left, top, width, height) = RatNavSettings.OverlayBounds.BoxShare;

                Width = screen.Width * width;
                Height = screen.Height * height;
                Left = screen.Left + (screen.Width * left);
                Top = screen.Top + (screen.Height * top);
            }
        }
        else
        {
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
        }

        // The window itself, not the map inside it.
        ApplyWindowOpacity();
        ApplyUiScale();

        if (bounds.Mode == RatNavSettings.OverlayMode.Wireframe)
        {
            // Nothing but the map. In box mode the readout is the point and the map supports it;
            // here that is inverted, and a title and a timestamp floating in the corners of the
            // screen are just text over the game where no text should be.
            Frame.Background = Brushes.Transparent;
            Frame.BorderThickness = new Thickness(0);
            MapFrame.Background = Brushes.Transparent;
            MapFrame.BorderThickness = new Thickness(0);

            Readout.Visibility = Visibility.Collapsed;
            StatusRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            // A scrim, not bare text on the game: Tarkov's backgrounds run from snow to unlit
            // basement, and no single text colour survives both.
            Frame.Background = new SolidColorBrush(Color.FromArgb(0xe6, 0x0b, 0x0f, 0x13));
            Frame.BorderThickness = new Thickness(1);
            MapFrame.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x14, 0x1b, 0x21));
            MapFrame.BorderThickness = new Thickness(1);

            Readout.Visibility = Visibility.Visible;
            StatusRow.Visibility = Visibility.Visible;
        }
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(220, Width + e.HorizontalChange);
        Height = Math.Max(160, Height + e.VerticalChange);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    /// <summary>
    /// Lets a click through the HUD unless it lands on a control.
    ///
    /// <para>Click-through is a whole-window style, which is right until the window is the whole
    /// screen. Then turning it off to reach a control means the entire screen stops passing
    /// clicks — so you cannot shoot, and an interact key pressed by accident mid-raid leaves you
    /// unable to click at anything. The corner panel never had this problem because it is small
    /// and in a corner; a HUD has no margin at all.</para>
    ///
    /// <para>So at full coverage the window answers per point instead: the controls take their
    /// clicks and everywhere else says it is not there. Panning by right-drag goes with it, which
    /// is the right trade — a HUD is centred on you and there is nothing to drag it to.</para>
    /// </summary>
    private IntPtr OnHitTest(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Below full coverage this is the centred *window*, which behaves as it always has.
        // And while click-through is on, the extended style has already answered.
        if (message != WM_NCHITTEST || !Hud || _clickThrough) return IntPtr.Zero;

        var packed = (int)(lParam.ToInt64() & 0xFFFFFFFF);
        var screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));

        try
        {
            if (OverControl(PointFromScreen(screen))) return IntPtr.Zero;
        }
        catch (InvalidOperationException)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(HTTRANSPARENT);
    }

    /// <summary>
    /// How far away something is, on the ground, as a short string — or null with no fix.
    ///
    /// <para>Straight-line distance rather than a walked one. RatNav has no idea what is between
    /// you and a door, and a number that pretended otherwise would be wrong in a way that matters;
    /// this one is only ever used to compare two exits, which it does honestly.</para>
    ///
    /// <para>Rounded hard on purpose. Metre-precision on a figure that changes every few steps is
    /// noise, and a readout that flickers is one you stop reading.</para>
    /// </summary>
    private string? Metres(ExtractPin extract)
    {
        if (_view?.WorldX is not { } px || _view.WorldZ is not { } pz) return null;

        var dx = extract.WorldX - px;
        var dz = extract.WorldZ - pz;
        var away = Math.Sqrt(dx * dx + dz * dz);

        return away >= 1000
            ? $"{away / 1000:0.0} km"
            : $"{Math.Round(away / 5) * 5:0} m";
    }

    /// <summary>Whether a point in window space is over something meant to be clicked.</summary>
    private bool OverControl(Point point)
    {
        foreach (var element in new FrameworkElement[] { ControlStack, CentredControls, QuickBar })
        {
            if (!element.IsVisible) continue;

            var origin = element.TransformToAncestor(this).Transform(new Point(0, 0));
            if (new Rect(origin, element.RenderSize).Contains(point)) return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps the controls stack clear of the things above and below it.
    ///
    /// <para>Its margins were fixed numbers — six from the top, twenty-six from the bottom — and
    /// both were right once. The footer has grown a second row since, and every piece of RatNav's
    /// furniture scales with the size dial, so any number written down here is wrong at some
    /// scale. The neighbours know how tall they are; this asks them.</para>
    ///
    /// <para>Measured in window space rather than from <c>ActualHeight</c>, because the pieces
    /// carry layout transforms and their own idea of their height is the one before scaling.</para>
    /// </summary>
    private void ClearNeighbours()
    {
        const double gap = 6;

        // The stack sits in the row under the grab bar, so its margin is measured from there
        // rather than from the top of the window.
        var rowTop = Bounds(DragHandle)?.Bottom ?? 0;

        var top = Math.Max(gap, (Bounds(QuickBar)?.Bottom ?? 0) + gap - rowTop);
        var bottom = Bounds(StatusRow) is { } footer
            ? Math.Max(gap, ActualHeight - footer.Top + gap)
            : gap;

        // The centred view's gear floats where the stack starts, so the stack starts under it.
        if (Bounds(CentredControls) is { } gear) top = Math.Max(top, gear.Bottom + gap - rowTop);

        var wanted = new Thickness(gap, top, 0, bottom);

        // Assigning triggers a layout pass, which is what measured these in the first place —
        // so only when it would actually move.
        if (Math.Abs(ControlStack.Margin.Top - wanted.Top) < 0.5
            && Math.Abs(ControlStack.Margin.Bottom - wanted.Bottom) < 0.5)
        {
            return;
        }

        ControlStack.Margin = wanted;
    }

    /// <summary>Where a piece of chrome sits in the window, transforms and all, or null if it is not showing.</summary>
    private Rect? Bounds(FrameworkElement element)
    {
        if (!element.IsVisible) return null;

        try
        {
            return element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>How much of the screen the centred view takes, clamped to something usable.</summary>
    private double Coverage => Math.Clamp(_settings.Overlay.WireframeScale, 0.3, 1.0);

    /// <summary>
    /// True when the centred view has been turned up to cover the whole screen.
    ///
    /// <para>The HUD and the windowed centred map are the same view at two ends of one dial, so
    /// this is what everything that only applies to the HUD — the edge fade, the glow, and letting
    /// clicks through where there is no control — asks.</para>
    /// </summary>
    private bool Hud =>
        _settings.Overlay.Mode == RatNavSettings.OverlayMode.Wireframe && Coverage >= 1;

    private void StepCoverage(double by)
    {
        Remember(_settings.Overlay with
        {
            WireframeScale = Math.Clamp(_settings.Overlay.WireframeScale + by, 0.3, 1.0),
        });

        ApplyBounds();
        ApplyControlStack();
        Draw();
    }

    private void StepEdgeFade(double by)
    {
        Remember(_settings.Overlay with
        {
            EdgeFade = Math.Clamp(_settings.Overlay.EdgeFade + by, 0.2, 1.0),
        });

        Draw();
    }

    private void StepGlow(double by)
    {
        Remember(_settings.Overlay with
        {
            Glow = Math.Clamp(_settings.Overlay.Glow + by, 1.0, 4.0),
        });

        Draw();
    }

    private Point? _panFrom;

    /// <summary>
    /// Starts a drag. Right button, and only while interactive — right-click is aim-down-sights,
    /// and an overlay that swallowed it during a raid would be worse than one that cannot pan.
    /// </summary>
    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough) return;

        _panFrom = e.GetPosition(this);
        Cursor = Cursors.SizeAll;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from || e.RightButton != MouseButtonState.Pressed) return;

        var to = e.GetPosition(this);
        _panFrom = to;

        // Looking somewhere else is a decision. Leaving following on would snap the map back on
        // the next fix and undo the drag a second after it was made.
        //
        // But switching it off has to leave the view exactly where it is. Following centres on
        // you; not following centres on the middle of the map — so flipping it mid-drag threw the
        // map to the centre and you carried on dragging from somewhere you had not chosen. The
        // offset you were already at is folded into the pan, which makes the change invisible.
        if (Following && _view is { X: { } atX, Y: { } atY })
            Place(p => p with { Follow = false, PanX = p.PanX + atX - 0.5, PanY = p.PanY + atY - 0.5 });
        else if (Following)
            Place(p => p with { Follow = false });

        Pan(to.X - from.X, to.Y - from.Y);
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (_panFrom is null) return;

        _panFrom = null;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Makes one list scroll under the wheel, and keeps the event to itself.</summary>
    /// <summary>
    /// Keeps the popped-out lists looking like part of the overlay.
    ///
    /// <para>Opacity and size are the overlay's, not each window's own: a list parked in its own
    /// window is the same list, and a solid full-size panel beside a faded scaled one reads as two
    /// different tools. The controls stay on the overlay rather than being grown a second time on
    /// every pop-out.</para>
    /// </summary>
    private void MatchPopOuts()
    {
        var opacity = Placement.WindowOpacity;
        var scale = Math.Clamp(EffectiveUiScale, 0.7, 3.0);

        _itemsWindow?.MatchOverlay(opacity, scale);
        _questWindow?.MatchOverlay(opacity, scale);
    }

    private static void ScrollOnWheel(ScrollViewer view)
    {
        view.PreviewMouseWheel += (_, e) =>
        {
            // Three lines a notch, which is what the rest of Windows does.
            view.ScrollToVerticalOffset(view.VerticalOffset - (e.Delta / 120.0 * 3 * 16));
            e.Handled = true;
        };
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        // Only while interactive, so a stray scroll mid-raid cannot move the map under you.
        if (_clickThrough) return;

        var zoom = Math.Clamp(Placement.Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 1, 8);
        Place(p => p with { Zoom = zoom });
        Draw();
    }

    private void Remember(RatNavSettings.OverlayBounds bounds)
    {
        _settings = _settings with { Overlay = bounds };
        QueueSave();
    }

    private DispatcherTimer? _saveTimer;

    /// <summary>
    /// Writes the arrangement out shortly after it stops changing, rather than as it changes.
    ///
    /// <para>Every drag of a divider or an edge raises a delta event per mouse move, and each one
    /// used to serialise the whole settings file and move it into place — dozens of writes a
    /// second for one gesture. Most were overwritten a frame later by the next one, so the disk
    /// work bought nothing, and the odds of catching the file momentarily busy went up with every
    /// extra write.</para>
    ///
    /// <para>The in-memory copy still updates immediately, so nothing on screen waits for this and
    /// nothing is lost if the mouse leaves the window mid-drag — which over a game it frequently
    /// does, and which is why this was written as it moves in the first place. Only the trip to
    /// disk waits, for a quarter of a second after the last change.</para>
    /// </summary>
    private void QueueSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };

            _saveTimer.Tick += (_, _) => FlushSave();
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Writes the arrangement now, and never throws.
    ///
    /// <para>This runs from a mouse handler. An exception here does not just fail the save — it
    /// unwinds the drag that was in progress, which leaves a divider parked wherever it got to and
    /// the mouse still captured, and puts an error dialog over the game. A layout that could not
    /// be written is worth none of that: the arrangement is correct in memory either way, and the
    /// next change tries again.</para>
    /// </summary>
    private void FlushSave()
    {
        _saveTimer?.Stop();

        try
        {
            _saveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to tell anybody. The settings on screen are the settings.
        }
    }

    /// <summary>How the presentation on screen is arranged — its own copy, not the other one's.</summary>
    private RatNavSettings.OverlayPlacement Placement => _settings.Overlay.Current;

    /// <summary>Changes that arrangement, leaving the other presentation's untouched.</summary>
    private void Place(Func<RatNavSettings.OverlayPlacement, RatNavSettings.OverlayPlacement> change) =>
        Remember(_settings.Overlay.WithCurrent(change(Placement)));

    /// <summary>
    /// Fetches and parses the current map once, so the overlay can draw the real thing. The
    /// service inks it first, which is the same styling the web app gets.
    /// </summary>
    private async Task EnsureMapAsync(RaidView view)
    {
        if (view.MapId is not { Length: > 0 } mapId) return;

        await EnsureFloorsAsync(mapId);

        var floor = FloorFor(view);
        var key = $"{mapId}|{floor}";
        if (key == _mapShapesFor) return;

        try
        {
            // The map exactly as its author drew it, because everything here is styled on this
            // side and the one thing that cannot be recovered is the original palette.
            //
            // This asked for ink=full, which is a *restyled* map: the service replaces the
            // author's stylesheet with RatNav's own flat one. Reading a palette back out of that
            // returned RatNav's wireframe colours, so the graphical level — whose whole job is to
            // use the map's own fifteen colours — drew the skeleton instead. Graphical is the one
            // level the service leaves untouched, which makes it the right thing to fetch.
            var url = $"{ServiceHost.Root}/api/maps/{Uri.EscapeDataString(mapId)}/image?ink=graphical";

            var svg = await _http.GetStringAsync(url);

            _mapViewBox = MapGeometry.ViewBoxOf(svg);
            _palette = MapPalette.Read(svg, mapId);
            _mapShapes = MapGeometry.Parse(svg, floor, key);

            // Nothing is ghosted when everything is drawn. Parse with no floor named and the
            // whole map comes back in document order, which is lowest level first — so upper
            // floors land on top without anything having to sort them.
            //
            // Ghosting still earns its place when a floor has been picked by hand: a single level
            // in isolation is hard to place, because the room means nothing without the street it
            // came off.
            var below = floor is { Length: > 0 }
                ? _floors
                    .TakeWhile(f => f.Layer != floor)
                    .Select(f => MapGeometry.Parse(svg, f.Layer, $"{mapId}|{f.Layer}"))
                    .ToList()
                : [];

            _ghostShapes = [.. below.SelectMany(shapes => shapes)];

            // Worked out once per floor rather than per draw: it walks every shape, and the answer
            // only changes when the floor does.
            _overlap = FloorOverlap.Of(_mapShapes, _mapViewBox);

            // On Streets the ground level is building *footprints* — what interiors exist live one
            // floor up. Standing inside a building at street level resolves to the ground floor,
            // which has nothing indoors on it, so the floor above is worth showing alongside.
            //
            // Kept in the ghost layer rather than merged into the floor you are on. Drawn at full
            // strength it reads as your floor, so a stairwell one storey up looks like a room
            // beside you — and it ignored the ghost toggle, which is not something a toggle
            // labelled "ghost" should be able to do.
            // Only meaningful when one floor is showing. Stacked, the floor above is already there.
            _aboveShapes = floor is { Length: > 0 }
                && _settings.Overlay.MergeGroundFloor && Merged(floor) is { } partner
                ? MapGeometry.Parse(svg, partner, $"{mapId}|{partner}")
                : [];
            _mapShapesFor = key;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // No map drawing this time. The route and readout still work, which is most of the value.
            _mapShapes = [];
            _ghostShapes = [];
            _aboveShapes = [];
            _overlap = null;
        }
    }

    /// <summary>The map's levels, fetched once per map so the floor control has something to step through.</summary>
    private async Task EnsureFloorsAsync(string mapId)
    {
        if (_floorsFor == mapId) return;

        try
        {
            var root = $"{ServiceHost.Root}/api";

            var maps = await _http.GetFromJsonAsync<List<MapSummary>>($"{root}/maps");
            _floors = maps?.FirstOrDefault(m => m.Id == mapId)?.Floors ?? [];

            _extracts = await _http.GetFromJsonAsync<List<ExtractPin>>(
                $"{root}/maps/{Uri.EscapeDataString(mapId)}/extracts") ?? [];

            _places = await _http.GetFromJsonAsync<List<PlaceLabel>>(
                $"{root}/maps/{Uri.EscapeDataString(mapId)}/places") ?? [];

            _marks = await _http.GetFromJsonAsync<List<CustomWaypoint>>(
                $"{root}/maps/{Uri.EscapeDataString(mapId)}/waypoints") ?? [];

            // Started quests only. Every quest in the game on one map is not a map.
            _objectives = await _http.GetFromJsonAsync<List<ObjectivePin>>(
                $"{root}/maps/{Uri.EscapeDataString(mapId)}/objectives?active=true") ?? [];

            _floorsFor = mapId;

            // A different map has different levels, and a floor chosen on the last one means
            // nothing here. Back to stacked, which is the answer that is never wrong.
            _floorOverride = null;
            FillFloorList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _floors = [];
            _extracts = [];
            _places = [];
            _marks = [];
            _objectives = [];
        }
    }

    /// <summary>
    /// Whether an extract is worth drawing. Shared ones always are — they work for either faction
    /// — so the faction choice only ever hides the ones the other side uses.
    ///
    /// <para>On top of that, once the game's own list has been read this raid, only what it named
    /// is drawn. Nothing read means nothing is hidden: "not asked yet" and "none are open" are
    /// different answers, and a map that goes blank because you have not pressed a key is worse
    /// than one showing an extract you cannot use.</para>
    /// </summary>
    private bool ShowsExtract(ExtractPin extract)
    {
        var mode = _settings.Overlay.Extracts;

        if (mode == "off") return false;

        var offered = _settings.Overlay.OfferedExtracts;

        if (_settings.Overlay.OnlyOfferedExtracts && offered.Count > 0
            && !offered.Contains(extract.Name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (mode == "both") return true;

        // Whoever is standing at a transit can take it, so the PMC/Scav dial has nothing to say
        // about one. Filtering them by faction would hide half the ways off a map from a player
        // who had asked to see only their own.
        if (extract.Transit) return true;

        return extract.Faction.Equals("shared", StringComparison.OrdinalIgnoreCase)
            || extract.Faction.Equals(mode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the extract list the game is showing and keeps only those on the map.
    ///
    /// <para>Pressed while the game's own list is up. RatNav cannot know you opened it without
    /// watching the keyboard, which it will not do — so it asks you to say when.</para>
    /// </summary>
    private void ReadOfferedExtracts()
    {
        if (!ScreenTextReader.Available)
        {
            ShowIdentifyCard(
                "Cannot read the screen",
                "Windows has no OCR language pack installed.",
                []);
            return;
        }

        var (x, y) = ScreenTextReader.CursorPosition();

        ShowIdentifyCard("Reading the extract list…", "", []);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var lines = await ScreenTextReader.ReadScreenAsync(x, y);

            try
            {
                var url = $"{ServiceHost.Root}/api/raid/extracts/read";
                var response = await _http.PostAsJsonAsync(url, new { lines });

                if (!response.IsSuccessStatusCode)
                {
                    ShowIdentifyCard("No map loaded", "Start a raid, or show a map first.", []);
                    return;
                }

                var read = await response.Content.ReadFromJsonAsync<OfferedExtractsResponse>();
                var offered = read?.Offered ?? [];

                _settings.Overlay = _settings.Overlay with { OfferedExtracts = offered };

                ShowIdentifyCard(
                    offered.Count > 0
                        ? $"{offered.Count} of {read?.Of ?? 0} extracts open to you"
                        : "No extract names found on screen",

                    offered.Count > 0
                        ? string.Join(" · ", offered)
                        : "Press it again with the game's extract list showing.",
                    []);

                Draw();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                ShowIdentifyCard("Could not reach RatNav's own service", "", []);
            }
        });
    }

    /// <summary>Back to every extract the map has, forgetting what the game listed.</summary>
    private void ForgetOfferedExtracts()
    {
        _settings.Overlay = _settings.Overlay with { OfferedExtracts = [] };

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await _http.DeleteAsync(
                    $"{ServiceHost.Root}/api/raid/extracts/read");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The overlay already forgot; the service catching up matters less than the map
                // redrawing now.
            }
        });

        ApplyControlStack();
        Draw();
    }

    // ---- the quest behind a waypoint

    /// <summary>The pictures for the quest currently open, and which one is showing.</summary>
    private IReadOnlyList<WikiImage> _briefImages = [];
    private int _briefImageAt;
    private string? _briefWikiUrl;

    /// <summary>One line of a quest's steps, coloured by where you are in it.</summary>
    private sealed record BriefStep(string Text, Brush Colour);

    /// <summary>Puts the brief and the sheet behind it away together.</summary>
    private void HideQuestBrief()
    {
        QuestBrief.Visibility = Visibility.Collapsed;
        QuestBriefScrim.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Sizes the brief to sit over the map rather than replace it.
    ///
    /// <para>It filled the map area edge to edge, which reads as the map having been swapped for
    /// something else rather than as something laid on top of it. Nine tenths of the height,
    /// centred, leaves a margin of map showing above and below — enough to say "this is in front
    /// of that". The width stays whatever the player made the map, because a modal narrower than
    /// its container gains nothing here and the pictures want the room.</para>
    ///
    /// <para>The steps are capped so a long quest cannot squeeze the pictures out. They are the
    /// half that turns "walk to this pin" into "find this door".</para>
    /// </summary>
    private void SizeQuestBrief()
    {
        var available = MapFrame.ActualHeight;
        if (available <= 0) return;

        QuestBrief.MaxHeight = available * 0.9;
        QuestBriefScroll.MaxHeight = Math.Max(48, available * 0.3);
    }

    private void ShowQuestBrief(RaidStop stop)
    {
        QuestBriefName.Text = stop.TaskName;
        QuestBriefSteps.ItemsSource = new[] { new BriefStep("Reading…", (Brush)FindResource("Muted")) };
        QuestBriefImage.Source = null;
        QuestBriefCaption.Text = "";

        SizeQuestBrief();
        QuestBriefScrim.Visibility = Visibility.Visible;
        QuestBrief.Visibility = Visibility.Visible;

        _briefImages = [];
        _briefImageAt = 0;
        _briefWikiUrl = null;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var url = $"{ServiceHost.Root}/api/tasks/"
                    + $"{Uri.EscapeDataString(stop.TaskId)}/brief"
                    + $"?objectiveId={Uri.EscapeDataString(stop.ObjectiveId)}";

                if (await _http.GetFromJsonAsync<QuestBriefing>(url) is not { } brief) return;

                QuestBriefName.Text = brief.TraderName is { Length: > 0 } trader
                    ? $"{brief.Name} · {trader}"
                    : brief.Name;

                QuestBriefSteps.ItemsSource = brief.Objectives?.Select(o => new BriefStep(
                    (o.Done ? "✓ " : o.Current ? "→ " : "· ") + o.Description,
                    (Brush)FindResource(o.Done ? "Muted" : o.Current ? "Accent" : "Ink"))).ToList()
                    ?? [];

                _briefWikiUrl = brief.WikiUrl;
                _briefImages = brief.Images ?? [];

                ShowQuestImage();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                QuestBriefSteps.ItemsSource = new[]
                {
                    new BriefStep("Could not load this quest.", (Brush)FindResource("Muted")),
                };
            }
        });
    }

    private void StepQuestImage(int by)
    {
        if (_briefImages.Count == 0) return;

        _briefImageAt = ((_briefImageAt + by) % _briefImages.Count + _briefImages.Count) % _briefImages.Count;
        ShowQuestImage();
    }

    /// <summary>
    /// Puts the current picture on screen.
    ///
    /// <para>Loaded straight from the wiki rather than cached to disk: they are other people's work
    /// under CC BY-SA, and a release that shipped them would be both a licensing question and a
    /// hundred megabytes.</para>
    /// </summary>
    private void ShowQuestImage()
    {
        var showing = _briefImages.Count > 0;

        QuestBriefBack.Visibility = _briefImages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        QuestBriefNext.Visibility = QuestBriefBack.Visibility;

        if (!showing)
        {
            QuestBriefImage.Source = null;
            QuestBriefCaption.Text = "no pictures on the wiki for this one";
            return;
        }

        var image = _briefImages[_briefImageAt];

        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();

            // Through the service, which keeps a copy. WPF happens not to send a Referer and so
            // could fetch these directly where a browser cannot, but going the same way as the web
            // app means one fetch is shared by both rather than each pulling several megabytes of
            // its own.
            bitmap.UriSource = new Uri(
                $"{ServiceHost.Root}/api/wiki/picture"
                + $"?url={Uri.EscapeDataString(image.Url)}");

            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            // Decoded down rather than at source size: these are 1920-wide screenshots and the
            // panel is a few hundred pixels across.
            bitmap.DecodePixelWidth = 900;
            bitmap.EndInit();

            QuestBriefImage.Source = bitmap;
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException or IOException)
        {
            QuestBriefImage.Source = null;
        }

        QuestBriefCaption.Text =
            $"{_briefImageAt + 1} of {_briefImages.Count} · Escape from Tarkov Wiki, CC BY-SA";
    }

    private void OpenWiki()
    {
        if (_briefWikiUrl is not { Length: > 0 } url) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // No browser, or none that will take it. Not worth interrupting a raid over.
        }
    }

    /// <summary>What the quest brief endpoint returns.</summary>
    private sealed record QuestBriefing(
        string? Name,
        string? TraderName,
        string? WikiUrl,
        List<BriefObjective>? Objectives,
        List<WikiImage>? Images);

    private sealed record BriefObjective(string? Description, bool Current, bool Done);

    /// <summary>What the extract-reading endpoint returns.</summary>
    private sealed record OfferedExtractsResponse(List<string>? Offered, int Of);

    /// <summary>
    /// Which level to draw. A hand-picked floor wins, but a fix taken since you picked it wins
    /// back — the map following where you actually are is the whole point of taking a fix.
    /// </summary>
    /// <summary>
    /// Which floor to draw: whichever you picked, or all of them stacked.
    ///
    /// <para>This used to follow your elevation, and it was solving a problem the map does not
    /// have. The map is for orienting against major structures — which warehouse, which building,
    /// which side of the road — and the room you are in comes from the quest's wiki pictures,
    /// which is the thing that actually answers "which door".</para>
    ///
    /// <para>It also could not be relied on. Customs declares Underground as −1000 to 0.5 and
    /// Ground as −1000 to 1000, so a fix at −3.9 satisfies both and the answer came down to which
    /// was listed last. When it chose a layer with no geometry the drawing emptied — leaving the
    /// labels and extract markers, which come from elsewhere, over a blank map.</para>
    ///
    /// <para>So: stacked by default, and a position fix never changes it underneath you. Picking a
    /// floor by hand still works and now stays picked.</para>
    /// </summary>
    private string? FloorFor(RaidView view) => _floorOverride;

    /// <summary>
    /// Fills the floor dropdown for the map on screen.
    ///
    /// <para>Only when the map's levels have actually changed. Rebuilt on every redraw it would
    /// close itself under the pointer mid-choice, which reads as the control refusing to open.</para>
    ///
    /// <para><b>Stacked is first and is the default.</b> Every floor drawn over the other is what
    /// the map does unless told otherwise — choosing one for you off your height was solving a
    /// problem the map does not have, and getting it wrong emptied the drawing entirely.</para>
    /// </summary>
    private void FillFloorList()
    {
        _fillingFloors = true;

        QuickFloor.Items.Clear();
        QuickFloor.Items.Add(StackedFloor);

        foreach (var floor in _floors) QuickFloor.Items.Add(floor.Name);

        QuickFloor.SelectedIndex = 0;
        QuickFloor.IsEnabled = _floors.Count > 1;

        _fillingFloors = false;
    }

    /// <summary>The first entry, and what the map does when nothing is chosen.</summary>
    private const string StackedFloor = "Stacked";

    /// <summary>True while the list is being rebuilt, so its own churn does not read as a choice.</summary>
    private bool _fillingFloors;

    private void OnFloorPicked()
    {
        if (_fillingFloors) return;

        var at = QuickFloor.SelectedIndex;

        _floorOverride = at <= 0 || at - 1 >= _floors.Count ? null : _floors[at - 1].Layer;
        _overrodeAt = DateTimeOffset.Now;

        Redraw();
    }

    private void StepFloor(int direction)
    {
        if (_floors.Count == 0 || _view is null) return;

        var current = FloorFor(_view);

        // Stacked sits below the bottom floor rather than outside the range, so stepping down off
        // the lowest level lands back on it. The stepper could not reach stacked at all before,
        // which left the drawer offering a way in with no way out.
        var at = current is null ? -1 : _floors.ToList().FindIndex(f => f.Layer == current);
        var next = Math.Clamp(at + direction, -1, _floors.Count - 1);

        _floorOverride = next < 0 ? null : _floors[next].Layer;
        _overrodeAt = DateTimeOffset.Now;

        Redraw();
    }

    /// <summary>
    /// Steps through the ink levels.
    ///
    /// <para>Arrows rather than one button that cycles. A single control reading "full" looks like
    /// a switch that is on, so there was nothing to say three other levels existed — or that the
    /// drawn map lives on one of them.</para>
    /// </summary>
    private void CycleInk(int direction)
    {
        var at = Array.IndexOf(InkLevels, Placement.Ink);
        var next = (at + direction + InkLevels.Length) % InkLevels.Length;

        Place(p => p with { Ink = InkLevels[next] });
        Redraw();
    }

    /// <summary>
    /// How solid the panel is. Kept apart from the map's fade, which inks the drawing — this is
    /// how much of the game the window covers while you are running around.
    /// </summary>
    private void StepWindowOpacity(double by)
    {
        Place(p => p with { WindowOpacity = Math.Clamp(p.WindowOpacity + by, 0.2, 1.0) });

        // Opacity only. This used to go through ApplyBounds, which re-applies the stored position
        // and size — so nudging the fade while the window was being arranged snapped it back to
        // wherever it had last been recorded. Changing how see-through something is has no
        // business moving it.
        ApplyWindowOpacity();
        Draw();
    }

    /// <summary>
    /// Fades the map, and only the map.
    ///
    /// <para>Applied to the window in the corner panel, where the thing being faded is the panel:
    /// its scrim, its lists and its map are one object and dimming them together is the point.</para>
    ///
    /// <para>The centred view has no panel. It is a map over the game with controls floating on
    /// top, and fading the window took the controls down with it — so the setting people actually
    /// want, a barely-there map, was the one that made the controls hardest to work. The map layer
    /// carries it there and the controls stay solid.</para>
    /// </summary>
    private void ApplyWindowOpacity()
    {
        var fade = Math.Clamp(Placement.WindowOpacity, 0.2, 1.0);
        var centred = _settings.Overlay.Mode == RatNavSettings.OverlayMode.Wireframe;

        Opacity = centred ? 1 : fade;
        MapInk.Opacity = centred ? fade : 1;
    }

    private void StepUiScale(double by)
    {
        Remember(_settings.Overlay with
        {
            UiScale = Math.Clamp(EffectiveUiScale + by, 0.7, 3.0),
        });

        ApplyUiScale();
        Draw();
    }

    /// <summary>
    /// Sizes RatNav's own furniture — the controls, the drawers, the headings.
    ///
    /// <para>A layout transform on each piece of chrome rather than on the whole window, because
    /// the map inside it is already sized by the window and the zoom. Scaling the lot would make
    /// "bigger buttons" mean "more zoomed in", which is a different control.</para>
    /// </summary>
    /// <summary>
    /// The scale in use: whatever was chosen, or 1.0.
    ///
    /// <para><b>There is no longer a scale worked out from the screen, and the reasoning that
    /// produced one was backwards.</b> The idea was sound — a 4K panel has four times the pixels
    /// of a 1080p one at much the same physical size, so chrome sized for one is wrong on the
    /// other — but it is a problem Windows has already solved. Every measurement here is in
    /// <b>device-independent pixels</b>, which is display scaling divided out: two monitors of the
    /// same physical size report the same DIP height whatever their pixel density.</para>
    ///
    /// <para>So the multiplier was applied to a number that had already been corrected, and sized
    /// everything a second time. The old code worried about exactly this — "so this is not applied
    /// twice on a laptop set to 150%" — and drew the opposite conclusion from the right
    /// observation. DIPs are the reason no further multiplier is needed, not the reason one is
    /// safe.</para>
    ///
    /// <para>What it looked like: 1.95 on any 1440p screen, which the first tester read
    /// immediately as "scaled to 2.0" because it was. And the author of the old numbers had been
    /// running 1.0 by hand on 4K against a derived 2.6, which is the same evidence from the other
    /// end of the range.</para>
    /// </summary>
    private double EffectiveUiScale => _settings.Overlay.UiScale ?? 1.0;

    private void ApplyUiScale()
    {
        var scale = Math.Clamp(EffectiveUiScale, 0.7, 3.0);

        foreach (var element in new FrameworkElement[]
                 { Readout, StatusRow, ControlStack, QuickBar, LeftDrawers, RightDrawers, ExpandControls })
        {
            element.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        }

        QuickScaleText.Text = $"{scale:0.0}×";
    }

    private void StepFade(double by)
    {
        Remember(_settings.Overlay with { MapOpacity = Math.Clamp(_settings.Overlay.MapOpacity + by, 0.1, 1.0) });
        Draw();
    }

    /// <summary>
    /// Switches between a map that holds still and one that keeps you centred. Persisted, because
    /// which of the two is right is a matter of how you read a map, not of what raid you are in.
    /// </summary>
    /// <summary>
    /// Opens or closes the items list.
    ///
    /// <para>In the centred map view there is nothing to open it <i>into</i> — that view exists to
    /// be a map and nothing else — so the button pops the list out instead. Attaching it there
    /// would put a panel back over the middle of the screen, which is the one thing the view is
    /// for avoiding.</para>
    /// </summary>
    private void ToggleItems()
    {
        if (_settings.Overlay.Mode == RatNavSettings.OverlayMode.Wireframe)
        {
            if (_itemsWindow is null) DetachItemsPanel();
            else _itemsWindow.Close();

            return;
        }

        // Folding the last list while the map is already folded would empty the overlay. The map
        // comes back instead — the list you just closed is closed either way, and what is left is
        // a window with something in it.
        if (Folded && _settings.Overlay.ShowItems && !_settings.Overlay.ShowQuests)
        {
            Remember(_settings.Overlay with { ShowItems = false });
            ToggleMapDrawer();
            return;
        }

        Remember(_settings.Overlay with { ShowItems = !_settings.Overlay.ShowItems });
        ApplyItemsPanel();
        RefreshItems();
    }

    /// <summary>
    /// Moves the list into a window of its own, for a second monitor. The overlay's copy closes
    /// when it does — two lists of the same thing in view at once is clutter, not choice.
    /// </summary>
    private void DetachItemsPanel()
    {
        if (_itemsWindow is not null)
        {
            _itemsWindow.Activate();
            return;
        }

        _itemsWindow = new ItemsWindow { PanelName = "Items" };
        _itemsWindow.Closed += (_, _) =>
        {
            _itemsWindow = null;

            // Back where it came from, folded away. Tearing off was otherwise a one-way trip:
            // there was no route back to the map at all.
            Remember(_settings.Overlay with { ShowItems = false });
            ApplyItemsPanel();
            RefreshItems();
        };

        _itemsWindow.Show();
        _itemsWindow.SetInteractive(!_clickThrough);

        ApplyItemsPanel();
        RefreshItems();
    }

    private void ApplyItemsPanel()
    {
        // Hidden while torn off, so the same list is never in two places at once — and never
        // attached in the centred map view, which exists to be a map. A list popped out from the
        // corner panel stays popped out across the switch, because it is its own window and the
        // mode has nothing to do with it.
        var inline = _settings.Overlay.ShowItems
            && _itemsWindow is null
            && _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box;

        var showQuests = _settings.Overlay.ShowQuests
            && _questWindow is null
            && _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box;

        ItemsPanel.Visibility = inline ? Visibility.Visible : Visibility.Collapsed;
        QuestPanel.Visibility = showQuests ? Visibility.Visible : Visibility.Collapsed;

        // Each drawer picks its own side, and two on the same side share it top and bottom.
        PlaceDrawer(ItemsPanel, _settings.Overlay.ItemsSide);
        PlaceDrawer(QuestPanel, _settings.Overlay.QuestsSide);

        ArrangeSide(LeftDrawers, LeftTopRow, LeftBottomRow, LeftStack);
        ArrangeSide(RightDrawers, RightTopRow, RightBottomRow, RightStack);

        // A column earns its width when a drawer is actually in it.
        ApplyItemsWidth();

        // A divider wherever a panel meets the map, which is both sides at once when they face
        // each other. One handle that followed the items list left the other panel with no edge.
        LeftSplitter.Visibility = Occupied(LeftDrawers) ? Visibility.Visible : Visibility.Collapsed;
        RightSplitter.Visibility = Occupied(RightDrawers) ? Visibility.Visible : Visibility.Collapsed;

        // Moving and tearing off are deliberate acts, offered only while the overlay takes the mouse.
        var editing = inline && !_clickThrough;
        DetachItems.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SwapSide.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        var editingQuests = showQuests && !_clickThrough;
        DetachQuests.Visibility = editingQuests ? Visibility.Visible : Visibility.Collapsed;
        SwapQuestsSide.Visibility = editingQuests ? Visibility.Visible : Visibility.Collapsed;

        ItemsDrawer.Content = _settings.Overlay.ShowItems ? "items ▾" : "items ▸";
        QuestDrawer.Content = _settings.Overlay.ShowQuests ? "quests ▾" : "quests ▸";

        // The handles go with the rest of the furniture when interact mode is off. They are
        // buttons, and a button that cannot be clicked is clutter over the game.
        var handles = _clickThrough ? Visibility.Collapsed : Visibility.Visible;

        ItemsDrawer.Visibility = handles;
        QuestDrawer.Visibility = handles;

        // Nothing to fold in the centred view, which is the map and little else.
        MapDrawer.Visibility = _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box
            ? handles
            : Visibility.Collapsed;
        CollapseItems.Visibility = handles;
        CollapseQuests.Visibility = handles;
    }

    /// <summary>
    /// Fills the items list.
    ///
    /// <para>Fetched only when a list is actually on screen — during a raid with the panel closed
    /// this costs nothing at all.</para>
    /// </summary>
    /// <summary>Reloads the items list because something outside the raid changed it.</summary>
    public void RefreshItemsNow() => Dispatcher.Invoke(RefreshItems);

    /// <summary>
    /// Re-reads the marks for the map on screen and redraws.
    ///
    /// <para>Marks are fetched with the rest of a map's furniture and cached against the map id,
    /// so adding one in the app would otherwise not show until the map changed. Clearing
    /// the cache key is what makes the next draw go and look again.</para>
    /// </summary>
    public void RefreshWaypointsNow() => Dispatcher.Invoke(() =>
    {
        _floorsFor = null;
        Redraw();
    });

    /// <summary>
    /// Fills whichever of the two lists is on screen.
    ///
    /// <para>Each half asks about its own panel. They used to share one early return — <i>if the
    /// items panel is closed, do nothing</i> — with the quest log filled after it, so the log was
    /// only ever refreshed as a side effect of the items list wanting a refresh. With the log open
    /// on its own it stayed empty until you opened the items list, and then stayed filled when you
    /// closed it again, which is the shape of the bug that was reported.</para>
    ///
    /// <para>The items half is the one worth guarding: it is an HTTP call. The quest log is built
    /// from the raid view already in hand and costs nothing to rebuild.</para>
    /// </summary>
    private void RefreshItems()
    {
        var wantsItems = _settings.Overlay.ShowItems || _itemsWindow is not null;
        var wantsQuests = _settings.Overlay.ShowQuests || _questWindow is not null;

        if (!wantsItems && !wantsQuests) return;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (wantsItems)
            {
                var sections = await LoadSectionsAsync();

                ItemsList.ItemsSource = sections;
                _itemsWindow?.Show(sections);
            }

            if (wantsQuests)
            {
                var quests = QuestSections();

                QuestList.ItemsSource = quests;
                QuestEmpty.Visibility = quests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                _questWindow?.Show(quests);
            }

            MatchPopOuts();
        });
    }

    private async Task<IReadOnlyList<ItemSection>> LoadSectionsAsync()
    {
        try
        {
            var url = $"{ServiceHost.Root}/api/items/panel";
            var panel = await _http.GetFromJsonAsync<ItemPanel>(url) ?? new ItemPanel();

            // The heading carries what is being counted. "Quests & hideout" alone does not say
            // whether it means tonight's upgrades or the next four, and the number changes a lot.
            var scope = panel.LookAhead <= 1 ? "buildable now" : $"+{panel.LookAhead - 1} ahead";

            // And the same for what is being looked past. The dial moves both — how far into the
            // hideout build order, and how far along the quest chain — so both headings say where
            // it currently sits rather than leaving the same list meaning different things on
            // different days.
            var ahead = panel.LookAhead <= 1
                ? "gated upgrades, quests you could take"
                : $"gated upgrades, quests {panel.LookAhead} deep";

            return
            [
                // Yours first. The watchlist is the short list you chose by hand; quests and the
                // hideout are worked out and far longer, and putting them above meant scrolling
                // past forty derived rows to reach three deliberate ones.
                Section("WATCHLIST", panel.Watchlist, label: "WATCHLIST · your targets"),

                // The things you are tracking yourself, one section each, directly under it.
                // They are the
                // same kind of thing — a decision you made rather than one derived from your
                // progress — and one section per collection means the one you are working on can
                // stay open while the rest fold away.
                //
                // Titled by id rather than by name so renaming a collection does not lose which
                // sections you had folded.
                .. panel.Goalsets.Select(group => Section(
                    $"GOAL:{group.Id}",
                    group.Rows,
                    label: $"{group.Name.ToUpperInvariant()} · {group.Rows.Count} left")),

                Section("QUESTS & HIDEOUT", panel.Now, label: $"QUESTS & HIDEOUT · {scope}"),

                // Gated upgrades and quests you could accept but have not. Folded by default: it
                // is the longest of the three and the least actionable, and an overlay that opens
                // with sixty rows on it is one people turn off.
                Section(
                    "LATER",
                    panel.Later,
                    expandedByDefault: false,
                    label: panel.LaterHidden > 0
                        ? $"LATER · {ahead} (+{panel.LaterHidden} more)"
                        : $"LATER · {ahead}"),
            ];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    /// <param name="title">
    /// Stable name, used to remember whether the section is folded. Kept separate from what is
    /// shown, because the heading carries a count that changes and folding must not reset with it.
    /// </param>
    /// <param name="label">What to display, when that differs from the title.</param>
    private ItemSection Section(
        string title, List<PanelRow> rows, bool expandedByDefault = true, string? label = null) =>
        Section(title, [.. rows.Select(ItemRow.From)], expandedByDefault, label);

    private ItemSection Section(
        string title, IReadOnlyList<ItemRow> rows, bool expandedByDefault = true, string? label = null)
    {
        var section = new ItemSection(
            label ?? title,
            rows,
            _settings.Overlay.CollapsedSections.Contains(title) ? false : expandedByDefault);

        // Folding is a preference, not a per-refresh state — it has to survive the list reloading
        // every time a fix comes in.
        section.Toggled += (_, _) =>
        {
            var collapsed = _settings.Overlay.CollapsedSections.ToList();

            if (section.Expanded) collapsed.Remove(title);
            else if (!collapsed.Contains(title)) collapsed.Add(title);

            Remember(_settings.Overlay with { CollapsedSections = collapsed });
        };

        return section;
    }

    /// <summary>
    /// Widens or narrows one side of the map.
    ///
    /// <para>Which way a drag grows a side depends on which side it is: dragging left widens a
    /// panel on the right, and narrows one on the left. Both edges move independently, so pulling
    /// one in does not push the other out.</para>
    /// </summary>
    private void OnSideResize(bool left, DragDeltaEventArgs e)
    {
        var delta = left ? e.HorizontalChange : -e.HorizontalChange;
        var current = SideWidth(left);

        // Both sides plus something for the map. Without the second term, dragging one edge past
        // the middle of a narrow overlay leaves the other side no room and the map none at all.
        //
        // The room kept for the map is a share of the overlay rather than a fixed 140, and the
        // narrowest a panel may be is a share too. Fixed numbers were sized against a large
        // overlay and do not shrink with one: at 477 wide, 140 for the map plus 120 for the other
        // side plus a floor of 90 left this divider about thirty pixels of travel, sitting at the
        // top of it — which reads exactly like a control that has stopped working, and is what
        // tuning the overlay down for 1080p produced.
        var floor = Math.Max(48, ActualWidth * 0.12);
        var mapRoom = Math.Max(60, ActualWidth * 0.2);

        // Max, not Clamp, and in this order: Clamp throws outright when its lower bound is above
        // its upper one, which a narrow enough overlay would otherwise arrange.
        var ceiling = Math.Max(floor, ActualWidth - SideWidth(!left) - mapRoom);
        var width = Math.Clamp(current + delta, floor, ceiling);

        // Saved as it moves rather than at the end. A drag that is only committed on release loses
        // everything if the mouse leaves the window, which over a game it frequently does.
        Remember(left
            ? _settings.Overlay with { LeftWidth = width }
            : _settings.Overlay with { RightWidth = width });

        ApplyItemsWidth();
    }

    /// <summary>
    /// Moves the boundary between two panels sharing a side.
    ///
    /// <para>Kept as a fraction rather than as two heights: the overlay is resized often, and a
    /// pair of pixel heights would either overflow it or leave a gap the moment it was. The
    /// clamp is what stops a drag past the end from collapsing one of them to nothing, which
    /// would leave a panel that is on screen, is not foldable, and cannot be got back.</para>
    /// </summary>
    private void OnStackResize(FrameworkElement side, DragDeltaEventArgs e)
    {
        var height = side.ActualHeight;
        if (height <= 0) return;

        // Dragging down with nothing hidden does nothing on screen, so it does not move the
        // setting either. Left to accumulate, the ceiling would climb somewhere far above the
        // content and the first inch of the drag back up would have no effect — a divider that
        // ignores you for a while is one you stop trusting.
        if (e.VerticalChange > 0 && QuestScroll.ScrollableHeight <= 0) return;

        var share = Math.Clamp(_settings.Overlay.QuestShare + e.VerticalChange / height, 0.15, 0.85);

        Remember(_settings.Overlay with { QuestShare = share });

        ArrangeSide(LeftDrawers, LeftTopRow, LeftBottomRow, LeftStack);
        ArrangeSide(RightDrawers, RightTopRow, RightBottomRow, RightStack);
    }

    /// <summary>What a side is currently set to, falling back to the width both sides start at.</summary>
    private double SideWidth(bool left) =>
        (left ? _settings.Overlay.LeftWidth : _settings.Overlay.RightWidth) ?? _settings.Overlay.ItemsWidth;

    /// <summary>
    /// Gives the items column its width, or takes it away.
    ///
    /// <para>The column kept its width whether or not anything was in it, so folding the list away
    /// left a stripe of empty space down one side and the map sitting off-centre in the window.
    /// A collapsed drawer should cost nothing.</para>
    /// </summary>
    private void ApplyItemsWidth()
    {
        // A column earns its width when a drawer is actually in it and showing. Reserved
        // regardless, it left a stripe of nothing down one side with the map pushed off-centre.
        var none = new GridLength(0);

        LeftSlot.Width = Occupied(LeftDrawers) ? new GridLength(SideWidth(left: true)) : none;
        RightSlot.Width = Occupied(RightDrawers) ? new GridLength(SideWidth(left: false)) : none;
    }

    /// <summary>
    /// Whether a side has a panel on it.
    ///
    /// <para>The two panels rather than any visible child, because the side also holds the
    /// divider that sits between them — and a divider counting as occupancy would keep a column
    /// open with nothing in it.</para>
    /// </summary>
    private static bool Occupied(Panel side) =>
        side.Children.OfType<Border>().Any(c => c.Visibility == Visibility.Visible);

    /// <summary>
    /// Lays out one side: one panel takes the whole height, two share it, and the divider between
    /// them only exists in the second case.
    ///
    /// <para>The quest log goes on top wherever the two meet. It is the shorter of the two and the
    /// one you read rather than scan, so a long list above it buries it — and that has to hold
    /// however the two were swapped around to end up together.</para>
    /// </summary>
    private void ArrangeSide(Grid side, RowDefinition top, RowDefinition bottom, UIElement divider)
    {
        var panels = side.Children.OfType<Border>()
            .Where(c => c.Visibility == Visibility.Visible)
            .ToList();

        divider.Visibility = panels.Count == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (panels.Count < 2)
        {
            // One panel, or none. Spanning all three rows means it does not have to care that the
            // rows exist, and leaves nothing behind when the other one folds away.
            foreach (var panel in side.Children.OfType<Border>())
            {
                Grid.SetRow(panel, 0);
                Grid.SetRowSpan(panel, 3);
            }

            top.Height = new GridLength(1, GridUnitType.Star);
            bottom.Height = new GridLength(0);

            ApplyQuestCeiling();
            return;
        }

        foreach (var panel in panels)
        {
            var quests = ReferenceEquals(panel, QuestPanel);

            Grid.SetRowSpan(panel, 1);
            Grid.SetRow(panel, quests ? 0 : 2);
        }

        // The divider is a ceiling, not a split.
        //
        // Two star rows gave the quest log its share of the height whether or not it had anything
        // to put there, so three quests sat in a section sized for ten and the items list below
        // was squeezed for nothing. Auto over star means it takes what it needs; the ceiling is
        // what stops a long plan from taking the whole side, and hitting it is what turns the
        // quest log into something that scrolls.
        top.Height = GridLength.Auto;
        bottom.Height = new GridLength(1, GridUnitType.Star);

        ApplyQuestCeiling();
    }

    /// <summary>
    /// Caps the quest log at the fraction the divider was dragged to — and only when it is sharing
    /// a side.
    ///
    /// <para>Read from whichever side the panel is actually in rather than from both in turn, or
    /// the second one to be laid out wins and the ceiling comes from a column the quest log is not
    /// in. Alone on a side there is nothing to share with, so the cap comes off entirely: it
    /// should use the height it has.</para>
    /// </summary>
    private void ApplyQuestCeiling()
    {
        if (QuestPanel.Parent is not FrameworkElement side
            || !ItemsPanel.IsVisible
            || !ReferenceEquals(ItemsPanel.Parent, side))
        {
            QuestPanel.MaxHeight = double.PositiveInfinity;
            return;
        }

        var available = side.ActualHeight;
        if (available <= 0) return;

        // A floor under it, because a ceiling of nothing is a panel that is on screen and cannot
        // show a single row of what it is for.
        QuestPanel.MaxHeight =
            Math.Max(48, available * Math.Clamp(_settings.Overlay.QuestShare, 0.15, 0.85));
    }

    /// <summary>
    /// Moves a drawer into the column it belongs in.
    ///
    /// <para>Reparented rather than built once per side: one panel that moves cannot drift from
    /// itself, where two that are shown and hidden in turn eventually do.</para>
    /// </summary>
    private void PlaceDrawer(FrameworkElement panel, string side)
    {
        var target = side == "left" ? LeftDrawers : RightDrawers;

        panel.Margin = side == "left"
            ? new Thickness(0, 0, 6, 6)
            : new Thickness(6, 0, 0, 6);

        // Grid is a Panel, so one case covers both the starting grid and the other side.
        if (ReferenceEquals(panel.Parent, target)) return;

        if (panel.Parent is Panel current) current.Children.Remove(panel);
        target.Children.Add(panel);

        // Which of the two goes on top is not decided here any more — it is a row, and
        // ArrangeSide sets it once both sides are known.
    }

    /// <summary>Opens or folds the quest log.</summary>
    private void ToggleQuests()
    {
        if (_settings.Overlay.Mode == RatNavSettings.OverlayMode.Wireframe)
        {
            if (_questWindow is null) DetachQuestPanel();
            else _questWindow.Close();

            return;
        }

        // As with the items list: the overlay never folds down to nothing.
        if (Folded && _settings.Overlay.ShowQuests && !_settings.Overlay.ShowItems)
        {
            Remember(_settings.Overlay with { ShowQuests = false });
            ToggleMapDrawer();
            return;
        }

        Remember(_settings.Overlay with { ShowQuests = !_settings.Overlay.ShowQuests });
        ApplyItemsPanel();
        RefreshItems();
    }

    private void SwapQuestsToOtherSide()
    {
        Remember(_settings.Overlay with
        {
            QuestsSide = _settings.Overlay.QuestsSide == "left" ? "right" : "left",
        });

        ApplyItemsPanel();
    }

    private void DetachQuestPanel()
    {
        if (_questWindow is not null)
        {
            _questWindow.Activate();
            return;
        }

        _questWindow = new ItemsWindow { Title = "RatNav — Quests", PanelName = "Quest Log" };
        _questWindow.Closed += (_, _) =>
        {
            _questWindow = null;

            Remember(_settings.Overlay with { ShowQuests = false });
            ApplyItemsPanel();
            RefreshItems();
        };

        _questWindow.Show();
        _questWindow.SetInteractive(!_clickThrough);

        ApplyItemsPanel();
        RefreshItems();
    }

    /// <summary>
    /// The plan's stops as a log: the number on the map, what it is for, and what it wants.
    ///
    /// <para>Built from the raid view rather than fetched. Everything needed is already there, and
    /// a second source for the same thing is a second thing to keep in step.</para>
    /// </summary>
    private IReadOnlyList<ItemSection> QuestSections()
    {
        var view = _view;
        if (view is null || view.Stops.Count == 0) return [];

        var order = 0;
        var rows = new List<ItemRow>();

        foreach (var stop in view.Stops)
        {
            if (!stop.Done) order++;

            var opened = stop;

            rows.Add(new ItemRow
            {
                Count = stop.Done ? "\u2713" : order.ToString(),
                Name = stop.TaskName,
                Reason = stop.Description is { Length: > 0 }
                    ? $"{stop.Place ?? stop.TaskName} — {stop.Description}"
                    : stop.Place ?? stop.TaskName,
                Colour = (Brush)FindResource(stop.Done ? "Muted" : "Route"),

                // Clicking a stop opens its quest, the same panel a waypoint on the map
                // opens. On one screen this is the only way to reach the wiki's pictures of
                // the place without leaving the game, and those pictures are what turn
                // "walk to this pin" into "find this door".
                Activate = opened.TaskId is { Length: > 0 }
                    ? new RelayCommand(() => ShowQuestBrief(opened))
                    : null,
            });
        }

        return [Section("QUEST LOG", rows, label: $"QUEST LOG · {view.Stops.Count}")];
    }

    /// <summary>Moves the list to the other side of the map. The overlay can sit against either edge.</summary>
    private void SwapItemsSide()
    {
        Remember(_settings.Overlay with
        {
            ItemsSide = _settings.Overlay.ItemsSide == "left" ? "right" : "left",
        });

        ApplyItemsPanel();
    }

    /// <summary>
    /// Reads whatever the mouse is hovering and says what it is for.
    ///
    /// <para>The capture is of the desktop, the same pixels a screenshot tool sees, and the OCR is
    /// Windows' own. Nothing is read from the game, injected into it, or asked of it â€” this is
    /// looking at the screen, which is what the player is already doing.</para>
    ///
    /// <para>Pressing the key again puts the card away, so one key both asks and dismisses.</para>
    /// </summary>
    private void IdentifyUnderCursor()
    {
        if (IdentifyCardPanelVisible)
        {
            HideIdentifyCard();
            return;
        }

        // Reading the screen needs Windows 10 2004 or later. RatNav itself runs further back than
        // that, so this is checked rather than assumed â€” the rest of the app is unaffected.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || !ScreenTextReader.Available)
        {
            ShowIdentifyCard(
                "Reading is unavailable",
                "Needs Windows 10 version 2004 or later, with an OCR language installed.",
                []);
            return;
        }

        var (x, y) = ScreenTextReader.CursorPosition();

        // Shown before the work starts. OCR takes a moment, and a key that appears to do nothing
        // gets pressed again.
        ShowIdentifyCard("Readingâ€¦", "", []);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var lines = await ScreenTextReader.ReadAroundAsync(x, y);

            if (lines.Count == 0)
            {
                ShowIdentifyCard("Nothing legible there", "Hover the item so its tooltip is showing.", []);
                return;
            }

            var match = await IdentifyAsync(lines);

            if (match is null)
            {
                ShowIdentifyCard(
                    "No item matched",
                    $"Read: {string.Join(" Â· ", lines.Take(3))}",
                    []);
                return;
            }

            // A hedge rather than silent confidence. OCR is wrong often enough that presenting a
            // guess as fact would eventually cost someone a quest item.
            var hedge = match.Confidence is { } confidence && confidence < 0.85
                ? $"best guess Â· {confidence * 100:F0}% sure"
                : null;

            ShowIdentifyCard(match.Item.Name, hedge, IdentifyCard.Reasons(match));
        });
    }

    private async Task<ItemDetail?> IdentifyAsync(IReadOnlyList<string> lines)
    {
        try
        {
            var url = $"{ServiceHost.Root}/api/items/identify";
            var response = await _http.PostAsJsonAsync(url, new { lines });

            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<IdentifyResponse>();
            return result?.Matches?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private bool IdentifyCardPanelVisible => IdentifyCardPanel.Visibility == Visibility.Visible;

    /// <summary>
    /// Takes the identify card away again on its own.
    ///
    /// <para>You read it in a moment and then it is sitting over the game. Dismissing it by hand is
    /// a keypress nobody wants to spend mid-raid, so it goes by itself.</para>
    /// </summary>
    private readonly DispatcherTimer _identifyCardTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5),
    };

    private void ShowIdentifyCard(string name, string? hedge, IReadOnlyList<IdentifyReason> reasons)
    {
        IdentifyName.Text = name;
        IdentifyHedge.Text = hedge ?? "";
        IdentifyHedge.Visibility = string.IsNullOrEmpty(hedge) ? Visibility.Collapsed : Visibility.Visible;
        IdentifyReasons.ItemsSource = reasons;

        IdentifyCardPanel.Visibility = Visibility.Visible;
        Show();

        // Restarted rather than left running, so reading item after item does not make one of them
        // vanish early — and so the "Reading…" card does not take the answer with it.
        _identifyCardTimer.Stop();
        _identifyCardTimer.Start();
    }

    private void HideIdentifyCard()
    {
        _identifyCardTimer.Stop();
        IdentifyCardPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>What the identify endpoint returns.</summary>
    private sealed record IdentifyResponse(List<ItemDetail>? Matches, List<string>? ReadText);

    private void StepMarker(double by)
    {
        Remember(_settings.Overlay with
        {
            MarkerScale = Math.Clamp(_settings.Overlay.MarkerScale + by, 1.0, 8.0),
        });

        Draw();
    }

    private void StepText(double by)
    {
        Remember(_settings.Overlay with
        {
            TextScale = Math.Clamp(_settings.Overlay.TextScale + by, 1.0, 6.0),
        });

        Draw();
    }

    private void StepPlaceNames(double by)
    {
        Remember(_settings.Overlay with
        {
            PlaceNameScale = Math.Clamp(_settings.Overlay.PlaceNameScale + by, 1.0, 6.0),
        });

        Draw();
    }

    /// <summary>
    /// Folds the control stack away, leaving one button to bring it back.
    ///
    /// <para>Remembered, because whether you want the controls visible is a preference about how
    /// you work rather than something to re-decide every time interact mode is entered.</para>
    /// </summary>
    private void ShowControls(bool visible)
    {
        Remember(_settings.Overlay with { ShowControls = visible });
        ApplyControlStack();
    }

    /// <summary>One line of the key-bind reminder strip.</summary>
    private sealed record HotkeyHint(string Key, string Does);

    /// <summary>
    /// Fills the reminder strip from the service.
    ///
    /// <para>From the same endpoint the app's footer reads, rather than a list written out
    /// twice — two copies of "which key does what" is exactly the pair that drifts.</para>
    /// </summary>
    private async Task LoadHotkeyHintsAsync()
    {
        try
        {
            var hints = await _http.GetFromJsonAsync<List<HotkeyHint>>(
                $"{ServiceHost.Root}/api/hotkeys/hints");

            if (hints is { Count: > 0 }) HotkeyHints.ItemsSource = hints;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A missing reminder strip is not worth a word about.
        }
    }

    private void ApplyControlStack()
    {
        // Only ever while the overlay takes the mouse. Controls you cannot click have no business
        // over the game.
        // Never with the map folded away: every control in the stack acts on a map that is not
        // on screen, and the gear that opens it would be a button that does nothing visible.
        var editing = !_clickThrough && !Folded;
        var open = _settings.Overlay.ShowControls;

        ControlStack.Visibility = editing && open ? Visibility.Visible : Visibility.Collapsed;

        // Filled here as well as at start-up. The first attempt runs while the window is being
        // built, which can be before Kestrel is listening — and a strip that lost that race stayed
        // empty for the rest of the session, which looks exactly like the feature not existing.
        if (HotkeyHints.ItemsSource is null) _ = LoadHotkeyHintsAsync();

        // The gear stays wherever interact mode is on, open or closed. Hiding it when the stack
        // opened made the row reflow and the quests and items buttons slide across underneath the
        // cursor — and left no way back except the collapse arrow, which is somewhere else.
        var centred = _settings.Overlay.Mode == RatNavSettings.OverlayMode.Wireframe;

        // The centred view is centred, and its size comes from the coverage dial. Dragging it or
        // pulling its corner would move it until the next thing that laid it out put it back,
        // which is worse than not offering either.
        DragHandle.Visibility = centred ? Visibility.Collapsed : Visibility.Visible;
        ResizeGrip.Visibility = centred ? Visibility.Collapsed : Visibility.Visible;

        HudControls.Visibility = centred ? Visibility.Visible : Visibility.Collapsed;
        FullScreenControls.Visibility = Hud ? Visibility.Visible : Visibility.Collapsed;

        // One gear each, because they live in different places for good reasons. The corner view's
        // is in the footer with the drawer handles; the centred view has no footer, so its own
        // floats over the map.
        ExpandControls.Visibility = editing && !centred ? Visibility.Visible : Visibility.Collapsed;
        CentredControls.Visibility = editing && centred ? Visibility.Visible : Visibility.Collapsed;

        var says = open ? "Hide the map controls" : "Show the map controls";
        ExpandControls.ToolTip = says;
        CentredControls.ToolTip = says;

        // A panel over the map rather than a column down the side of it. Stretched, it runs the
        // whole height of a screen-filling view, which is most of the game covered by controls
        // nobody is reading.
        ControlStack.VerticalAlignment = centred
            ? System.Windows.VerticalAlignment.Top
            : System.Windows.VerticalAlignment.Stretch;

        ControlStack.MaxHeight = centred
            ? Math.Max(200, ActualHeight * 0.75)
            : double.PositiveInfinity;

        ClearNeighbours();

        // The bar goes with the rest of the furniture.
        //
        // With interact mode off there is nothing it can do — it cannot be dragged, and the wheel
        // is the game's — so it was a mark sitting over a raid for no reason. Hidden rather than
        // disabled, because Hidden still scrolls: the wheel keeps working the instant the mouse
        // comes back, and the bar reappears with it.
        // Read from the mouse alone, not from `editing`, which also means "the map is showing".
        // With the map folded away the lists *are* the overlay, and they still scroll.
        var bars = _clickThrough
            ? ScrollBarVisibility.Hidden
            : ScrollBarVisibility.Auto;

        QuestScroll.VerticalScrollBarVisibility = bars;
        ItemsScroll.VerticalScrollBarVisibility = bars;
        ControlScroll.VerticalScrollBarVisibility = bars;
        QuestBriefScroll.VerticalScrollBarVisibility = bars;

        // Torn-off panels follow the same mode: click-through and bare during a raid, taking
        // the mouse and wearing their furniture while the overlay is being arranged.
        _itemsWindow?.SetInteractive(!_clickThrough);
        _questWindow?.SetInteractive(!_clickThrough);

        // The grab bar runs the width of the window across the row the drawer handles live in.
        // Interact mode pushes the content clear by a full button height rather than a hair — a
        // few pixels still reads as crowded, and a handle you can see but not press is worse than
        // no handle at all.
        Frame.Padding = editing ? new Thickness(0, 28, 0, 0) : new Thickness(0);
    }

    private void StepPlayer(double by)
    {
        Remember(_settings.Overlay with
        {
            PlayerScale = Math.Clamp(_settings.Overlay.PlayerScale + by, 1.0, 8.0),
        });

        Draw();
    }

    private void StepShrink(double by)
    {
        Remember(_settings.Overlay with
        {
            ScaleWithZoom = Math.Clamp(_settings.Overlay.ScaleWithZoom + by, 0, 1),
        });

        Draw();
    }

    private void ToggleGhost()
    {
        Remember(_settings.Overlay with { GhostOtherFloors = !_settings.Overlay.GhostOtherFloors });
        Draw();
    }

    private void TogglePlaces()
    {
        Remember(_settings.Overlay with { ShowPlaceNames = !_settings.Overlay.ShowPlaceNames });
        Draw();
    }

    private void ToggleHalo()
    {
        Remember(_settings.Overlay with { Halo = !_settings.Overlay.Halo });
        Draw();
    }

    private void StepWeight(double by)
    {
        Remember(_settings.Overlay with
        {
            LineWeight = Math.Clamp(_settings.Overlay.LineWeight + by, 0.5, 4.0),
        });

        Draw();
    }

    private void CycleExtracts()
    {
        var at = Array.IndexOf(ExtractModes, _settings.Overlay.Extracts);
        Remember(_settings.Overlay with { Extracts = ExtractModes[(at + 1 + ExtractModes.Length) % ExtractModes.Length] });
        Draw();
    }

    private void CycleQuests()
    {
        var at = Array.IndexOf(QuestModes, _settings.Overlay.Quests);
        Remember(_settings.Overlay with { Quests = QuestModes[(at + 1 + QuestModes.Length) % QuestModes.Length] });
        Draw();
    }

    private void SetZoom(double zoom)
    {
        Place(p => p with { Zoom = Math.Clamp(zoom, 1, 8) });
        Draw();
    }

    /// <summary>Re-fetches the map when something about how it is drawn has changed.</summary>
    private void Redraw() => Dispatcher.InvokeAsync(async () =>
    {
        if (_view is not null) await EnsureMapAsync(_view);
        Draw();
    });

    private void Draw()
    {
        var view = _view;

        MapInk.Children.Clear();
        MapCanvas.Children.Clear();

        ApplyEdgeFade();

        // Cleared with the scene they describe. A hover target left pointing at a marker that is
        // no longer there is worse than none — it names the wrong thing rather than nothing.
        _hoverTargets.Clear();
        HoverCard.Visibility = Visibility.Collapsed;

        // Drawn whenever there is a map, not only in a raid. The overlay used to go blank while a
        // raid was still loading and while looking a map over beforehand — the two times you most
        // want to see one.
        if (view is null || !view.HasMap)
        {
            ProgressText.Text = "";
            FixAgeText.Text = "";
            UpdateControls(null);
            return;
        }

        // Only stops in *this* plan count. Completed objectives outlive the plan they were
        // finished under, so counting all of them against the current plan's stops produced
        // "1/0 done" — a figure that cannot be read whichever way round you take it.
        var done = view.Stops.Count(s => view.CompletedObjectiveIds.Contains(s.ObjectiveId));

        ProgressText.Text = view.Stops.Count > 0 ? $"{done}/{view.Stops.Count} done" : "";

        // How old the marker is, in words rather than jargon. It used to read "FIX 58S AGO" — a
        // fix is what RatNav calls a position reading internally and not something anyone else
        // would call it, and the number is only there to say how much to trust the marker.
        //
        // Nothing animates between readings, so saying when the last one was is the honest way to
        // convey that rather than letting a stale marker imply it is current.
        FixAgeText.Text = view.FixedAt is { } at
            ? $"position {Age(DateTimeOffset.Now - at)}"
            : view.InRaid
                ? $"tap {_settings.ScreenshotKey.ToLowerInvariant()} for position"

                // Rather than telling someone to press a key that will do nothing: outside a raid
                // the game writes no position for RatNav to read.
                : "not in raid";

        // Said loudly, and only while it is true: in a raid, with a map, and no position taken
        // yet. The footer carries the same words in small text; this is the one moment they are
        // worth the middle of the map.
        var lost = view.InRaid && view.X is null;

        NoFixPrompt.Visibility = lost ? Visibility.Visible : Visibility.Collapsed;

        // Named when there is a name, and plain words when there is not. Naming the key you bound
        // is the better answer — it is the one you will actually press — but an empty setting must
        // not produce "press " with nothing after it.
        if (lost)
        {
            NoFixKey.Text = _settings.ScreenshotKey is { Length: > 0 } key
                ? $"press {key}"
                : "take a screenshot";
        }

        DrawMap(view);
        DrawRoute(view);
        UpdateControls(view);
    }

    /// <summary>
    /// Makes the controls report the state they are editing. They live behind interact mode, so
    /// this costs nothing during a raid — but a control that shows a stale value is worse than none.
    /// </summary>
    private void UpdateControls(RaidView? view)
    {
        var floor = view is null ? null : FloorFor(view);
        var named = _floors.FirstOrDefault(f => f.Layer == floor);

        FloorText.Text = named?.Name ?? floor ?? StackedFloor;

        // Say when you are looking somewhere other than where you stand, because forgetting that
        // is how you plan a route across a floor you are not on.
        FloorText.Foreground = (Brush)FindResource(_floorOverride is null ? "Ink" : "Route");

        var at = _floors.ToList().FindIndex(f => f.Layer == floor);
        FloorUp.IsEnabled = _floors.Count > 0 && at < _floors.Count - 1;
        FloorDown.IsEnabled = _floors.Count > 0 && _floorOverride is not null;

        // Two controls onto one value, so the drawer's stepper and the quick panel's dropdown
        // cannot disagree about which floor is being looked at.
        var wanted = _floorOverride is null ? 0 : at + 1;

        if (QuickFloor.SelectedIndex != wanted && wanted < QuickFloor.Items.Count)
        {
            _fillingFloors = true;
            QuickFloor.SelectedIndex = wanted;
            _fillingFloors = false;
        }

        InkButton.Text = Titled(Placement.Ink);
        FadeText.Text = $"{_settings.Overlay.MapOpacity * 100:F0}%";
        ZoomReset.Content = $"{Placement.Zoom:0.0}×";
        FollowButton.Content = Following ? "follows you" : "still";

        // The quick bar carries the three reached for constantly, so they do not need the stack
        // opened to get at them.
        QuickFadeText.Text = $"{Placement.WindowOpacity * 100:F0}%";
        QuickZoomText.Text = $"{Placement.Zoom:0.0}×";
        QuickFollow.Content = Following ? "follows" : "still";

        // Says which way it will go, not which way it is: a control labelled with the state you
        // are already in leaves you guessing what pressing it does.
        QuickOffered.Content = _settings.Overlay.OnlyOfferedExtracts ? "all exits" : "my exits";
        QuickOffered.Visibility = _settings.Overlay.OfferedExtracts.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Only worth offering when it would do something. A crosshair that is always lit teaches
        // people to ignore it.
        RecentreButton.Visibility = Panned || !Following ? Visibility.Visible : Visibility.Collapsed;
        // Title case, matching the app's Maps page rather than the raw setting value.
        ExtractButton.Content = Titled(_settings.Overlay.Extracts);
        QuestVisibility.Content = Titled(_settings.Overlay.Quests);

        // Only worth a button once there is something to undo. Until the list has been read this
        // does nothing, and a control that does nothing is one more thing to wonder about.
        var offered = _settings.Overlay.OfferedExtracts.Count;

        OfferedButton.Visibility = offered > 0 ? Visibility.Visible : Visibility.Collapsed;
        OfferedButton.Content = $"showing {offered} · all";
        MarkerText.Text = $"{_settings.Overlay.MarkerScale:0.0}×";
        TextScaleText.Text = $"{_settings.Overlay.TextScale:0.0}×";
        PlaceNameText.Text = $"{_settings.Overlay.PlaceNameScale:0.0}×";

        TurnButton.Content = _settings.Overlay.TurnWithYou ? "turns with you" : "north up";

        CoverageText.Text = $"{Coverage * 100:F0}%";
        FadeEdgeText.Text = $"{_settings.Overlay.EdgeFade * 100:F0}%";
        GlowText.Text = $"{_settings.Overlay.Glow:0.0}×";
        ShrinkText.Text = $"{_settings.Overlay.ScaleWithZoom:0.00}";
        YouText.Text = $"{_settings.Overlay.PlayerScale:0.0}×";
        HaloButton.Content = _settings.Overlay.Halo ? "halo on" : "halo off";
        GhostButton.Content = _settings.Overlay.GhostOtherFloors ? "ghost on" : "ghost off";
        PlacesButton.Content = _settings.Overlay.ShowPlaceNames ? "names on" : "names off";
        WeightText.Text = $"{_settings.Overlay.LineWeight:0.00}×";
    }

    /// <summary>
    /// Draws the map itself: terrain as a whisper, structure and roads carrying it. Whether zoom
    /// holds the map still or keeps you centred is <see cref="RatNavSettings.OverlayBounds.FollowPlayer"/>.
    /// </summary>
    /// <summary>
    /// Dissolves the map into the game toward the edges of the full-screen HUD.
    ///
    /// <para>A radial <see cref="OpacityMask"/> over the map layer, which costs nothing per shape
    /// — it is one brush over the finished layer rather than an effect on each of several hundred
    /// paths. Relative units, so it stays an ellipse matching the screen and reaches every edge at
    /// the same point rather than fading the sides sooner than the top.</para>
    ///
    /// <para>Only in the HUD. A windowed centred map has a border of its own, and something
    /// fading out just inside a visible border reads as a rendering fault.</para>
    /// </summary>
    private void ApplyEdgeFade()
    {
        if (!Hud)
        {
            MapInk.OpacityMask = null;
            return;
        }

        var starts = Math.Clamp(_settings.Overlay.EdgeFade, 0.2, 1.0);

        var mask = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Colors.White, starts),
                new GradientStop(Colors.Transparent, 1),
            },
        };

        mask.Freeze();
        MapInk.OpacityMask = mask;
    }

    private void DrawMap(RaidView view)
    {
        if (_mapShapes.Count == 0) return;

        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var opacity = _settings.Overlay.MapOpacity;
        var ink = Placement.Ink;
        var fit = FitScale(width, height);

        var transform = MapTransform(view, width, height);

        if (_settings.Overlay.GhostOtherFloors)
        {
            // Floors conflict only where they overlap. A stairwell drawn directly above a
            // corridor is ambiguous; a warehouse at the other end of the map is not — and fading
            // that too turned the whole map to frosted glass for the sake of a few square metres.
            //
            // So anything with nothing above or below it is drawn in full, and only the parts that
            // actually clash are held back.
            Ghost(_ghostShapes, transform, fit, opacity * 0.40, "Muted");
            Ghost(_aboveShapes, transform, fit, opacity * 0.75, "Terrain");
        }

        foreach (var shape in _mapShapes)
        {
            // Drop shadows are real in the source and noise on a translucent overlay.
            if (shape.Role == MapShapeRole.Decoration) continue;
            if (!ShownAt(ink, shape.Role)) continue;

            // The map as it was drawn: its own colours, its own line weights. Everything below
            // recolours by role, which is right over a dark scene and wrong when you want to read
            // the map as a place rather than as a diagram.
            if (ink == "graphical" && MapPalette.For(_palette, shape.Classes) is { } styled)
            {
                // The fill's own opacity, folded into the layer's. Streets marks its sniper zones
                // translucent red — dropping that turned three warnings into three solid blocks
                // covering the map underneath them.
                var paint = styled.Fill;

                if (styled.FillOpacity is { } alpha && paint is not null)
                {
                    paint = paint.Clone();
                    paint.Opacity = alpha;
                    paint.Freeze();
                }

                var drawn = new Path
                {
                    Data = shape.Geometry,
                    RenderTransform = transform,
                    Fill = paint,
                    Stroke = styled.Stroke,
                    StrokeThickness = (styled.StrokeWidth > 0 ? styled.StrokeWidth : 1)
                        * _settings.Overlay.LineWeight,
                    Opacity = opacity,
                    IsHitTestVisible = false,
                };

                // A dashed outline is how a hazard says "keep out" rather than "here is a wall".
                if (styled.Dash is { Count: > 0 } dash)
                    drawn.StrokeDashArray = [.. dash];

                MapInk.Children.Add(drawn);

                continue;
            }

            var (stroke, fill, thickness) = shape.Role switch
            {
                MapShapeRole.Terrain => ((Brush?)null, (Brush?)FindResource("Terrain"), 0.0),
                MapShapeRole.Structure => ((Brush?)FindResource("Accent"), (Brush?)FindResource("Accent"), 1.0),
                MapShapeRole.Boundary => ((Brush?)FindResource("Accent"), null, 0.8),
                MapShapeRole.Route => ((Brush?)FindResource("Route"), null, 1.2),
                MapShapeRole.Hazard => ((Brush?)FindResource("Need"), (Brush?)FindResource("Need"), 0.8),

                // Something the vocabulary does not name yet. Drawn thin rather than dropped: a map
                // RatNav has not learnt should look sparse, never blank.
                _ => ((Brush?)FindResource("Terrain"), null, 0.6),
            };

            // Outline draws edges only, so a busy map reduces to the shapes you navigate by.
            var outline = ink == "outline";
            var weight = thickness * _settings.Overlay.LineWeight / fit;

            var shapeOpacity = opacity * shape.Role switch
            {
                MapShapeRole.Terrain => 0.10,
                MapShapeRole.Structure => outline ? 0.85 : 0.30,
                MapShapeRole.Hazard => 0.35,
                MapShapeRole.Other => 0.22,
                _ => 0.85,
            };

            // A dark stroke underneath, drawn first and wider.
            //
            // This is the one thing that makes a translucent map readable over Tarkov, whose
            // backgrounds run from snowfield to unlit basement. No single line colour survives
            // both, and turning the opacity up until it does buries the game instead. A halo
            // separates the line from whatever is behind it, so the line itself can stay thin.
            //
            // In the HUD the same pass does the opposite job. There the map is meant to read as
            // light rather than as ink — outlines glowing over the game — so the wide stroke
            // underneath takes the line's own colour at low opacity instead of the dark ground.
            // A blur would be the obvious way to glow and the wrong one: several hundred paths
            // each carrying a DropShadowEffect is a frame budget spent on decoration.
            if ((_settings.Overlay.Halo || Hud) && stroke is not null && weight > 0)
            {
                var glowing = Hud;

                var halo = new Path
                {
                    Data = shape.Geometry,
                    RenderTransform = transform,
                    Stroke = glowing ? stroke : (Brush)FindResource("Ground"),
                    StrokeThickness = weight * (glowing ? 2.0 * _settings.Overlay.Glow : 3.2),
                    Opacity = glowing
                        ? Math.Min(1, shapeOpacity * 0.35)
                        : Math.Min(1, shapeOpacity * 1.4),
                    IsHitTestVisible = false,
                };

                MapInk.Children.Add(halo);
            }

            var path = new Path
            {
                Data = shape.Geometry,
                RenderTransform = transform,
                Stroke = stroke,
                StrokeThickness = weight,   // constant on screen however far you zoom
                Fill = outline ? null : fill,
                Opacity = shapeOpacity,
                IsHitTestVisible = false,
            };

            MapInk.Children.Add(path);
        }

        // Structures traced over the top of the graphical base.
        //
        // The map's own palette draws buildings at #1a2632 against #1f5054 terrain — near-black on
        // dark teal, which at any sensible opacity over a game is invisible. Woods has 111
        // buildings and Sawmill read as roads through empty ground. The colours below are the
        // map's, and correct; what they are not is legible over something else.
        if (ink == "graphical")
        {
            foreach (var shape in _mapShapes)
            {
                if (shape.Role is not (MapShapeRole.Structure or MapShapeRole.Boundary)) continue;

                MapInk.Children.Add(new Path
                {
                    Data = shape.Geometry,
                    RenderTransform = transform,
                    Stroke = (Brush)FindResource(
                        shape.Role == MapShapeRole.Structure ? "Accent" : "Ink"),
                    StrokeThickness = 0.8 * _settings.Overlay.LineWeight / fit,
                    Opacity = Math.Min(1, opacity * 1.5),
                    IsHitTestVisible = false,
                });
            }
        }
    }

    /// <summary>Draws a floor that is not the one you are on, at a strength that says so.</summary>
    private void Ghost(
        IReadOnlyList<MapShape> shapes, Transform transform, double fit, double opacity, string brush)
    {
        foreach (var shape in shapes)
        {
            if (shape.Role is MapShapeRole.Decoration or MapShapeRole.Terrain) continue;

            // Nothing on your floor is above or below this, so there is nothing to be confused
            // about — draw it properly. Only where the two genuinely stack does it need holding
            // back, and only then is a dashed line worth the loss of clarity.
            var clashes = _overlap?.Conflicts(shape) ?? true;

            MapInk.Children.Add(new Path
            {
                Data = shape.Geometry,
                RenderTransform = transform,
                Stroke = (Brush)FindResource(clashes ? brush : "Accent"),

                // Dashed only where it competes. A dashed line everywhere reads as "none of this
                // is real", which is the opposite of what a map with no conflict should say.
                StrokeDashArray = clashes ? [3, 2] : null,
                StrokeThickness = (clashes ? 0.9 : 1.0) * _settings.Overlay.LineWeight / fit,
                Opacity = clashes ? opacity : Math.Min(1, opacity * 1.9),
                IsHitTestVisible = false,
            });
        }
    }

    /// <summary>
    /// The ink dial, in one place. It drops whole categories rather than fading everything, which
    /// is the difference between a map you can read over a firefight and a grey wash. Hazards and
    /// boundaries survive every level: a minefield is not detail.
    /// </summary>
    private static bool ShownAt(string ink, MapShapeRole role) => ink switch
    {
        "outline" => role is MapShapeRole.Structure or MapShapeRole.Boundary or MapShapeRole.Hazard,
        "structure" => role is not MapShapeRole.Terrain and not MapShapeRole.Other,

        // Graphical and full both draw everything; the difference is whose colours.
        _ => true,
    };

    /// <summary>
    /// Where the map sits inside the canvas.
    ///
    /// <para>Two behaviours, and the choice is the player's. <b>Still</b> anchors the map's centre
    /// to the canvas and lets the marker travel across it, so a building stays where you last saw
    /// it. <b>Follow</b> keeps you centred and slides the map underneath, which is what you want
    /// once zoomed in far enough to walk off the edge.</para>
    ///
    /// <para>Built once per draw and shared by every layer, so the map, the route, and the marker
    /// cannot disagree about where anything is.</para>
    /// </summary>
    private Transform MapTransform(RaidView view, double width, double height)
    {
        var fit = FitScale(width, height);
        var (focusX, focusY) = Focus(view);

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(fit, fit));
        transform.Children.Add(new TranslateTransform(
            width / 2 - focusX * _mapViewBox.Width * fit,
            height / 2 - focusY * _mapViewBox.Height * fit));

        // Turned so that up the screen is the way you are facing.
        //
        // Applied last, about the middle of the view, which is where you are — the rotation is
        // around the player, not around the middle of the map.
        if (TurnBy(view) is { } degrees)
            transform.Children.Add(new RotateTransform(degrees, width / 2, height / 2));

        transform.Freeze();
        return transform;
    }

    /// <summary>
    /// How far to turn the map so your heading points up the screen, or null to leave it north-up.
    ///
    /// <para><b>The centred view only.</b> That view is in front of you and is read while moving,
    /// so a route behind you being drawn above you means doing the rotation in your head every
    /// time you glance at it. Turning the map instead makes what is at the top of the screen the
    /// thing that is in front of you, which is the reason to have a map in the middle of the
    /// screen at all.</para>
    ///
    /// <para><b>Never the corner panel.</b> That is a small static map you glance at to orient
    /// against buildings, and one that spun every time you turned would be unreadable. Its cone
    /// already answers which way you are facing. The two views are for different things, and this
    /// is the sharpest case of it.</para>
    /// </summary>
    private double? TurnBy(RaidView view)
    {
        if (_settings.Overlay.Mode != RatNavSettings.OverlayMode.Wireframe) return null;
        if (!_settings.Overlay.TurnWithYou) return null;

        // Only while the map is following you. The turn is about the middle of the view, which is
        // where you are *because* it follows — hold the map still and the middle is an arbitrary
        // point, so the map would spin around somewhere that is not you and swing you around the
        // outside of it.
        if (!Following) return null;

        return view.HeadingDegrees is { } heading ? -heading : null;
    }

    /// <summary>
    /// A point in map space (0 to 1 across the image) placed on the canvas.
    ///
    /// <para>Derived from the same fit and focus the map itself uses. It has to be: the previous
    /// version stretched to the canvas aspect while the map scaled uniformly, which put every pin
    /// off the building it belonged to on any overlay that was not square.</para>
    /// </summary>
    /// <summary>Put an element on the canvas at a point, and hand it back for adding.</summary>
    private static UIElement Positioned(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);

        return element;
    }

    /// <summary>
    /// Records that something at a point can be hovered, what it would say, and — for a quest
    /// waypoint — which stop it is, so a click has something to open.
    /// </summary>
    private void Hoverable(Point at, double radius, string text, RaidStop? stop = null)
    {
        // A floor under the size, because a marker drawn small is still something you are trying
        // to point at — and a target smaller than the cursor is one nobody can hit.
        var reach = Math.Max(8, radius);

        _hoverTargets.Add((new Rect(at.X - reach, at.Y - reach, reach * 2, reach * 2), text, stop));
    }

    /// <summary>
    /// Opens the quest behind whatever was clicked.
    ///
    /// <para>A pin says where to walk and nothing else — not which of six identical buildings, not
    /// which door inside it, not which step of the quest this even is. Those answers are a click
    /// away now rather than in a browser tab.</para>
    /// </summary>
    private void OnMapClick(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(MapCanvas);

        // Newest first, which is drawing order: a stop drawn over an extract is what you clicked.
        for (var i = _hoverTargets.Count - 1; i >= 0; i--)
        {
            if (!_hoverTargets[i].Bounds.Contains(at)) continue;
            if (_hoverTargets[i].Stop is not { } stop) continue;
            if (stop.TaskId is not { Length: > 0 }) continue;

            ShowQuestBrief(stop);
            e.Handled = true;
            return;
        }
    }

    /// <summary>
    /// Shows what the cursor is over, drawn into the scene rather than popped.
    ///
    /// <para>The window is Topmost with WS_EX_NOACTIVATE, and a WPF ToolTip is a popup owned by a
    /// window that never activates — so it opened and was dismissed in the same instant, which is
    /// exactly what hovering a waypoint looked like.</para>
    /// </summary>
    private void OnHoverMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(MapCanvas);

        // Searched from the end because that is drawing order: a stop drawn over an extract is
        // the thing you are pointing at.
        for (var i = _hoverTargets.Count - 1; i >= 0; i--)
        {
            if (!_hoverTargets[i].Bounds.Contains(at)) continue;

            HoverText.Text = _hoverTargets[i].Text;
            HoverCard.Visibility = Visibility.Visible;

            // Offset from the cursor, and folded back inside the map when it would run off the
            // right or bottom edge — a label you have to move the mouse away to read is no label.
            HoverCard.UpdateLayout();

            var width = HoverCard.ActualWidth;
            var height = HoverCard.ActualHeight;

            var left = at.X + 14 + width > MapCanvas.ActualWidth ? at.X - 14 - width : at.X + 14;
            var top = at.Y + 12 + height > MapCanvas.ActualHeight ? at.Y - 12 - height : at.Y + 12;

            HoverCard.Margin = new Thickness(Math.Max(0, left), Math.Max(0, top), 0, 0);
            return;
        }

        HoverCard.Visibility = Visibility.Collapsed;
    }

    private Point ToCanvas(RaidView view, double x, double y, double width, double height)
    {
        var fit = FitScale(width, height);
        var (focusX, focusY) = Focus(view);

        var at = new Point(
            width / 2 + (x - focusX) * _mapViewBox.Width * fit,
            height / 2 + (y - focusY) * _mapViewBox.Height * fit);

        // Positions turn with the map; the things drawn at them do not.
        //
        // The pins, captions and place names sit on their own layer above the map precisely so
        // this can be true. Rotating that layer wholesale would turn the text with it, and a map
        // whose labels are upside down whenever you walk north is worse than one that does not
        // turn at all.
        if (TurnBy(view) is not { } degrees) return at;

        var radians = degrees * Math.PI / 180;
        var (sin, cos) = (Math.Sin(radians), Math.Cos(radians));
        var (dx, dy) = (at.X - width / 2, at.Y - height / 2);

        return new Point(
            width / 2 + dx * cos - dy * sin,
            height / 2 + dx * sin + dy * cos);
    }

    private double FitScale(double width, double height) =>
        Math.Min(width / _mapViewBox.Width, height / _mapViewBox.Height) * Placement.Zoom;

    /// <summary>
    /// Whether the map keeps you centred, for whichever presentation is on screen.
    ///
    /// <para>Two settings rather than one, because the right answer differs. The corner box is too
    /// small to hold a map usefully, so it follows; the centred map is big enough to read as a
    /// map, and one that re-centres on every fix puts the same building somewhere new each time
    /// you look.</para>
    /// </summary>
    private bool Following => Placement.Follow;

    /// <summary>Whether the map has been dragged away from where it would otherwise sit.</summary>
    private bool Panned => Placement.PanX != 0 || Placement.PanY != 0;

    /// <summary>
    /// Puts the map back on you — once — without starting to follow.
    ///
    /// <para>A still map is still on purpose: big enough to read, and one that re-centres on every
    /// fix moves the same building each time you look at it. The cost was that asking "where am I
    /// now" meant turning follow on, which throws away the framing you chose and then has to be
    /// turned off again.</para>
    ///
    /// <para>With follow off, <see cref="Focus"/> centres on the middle of the map plus however
    /// far it has been dragged — so putting you in the middle is a pan to your own offset, and the
    /// map stays exactly as still afterwards as it was before. With follow already on, the same
    /// key clears the drag, which is the same request answered the way that mode can answer it.</para>
    ///
    /// <para>Nothing to do without a fix: there is no "you" to centre on until a screenshot has
    /// said where you are, and guessing the middle of the map would look like the key failing.</para>
    /// </summary>
    public void CenterOnPlayer() => Dispatcher.Invoke(() =>
    {
        if (_view?.X is not { } x || _view.Y is not { } y) return;

        Place(p => p.Follow
            ? p with { PanX = 0, PanY = 0 }
            : p with { PanX = x - 0.5, PanY = y - 0.5 });
    });

    /// <summary>
    /// What sits at the centre of the canvas: you, or the middle of the map — plus however far it
    /// has been dragged.
    /// </summary>
    private (double X, double Y) Focus(RaidView view)
    {
        var (x, y) = Following ? (view.X ?? 0.5, view.Y ?? 0.5) : (0.5, 0.5);
        return (x + Placement.PanX, y + Placement.PanY);
    }

    /// <summary>
    /// Drags the map. Offsets are in map space rather than pixels, so a drag means the same
    /// distance across the map however far you are zoomed in.
    /// </summary>
    private void Pan(double dxPixels, double dyPixels)
    {
        var fit = FitScale(MapCanvas.ActualWidth, MapCanvas.ActualHeight);
        if (fit <= 0) return;

        Place(p => p with
        {
            PanX = p.PanX - dxPixels / (fit * _mapViewBox.Width),
            PanY = p.PanY - dyPixels / (fit * _mapViewBox.Height),
        });

        Draw();
    }

    /// <summary>
    /// Puts the map back on you and locks it there — the crosshair, in the sense every mapping
    /// app uses it. Dragging is a deliberate look somewhere else, so it switches following off;
    /// this is how you say you are done looking.
    /// </summary>
    private void Recentre()
    {
        Place(p => p with { PanX = 0, PanY = 0, Follow = true });
        Draw();
    }

    private void ToggleFollowing()
    {
        // Turning it on means "put the map on me", so the pan is cleared and the view moves — that
        // is the point of pressing it.
        //
        // Turning it off means "let me look around from here", so the view must not move at all.
        // Where you were is folded into the pan, exactly as it is when a drag switches it off.
        if (Following)
        {
            var (x, y) = (_view?.X ?? 0.5, _view?.Y ?? 0.5);
            Place(p => p with { Follow = false, PanX = p.PanX + x - 0.5, PanY = p.PanY + y - 0.5 });
        }
        else
        {
            Place(p => p with { PanX = 0, PanY = 0, Follow = true });
        }

        Draw();
    }

    /// <summary>
    /// Whether a point has fallen outside the visible map, and if so where on the edge it should
    /// be shown and which way it lies.
    ///
    /// <para>Zooming in or panning pushes things off the view, and something that simply vanishes
    /// tells you nothing. Pinned to the edge on the line between the middle of the view and where
    /// it really is, it still says "that way" — which is enough to walk on.</para>
    /// </summary>
    private static bool OffView(Point at, double width, double height, out Point edge, out double bearing)
    {
        const double inset = 12;

        edge = at;
        bearing = 0;

        if (at.X >= inset && at.X <= width - inset && at.Y >= inset && at.Y <= height - inset)
            return false;

        var centre = new Point(width / 2, height / 2);
        var dx = at.X - centre.X;
        var dy = at.Y - centre.Y;

        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return false;

        // How far along the line from the centre the view's edge sits. The smaller of the two
        // axis limits is the one actually hit first.
        var limitX = dx == 0 ? double.PositiveInfinity : (width / 2 - inset) / Math.Abs(dx);
        var limitY = dy == 0 ? double.PositiveInfinity : (height / 2 - inset) / Math.Abs(dy);
        var scale = Math.Min(limitX, limitY);

        edge = new Point(centre.X + dx * scale, centre.Y + dy * scale);

        // Screen degrees, clockwise from up, matching how the facing cone is drawn.
        bearing = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return true;
    }

    /// <summary>
    /// Whether waypoints off the edge of the view get an arrow pointing at them.
    ///
    /// <para><b>The corner panel only.</b> That map is small enough that something just outside it
    /// is genuinely lost, which is what an arrow is for. The centred view is large and already
    /// shows the ground you are crossing, so the same arrows became a ring of clutter around the
    /// edge of exactly the thing you were trying to see through — and the edges of that view are
    /// where the drawing is deliberately fading out.</para>
    ///
    /// <para>This reverses part of the HUD's original premise, which called the edge arrows the
    /// part that made it worth doing. They were, in the small view.</para>
    /// </summary>
    private bool ShowsEdgeMarkers => _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box;

    /// <summary>An arrow on the edge of the view, pointing at something you cannot currently see.</summary>
    private void EdgeMarker(Point at, double bearing, Brush colour, double scale, string? label)
    {
        if (!ShowsEdgeMarkers) return;

        var arrow = new Path
        {
            Data = Geometry.Parse("M 0,-7 L 5,4 L 0,1 L -5,4 Z"),
            Fill = colour,
            Stroke = (Brush)FindResource("Ground"),
            StrokeThickness = 1,

            // The same size as everything else on the map.
            //
            // It used to be drawn at 0.7 on the theory that a direction should not be mistaken for
            // a position. In practice it just made the one marker you cannot see the position of
            // the hardest one to read, next to full-size pins and place names — and an arrow is
            // already unmistakably an arrow.
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(scale, scale),
                    new RotateTransform(bearing),
                },
            },

            IsHitTestVisible = false,
        };

        Canvas.SetLeft(arrow, at.X);
        Canvas.SetTop(arrow, at.Y);
        MapCanvas.Children.Add(arrow);

        if (label is not { Length: > 0 }) return;

        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9 * _settings.Overlay.TextScale,
            FontWeight = FontWeights.Bold,
            Foreground = colour,
            IsHitTestVisible = false,
        };

        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Nudged back toward the middle, so the label sits inside the view rather than half off it.
        Canvas.SetLeft(text, at.X - text.DesiredSize.Width / 2);
        Canvas.SetTop(text, at.Y + 6 * scale);
        MapCanvas.Children.Add(text);
    }

    /// <summary>
    /// A short caption on the map, with a dark backing so it survives whatever is behind it.
    ///
    /// <para><b>Every caption is drawn, overlaps and all.</b> They used to claim their space and a
    /// caption that could not find room was dropped, on the theory that a legible half beats an
    /// illegible whole. It does not: overlapping text is ugly, but a label that is not there
    /// cannot be read at all, and reading them is the entire reason they exist. Worse, which ones
    /// vanished depended on draw order, so the same map lost different names at different
    /// zooms.</para>
    /// </summary>
    private void Label(string text, double x, double y, Brush colour, double size)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            Foreground = colour,
            IsHitTestVisible = false,

            // Cheaper than drawing the text twice, and the only way a caption stays legible over
            // a snowfield and a basement without picking a different colour for each.
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 0,
                BlurRadius = 4,
                Opacity = 1,
            },
        };

        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, y);
        MapCanvas.Children.Add(label);
    }

    /// <summary>
    /// An extract's name, shortened for the edge of the map.
    ///
    /// <para>Off-view markers bunch along an edge, and full names — "Northern Checkpoint", "RUAF
    /// Roadblock" — overrun each other there. The first word carries almost all the meaning:
    /// "RAIL", "NORTH", "V-EX".</para>
    /// </summary>
    private static string Abbreviate(string name)
    {
        var first = name.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        return (first.Length <= 6 ? first : first[..5] + "\u2026").ToUpperInvariant();
    }

    private void DrawRoute(RaidView view)
    {
        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        Point Place(double x, double y) => ToCanvas(view, x, y, width, height);

        // Markers ease off as the map zooms out.
        //
        // Sized for reading a building they become a wall of overlapping furniture across a whole
        // map; held perfectly still they crowd, scaled with the map they vanish. The exponent is
        // the compromise and the player sets it.
        //
        // Measured from a middling zoom rather than from fully-out, so the size you chose is the
        // size you get around where you actually work. The first attempt used a negative exponent,
        // which shrank markers as you zoomed *in* — precisely backwards, and it made a waypoint
        // unusable at the zoom where you most need to see it.
        const double reference = 2.0;

        var relative = Math.Clamp(
            Math.Pow(Math.Max(0.1, Placement.Zoom) / reference, _settings.Overlay.ScaleWithZoom),
            0.6,
            1.5);

        var scale = _settings.Overlay.MarkerScale * relative;
        var textScale = _settings.Overlay.TextScale * relative;

        // Place names have their own dial. They are the backdrop rather than a destination, and
        // want a different size from the captions on the things you are walking to.
        var placeScale = _settings.Overlay.PlaceNameScale * relative;

        // Place names first — they are the backdrop. Drawn after the pins they sat on top of the
        // thing you were navigating to, which is exactly backwards.
        if (_settings.Overlay.ShowPlaceNames)
        {
            foreach (var place in _places)
            {
                var at = Place(place.X, place.Y);

                // White. At full ink the map's own roads and rock are pale enough that a muted
                // grey caption disappears into them.
                Label(place.Text, at.X, at.Y, Brushes.White, 9 * placeScale);
            }
        }

        foreach (var extract in _extracts.Where(ShowsExtract))
        {
            var at = Place(extract.X, extract.Y);
            var scav = extract.Faction.Equals("scav", StringComparison.OrdinalIgnoreCase);

            // Green for PMC, yellow for Scav, amber for a transit — and the transit gets a shape
            // of its own as well, because it is a different kind of thing rather than the same
            // thing for somebody else. Colour carries the faction; shape carries what it does.
            var colour = (Brush)FindResource(
                extract.Transit ? "Transit"
                : scav ? "ScavExit"
                : "PmcExit");

            // Off the view entirely — zoomed in, or panned away. Pinned to the edge pointing at
            // where it really is, so you can walk that way until it comes back into sight.
            if (OffView(at, width, height, out var edge, out var bearing))
            {
                EdgeMarker(edge, bearing, colour, scale, Abbreviate(extract.Name));
                continue;
            }

            // A door with an arrow through it, drawn from the marker's own centre — and for a
            // transit, two chevrons pointing the same way, which reads as "onward" rather than
            // "out" without needing the label.
            var mark = new Path
            {
                Data = Geometry.Parse(extract.Transit
                    ? "M -6,-6 L 0,0 L -6,6 M 0,-6 L 6,0 L 0,6"
                    : "M -6,-7 L -6,7 L 6,7 L 6,-7 Z M -2,0 L 4,0 M 1,-3 L 4,0 L 1,3"),
                Stroke = colour,
                StrokeThickness = 1.8,

                // The door is a closed shape and wants the ground behind it knocked out; the
                // chevrons are two open strokes, and filling them would draw triangles nobody
                // asked for.
                Fill = extract.Transit ? null : (Brush)FindResource("Ground"),
                Opacity = 0.95,
                RenderTransform = new ScaleTransform(scale, scale),
            };

            Canvas.SetLeft(mark, at.X);
            Canvas.SetTop(mark, at.Y);
            MapCanvas.Children.Add(mark);

            Label(extract.Name, at.X, at.Y + 9 * scale, colour, 9 * textScale);

            // How far, in metres, under the name. Straight-line across the ground — not a walk,
            // and not claiming to be one; what it answers is "which of these two is nearer",
            // which is the question that was actually asked. Muted and a size down, because a
            // number under every exit competes with the names if it is given equal weight.
            if (Metres(extract) is { } metres)
            {
                Label(
                    metres,
                    at.X,
                    at.Y + 9 * scale + 11 * textScale,
                    (Brush)FindResource("Muted"),
                    8 * textScale);
            }
            Hoverable(at, 8 * scale, extract.Transit
                ? $"{extract.Name} · transit to another map"
                : $"{extract.Name} · {extract.Faction} extract");
        }

        // Marks of your own, in their own colour.
        //
        // A different colour from a quest waypoint on purpose: what a quest asked for and what you
        // decided to note are different kinds of thing, and telling them apart has to survive a
        // glance rather than needing the label read.
        // A mark that has joined a plan is drawn by the stop loop below, not here.
        //
        // It is in both lists — a plan stop *and* a mark — and both loops drew a pin and a caption
        // at the same point, so the name appeared twice, once red and once orange. The stop is the
        // version worth keeping: it carries the number that ties it to the quest log.
        var inThePlan = (view.Stops ?? [])
            .Select(s => s.ObjectiveId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mark in _marks)
        {
            if (inThePlan.Contains(mark.Id)) continue;

            var at = Place(mark.X, mark.Y);
            var colour = (Brush)FindResource("Mark");

            if (OffView(at, width, height, out var markEdge, out var markBearing))
            {
                EdgeMarker(markEdge, markBearing, colour, scale, Abbreviate(mark.Label));
                continue;
            }

            // Shape carries two things at once. Against a quest's pin it says this is yours —
            // colour alone fails for anyone who cannot separate the hues, and a navigation overlay
            // is a bad place to learn that. Between marks it says which kind: a diamond is a
            // place, a box is something to pick up when you get there.
            MapCanvas.Children.Add(Positioned(
                new Path
                {
                    // The same pin a quest stop draws, in its own colour.
                    //
                    // It used to be a diamond or a box depending on the mark's kind, on the
                    // reasoning that colour alone fails for anyone who cannot separate the hues.
                    // The kind is no longer something anybody chooses, and a waypoint is a
                    // waypoint — what separates it from a quest's is where it came from, which is
                    // exactly what a colour is for.
                    Data = Geometry.Parse(
                        "M 0,0 C -3,-5 -7,-8 -7,-12 A 7,7 0 1 1 7,-12 C 7,-8 3,-5 0,0 Z"),
                    Fill = colour,
                    Stroke = (Brush)FindResource("Ground"),
                    StrokeThickness = 1.5,
                    RenderTransform = new ScaleTransform(scale, scale),
                },
                at.X,
                at.Y));

            Label(mark.Label, at.X, at.Y + 9 * scale, colour, 9 * textScale);
            // The note is the half worth having: the label says where, the note says the thing
            // you cannot remember at the time.
            Hoverable(
                at, 8 * scale,
                mark.Note is { Length: > 0 } note
                    ? $"{mark.Label} — {note}"
                    : $"{mark.Label} · your waypoint");
        }

        var quests = _settings.Overlay.Quests;

        // Every other started quest's objective, drawn under the plan's own stops.
        //
        // Hollow and unnumbered, because they are context rather than a route: things you could
        // pick up while you are here, not stops you set out to make. The plan's stops are filled
        // and numbered, and the difference has to be visible at a glance or the plan stops being
        // a plan.
        if (quests == "all")
        {
            var planned = view.Stops
                .Select(s => s.ObjectiveId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var objective in _objectives)
            {
                if (planned.Contains(objective.ObjectiveId)) continue;

                var at = Place(objective.X, objective.Y);
                if (OffView(at, width, height, out _, out _)) continue;

                MapCanvas.Children.Add(Positioned(
                    new Path
                    {
                        Data = Geometry.Parse("M 0,-6 A 6,6 0 1 1 0,6 A 6,6 0 1 1 0,-6 Z"),
                        Stroke = (Brush)FindResource("Muted"),
                        StrokeThickness = 1.5,
                        Fill = (Brush)FindResource("Ground"),
                        Opacity = 0.8,
                        RenderTransform = new ScaleTransform(scale * 0.8, scale * 0.8),
                    },
                    at.X,
                    at.Y));

                Hoverable(at, 7 * scale, $"{objective.TaskName} · {objective.Description}");
            }
        }

        // No line between stops. A dashed path across a map implies a route through walls and
        // buildings that does not exist, and the order is already carried by the numbers.
        var order = 0;

        foreach (var stop in quests == "off" ? [] : view.Stops)
        {
            if (!stop.Done) order++;

            var at = Place(stop.X, stop.Y);

            // A stop with no quest behind it is one of your own waypoints, and is drawn in the
            // waypoint colour rather than the quests'. Marks are given an empty TaskId when they
            // join a plan, precisely because there is nothing to turn in.
            var mine = string.IsNullOrEmpty(stop.TaskId);
            var ink = (Brush)FindResource(stop.Done ? "Muted" : mine ? "Mark" : "Need");

            if (OffView(at, width, height, out var stopEdge, out var stopBearing))
            {
                EdgeMarker(stopEdge, stopBearing, ink, scale, stop.Done ? null : order.ToString());
                continue;
            }

            // A map pin, pointing at where the objective is rather than sitting on top of it —
            // which is what lets a bigger marker stay precise.
            var pin = new Path
            {
                Data = Geometry.Parse(
                    "M 0,0 C -3,-5 -7,-8 -7,-12 A 7,7 0 1 1 7,-12 C 7,-8 3,-5 0,0 Z"),
                Fill = ink,
                Stroke = (Brush)FindResource("Ground"),
                StrokeThickness = 1.5,
                Opacity = stop.Done ? 0.45 : 1,
                RenderTransform = new ScaleTransform(scale, scale),
            };

            Canvas.SetLeft(pin, at.X);
            Canvas.SetTop(pin, at.Y);
            MapCanvas.Children.Add(pin);

            // The pin hangs above its point, so the reach does too. The stop travels with it, so
            // a click can open the quest rather than only naming it.
            Hoverable(new Point(at.X, at.Y - 8 * scale), 9 * scale, Describe(stop), stop);

            if (stop.Done) continue;

            // The number is the route order, so the sequence reads off the map without a line
            // drawn through terrain nobody can walk.
            var label = new TextBlock
            {
                Text = order.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10 * scale * 0.6,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("Ground"),
                IsHitTestVisible = false,
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, at.Y - 15 * scale);
            MapCanvas.Children.Add(label);

            // The name too, under the pin, so a plan can be read without hovering — which matters
            // because hover over a window that never takes focus is unreliable.
            if (_settings.Overlay.ShowPlaceNames)
                Label(stop.TaskName, at.X, at.Y + 3, ink, 9 * textScale);
        }

        // Breadcrumbs: where fixes were taken, so the gap since the last one is visible rather
        // than implied.
        foreach (var crumb in view.Trail)
        {
            var dot = new Ellipse
            {
                Width = 3, Height = 3,
                Fill = (Brush)FindResource("Accent"),
                Opacity = 0.35,
                IsHitTestVisible = false,
            };

            var at = Place(crumb.X, crumb.Y);
            Canvas.SetLeft(dot, at.X - 1.5);
            Canvas.SetTop(dot, at.Y - 1.5);
            MapCanvas.Children.Add(dot);
        }

        if (view.X is null || view.Y is null) return;

        // Placed like everything else. Pinning this to the canvas centre is what made a still map
        // impossible: the marker could not move, so the map had to.
        var centre = Place(view.X.Value, view.Y.Value);

        // Your marker and your facing keep one size at every zoom.
        //
        // Everything else eases off as the map pulls back, which is right for furniture — but you
        // pull back precisely to ask "where am I and which way am I pointing", and a marker that
        // shrank along with the pins stops answering it at the moment it is asked.
        var you = _settings.Overlay.PlayerScale;

        if (view.HeadingDegrees is { } heading)
        {
            var cone = new Path
            {
                Fill = (Brush)FindResource("Accent"),
                Opacity = 0.25,
                Data = Geometry.Parse("M 0,0 L -9,-24 L 9,-24 Z"),
                RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        new ScaleTransform(you * 0.7, you * 0.7),

                        // Straight up once the map turns with you, because up is then where you
                        // are looking. Turning the cone as well would turn it twice.
                        new RotateTransform(TurnBy(view) is null ? heading : 0),
                    },
                },
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(cone, centre.X);
            Canvas.SetTop(cone, centre.Y);
            MapCanvas.Children.Add(cone);
        }

        var marker = new Ellipse
        {
            Width = 9 * you * 0.6, Height = 9 * you * 0.6,
            Fill = (Brush)FindResource("Accent"),
            Stroke = (Brush)FindResource("Ground"),
            StrokeThickness = 2,
        };

        Canvas.SetLeft(marker, centre.X - marker.Width / 2);
        Canvas.SetTop(marker, centre.Y - marker.Height / 2);
        MapCanvas.Children.Add(marker);
    }

    /// <summary>A stop in one line: which quest, and what it wants there.</summary>
    private static string Describe(RaidStop stop)
    {
        var where = stop.Place is { Length: > 0 } place ? $"{place} · " : "";
        var owner = stop.Owner is { Length: > 0 } who ? $" ({who})" : "";

        return $"{where}{stop.TaskName}{owner}{Environment.NewLine}{stop.Description}";
    }

    private static string Age(TimeSpan since) =>
        since.TotalSeconds < 60 ? $"{since.TotalSeconds:F0}s ago" : $"{since.TotalMinutes:F0}m ago";

    protected override void OnClosed(EventArgs e)
    {
        // Anything still waiting on the quarter-second goes out now, or quitting inside it would
        // lose the last thing you moved.
        FlushSave();

        _hotkeys?.Dispose();
        _http.Dispose();
        base.OnClosed(e);
    }
}
