using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace RatNav.App;

/// <summary>
/// The expanded overlay: the full management UI, in place, without alt-tabbing.
///
/// <para>It hosts the same web app the service serves to a browser, so there is one management UI
/// to build, design and maintain rather than two that drift apart. The WebView2 is created when
/// the window opens and destroyed when it closes, so Chromium's memory is only spent while the
/// panel is actually up — which is when you are in the hideout planning, not mid-raid.</para>
///
/// <para>This window takes focus deliberately, unlike the compact HUD. You are typing in it.</para>
/// </summary>
public sealed class ExpandedWindow : Window
{
    private readonly WebView2 _browser;

    public ExpandedWindow(string url)
    {
        Title = "RatNav";
        Width = 1100;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = System.Windows.Media.Brushes.Black;

        // Above the game, like the HUD, because a single-monitor player is looking at it while
        // the game is still running behind.
        Topmost = true;

        _browser = new WebView2 { Source = new Uri(url) };
        Content = _browser;
    }

    protected override void OnClosed(EventArgs e)
    {
        _browser.Dispose();
        base.OnClosed(e);
    }
}
