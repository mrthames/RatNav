using RatNav.Core.Game;

namespace RatNav.Core.Tests;

/// <summary>
/// Finding an install that is not where the installer put it.
///
/// <para>Detection used to probe a fixed list of paths under each drive root and nothing else, so
/// it only ever found an install nobody had moved. The first user test hit exactly that: the game
/// was found by hand with Browse — an ordinary enough folder — while RatNav reported "game not
/// found" and left Quests and Hideout empty with nothing saying why.</para>
///
/// <para>So the usual library folders are swept one level deep. One level, deliberately: a full
/// disk walk finds everything and takes a minute doing it, on a path that runs at start-up.</para>
/// </summary>
public sealed class GameInstallSweepTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ratnav-install", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    // Where a games library usually sits, and the drive root itself.
    [InlineData(@"Games\Escape From Tarkov")]
    [InlineData(@"Games\Battlestate Games\EFT")]
    [InlineData(@"Program Files\Escape From Tarkov")]
    [InlineData(@"Program Files (x86)\Tarkov")]
    [InlineData(@"SteamLibrary\EscapeFromTarkov")]
    [InlineData(@"Tarkov-live")]
    public void An_install_in_a_library_folder_is_found(string where)
    {
        Install(where);

        var install = Assert.Single(GameInstallFinder.FindUnder(_root));

        Assert.Equal(Path.Combine(_root, where), install.Directory);
    }

    [Fact]
    public void The_places_an_installer_puts_it_still_work()
    {
        Install(@"Battlestate Games\EFT");

        Assert.Single(GameInstallFinder.FindUnder(_root));
    }

    [Fact]
    public void A_folder_that_merely_reads_like_tarkov_is_not_an_install()
    {
        // Named right, but holding neither the executable nor a Logs folder.
        Directory.CreateDirectory(Path.Combine(_root, "Games", "Tarkov Notes"));

        Assert.Empty(GameInstallFinder.FindUnder(_root));
    }

    [Fact]
    public void Games_that_merely_contain_the_letters_are_left_alone()
    {
        // "eft" is matched as a whole word, so these are never even opened. If they were, both
        // would still fail the executable check — this is about not paying to look.
        foreach (var name in new[] { "Left 4 Dead", "Drefting", "Theft Auto" })
        {
            Directory.CreateDirectory(Path.Combine(_root, "Games", name));
        }

        Assert.Empty(GameInstallFinder.FindUnder(_root));
    }

    [Fact]
    public void The_sweep_does_not_go_deeper_than_one_level()
    {
        // Two levels down inside a library folder. Browse is the answer for something this
        // unusual; a disk walk at start-up is not.
        Install(@"Games\Launchers\Battlestate\Escape From Tarkov");

        Assert.Empty(GameInstallFinder.FindUnder(_root));
    }

    /// <summary>A directory that looks like an install: it has the executable.</summary>
    private void Install(string relative)
    {
        var directory = Path.Combine(_root, relative);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "EscapeFromTarkov.exe"), "");
    }
}
