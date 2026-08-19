using System.Windows;

namespace RatNav.App;

/// <summary>
/// The items list in a window of its own.
///
/// <para>Created only when asked for, so a single-monitor player never pays for it. The overlay
/// keeps ownership of the data and pushes rows in — this window has no idea the service exists,
/// which keeps one fetch path rather than two that can disagree.</para>
/// </summary>
public partial class ItemsWindow : Window
{
    public ItemsWindow() => InitializeComponent();

    public void Show(IReadOnlyList<ItemRow> rows)
    {
        ItemsList.ItemsSource = rows;
        Heading.Text = rows.Count == 0 ? "NOTHING WANTED" : $"WANTED · {rows.Count}";
    }
}
