using System.Text.RegularExpressions;

namespace RatNav.Core.Game;

/// <summary>One Escape from Tarkov installation found on this machine.</summary>
public sealed record GameInstall
{
    /// <summary>Install root, e.g. <c>F:\Escape From Tarkov</c>.</summary>
    public required string Directory { get; init; }

    public required string LogsDirectory { get; init; }

    /// <summary>Version from the newest log session directory, e.g. <c>1.1.0.1.46777</c>.</summary>
    public string? Version { get; init; }

    /// <summary>When this install last wrote a log. The signal that separates live from abandoned.</summary>
    public DateTimeOffset? LastPlayed { get; init; }

    public bool HasLogs => LastPlayed is not null;
}

/// <summary>
/// Finds the Tarkov install RatNav should actually watch.
///
/// The naive version of this — scan for <c>EscapeFromTarkov.exe</c>, take the first hit — is
/// wrong in a way that is silent and permanent. A real machine this was developed against had
/// two installs: a stale v0.16 from over a year earlier on <c>D:</c>, and the live 1.1.0 on
/// <c>F:</c>. First-hit ordering picks the dead one and then reads year-old logs forever,
/// reporting no raids and never explaining why.
///
/// So installs are ranked by <b>when they last wrote a log</b>, which is the only evidence that
/// actually says "this is the one being played".
/// </summary>
public static partial class GameInstallFinder
{
    /// <summary>Places Tarkov commonly lands, checked before falling back to a wider sweep.</summary>
    private static readonly string[] CommonSubPaths =
    [
        @"Battlestate Games\EFT",
        @"Battlestate Games\Escape from Tarkov",
        "Escape From Tarkov",
        "EFT",
        @"Games\Escape From Tarkov",
        @"Games\EFT",
    ];

    /// <summary>
    /// Every install found, newest-played first. The head of this list is the right default;
    /// the rest are worth showing in settings so someone with two copies can see why RatNav
    /// chose the one it did.
    /// </summary>
    public static IReadOnlyList<GameInstall> FindAll()
    {
        var found = new Dictionary<string, GameInstall>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in DriveRoots())
        {
            foreach (var sub in CommonSubPaths)
            {
                var candidate = Path.Combine(root, sub);
                if (Describe(candidate) is { } install)
                    found.TryAdd(install.Directory, install);
            }
        }

        return
        [
            .. found.Values
                .OrderByDescending(i => i.LastPlayed ?? DateTimeOffset.MinValue)
                .ThenBy(i => i.Directory, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The install RatNav should watch, or null if none was found.</summary>
    public static GameInstall? Find() => FindAll().FirstOrDefault();

    /// <summary>
    /// Describes a directory as an install, or returns null if it isn't one.
    /// Public so a manually-configured path can be validated the same way a discovered one is.
    /// </summary>
    public static GameInstall? Describe(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;

        try
        {
            if (!Directory.Exists(directory)) return null;

            var exe = Path.Combine(directory, "EscapeFromTarkov.exe");
            var logs = Path.Combine(directory, "Logs");

            // Either marker is enough on its own: an install that has never been launched has no
            // Logs, and a Logs folder left behind by a moved install still tells us where someone
            // used to play.
            if (!File.Exists(exe) && !Directory.Exists(logs)) return null;

            var newest = NewestSession(logs);

            return new GameInstall
            {
                Directory = directory,
                LogsDirectory = logs,
                Version = newest is null ? null : VersionFrom(Path.GetFileName(newest)),
                LastPlayed = newest is null
                    ? null
                    : new DateTimeOffset(Directory.GetLastWriteTimeUtc(newest), TimeSpan.Zero),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The newest <c>log_&lt;date&gt;_&lt;version&gt;</c> session directory, or null.
    ///
    /// <para>Ordered by the timestamp in the name first, and only then by the filesystem's. The
    /// name is written by the game and never changes; a write time is set by whatever last
    /// touched the folder, so copying an install or restoring a backup rewrites all of them and
    /// makes an ancient session look like tonight's. Getting this wrong reports the wrong game
    /// version, which makes RatNav refresh data it did not need to or — worse — not refresh data
    /// it did.</para>
    /// </summary>
    public static string? NewestSession(string logsDirectory)
    {
        if (!Directory.Exists(logsDirectory)) return null;

        try
        {
            return Directory.EnumerateDirectories(logsDirectory, "log_*")
                // By the date in the name first. The date part is fixed-width so it sorts
                // correctly as text; the time is not — the game writes "8-25-33", which would sort
                // after "23-01-52" — so same-day sessions fall through to the write time, where
                // that ambiguity does not exist.
                .OrderByDescending(d => DateFrom(Path.GetFileName(d)), StringComparer.Ordinal)
                .ThenByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The <c>YYYY.MM.DD</c> out of a session name, or empty when it is not one.</summary>
    private static string DateFrom(string sessionDirectoryName)
    {
        var match = SessionDate().Match(sessionDirectoryName ?? "");
        return match.Success ? match.Groups["date"].Value : "";
    }

    /// <summary>
    /// Pulls the game version out of a session directory name, e.g.
    /// <c>log_2026.08.18_19-42-49_1.1.0.1.46777</c> gives <c>1.1.0.1.46777</c>.
    /// This is what patch detection compares against, and it is more reliable than the
    /// executable's own file version — which reports 1.1.0.46777 for the same build.
    /// </summary>
    public static string? VersionFrom(string sessionDirectoryName)
    {
        var match = SessionName().Match(sessionDirectoryName ?? "");
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static IEnumerable<string> DriveRoots()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            // Network and removable drives are skipped: enumerating a disconnected share can
            // block for many seconds, and nobody installs Tarkov on a memory stick.
            if (drive.DriveType != DriveType.Fixed) continue;

            var ready = false;
            try { ready = drive.IsReady; } catch (IOException) { }

            if (ready) yield return drive.RootDirectory.FullName;
        }
    }

    /// <summary>
    /// A log session directory name.
    ///
    /// <para>The hour is <b>not</b> zero-padded — the game writes
    /// <c>log_2026.08.19_8-25-33_1.1.0.1.46777</c> before 10am and
    /// <c>log_2026.08.18_23-01-52_...</c> after. Requiring two digits meant that for ten hours a
    /// day RatNav could not read the game version out of the newest session, so patch detection
    /// quietly stopped working and Setup reported "no log sessions yet" over a folder full of
    /// them.</para>
    /// </summary>
    [GeneratedRegex(@"^log_\d{4}\.\d{2}\.\d{2}_\d{1,2}-\d{1,2}-\d{1,2}_(?<version>[\d.]+)$")]
    private static partial Regex SessionName();

    [GeneratedRegex(@"^log_(?<date>\d{4}\.\d{2}\.\d{2})_")]
    private static partial Regex SessionDate();
}
