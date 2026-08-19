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

    public OverlayWindow(RatNavSettings settings, Action<RatNavSettings> saveSettings)
    {
        InitializeComponent();

        _settings = settings;
        _saveSettings = saveSettings;

        ApplyBounds();

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

            // No panel, no chrome — just the map over the game.
            Frame.Background = Brushes.Transparent;
            Frame.BorderThickness = new Thickness(0);
            MapFrame.Background = Brushes.Transparent;
            MapFrame.BorderThickness = new Thickness(0);
            Readout.Opacity = 0.9;
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
            Readout.Opacity = 1;
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
            var url = $"http://localhost:{ServiceHost.DefaultPort}/api/maps";
            var maps = await _http.GetFromJsonAsync<List<MapSummary>>(url);

            _floors = maps?.FirstOrDefault(m => m.Id == mapId)?.Floors ?? [];
            _floorsFor = mapId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _floors = [];
        }
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
    }

    /// <summary>
    /// Draws the map itself: terrain as a whisper, structure and roads carrying it. Zoom keeps the
    /// player centred, because a zoomed map that does not follow you is a picture, not a tool.
    /// </summary>
    private void DrawMap(RaidView view)
    {
        if (_mapShapes.Count == 0) return;

        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var zoom = _settings.Overlay.Zoom;
        var opacity = _settings.Overlay.MapOpacity;
        var ink = _settings.Overlay.Ink;

        // Fit the drawing to the canvas, then zoom about wherever the player is.
        var fit = Math.Min(width / _mapViewBox.Width, height / _mapViewBox.Height) * zoom;

        var focusX = (view.X ?? 0.5) * _mapViewBox.Width;
        var focusY = (view.Y ?? 0.5) * _mapViewBox.Height;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(fit, fit));
        transform.Children.Add(new TranslateTransform(
            width / 2 - focusX * fit,
            height / 2 - focusY * fit));
        transform.Freeze();

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

    private void DrawRoute(RaidView view)
    {
        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var zoom = _settings.Overlay.Zoom;
        var focusX = view.X ?? 0.5;
        var focusY = view.Y ?? 0.5;

        // Route and pins follow the same zoom as the map, so they stay on top of it.
        Point Place(double x, double y) => new(
            width / 2 + (x - focusX) * width * zoom,
            height / 2 + (y - focusY) * height * zoom);

        var remaining = view.Stops.Where(s => !s.Done).ToList();

        if (remaining.Count > 1)
        {
            var line = new Polyline
            {
                Stroke = (Brush)FindResource("Route"),
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                Opacity = 0.9,
                IsHitTestVisible = false,
            };

            foreach (var stop in remaining) line.Points.Add(Place(stop.X, stop.Y));
            MapCanvas.Children.Add(line);
        }

        foreach (var stop in view.Stops)
        {
            var pin = new Ellipse
            {
                Width = stop.Done ? 5 : 8,
                Height = stop.Done ? 5 : 8,
                Fill = (Brush)FindResource(stop.Done ? "Muted" : "Need"),
                Opacity = stop.Done ? 0.5 : 1,
                ToolTip = $"{stop.TaskName} — {stop.Description}",
            };

            var at = Place(stop.X, stop.Y);
            Canvas.SetLeft(pin, at.X - pin.Width / 2);
            Canvas.SetTop(pin, at.Y - pin.Height / 2);
            MapCanvas.Children.Add(pin);
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

        var centre = new Point(width / 2, height / 2);

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

    private static string Age(TimeSpan since) =>
        since.TotalSeconds < 60 ? $"{since.TotalSeconds:F0}S AGO" : $"{since.TotalMinutes:F0}M AGO";

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys?.Dispose();
        _http.Dispose();
        base.OnClosed(e);
    }
}
