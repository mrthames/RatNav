using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RatNav.App.Interop;
using RatNav.Service;

// WinForms comes in for the tray icon and brings clashing drawing types with it.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

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
    private Size _mapViewBox = new(1000, 1000);

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

    private static readonly string[] InkLevels = ["full", "structure", "outline"];
    private static readonly string[] ExtractModes = ["pmc", "scav", "off"];

    /// <summary>Extracts for the current map, fetched once per map alongside its floors.</summary>
    private IReadOnlyList<ExtractPin> _extracts = [];

    /// <summary>The torn-off items window, while one is open.</summary>
    private ItemsWindow? _itemsWindow;

    public OverlayWindow(RatNavSettings settings, Action<RatNavSettings> saveSettings)
    {
        InitializeComponent();

        _settings = settings;
        _saveSettings = saveSettings;

        ApplyBounds();
        ApplyItemsPanel();

        SourceInitialized += (_, _) => OverlayWindowStyles.Apply(this, _clickThrough);
        SizeChanged += (_, _) => Draw();

        DragHandle.MouseLeftButtonDown += (_, _) => DragMove();
        ResizeGrip.DragDelta += OnResize;
        MouseWheel += OnWheel;

        FloorUp.Click += (_, _) => StepFloor(+1);
        FloorDown.Click += (_, _) => StepFloor(-1);
        InkButton.Click += (_, _) => CycleInk();
        FadeUp.Click += (_, _) => StepFade(+0.1);
        FadeDown.Click += (_, _) => StepFade(-0.1);
        ZoomReset.Click += (_, _) => SetZoom(1);
        FollowButton.Click += (_, _) => ToggleFollow();
        ExtractButton.Click += (_, _) => CycleExtracts();
        ItemsButton.Click += (_, _) => ToggleItems();
        DetachItems.Click += (_, _) => DetachItemsPanel();
    }

    public event EventHandler? ExpandRequested;
    public event EventHandler? CompleteRequested;

    /// <summary>Binds the configured hotkeys, reporting any that could not be set.</summary>
    public void BindHotKeys(Action<string> onProblem)
    {
        _hotkeys = new GlobalHotKey(this);
        var keys = _settings.Hotkeys;

        Bind(keys.ToggleOverlay, "Show/hide overlay", ToggleVisible);
        Bind(keys.ToggleInteract, "Interact with overlay", ToggleInteractive);
        Bind(keys.ExpandPanel, "Open panel", () => ExpandRequested?.Invoke(this, EventArgs.Empty));
        Bind(keys.CompleteObjective, "Tick objective off", () => CompleteRequested?.Invoke(this, EventArgs.Empty));
        Bind(keys.ToggleMode, "Switch overlay style", ToggleMode);
        Bind(keys.IdentifyItem, "Identify item under cursor", IdentifyUnderCursor);

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
        if (IsVisible)
        {
            if (!_clickThrough) SetInteractive(false);
            Hide();
        }
        else
        {
            Show();
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
        ApplyItemsPanel();

        if (interactive)
        {
            Show();
            Activate();
        }
        else
        {
            // Where it ended up is where it should be next time.
            Remember(_settings.Overlay with { Left = Left, Top = Top, Width = Width, Height = Height });
        }
    }

    /// <summary>Switches between the corner panel and the centred wireframe map.</summary>
    public void ToggleMode()
    {
        var mode = _settings.Overlay.Mode == RatNavSettings.OverlayMode.Box
            ? RatNavSettings.OverlayMode.Wireframe
            : RatNavSettings.OverlayMode.Box;

        Remember(_settings.Overlay with { Mode = mode });
        ApplyBounds();
        Draw();
    }

    public void Update(RaidView view)
    {
        _view = view;
        Dispatcher.Invoke(async () =>
        {
            await EnsureMapAsync(view);
            Draw();
            RefreshItems();
        });
    }

    private void ApplyBounds()
    {
        var bounds = _settings.Overlay;

        if (bounds.Mode == RatNavSettings.OverlayMode.Wireframe)
        {
            // Centred and large: the map is the point in this mode, not the readout.
            var screen = SystemParameters.WorkArea;
            Width = screen.Width * bounds.WireframeScale;
            Height = screen.Height * bounds.WireframeScale;
            Left = screen.Left + (screen.Width - Width) / 2;
            Top = screen.Top + (screen.Height - Height) / 2;

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
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

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

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        // Only while interactive, so a stray scroll mid-raid cannot move the map under you.
        if (_clickThrough) return;

        var zoom = Math.Clamp(_settings.Overlay.Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 1, 8);
        Remember(_settings.Overlay with { Zoom = zoom });
        Draw();
    }

    private void Remember(RatNavSettings.OverlayBounds bounds)
    {
        _settings = _settings with { Overlay = bounds };
        _saveSettings(_settings);
    }

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
            // Always the unabridged map. The ink level is applied when drawing, so changing it is
            // instant rather than a round trip — and the shapes are only parsed once either way.
            var url = $"http://localhost:{ServiceHost.DefaultPort}/api/maps/{Uri.EscapeDataString(mapId)}/image?ink=full";

            var svg = await _http.GetStringAsync(url);

            _mapViewBox = MapGeometry.ViewBoxOf(svg);
            _mapShapes = MapGeometry.Parse(svg, floor, key);
            _mapShapesFor = key;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // No map drawing this time. The route and readout still work, which is most of the value.
            _mapShapes = [];
        }
    }

    /// <summary>The map's levels, fetched once per map so the floor control has something to step through.</summary>
    private async Task EnsureFloorsAsync(string mapId)
    {
        if (_floorsFor == mapId) return;

        try
        {
            var root = $"http://localhost:{ServiceHost.DefaultPort}/api";

            var maps = await _http.GetFromJsonAsync<List<MapSummary>>($"{root}/maps");
            _floors = maps?.FirstOrDefault(m => m.Id == mapId)?.Floors ?? [];

            _extracts = await _http.GetFromJsonAsync<List<ExtractPin>>(
                $"{root}/maps/{Uri.EscapeDataString(mapId)}/extracts") ?? [];

            _floorsFor = mapId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _floors = [];
            _extracts = [];
        }
    }

    /// <summary>
    /// Whether an extract is worth drawing. Shared ones always are — they work for either faction
    /// — so the choice only ever hides the ones the other side uses.
    /// </summary>
    private bool ShowsExtract(ExtractPin extract)
    {
        var mode = _settings.Overlay.Extracts;
        if (mode == "off") return false;

        return extract.Faction.Equals("shared", StringComparison.OrdinalIgnoreCase)
            || extract.Faction.Equals(mode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Which level to draw. A hand-picked floor wins, but a fix taken since you picked it wins
    /// back — the map following where you actually are is the whole point of taking a fix.
    /// </summary>
    private string? FloorFor(RaidView view)
    {
        if (_floorOverride is not null && (view.FixedAt is null || _overrodeAt is null || view.FixedAt <= _overrodeAt))
            return _floorOverride;

        _floorOverride = null;
        _overrodeAt = null;
        return view.Floor;
    }

    private void StepFloor(int direction)
    {
        if (_floors.Count == 0 || _view is null) return;

        var current = FloorFor(_view);
        var at = _floors.ToList().FindIndex(f => f.Layer == current);

        // An unknown current level starts from the bottom rather than doing nothing.
        var next = Math.Clamp(at < 0 ? 0 : at + direction, 0, _floors.Count - 1);

        _floorOverride = _floors[next].Layer;
        _overrodeAt = DateTimeOffset.Now;

        Redraw();
    }

    private void CycleInk()
    {
        var at = Array.IndexOf(InkLevels, _settings.Overlay.Ink);
        Remember(_settings.Overlay with { Ink = InkLevels[(at + 1 + InkLevels.Length) % InkLevels.Length] });
        Redraw();
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
    /// <summary>Opens or closes the items list.</summary>
    private void ToggleItems()
    {
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

        _itemsWindow = new ItemsWindow();
        _itemsWindow.Closed += (_, _) =>
        {
            _itemsWindow = null;
            ApplyItemsPanel();
        };

        _itemsWindow.Show();

        ApplyItemsPanel();
        RefreshItems();
    }

    private void ApplyItemsPanel()
    {
        // Hidden while torn off, so the same list is never in two places at once.
        var inline = _settings.Overlay.ShowItems && _itemsWindow is null;

        ItemsPanel.Visibility = inline ? Visibility.Visible : Visibility.Collapsed;

        // Tearing off is a deliberate act, offered only while the overlay accepts the mouse.
        DetachItems.Visibility = inline && !_clickThrough ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Fills the items list: the watchlist first, then anything active quests and hideout modules
    /// still want. Fetched only when a list is actually on screen — during a raid with the panel
    /// closed this costs nothing at all.
    /// </summary>
    private void RefreshItems()
    {
        if (!_settings.Overlay.ShowItems && _itemsWindow is null) return;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var rows = await LoadItemsAsync();

            ItemsList.ItemsSource = rows;
            ItemsHeading.Text = rows.Count == 0 ? "NOTHING WANTED" : $"WANTED · {rows.Count}";

            _itemsWindow?.Show(rows);
        });
    }

    private async Task<IReadOnlyList<ItemRow>> LoadItemsAsync()
    {
        try
        {
            var root = $"http://localhost:{ServiceHost.DefaultPort}/api";

            var watchlist = await _http.GetFromJsonAsync<List<TrackedItemView>>($"{root}/items/watchlist") ?? [];
            var needed = await _http.GetFromJsonAsync<List<TrackedItemView>>($"{root}/items/needed") ?? [];

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ItemRow>();

            // Watchlist first. It is the shorter list and the one you chose deliberately, so it
            // should not be buried under forty quest items.
            foreach (var entry in watchlist.Concat(needed))
            {
                if (!seen.Add(entry.Id)) continue;
                rows.Add(ItemRow.From(entry));
            }

            return rows;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads whatever the mouse is hovering and says what it is for.
    ///
    /// <para>The capture is of the desktop, the same pixels a screenshot tool sees, and the OCR is
    /// Windows' own. Nothing is read from the game, injected into it, or asked of it — this is
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
        // that, so this is checked rather than assumed — the rest of the app is unaffected.
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
        ShowIdentifyCard("Reading…", "", []);

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
                    $"Read: {string.Join(" · ", lines.Take(3))}",
                    []);
                return;
            }

            // A hedge rather than silent confidence. OCR is wrong often enough that presenting a
            // guess as fact would eventually cost someone a quest item.
            var hedge = match.Confidence is { } confidence && confidence < 0.85
                ? $"best guess · {confidence * 100:F0}% sure"
                : null;

            ShowIdentifyCard(match.Item.Name, hedge, IdentifyCard.Reasons(match));
        });
    }

    private async Task<ItemDetail?> IdentifyAsync(IReadOnlyList<string> lines)
    {
        try
        {
            var url = $"http://localhost:{ServiceHost.DefaultPort}/api/items/identify";
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

    private void ShowIdentifyCard(string name, string? hedge, IReadOnlyList<IdentifyReason> reasons)
    {
        IdentifyName.Text = name;
        IdentifyHedge.Text = hedge ?? "";
        IdentifyHedge.Visibility = string.IsNullOrEmpty(hedge) ? Visibility.Collapsed : Visibility.Visible;
        IdentifyReasons.ItemsSource = reasons;

        IdentifyCardPanel.Visibility = Visibility.Visible;
        Show();
    }

    private void HideIdentifyCard() => IdentifyCardPanel.Visibility = Visibility.Collapsed;

    /// <summary>What the identify endpoint returns.</summary>
    private sealed record IdentifyResponse(List<ItemDetail>? Matches, List<string>? ReadText);

    private void CycleExtracts()
    {
        var at = Array.IndexOf(ExtractModes, _settings.Overlay.Extracts);
        Remember(_settings.Overlay with { Extracts = ExtractModes[(at + 1 + ExtractModes.Length) % ExtractModes.Length] });
        Draw();
    }

    private void ToggleFollow()
    {
        Remember(_settings.Overlay with { FollowPlayer = !_settings.Overlay.FollowPlayer });
        Draw();
    }

    private void SetZoom(double zoom)
    {
        Remember(_settings.Overlay with { Zoom = Math.Clamp(zoom, 1, 8) });
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
        MapCanvas.Children.Clear();

        if (view is null || !view.InRaid)
        {
            MapNameText.Text = "RATNAV";
            NextStopText.Text = "No plan";
            BearingText.Text = "";
            ProgressText.Text = "";
            FixAgeText.Text = "";
            UpdateControls(null);
            return;
        }

        MapNameText.Text = view.MapName?.ToUpperInvariant() ?? "";
        NextStopText.Text = view.NextStopName ?? "Plan complete";

        BearingText.Text = view.NextStopMetres is { } metres && view.NextStopRelativeBearing is { } bearing
            ? $"{metres:F0} m · {Math.Abs(bearing):F0}° {(bearing > 0 ? "right" : "left")}"
            : "";

        ProgressText.Text = $"{view.CompletedObjectiveIds.Count}/{view.Stops.Count} DONE";

        // Said plainly. The marker moves when you take a fix and at no other time, and pretending
        // otherwise is how an overlay gets someone killed.
        // Naming the key beats "no fix yet": the first thing anyone needs to know is which
        // button makes the marker appear.
        FixAgeText.Text = view.FixedAt is { } at
            ? $"FIX {Age(DateTimeOffset.Now - at)}"
            : $"TAP {_settings.ScreenshotKey.ToUpperInvariant()}";

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

        FloorText.Text = named?.Name ?? floor ?? "—";

        // Say when you are looking somewhere other than where you stand, because forgetting that
        // is how you plan a route across a floor you are not on.
        FloorText.Foreground = (Brush)FindResource(_floorOverride is null ? "Ink" : "Route");

        var at = _floors.ToList().FindIndex(f => f.Layer == floor);
        FloorUp.IsEnabled = _floors.Count > 0 && at < _floors.Count - 1;
        FloorDown.IsEnabled = _floors.Count > 0 && at > 0;

        InkButton.Content = _settings.Overlay.Ink;
        FadeText.Text = $"{_settings.Overlay.MapOpacity * 100:F0}%";
        ZoomReset.Content = $"{_settings.Overlay.Zoom:0.0}×";
        FollowButton.Content = _settings.Overlay.FollowPlayer ? "follows you" : "still";
        ExtractButton.Content = _settings.Overlay.Extracts;
        ItemsButton.Content = _settings.Overlay.ShowItems ? "items ▾" : "items ▸";
    }

    /// <summary>
    /// Draws the map itself: terrain as a whisper, structure and roads carrying it. Whether zoom
    /// holds the map still or keeps you centred is <see cref="RatNavSettings.OverlayBounds.FollowPlayer"/>.
    /// </summary>
    private void DrawMap(RaidView view)
    {
        if (_mapShapes.Count == 0) return;

        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var opacity = _settings.Overlay.MapOpacity;
        var ink = _settings.Overlay.Ink;
        var fit = FitScale(width, height);

        var transform = MapTransform(view, width, height);

        foreach (var shape in _mapShapes)
        {
            // Drop shadows are real in the source and noise on a translucent overlay.
            if (shape.Role == MapShapeRole.Decoration) continue;
            if (!ShownAt(ink, shape.Role)) continue;

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

            var path = new Path
            {
                Data = shape.Geometry,
                RenderTransform = transform,
                Stroke = stroke,
                StrokeThickness = thickness / fit,   // constant on screen however far you zoom
                // Outline draws edges only, so a busy map reduces to the shapes you navigate by.
                Fill = ink == "outline" ? null : fill,
                Opacity = opacity * shape.Role switch
                {
                    MapShapeRole.Terrain => 0.10,
                    MapShapeRole.Structure => 0.30,
                    MapShapeRole.Hazard => 0.35,
                    MapShapeRole.Other => 0.22,
                    _ => 0.85,
                },
                IsHitTestVisible = false,
            };

            MapCanvas.Children.Add(path);
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

        transform.Freeze();
        return transform;
    }

    /// <summary>
    /// A point in map space (0 to 1 across the image) placed on the canvas.
    ///
    /// <para>Derived from the same fit and focus the map itself uses. It has to be: the previous
    /// version stretched to the canvas aspect while the map scaled uniformly, which put every pin
    /// off the building it belonged to on any overlay that was not square.</para>
    /// </summary>
    private Point ToCanvas(RaidView view, double x, double y, double width, double height)
    {
        var fit = FitScale(width, height);
        var (focusX, focusY) = Focus(view);

        return new Point(
            width / 2 + (x - focusX) * _mapViewBox.Width * fit,
            height / 2 + (y - focusY) * _mapViewBox.Height * fit);
    }

    private double FitScale(double width, double height) =>
        Math.Min(width / _mapViewBox.Width, height / _mapViewBox.Height) * _settings.Overlay.Zoom;

    /// <summary>What sits at the centre of the canvas: you, or the middle of the map.</summary>
    private (double X, double Y) Focus(RaidView view) =>
        _settings.Overlay.FollowPlayer ? (view.X ?? 0.5, view.Y ?? 0.5) : (0.5, 0.5);

    private void DrawRoute(RaidView view)
    {
        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        Point Place(double x, double y) => ToCanvas(view, x, y, width, height);

        foreach (var extract in _extracts.Where(ShowsExtract))
        {
            var at = Place(extract.X, extract.Y);

            // A different shape as well as a different colour, so the two kinds of marker are
            // still tellable apart by anyone who cannot rely on hue.
            var mark = new Path
            {
                Data = Geometry.Parse("M 0,-5 L 5,0 L 0,5 L -5,0 Z"),
                Fill = (Brush)FindResource("Ground"),
                Stroke = (Brush)FindResource(
                    extract.Faction.Equals("scav", StringComparison.OrdinalIgnoreCase) ? "Route" : "Accent"),
                StrokeThickness = 1.6,
                Opacity = 0.85,
                ToolTip = $"{extract.Name} · {extract.Faction} extract",
            };

            Canvas.SetLeft(mark, at.X);
            Canvas.SetTop(mark, at.Y);
            MapCanvas.Children.Add(mark);
        }

        // No line between stops. A dashed path across a map implies a route through walls and
        // buildings that does not exist, and the order is already carried by the numbers.
        var order = 0;

        foreach (var stop in view.Stops)
        {
            if (!stop.Done) order++;

            var pin = new Ellipse
            {
                Width = stop.Done ? 6 : 11,
                Height = stop.Done ? 6 : 11,
                Fill = (Brush)FindResource(stop.Done ? "Muted" : "Need"),
                Stroke = (Brush)FindResource("Ground"),
                StrokeThickness = 1.5,
                Opacity = stop.Done ? 0.45 : 1,

                // Shown on hover while the overlay takes the mouse. Named the way the planner
                // does — the quest first, because "which quest is this for" is the question.
                ToolTip = Describe(stop),
            };

            var at = Place(stop.X, stop.Y);
            Canvas.SetLeft(pin, at.X - pin.Width / 2);
            Canvas.SetTop(pin, at.Y - pin.Height / 2);
            MapCanvas.Children.Add(pin);

            if (stop.Done) continue;

            // The number is the route order, so the sequence reads off the map without a line
            // drawn through terrain nobody can walk.
            var label = new TextBlock
            {
                Text = order.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("Ground"),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(label, at.X - 3);
            Canvas.SetTop(label, at.Y - 7);
            MapCanvas.Children.Add(label);
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

        if (view.HeadingDegrees is { } heading)
        {
            var cone = new Path
            {
                Fill = (Brush)FindResource("Accent"),
                Opacity = 0.25,
                Data = Geometry.Parse("M 0,0 L -9,-24 L 9,-24 Z"),
                RenderTransform = new RotateTransform(heading),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(cone, centre.X);
            Canvas.SetTop(cone, centre.Y);
            MapCanvas.Children.Add(cone);
        }

        var you = new Ellipse
        {
            Width = 9, Height = 9,
            Fill = (Brush)FindResource("Accent"),
            Stroke = (Brush)FindResource("Ground"),
            StrokeThickness = 2,
        };

        Canvas.SetLeft(you, centre.X - 4.5);
        Canvas.SetTop(you, centre.Y - 4.5);
        MapCanvas.Children.Add(you);
    }

    /// <summary>A stop in one line: which quest, and what it wants there.</summary>
    private static string Describe(RaidStop stop)
    {
        var where = stop.Place is { Length: > 0 } place ? $"{place} · " : "";
        var owner = stop.Owner is { Length: > 0 } who ? $" ({who})" : "";

        return $"{where}{stop.TaskName}{owner}{Environment.NewLine}{stop.Description}";
    }

    private static string Age(TimeSpan since) =>
        since.TotalSeconds < 60 ? $"{since.TotalSeconds:F0}S AGO" : $"{since.TotalMinutes:F0}M AGO";

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys?.Dispose();
        _http.Dispose();
        base.OnClosed(e);
    }
}
