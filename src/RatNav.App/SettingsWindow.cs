using System.Windows;
using RatNav.App.Interop;

// WinForms is in scope for the tray icon and brings a Brushes of its own.
using Brushes = System.Windows.Media.Brushes;

namespace RatNav.App;

/// <summary>
/// The map settings, in a window of their own.
///
/// <para>They were a panel inside the overlay, and every version of that had the same fault: the
/// settings covered the map. Centered they covered most of it; moved to one edge they covered less
/// of it and started colliding with the quick controls floating there instead. There is no
/// position inside a small overlay for a panel this size, because the overlay is the thing being
/// configured.</para>
///
/// <para>Out here it covers nothing. The map is fully visible while every dial is turned, which is
/// what makes the numbers mean anything — you are setting how big a pin should look, and the pins
/// are right there.</para>
///
/// <para>Built like the other pop-outs rather than as a second implementation: the settings panel
/// is <em>moved</em> into this window and moved back when it closes, so there is one set of
/// controls and no copy to keep in step.</para>
/// </summary>
public sealed class SettingsWindow : Window
{
    public SettingsWindow()
    {
        Title = "RatNav — Settings";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        // As tall as its contents and no taller. Fixed, it opened with a hand's width of empty
        // panel under the last control — and a window mostly made of nothing reads as one that has
        // failed to load something.
        SizeToContent = SizeToContent.Height;

        MinWidth = 280;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;

        Width = 380;

        // Beside the work area's right edge rather than centered on the screen, because the middle
        // of the screen is where the game is and where the centered overlay draws.
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;

        // Placed once it knows how tall it is, which with SizeToContent is not yet.
        SizeChanged += (_, _) =>
        {
            if (Top <= 0) Top = Math.Max(work.Top, work.Top + (work.Height - ActualHeight) / 2);
        };

        Top = 0;
    }

    /// <summary>
    /// Borderless, so <c>ResizeMode</c> means nothing without this — Windows sizes a window by a
    /// frame and there is no frame. Somebody will want this taller than it opens.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ResizeBorder.Attach(this);

        // Never takes focus off the game. It is a window over a running raid, and stealing focus
        // is how an overlay gets somebody killed.
        OverlayWindowStyles.Apply(this, clickThrough: false);
    }
}
