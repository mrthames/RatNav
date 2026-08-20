namespace RatNav.Core.Tests;

using RatNav.Core;
using RatNav.Core.Model;
using RatNav.Core.Progress;
using RatNav.Core.Tracking;

/// <summary>
/// Keeping three characters apart.
///
/// <para>The game gives you a PvE character, a PvP one and a seasonal PvP one, sharing nothing.
/// The rule these all test is the one that is easy to get wrong and expensive when you do: a
/// profile with nothing saved yet has to read as a <i>fresh</i> character, not as whichever one
/// was loaded a moment ago.</para>
/// </summary>
public sealed class ProfileTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ratnav-profiles", Guid.NewGuid().ToString());

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Each_profile_gets_its_own_directory()
    {
        var profile = new RatNavProfile(_dir);

        var pvp = profile.DirectoryFor("pvp");
        var pve = profile.DirectoryFor("pve");

        Assert.NotEqual(pvp, pve);
    }

    [Fact]
    public void An_unknown_profile_is_refused()
    {
        var profile = new RatNavProfile(_dir);

        Assert.False(profile.Use("nonsense"));
        Assert.Equal("pvp", profile.Current);
    }

    /// <summary>
    /// The one that matters. Switching to a character with nothing saved must not show the
    /// previous character's progress — it would be wrong on screen and then written to the new
    /// profile the first time anything saved.
    /// </summary>
    [Fact]
    public void A_profile_with_nothing_saved_reads_as_a_fresh_character()
    {
        var profile = new RatNavProfile(_dir);
        var progress = new ProgressStore(profile);

        progress.SetManual("quest", QuestState.Completed);
        progress.SetPlayerLevel(42);

        Assert.True(profile.Use("pve"));
        progress.Load();

        Assert.Equal(QuestState.NotStarted, progress.StateOf("quest"));
        Assert.Null(progress.PlayerLevel);
    }

    [Fact]
    public void Switching_back_finds_the_first_character_intact()
    {
        var profile = new RatNavProfile(_dir);
        var progress = new ProgressStore(profile);

        progress.SetManual("quest", QuestState.Completed);
        progress.SetPlayerLevel(42);

        profile.Use("pve");
        progress.Load();
        profile.Use("pvp");
        progress.Load();

        Assert.Equal(QuestState.Completed, progress.StateOf("quest"));
        Assert.Equal(42, progress.PlayerLevel);
    }

    [Fact]
    public void Watchlists_are_kept_apart_too()
    {
        var profile = new RatNavProfile(_dir);
        var tracker = new ItemTracker(profile);

        tracker.SetHave("bolts", 12);

        profile.Use("pve");
        tracker.Load();

        Assert.Equal(0, tracker.GetHave("bolts"));
    }

    [Fact]
    public void A_wipe_clears_one_character_and_leaves_the_others()
    {
        var profile = new RatNavProfile(_dir);
        var progress = new ProgressStore(profile);

        progress.SetManual("quest", QuestState.Completed);

        profile.Use("pve");
        progress.Load();
        progress.SetManual("other", QuestState.Completed);

        Assert.True(profile.Wipe("pve"));
        progress.Load();
        Assert.Equal(QuestState.NotStarted, progress.StateOf("other"));

        profile.Use("pvp");
        progress.Load();
        Assert.Equal(QuestState.Completed, progress.StateOf("quest"));
    }

    /// <summary>
    /// An install from before profiles existed keeps its progress, and keeps its originals: the
    /// files are copied rather than moved, so a failure here cannot cost somebody a wipe's worth
    /// of quest state.
    /// </summary>
    [Fact]
    public void A_pre_profiles_install_is_adopted_without_losing_the_originals()
    {
        Directory.CreateDirectory(_dir);

        var loose = Path.Combine(_dir, "progress.json");
        File.WriteAllText(loose, "{}");

        new RatNavProfile(_dir).AdoptLooseFiles();

        Assert.True(File.Exists(Path.Combine(_dir, "profiles", "pvp", "progress.json")));
        Assert.True(File.Exists(loose));
    }

    [Fact]
    public void Adopting_never_overwrites_a_profile_that_already_has_files()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "progress.json"), "loose");

        var profile = new RatNavProfile(_dir);
        var already = Path.Combine(profile.DirectoryFor("pvp"), "progress.json");
        File.WriteAllText(already, "already here");

        profile.AdoptLooseFiles();

        Assert.Equal("already here", File.ReadAllText(already));
    }
}
