namespace RatNav.Core;

/// <summary>
/// Where RatNav keeps its files.
///
/// Nothing here is hardcoded to one machine — this is an open-source tool, and someone else's
/// install has to work on first run. Every path is either derived from a well-known Windows
/// folder or overridden in settings.
/// </summary>
public static class RatNavPaths
{
    /// <summary>
    /// Per-user data: cached game data, downloaded map images, plans, tracking, settings.
    /// Lives under LocalAppData rather than next to the executable so it survives an upgrade
    /// and does not need write access to Program Files.
    /// </summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("RATNAV_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RatNav");

    /// <summary>
    /// The default screenshot folder Escape from Tarkov writes to. Users who have moved their
    /// Documents folder (OneDrive does this) override it in settings.
    /// </summary>
    public static string DefaultScreenshotDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Escape from Tarkov",
            "Screenshots");

    public static string EnsureDataDirectory()
    {
        var path = DataDirectory;
        Directory.CreateDirectory(path);
        return path;
    }
}
