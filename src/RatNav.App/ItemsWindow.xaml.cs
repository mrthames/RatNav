using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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
    }

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
