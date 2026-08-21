using System.Windows;
using System.Windows.Controls;

// This project switches on both WPF and Windows Forms — the tray icon needs Forms — so Panel and
// Size each name two different types here. Spelled out rather than left to the compiler.
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace RatNav.App;

/// <summary>
/// A wrap panel whose rows are each centred, rather than each starting at the left edge.
///
/// <para>WPF's own <see cref="WrapPanel"/> centres nothing: setting <c>HorizontalAlignment</c> on
/// it centres the panel inside its parent and leaves every row inside the panel hard against the
/// left. With one row that is indistinguishable from centred, which is why it looks right until
/// the day it wraps — and then the first row is centred and the second is not, which reads as a
/// layout that has come apart.</para>
///
/// <para>The hotkey strip is the case that wants this. It sits under the map, it is the width of
/// however many keys are bound, and it wraps as soon as the overlay is narrow or the UI scale is
/// up — which at 1080p is most of the time.</para>
///
/// <para>Horizontal only. A vertical version would need the same arithmetic transposed and there
/// is nothing here that wants one.</para>
/// </summary>
public sealed class CenteredWrapPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        // Children are measured against the panel's width and their own natural height. An
        // infinite width — which is what a horizontal StackPanel would offer — would mean nothing
        // ever reported itself as too wide, and nothing would ever wrap.
        var line = new Size();
        var total = new Size();

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            var wanted = child.DesiredSize;

            if (line.Width + wanted.Width > available.Width && line.Width > 0)
            {
                // This child starts a new row, so the one being built is finished.
                total.Width = Math.Max(total.Width, line.Width);
                total.Height += line.Height;
                line = new Size();
            }

            line.Width += wanted.Width;
            line.Height = Math.Max(line.Height, wanted.Height);
        }

        total.Width = Math.Max(total.Width, line.Width);
        total.Height += line.Height;

        return total;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var row = new List<UIElement>();
        var width = 0.0;
        var height = 0.0;
        var y = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            var wanted = child.DesiredSize;

            if (width + wanted.Width > final.Width && row.Count > 0)
            {
                PlaceRow(row, width, height, y, final.Width);
                y += height;

                row.Clear();
                width = 0;
                height = 0;
            }

            row.Add(child);
            width += wanted.Width;
            height = Math.Max(height, wanted.Height);
        }

        PlaceRow(row, width, height, y, final.Width);

        return final;
    }

    /// <summary>Lays one finished row out, starting at whatever offset centres it.</summary>
    private static void PlaceRow(List<UIElement> row, double width, double height, double y, double available)
    {
        var x = Math.Max(0, (available - width) / 2);

        foreach (var child in row)
        {
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width;
        }
    }
}
