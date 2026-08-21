using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RatNav.App.Interop;

namespace RatNav.App;

/// <summary>
/// The items list in a window of its own.
///
/// <para>Styled like the overlay rather than like a normal window — borderless, the same dark
/// ground, the same rows — because it is the same list, just parked somewhere else.</para>
///
/// <para>Dragging is by the title strip, not by the whole surface. Whole-surface dragging was
/// tried and cannot work: a click on a row is also a click on the window, and there is no way to
/// tell which was meant. A strip that says "drag to move" is both unambiguous and findable.</para>
/// </summary>
public partial class ItemsWindow : Window
{
    public ItemsWindow()
    {
        InitializeComponent();

        TitleStrip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        DockBack.Click += (_, _) => Close();

        // The same corner the main overlay uses. The invisible edges stay underneath — they cost
        // nothing and they are what a window normally does; what was missing was something to see.
        ResizeGrip.DragDelta += (_, e) =>
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        };
    }

    /// <summary>
    /// Hands the window its resize edges.
    ///
    /// <para>Borderless and transparent, so <c>ResizeMode="CanResize"</c> on its own does nothing:
    /// there is no frame for Windows to size it by. <see cref="ResizeBorder"/> answers for the
    /// outer few pixels instead, which is enough to get the real resize back.</para>
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ResizeBorder.Attach(this);

        // Torn off, but still the overlay: it starts click-through like every other part of it.
        // Without this line the window is an ordinary top-level one and eats every click that
        // lands on it — see SetInteractive.
        OverlayWindowStyles.Apply(this, !_interactive);
    }

    /// <summary>What the strip calls this panel. Set once, when it is torn off.</summary>
    public string PanelName
    {
        get => Heading.Text;
        set => Heading.Text = value;
    }

    private bool _interactive;

    /// <summary>
    /// Follows the overlay's interact mode: takes the mouse, or gets out of its way.
    ///
    /// <para>This window never had the overlay's extended styles applied to it at all. Being
    /// <c>Topmost</c> and borderless made it look like part of the overlay while behaving like an
    /// ordinary window, so it sat over the game swallowing every click inside its rectangle —
    /// whatever interact mode said, and whatever the controls toggle said. Turning the controls
    /// off hid the furniture and changed nothing about the clicks, which is exactly the complaint
    /// the first user test produced: the game underneath could not be reached.</para>
    ///
    /// <para>The furniture goes with it. A drag strip, a dock-back button and a resize corner are
    /// all things you press, and with the mouse handed back to the game none of them can be —
    /// so they are clutter over a raid rather than controls.</para>
    /// </summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        OverlayWindowStyles.Apply(this, !interactive);

        var furniture = interactive ? Visibility.Visible : Visibility.Collapsed;
        TitleStrip.Visibility = furniture;
        DockBack.Visibility = furniture;
        ResizeGrip.Visibility = furniture;

        ShowScrollBar(interactive);
    }

    /// <summary>
    /// Shows or hides the scroll bar, following the overlay's interact mode.
    ///
    /// <para>Hidden rather than disabled: the list still scrolls on the wheel, so the bar is only
    /// the mark that says there is more — and a mark over a raid that cannot be dragged is one
    /// more thing on screen for nothing.</para>
    /// </summary>
    public void ShowScrollBar(bool visible) =>
        Scroll.VerticalScrollBarVisibility = visible
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

    public void Show(IReadOnlyList<ItemSection> sections)
    {
        Sections.ItemsSource = sections;
        FitToContent(sections);
    }

    /// <summary>
    /// Matches the overlay's own opacity and size.
    ///
    /// <para>A popped-out list is the same list parked somewhere else, so it has no business
    /// looking like a different tool — and it had none of these, which showed as a solid,
    /// differently-sized panel beside a translucent one. The controls stay on the overlay: there
    /// is no reason to grow a second set of them on every pop-out.</para>
    /// </summary>
    public void MatchOverlay(double opacity, double scale)
    {
        Opacity = Math.Clamp(opacity, 0.2, 1.0);

        Root.LayoutTransform = Math.Abs(scale - 1) < 0.001
            ? Transform.Identity
            : new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Sizes the window to the list it is showing, once.
    ///
    /// <para>Opening at a fixed height meant dragging it to size every single time. It is only
    /// done while the window is still at its opening size — after that the player has chosen a
    /// size, and resizing under them would be worse than opening small.</para>
    /// </summary>
    private void FitToContent(IReadOnlyList<ItemSection> sections)
    {
        if (_sized) return;

        var rows = sections.Where(s => s.Expanded).Sum(s => s.Rows.Count);
        var headers = sections.Count;

        // Row height, header height, and the chrome above and below the list.
        var wanted = rows * 15 + headers * 20 + 64;
        var ceiling = SystemParameters.WorkArea.Height * 0.9;

        Height = Math.Clamp(wanted, MinHeight, ceiling);
        _sized = true;
    }

    private bool _sized;
}
