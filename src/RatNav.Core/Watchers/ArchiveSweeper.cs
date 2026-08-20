namespace RatNav.Core.Watchers;

/// <summary>What a sweep removed.</summary>
public sealed record SweepResult(int Removed, long BytesFreed)
{
    public static readonly SweepResult Nothing = new(0, 0);
}

/// <summary>
/// Keeps the archive of processed screenshots from eating the disk.
///
/// <para>These are full-resolution PNGs of a 4K screen: thirteen megabytes each. Three hundred of
/// them is four gigabytes, which is what one development machine had accumulated before anybody
/// looked.</para>
///
/// <para>The rule is what the file is still <i>for</i>, not how old it is. A position fix's entire
/// information content is its filename — the coordinates and the camera rotation are in the name,
/// RatNav reads them, and the pixels then carry nothing it needs. The names are kept, in a log
/// measured in kilobytes; the pixels are not. An inventory screenshot is the other way round: the
/// pixels are the data, right up until the scan has been reviewed and applied.</para>
///
/// <para>Age and size caps sit underneath as a backstop, so nothing can grow like that again even
/// if something above goes wrong.</para>
/// </summary>
public static class ArchiveSweeper
{
    /// <summary>The folder processed screenshots are moved to, inside the screenshot folder.</summary>
    public const string FolderName = "RatNav archive";

    /// <summary>
    /// Sweeps the archive.
    ///
    /// <para>Oldest first, until both caps are satisfied. Newest kept, because if anything is worth
    /// looking at it is the raid you just finished.</para>
    /// </summary>
    /// <param name="olderThan">Anything older than this goes, whatever the size cap says.</param>
    /// <param name="mostBytes">And the newest are kept only up to this much.</param>
    public static SweepResult Sweep(
        string screenshotDirectory,
        TimeSpan olderThan,
        long mostBytes,
        DateTimeOffset? now = null)
    {
        var archive = Path.Combine(screenshotDirectory, FolderName);

        if (!Directory.Exists(archive)) return SweepResult.Nothing;

        List<FileInfo> files;

        try
        {
            files = [.. new DirectoryInfo(archive)
                .EnumerateFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SweepResult.Nothing;
        }

        var cutoff = (now ?? DateTimeOffset.UtcNow) - olderThan;

        var removed = 0;
        long freed = 0;
        long kept = 0;

        foreach (var file in files)
        {
            var tooOld = file.LastWriteTimeUtc < cutoff;
            var tooMuch = kept + file.Length > mostBytes;

            if (!tooOld && !tooMuch)
            {
                kept += file.Length;
                continue;
            }

            try
            {
                var size = file.Length;

                file.Delete();

                removed++;
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open by something, or read-only. Not worth stopping the sweep over; the
                // next one will get it.
                kept += file.Length;
            }
        }

        return new SweepResult(removed, freed);
    }
}
