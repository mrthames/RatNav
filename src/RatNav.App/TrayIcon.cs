using System.Drawing;
using System.Windows.Forms;

namespace RatNav.App;

/// <summary>
/// The tray icon, which is RatNav's only presence when the overlay is hidden.
///
/// <para>It exists so the app is never invisible. An overlay that can be toggled off with a
/// hotkey needs somewhere to be found again when someone forgets which key they bound, and a
/// background process with no window and no icon is indistinguishable from one that has crashed.</para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onToggleOverlay, Action onOpenPanel, Action onOpenBrowser, Action onQuit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show / hide overlay\tAlt+`", null, (_, _) => onToggleOverlay());
        menu.Items.Add("Open panel\tAlt+Shift+`", null, (_, _) => onOpenPanel());
        menu.Items.Add("Open in browser", null, (_, _) => onOpenBrowser());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit RatNav", null, (_, _) => onQuit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RatNav",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => onToggleOverlay();
    }

    /// <summary>
    /// Tells the player something they need to know — a hotkey another app already owns, say.
    /// Rare by design: an overlay that interrupts is worse than no overlay.
    /// </summary>
    public void Warn(string message) =>
        _icon.ShowBalloonTip(5000, "RatNav", message, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
