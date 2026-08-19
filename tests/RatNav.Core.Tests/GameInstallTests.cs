using RatNav.Core.Game;

namespace RatNav.Core.Tests;

/// <summary>
/// Validating a folder someone typed in.
///
/// <para>This is the first thing a new install depends on, and the failure is nasty because it is
/// silent: a wrong folder produces an overlay that never reports a raid, which looks exactly like
/// RatNav being broken. So a folder that is not an install has to be refused at the point it is
/// entered, while a real one has to be accepted even in the states a real one turns up in.</para>
/// </summary>
public class GameInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public GameInstallTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void A_folder_that_is_not_an_install_is_refused()
    {
        // Accepting this quietly is what turns "wrong folder" into "RatNav is broken".
        Assert.Null(GameInstallFinder.Describe(Folder("Documents")));
    }

    [Fact]
    public void A_missing_folder_is_refused_rather_than_throwing()
    {
        Assert.Null(GameInstallFinder.Describe(Path.Combine(_root, "gone")));
    }

    [Fact]
    public void Nothing_typed_is_refused()
    {
        Assert.Null(GameInstallFinder.Describe(""));
        Assert.Null(GameInstallFinder.Describe("   "));
    }

    [Fact]
    public void An_install_that_has_never_been_launched_is_still_an_install()
    {
        // No Logs folder yet, because nobody has played. Refusing it would mean a fresh install
        // cannot be configured until after the first raid.
        var install = Folder("EFT-fresh");
        File.WriteAllText(Path.Combine(install, "EscapeFromTarkov.exe"), "");

        var described = GameInstallFinder.Describe(install);

        Assert.NotNull(described);
        Assert.Null(described.Version);
        Assert.False(described.HasLogs);
    }

    [Fact]
    public void Logs_alone_are_enough_to_recognise_a_folder()
    {
        // The executable can be absent — a moved or partially uninstalled copy — and the logs
        // still say where someone played.
        var install = Folder("EFT-moved");
        Directory.CreateDirectory(Path.Combine(install, "Logs"));

        Assert.NotNull(GameInstallFinder.Describe(install));
    }

    [Fact]
    public void The_hour_in_a_session_name_is_not_zero_padded()
    {
        // The game writes "8-25-33" before 10am and "23-01-52" after. Requiring two digits meant
        // that for ten hours a day the version could not be read out of the newest session, so
        // patch detection quietly stopped and Setup said "no log sessions yet" over a folder
        // full of them.
        var install = Folder("EFT-morning");
        Directory.CreateDirectory(Path.Combine(install, "Logs", "log_2026.08.19_8-25-33_1.1.0.1.46777"));

        Assert.Equal("1.1.0.1.46777", GameInstallFinder.Describe(install)!.Version);
    }

    [Fact]
    public void A_morning_session_still_beats_the_previous_evening()
    {
        // Sorting session names as text puts "8-25-33" after "23-01-52", so the time cannot be
        // compared that way — only the date can, and same-day ties fall through to write times.
        var install = Folder("EFT-overnight");
        var logs = Path.Combine(install, "Logs");

        Directory.CreateDirectory(Path.Combine(logs, "log_2026.08.18_23-01-52_1.0.0.0.11111"));
        Directory.CreateDirectory(Path.Combine(logs, "log_2026.08.19_8-25-33_1.1.0.1.46777"));

        Assert.Equal("1.1.0.1.46777", GameInstallFinder.Describe(install)!.Version);
    }

    [Fact]
    public void The_version_comes_from_the_newest_log_session()
    {
        var install = Folder("EFT-played");
        var logs = Path.Combine(install, "Logs");

        Directory.CreateDirectory(Path.Combine(logs, "log_2026.01.02_10-00-00_0.16.0.1.11111"));
        Directory.CreateDirectory(Path.Combine(logs, "log_2026.08.18_23-01-52_1.1.0.1.46777"));

        var described = GameInstallFinder.Describe(install);

        // Which version is running decides whether cached game data is stale, so the newest
        // session wins rather than whichever the filesystem happened to list first.
        Assert.Equal("1.1.0.1.46777", described!.Version);
        Assert.True(described.HasLogs);
    }
}
