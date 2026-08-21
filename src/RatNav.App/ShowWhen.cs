using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RatNav.App;

/// <summary>
/// True is visible, anything else is collapsed.
///
/// <para>WPF has no built-in for this, which is why every WPF codebase grows one. Collapsed
/// rather than hidden, because a hidden element still takes its space — and the space here is a
/// pair of buttons on the end of every row of a list read over a game.</para>
/// </summary>
public sealed class ShowWhen : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
