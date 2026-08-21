using System.Windows;
using RatNav.App.Interop;

// WinForms is in scope for the tray icon and brings a Brushes of its own.
using Brushes = System.Windows.Media.Brushes;

namespace RatNav.App;

/// <summary>
/// The map settings, in a window of their own.
///
/// <para>They were a panel inside the overlay, and every version of that had the same fault: the
/// settings covered the map. Centred they covered most of it; moved to one edge they covered less
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
        SizeToContent = SizeToContent.Manual;

        MinWidth = 280;
        MinHeight = 220;

        Width = 380;
        Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.8);

        // Beside the work area's right edge rather than centred on the screen, because the middle
        // of the screen is where the game is and where the centred overlay draws.
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Top + (work.Height - Height) / 2;
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
