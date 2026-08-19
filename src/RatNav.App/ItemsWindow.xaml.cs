using System.Windows;
using System.Windows.Input;

namespace RatNav.App;

/// <summary>
/// The items list in a window of its own.
///
/// <para>Styled like the overlay rather than like a normal window — borderless, the same dark
/// ground, the same row spacing — because it is the same list, just parked somewhere else. A
/// chrome-heavy dialog beside a frameless overlay would read as a different application.</para>
///
/// <para>Created only when asked for, so a single-monitor player never pays for it. The overlay
/// keeps ownership of the data and pushes rows in, which keeps one fetch path rather than two
/// that can disagree.</para>
/// </summary>
public partial class ItemsWindow : Window
{
    public ItemsWindow()
    {
        InitializeComponent();

        // No title bar to grab, so the whole surface drags.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    public void Show(IReadOnlyList<ItemSection> sections) => Sections.ItemsSource = sections;
}
