using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RatNav.App.Interop;

/// <summary>
/// Makes a window behave like an overlay: always on top, never taking focus, and — when asked —
/// invisible to the mouse so clicks land on the game behind it.
///
/// <para>This is the entire mechanism by which RatNav appears over Escape from Tarkov. It is an
/// ordinary top-level window composited by the desktop, with three extended styles set on it.
/// Nothing is injected, nothing is hooked, and the game is not touched — which is exactly why
/// this is safe to run.</para>
///
/// <para>The one requirement it puts on the player is that the game runs in Borderless or
/// Windowed mode. Exclusive fullscreen bypasses the desktop compositor and draws above every
/// overlay ever written; no amount of window styling changes that.</para>
/// </summary>
public static class OverlayWindowStyles
{
    private const int GWL_EXSTYLE = -20;

    /// <summary>Clicks pass through to whatever is behind.</summary>
    private const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Never becomes the foreground window, so tabbing to it cannot pull focus off the game.</summary>
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Keeps it out of the taskbar and the alt-tab list, where an overlay has no business.</summary>
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    /// <summary>Applies the overlay styles. Call once the window has a handle.</summary>
    public static void Apply(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var style = (long)GetWindowLongPtr(handle, GWL_EXSTYLE);

        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        // Click-through is toggled rather than fixed: the compact HUD should never eat a click
        // mid-firefight, but panning the map or ticking an objective needs the mouse back.
        if (clickThrough) style |= WS_EX_TRANSPARENT;
        else style &= ~WS_EX_TRANSPARENT;

        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
    }
}
