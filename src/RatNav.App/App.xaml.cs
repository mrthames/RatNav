using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RatNav.Core;
using RatNav.App.Interop;
using RatNav.Service;

// WinForms is here only for the tray icon, and its Application type would otherwise shadow WPF's.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

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

        // OnStartup is async void, which means anything thrown after the first await is lost.
        // Without these, a service that fails to start leaves an empty window on screen and no
        // reason anywhere — which is exactly how it behaved when the published build could not
        // find its web files.
        DispatcherUnhandledException += (_, args) =>
        {
            Report("RatNav hit an error", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report("RatNav hit an error", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report("RatNav hit a background error", args.Exception);
            args.SetObserved();
        };

        try
        {
            await StartAsync(e);
        }
        catch (Exception ex)
        {
            Report("RatNav could not start", ex);
            Shutdown(1);
        }
    }

    private async Task StartAsync(StartupEventArgs e)
    {
        _service = ServiceHost.Build(e.Args);
        await _service.StartAsync();

        var session = _service.Services.GetRequiredService<RaidSession>();
        var settings = _service.Services.GetRequiredService<RatNavSettings>();
        var dataDirectory = RatNavPaths.EnsureDataDirectory();

        _overlay = new OverlayWindow(settings, updated => updated.Save(dataDirectory));
        _overlay.Show();

        // Hotkeys need a window handle, so they are bound after the first show.
        _overlay.BindHotKeys(problem => _tray?.Warn(problem));

        // Changing a hotkey in Setup rebinds it here rather than asking for a restart.
        ApiEndpoints.HotkeysChanged += updated =>
            _overlay?.Apply(updated, problem => _tray?.Warn(problem));

        // Starring an item in the app now reaches the overlay immediately, in a raid or out
        // of one, rather than waiting for the next thing the raid happens to do.
        ApiEndpoints.ItemsChanged += () => _overlay?.RefreshItemsNow();
        ApiEndpoints.WaypointsChanged += () => _overlay?.RefreshWaypointsNow();
        ApiEndpoints.OverlayResetRequested += () => _overlay?.ReturnToDefaultPlace();

        // A folder picker, for the drive somebody keeps their games on. Marshalled onto the UI
        // thread because it puts a window on screen, and the request that asked for it arrives on
        // a Kestrel thread that has no business doing that.
        // Windows' own OCR, which lives on this side of the app. The service asks; the pixels
        // never leave here.

        ApiEndpoints.BrowseForFolder = start => Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Where Escape from Tarkov is installed",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };

            if (start is { Length: > 0 } && Directory.Exists(start))
                dialog.SelectedPath = start;

            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                ? dialog.SelectedPath
                : null;
        });

        _overlay.ExpandRequested += (_, _) => ToggleExpanded();
        _overlay.OpenAppRequested += (_, _) => OpenInBrowser();

        // Push, not poll: the overlay redraws when something happens and at no other time.
        session.Changed += (_, view) => _overlay.Update(view);
        _overlay.Update(session.View());

        _tray = new TrayIcon(
            hotkeys: settings.Hotkeys,
            onToggleOverlay: () => _overlay.ToggleVisible(),
            onToggleInteract: () => _overlay.ToggleInteractive(),
            onToggleMode: () => _overlay.ToggleMode(),
            onOpenPanel: ToggleExpanded,
            onOpenBrowser: OpenInBrowser,
            onQuit: Shutdown);

        // The app opens with RatNav. The hotkey that used to put the panel over the game is
        // gone, and something has to bring up the half of the app that does the planning — being
        // told to type a localhost address is not that.
        // The legacy key wins when it is present, so a choice made before the rename survives it.
        if (settings.OpenBuddyAppAtStartLegacy ?? settings.OpenAppAtStart) OpenInBrowser();
    }

    /// <summary>
    /// Says what went wrong, on screen and in a file.
    ///
    /// <para>The file matters more than the dialog: this is a tool other people download, and
    /// "it opened and did nothing" is not a bug report anyone can act on.</para>
    /// </summary>
    private static void Report(string headline, Exception? error)
    {
        var detail = error?.ToString() ?? "No detail available.";
        var path = "";

        try
        {
            path = Path.Combine(RatNavPaths.EnsureDataDirectory(), "ratnav-error.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:u}  {headline}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write. The dialog is still worth showing.
        }

        var where = path.Length > 0 ? $"{Environment.NewLine}{Environment.NewLine}Written to {path}" : "";

        MessageBox.Show(
            $"{error?.Message ?? "Unknown error."}{where}",
            headline,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
