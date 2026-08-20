using System.Windows;
using System.Windows.Interop;

// WinForms is in scope for the tray icon, and it brings a Point of its own.
using Point = System.Windows.Point;

namespace RatNav.App.Interop;

/// <summary>
/// Gives a borderless window its resize edges back.
///
/// <para>A window with <c>WindowStyle="None"</c> and <c>AllowsTransparency="True"</c> has no
/// non-client area at all, which is what makes it possible to draw an overlay that is not a box
/// with a title bar. The cost is that <c>ResizeMode="CanResize"</c> stops meaning anything:
/// Windows sizes a window by its frame, and there is no frame, so every point in the window
/// answers "client area" and the pointer never turns into a resize cursor.</para>
///
/// <para>The fix is to answer that question differently. Windows asks what is under the pointer
/// before it decides what a press means; naming the outer few pixels as an edge or a corner is
/// enough for it to run its own resize loop from there — the real one, with the real cursors, the
/// snapping, and the minimum sizes already set on the window.</para>
///
/// <para>Chosen over a corner grip, which is what the main overlay uses: a grip is one direction
/// from one corner, and these panels are narrow and tall against an edge of the screen, where the
/// side you want to pull is as often the left as the right.</para>
/// </summary>
public static class ResizeBorder
{
    private const int WM_NCHITTEST = 0x0084;

    // What Windows expects back: which part of the window the pointer is over.
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    /// <summary>
    /// How wide the grabbable edge is, in device-independent pixels.
    ///
    /// <para>Wider than a window border looks, because it is invisible. Six is about the width of
    /// the panel's own padding, so it lands on the dark margin rather than on a row of text.</para>
    /// </summary>
    private const double Edge = 6;

    /// <summary>Call once the window has a handle — <c>OnSourceInitialized</c> is the place.</summary>
    public static void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.AddHook((IntPtr _, int message, IntPtr _, IntPtr lParam, ref bool handled) =>
                OnMessage(window, message, lParam, ref handled));
    }

    private static IntPtr OnMessage(Window window, int message, IntPtr lParam, ref bool handled)
    {
        if (message != WM_NCHITTEST) return IntPtr.Zero;

        var where = Where(window, lParam);
        if (where == HTCLIENT) return IntPtr.Zero;

        handled = true;
        return new IntPtr(where);
    }

    private static int Where(Window window, IntPtr lParam)
    {
        // The pointer arrives in screen pixels, packed two signed shorts to a word. Unpacked as
        // unsigned it is wrong on every monitor left of or above the primary one, where the
        // coordinates are legitimately negative.
        var packed = (int)(lParam.ToInt64() & 0xFFFFFFFF);
        var screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));

        Point point;
        try
        {
            point = window.PointFromScreen(screen);
        }
        catch (InvalidOperationException)
        {
            // No source yet, which means nothing to resize either.
            return HTCLIENT;
        }

        var left = point.X <= Edge;
        var right = point.X >= window.ActualWidth - Edge;
        var top = point.Y <= Edge;
        var bottom = point.Y >= window.ActualHeight - Edge;

        // Corners first: inside one, both of its edges are true and either alone would be a worse
        // answer — a corner that only stretches one way is the thing people complain about.
        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;

        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;

        return HTCLIENT;
    }
}
