using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RatNav.App.Interop;
using RatNav.Service;

// WinForms comes in for the tray icon and brings clashing drawing types with it.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace RatNav.App;

/// <summary>
/// The compact heads-up display that sits over the game.
///
/// <para>It shows where you are going and how far, the route as pins and a line, and how old your
/// last position fix is. Everything else is in the expanded panel.</para>
///
/// <para><b>Nothing here animates or polls.</b> The scene is redrawn when the raid state changes —
/// a position fix, a stop ticked off, a new plan — and at no other time. A marker that slid
/// smoothly between fixes would be inventing movement it cannot know about, and would cost frames
/// during a firefight to do it.</para>
/// </summary>
public partial class OverlayWindow : Window
{
    private GlobalHotKey? _hotkeys;
    private bool _clickThrough = true;
    private RaidView? _view;

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => OverlayWindowStyles.Apply(this, _clickThrough);
        SizeChanged += (_, _) => Draw();
    }

    /// <summary>Raised when the player asks for the full panel.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>Raised when the player ticks the current objective off.</summary>
    public event EventHandler? CompleteRequested;

    /// <summary>Binds the hotkeys. Called once the window has a handle.</summary>
    public void BindHotKeys(Action<string> onProblem)
    {
        _hotkeys = new GlobalHotKey(this);

        Bind(System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.OemTilde, ToggleVisible, "Alt+`");
        Bind(System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift,
            System.Windows.Input.Key.OemTilde, () => ExpandRequested?.Invoke(this, EventArgs.Empty), "Alt+Shift+`");
        Bind(System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.I, ToggleClickThrough, "Alt+I");
        Bind(System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.OemBackslash,
            () => CompleteRequested?.Invoke(this, EventArgs.Empty), "Alt+\\");

        void Bind(System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key, Action action, string name)
        {
            // Another application may already own a combination. Saying so beats leaving someone
            // pressing a key that does nothing.
            if (!_hotkeys!.Register(modifiers, key, action)) onProblem($"{name} is already taken by another app.");
        }
    }

    public void ToggleVisible()
    {
        // Hidden means hidden: the window draws nothing at all, rather than rendering at zero
        // opacity and still costing compositing work.
        if (IsVisible) Hide();
        else Show();
    }

    /// <summary>
    /// Hands the mouse back so the map can be panned or a stop ticked, then takes it away again.
    /// The HUD must never eat a click during a firefight, which is why this is off by default.
    /// </summary>
    public void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        OverlayWindowStyles.Apply(this, _clickThrough);
    }

    /// <summary>Takes new raid state and redraws. The only thing that changes what is on screen.</summary>
    public void Update(RaidView view)
    {
        _view = view;
        Dispatcher.Invoke(Draw);
    }

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
        FixAgeText.Text = view.FixedAt is { } at
            ? $"FIX {Age(DateTimeOffset.Now - at)}"
            : "NO FIX YET";

        DrawRoute(view);
    }

    private void DrawRoute(RaidView view)
    {
        var width = MapCanvas.ActualWidth;
        var height = MapCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var remaining = view.Stops.Where(s => !s.Done).ToList();

        // The line joining the stops still to make, in order.
        if (remaining.Count > 1)
        {
            var line = new Polyline
            {
                Stroke = (Brush)FindResource("Route"),
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                Opacity = 0.8,
            };

            foreach (var stop in remaining)
                line.Points.Add(new Point(stop.X * width, stop.Y * height));

            MapCanvas.Children.Add(line);
        }

        foreach (var stop in view.Stops)
        {
            var done = stop.Done;

            var pin = new Ellipse
            {
                Width = done ? 5 : 8,
                Height = done ? 5 : 8,
                Fill = (Brush)FindResource(done ? "Muted" : "Need"),
                Opacity = done ? 0.5 : 1,
                ToolTip = $"{stop.TaskName} — {stop.Description}",
            };

            Canvas.SetLeft(pin, stop.X * width - pin.Width / 2);
            Canvas.SetTop(pin, stop.Y * height - pin.Height / 2);
            MapCanvas.Children.Add(pin);
        }

        // Breadcrumbs: where you have taken fixes, so the gap since the last one is visible
        // rather than implied.
        foreach (var crumb in view.Trail)
        {
            var dot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = (Brush)FindResource("Accent"),
                Opacity = 0.35,
            };

            Canvas.SetLeft(dot, crumb.X * width - 1.5);
            Canvas.SetTop(dot, crumb.Y * height - 1.5);
            MapCanvas.Children.Add(dot);
        }

        if (view.X is not { } x || view.Y is not { } y) return;

        // Facing, drawn as a wedge. The heading is already in image space, so no maths here.
        if (view.HeadingDegrees is { } heading)
        {
            var cone = new Path
            {
                Fill = (Brush)FindResource("Accent"),
                Opacity = 0.25,
                Data = Geometry.Parse("M 0,0 L -8,-20 L 8,-20 Z"),
                RenderTransform = new RotateTransform(heading),
            };

            Canvas.SetLeft(cone, x * width);
            Canvas.SetTop(cone, y * height);
            MapCanvas.Children.Add(cone);
        }

        var you = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = (Brush)FindResource("Accent"),
            Stroke = (Brush)FindResource("Ground"),
            StrokeThickness = 2,
        };

        Canvas.SetLeft(you, x * width - 4.5);
        Canvas.SetTop(you, y * height - 4.5);
        MapCanvas.Children.Add(you);
    }

    private static string Age(TimeSpan since) =>
        since.TotalSeconds < 60 ? $"{since.TotalSeconds:F0}S AGO" : $"{since.TotalMinutes:F0}M AGO";

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys?.Dispose();
        base.OnClosed(e);
    }
}
