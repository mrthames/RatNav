using System.Diagnostics;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RatNav.Service;

// WinForms is here only for the tray icon, and its Application type would otherwise shadow WPF's.
using Application = System.Windows.Application;

namespace RatNav.App;

/// <summary>
/// RatNav's one process: the local service, the overlay, and a tray icon.
///
/// <para>Hosting the service in-process is what makes this a single executable with nothing to
/// start separately. The overlay reads the same <see cref="RaidSession"/> the browser reads over
/// its WebSocket, so the compact HUD, the expanded panel and a second monitor cannot disagree
/// about what is happening.</para>
/// </summary>
public partial class App : Application
{
    private WebApplication? _service;
    private OverlayWindow? _overlay;
    private ExpandedWindow? _expanded;
    private TrayIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _service = ServiceHost.Build(e.Args);
        await _service.StartAsync();

        var session = _service.Services.GetRequiredService<RaidSession>();

        _overlay = new OverlayWindow();
        _overlay.Show();

        // Hotkeys need a window handle, so they are bound after the first show.
        _overlay.BindHotKeys(problem => _tray?.Warn(problem));

        _overlay.ExpandRequested += (_, _) => ToggleExpanded();
        _overlay.CompleteRequested += (_, _) => CompleteCurrentStop(session);

        // Push, not poll: the overlay redraws when something happens and at no other time.
        session.Changed += (_, view) => _overlay.Update(view);
        _overlay.Update(session.View());

        _tray = new TrayIcon(
            onToggleOverlay: () => _overlay.ToggleVisible(),
            onOpenPanel: ToggleExpanded,
            onOpenBrowser: OpenInBrowser,
            onQuit: Shutdown);
    }

    /// <summary>
    /// The expanded panel: the same web app the browser serves, in a WebView2 created on demand
    /// and destroyed on close. Single-monitor players get the full management UI without
    /// alt-tabbing, and pay for Chromium only while it is open — which is when they are in the
    /// hideout deciding what to run, not mid-firefight.
    /// </summary>
    private void ToggleExpanded()
    {
        if (_expanded is { IsVisible: true })
        {
            _expanded.Close();
            _expanded = null;
            return;
        }

        _expanded = new ExpandedWindow($"http://localhost:{ServiceHost.DefaultPort}/");
        _expanded.Closed += (_, _) => _expanded = null;
        _expanded.Show();
    }

    private static void CompleteCurrentStop(RaidSession session)
    {
        var next = session.View().Stops.FirstOrDefault(s => !s.Done);
        if (next is not null) session.Complete(next.ObjectiveId);
    }

    private static void OpenInBrowser() =>
        Process.Start(new ProcessStartInfo($"http://localhost:{ServiceHost.DefaultPort}/")
        {
            UseShellExecute = true,
        });

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_service is not null) await _service.StopAsync();

        base.OnExit(e);
    }
}
