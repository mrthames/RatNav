namespace RatNav.Core.Tests;

using System.Text.Json;
using RatNav.Service;

/// <summary>
/// Moving an existing settings file onto new defaults without trampling anybody's own choices.
///
/// <para>This is the part of settings that is easy to get wrong and expensive when you do. A
/// migration that fires every launch means a key you deliberately rebound goes back to where the
/// shipped defaults put it, every time, and there is nothing you can do about it. A migration
/// that never fires means everyone who installed before today keeps an arrangement the
/// documentation no longer describes.</para>
///
/// <para>So the rule under test is: once, on files that are still on the old shipped pair, and
/// then never again.</para>
/// </summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ratnav-settings", Guid.NewGuid().ToString());

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_fresh_install_gets_the_new_pairing()
    {
        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleMode);
        Assert.Equal("F7", settings.Hotkeys.ToggleInteract);
    }

    [Fact]
    public void A_file_on_the_old_pair_is_swapped()
    {
        WriteHotkeys(new { toggleOverlay = "F5", toggleInteract = "F6", toggleMode = "F7" });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleMode);
        Assert.Equal("F7", settings.Hotkeys.ToggleInteract);

        // Untouched keys stay where they were.
        Assert.Equal("F5", settings.Hotkeys.ToggleOverlay);
    }

    [Fact]
    public void The_swap_is_written_back_so_it_only_happens_once()
    {
        WriteHotkeys(new { toggleInteract = "F6", toggleMode = "F7" });

        RatNavSettings.Load(_dir);

        // Having been through it, the file says so — and now honours a deliberate move back.
        WriteHotkeys(new { toggleInteract = "F6", toggleMode = "F7" }, revision: 1);

        var again = RatNavSettings.Load(_dir);

        Assert.Equal("F6", again.Hotkeys.ToggleInteract);
        Assert.Equal("F7", again.Hotkeys.ToggleMode);
    }

    [Fact]
    public void A_half_match_is_somebody_elses_arrangement_and_is_left_alone()
    {
        // F6 for the controls, but the mode key moved by hand. That is a choice, not the pair
        // RatNav shipped, so nothing here is ours to rearrange.
        WriteHotkeys(new { toggleInteract = "F6", toggleMode = "F12" });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F12", settings.Hotkeys.ToggleMode);
    }

    [Fact]
    public void The_older_renumbering_still_runs_before_the_swap()
    {
        // The oldest arrangement: mode stranded at F9, and the controls still on F6. It has to
        // come down to F7 first and then swap, landing on the same place as everyone else.
        WriteHotkeys(new { toggleInteract = "F6", toggleMode = "F9", identifyItem = "F10", readExtracts = "F11" });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleMode);
        Assert.Equal("F7", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F8", settings.Hotkeys.IdentifyItem);
        Assert.Equal("F9", settings.Hotkeys.ReadExtracts);
    }

    private void WriteHotkeys(object hotkeys, int? revision = null)
    {
        Directory.CreateDirectory(_dir);

        var document = revision is { } r
            ? (object)new { revision = r, hotkeys }
            : new { hotkeys };

        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
