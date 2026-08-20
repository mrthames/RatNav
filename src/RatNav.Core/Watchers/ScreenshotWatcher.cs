using RatNav.Core.Maps;
using RatNav.Core.Model;

namespace RatNav.Core.Watchers;

/// <summary>What to do with a screenshot once its position has been read.</summary>
public enum ScreenshotDisposal
{
    /// <summary>Leave it. The folder grows without limit, which is what makes people think this is slow.</summary>
    Keep,

    /// <summary>Move it to an archive folder alongside the originals.</summary>
    Archive,

    /// <summary>Delete it. The coordinates are already read; the image itself was never the point.</summary>
    Delete,
}

/// <summary>
/// Watches the folder Escape from Tarkov writes screenshots to and turns each new one into a
/// position.
///
/// <para>This is the whole of RatNav's position tracking. The game encodes the player's world
/// coordinates and camera rotation into the <i>filename</i>, so reading a position means reading a
/// directory entry — the image is never opened, and nothing touches the game.</para>
///
/// <para><b>Housekeeping is not a nicety.</b> Every fix leaves a multi-megabyte PNG behind, and a
/// folder with thousands of them is the actual cause of the slowdown people blame on
/// screenshot-based tracking. Processed files are archived or deleted by default.</para>
/// </summary>
public sealed class ScreenshotWatcher : IDisposable
{
    private readonly string _directory;
    private readonly FileSystemWatcher _watcher;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>Raised for each screenshot that carried coordinates.</summary>
    public event EventHandler<PositionFix>? PositionFixed;

    /// <summary>What to do with a screenshot after reading it.</summary>
    public ScreenshotDisposal Disposal { get; set; } = ScreenshotDisposal.Archive;

    /// <summary>
    /// Where to record the names of screenshots that have been read, or null not to.
    ///
    /// <para>Set by the host to a file in RatNav's own data folder, so the evidence outlives the
    /// picture it came from.</para>
    /// </summary>
    public string? LogPath { get; set; }

    /// <summary>The most recent fix, or null if none this session.</summary>
    public PositionFix? Latest { get; private set; }

    public ScreenshotWatcher(string? directory = null)
    {
        _directory = directory ?? RatNavPaths.DefaultScreenshotDirectory;

        // The folder does not exist until the player takes their first in-game screenshot, and a
        // watcher cannot be pointed at a missing directory.
        Directory.CreateDirectory(_directory);

        _watcher = new FileSystemWatcher(_directory, "*.png")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };

        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
    }

    public void Start() => _watcher.EnableRaisingEvents = true;
    public void Stop() => _watcher.EnableRaisingEvents = false;

    /// <summary>
    /// Reads the newest screenshot already on disk, for picking up a fix taken before RatNav was
    /// watching — including the very first one, which is usually taken before anyone thinks to
    /// start the app.
    /// </summary>
    public PositionFix? ReadLatestExisting()
    {
        try
        {
            var newest = new DirectoryInfo(_directory)
                .EnumerateFiles("*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest is not null && Handle(newest.FullName) ? Latest : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Handle(e.FullPath);

    private bool Handle(string path)
    {
        lock (_gate)
        {
            // The watcher fires more than once for a single file — created, then written. Reading
            // the same screenshot twice would emit a duplicate fix and re-run housekeeping on a
            // file that is already gone.
            if (!_seen.Add(path)) return false;
        }

        if (!ScreenshotFilename.TryParse(path, out var fix))
        {
            // Menu and hideout screenshots carry no coordinates. Not an error, and not ours to
            // clean up either — only files we actually used get disposed of.
            return false;
        }

        Latest = fix;
        PositionFixed?.Invoke(this, fix);

        // The name, before the file goes. Everything a position fix knows is in its filename —
        // the coordinates and the camera rotation — so this is the whole of it, at a few dozen
        // bytes instead of thirteen megabytes.
        Remember(path);

        Dispose(path);
        return true;
    }

    /// <summary>
    /// Writes a processed screenshot's name to a log beside the archive.
    ///
    /// <para>This is what makes throwing the picture away safe. A marker that lands in the wrong
    /// place is diagnosed from the coordinates in the filename, and those survive here — the
    /// pixels never had anything to add.</para>
    /// </summary>
    private void Remember(string path)
    {
        if (LogPath is not { Length: > 0 } log) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, Path.GetFileName(path) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log nobody can write is not worth losing a fix over.
        }
    }

    private void Dispose(string path)
    {
        try
        {
            switch (Disposal)
            {
                case ScreenshotDisposal.Delete:
                    File.Delete(path);
                    break;

                case ScreenshotDisposal.Archive:
                    var archive = Path.Combine(_directory, "RatNav archive");
                    Directory.CreateDirectory(archive);
                    File.Move(path, Path.Combine(archive, Path.GetFileName(path)), overwrite: true);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game may still hold the file, or OneDrive may be uploading it. The fix is
            // already read, so failing to tidy up is not worth telling anyone about.
        }
    }

    void IDisposable.Dispose()
    {
        _watcher.Created -= OnChanged;
        _watcher.Renamed -= OnChanged;
        _watcher.Dispose();
    }
}
