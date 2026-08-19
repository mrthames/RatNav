using System.Windows;
using System.Windows.Input;

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
