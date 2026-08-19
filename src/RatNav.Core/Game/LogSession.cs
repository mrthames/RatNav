namespace RatNav.Core.Game;

/// <summary>
/// Reads Escape from Tarkov's log files while the game has them open.
///
/// Two things about these logs are easy to get wrong, and both were found the hard way against
/// a live 1.1.0 client:
///
/// 1. <b>The game holds them open and Windows reports them as zero bytes.</b> An ordinary read
///    returns nothing at all — not an error, just an empty string, which looks exactly like
///    "nothing has happened yet". Opening with <see cref="FileShare.ReadWrite"/> reads the real
///    content.
/// 2. <b>The filenames changed.</b> Older clients wrote
///    <c>&lt;timestamp&gt; application.log</c>; 1.1.0 writes <c>application_000.log</c>. Matching
///    either exact name breaks on the other, so this matches on the word alone.
/// </summary>
public static class LogSession
{
    /// <summary>Finds a log file of a given kind in a session directory, across naming schemes.</summary>
    public static string? FindLog(string sessionDirectory, string kind = "application")
    {
        if (!Directory.Exists(sessionDirectory)) return null;

        try
        {
            return Directory.EnumerateFiles(sessionDirectory, "*.log")
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .Contains(kind, StringComparison.OrdinalIgnoreCase))
                // Prefer the newest part when a log has rolled over (_000, _001, ...).
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads from <paramref name="offset"/> to the end of a log the game is still writing.
    /// Returns the text read and the new offset, so a watcher can tail rather than re-read.
    /// </summary>
    public static (string Text, long Offset) ReadFrom(string path, long offset)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // A shorter file than last time means the log rolled over — start again rather than
            // seeking past the end and reading nothing forever.
            if (offset > stream.Length) offset = 0;

            stream.Seek(offset, SeekOrigin.Begin);

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            return (text, stream.Position);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game briefly locks the file exclusively while rolling it over. Returning the
            // unchanged offset means the next tick simply picks up where this one left off.
            return ("", offset);
        }
    }

    /// <summary>Reads a whole log the game may still hold open.</summary>
    public static string ReadAll(string path) => ReadFrom(path, 0).Text;
}
