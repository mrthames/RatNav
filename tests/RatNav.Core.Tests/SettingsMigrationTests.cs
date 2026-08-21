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

        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F7", settings.Hotkeys.ToggleMode);
    }

    [Fact]
    public void A_file_from_before_round_one_lands_where_it_started()
    {
        WriteHotkeys(new { toggleOverlay = "F5", toggleInteract = "F6", toggleMode = "F7" });

        var settings = RatNavSettings.Load(_dir);

        // Round 1 moved this pair one way and round 3 moved it back, so the oldest files end up
        // on the arrangement they already had. That is the right answer rather than a coincidence:
        // round 3 exists because round 1 was wrong, and a file that never moved was never wrong.
        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F7", settings.Hotkeys.ToggleMode);

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

    /// <summary>
    /// A later migration must not drag an earlier one along with it.
    ///
    /// <para>Gated on the current round rather than on their own, adding a second migration
    /// re-runs the first — so a key deliberately bound back after round 1 is taken away again the
    /// day round 2 ships. That is the stickiness the revision stamp exists to prevent, arriving by
    /// the back door.</para>
    /// </summary>
    [Fact]
    public void A_later_migration_does_not_re_run_an_earlier_one()
    {
        // Through round 1, and deliberately back on the old pair afterwards.
        WriteSettings(new
        {
            revision = 1,
            hotkeys = new { toggleInteract = "F6", toggleMode = "F7" },
        });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F7", settings.Hotkeys.ToggleMode);

        // Round 2 still ran.
        Assert.False(settings.Overlay.ShowControls);
        Assert.Equal(6, settings.Revision);
    }

    /// <summary>The map settings stack shipped open and should not have; files on it come along.</summary>
    [Fact]
    public void The_controls_stack_is_folded_on_a_file_that_never_chose_it()
    {
        WriteSettings(new { overlay = new { showControls = true } });

        Assert.False(RatNavSettings.Load(_dir).Overlay.ShowControls);
    }

    /// <summary>And having been folded once, opening it is a choice that sticks.</summary>
    [Fact]
    public void An_opened_controls_stack_stays_open()
    {
        WriteSettings(new { revision = 2, overlay = new { showControls = true } });

        Assert.True(RatNavSettings.Load(_dir).Overlay.ShowControls);
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

        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F7", settings.Hotkeys.ToggleMode);
        Assert.Equal("F8", settings.Hotkeys.IdentifyItem);
        Assert.Equal("F9", settings.Hotkeys.ReadExtracts);
    }

    /// <summary>
    /// Round 3: the pair round 1 shipped is moved back, putting edit mode beside show/hide.
    ///
    /// <para>Four hours of watching somebody use it settled this — he reached for edit mode far
    /// more than for the view and called it edit mode unprompted throughout.</para>
    /// </summary>
    [Fact]
    public void A_file_on_the_round_two_pair_is_moved_to_edit_mode_beside_show_hide()
    {
        WriteSettings(new
        {
            revision = 2,
            hotkeys = new { toggleMode = "F6", toggleInteract = "F7" },
        });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleInteract);
        Assert.Equal("F7", settings.Hotkeys.ToggleMode);
    }

    /// <summary>
    /// Round 4: F5 to F11 run in the order the keys are used.
    ///
    /// <para>They had accumulated — identify and extracts took F8 and F9 because those were next,
    /// and the two that move the map arrived later and took what was left — so the keys that read
    /// the screen sat in the middle of the run.</para>
    /// </summary>
    [Fact]
    public void The_f_keys_are_laid_out_in_the_order_they_are_used()
    {
        WriteSettings(new
        {
            revision = 3,
            hotkeys = new
            {
                toggleOverlay = "F5",
                toggleInteract = "F6",
                toggleMode = "F7",
                identifyItem = "F8",
                readExtracts = "F9",
                centerMap = "F10",
                toggleFollow = "F11",
            },
        });

        var keys = RatNavSettings.Load(_dir).Hotkeys;

        Assert.Equal("F5", keys.ToggleOverlay);
        Assert.Equal("F6", keys.ToggleInteract);
        Assert.Equal("F7", keys.ToggleMode);
        Assert.Equal("F8", keys.ToggleFollow);
        Assert.Equal("F9", keys.CenterMap);
        Assert.Equal("F10", keys.ReadExtracts);
        Assert.Equal("F11", keys.IdentifyItem);
    }

    /// <summary>
    /// Round 5: the look-ahead counts from zero.
    ///
    /// <para>It stored a wave count where one meant "only what is buildable now", which put a 1 on
    /// a dial above a sentence saying it was looking at nothing ahead. Every existing file is one
    /// higher than it should be on the new scale.</para>
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    public void The_look_ahead_moves_onto_a_scale_that_starts_at_zero(int before, int after)
    {
        WriteSettings(new { revision = 4, hideoutLookAhead = before });

        Assert.Equal(after, RatNavSettings.Load(_dir).HideoutLookAhead);
    }

    /// <summary>
    /// Round 6: a file still on the old shipped sizes comes onto the tuned ones.
    ///
    /// <para>The defaults were wrong — 3.0 markers on a 1440p screen were called "crazy big" by
    /// the first person to see them — which is the whole reason they were measured again. A file
    /// that never chose them has nothing to lose by moving.</para>
    /// </summary>
    [Fact]
    public void A_file_on_the_old_shipped_sizes_reads_one_afterwards()
    {
        WriteSettings(new
        {
            revision = 5,
            overlay = new { markerScale = 3.0, textScale = 2.0, placeNameScale = 2.0, playerScale = 3.0 },
        });

        var o = RatNavSettings.Load(_dir).Overlay;

        Assert.Equal(1.0, o.MarkerScale);
        Assert.Equal(1.0, o.TextScale);
        Assert.Equal(1.0, o.PlaceNameScale);
        Assert.Equal(1.0, o.PlayerScale);
    }

    /// <summary>
    /// And a size somebody chose keeps drawing at that size.
    ///
    /// <para>0.75 was the tuned base, so a marker set to 1.5 by hand is twice it and stays twice
    /// it. The number in the file changes; nothing on screen does.</para>
    /// </summary>
    [Fact]
    public void A_size_chosen_by_hand_still_draws_the_same()
    {
        WriteSettings(new { revision = 5, overlay = new { markerScale = 1.5, playerScale = 5.0 } });

        var o = RatNavSettings.Load(_dir).Overlay;

        Assert.Equal(2.0, o.MarkerScale);
        Assert.Equal(2.0, o.PlayerScale);
    }

    /// <summary>A UI scale never set is never chosen, so it comes onto the default.</summary>
    [Fact]
    public void An_unset_ui_scale_stays_unset()
    {
        WriteSettings(new { revision = 5, overlay = new { markerScale = 3.0 } });

        Assert.Null(RatNavSettings.Load(_dir).Overlay.UiScale);
    }

    /// <summary>And a fresh install reads 1.0 on every dial, which is the point of the exercise.</summary>
    [Fact]
    public void A_fresh_install_reads_one_everywhere()
    {
        var o = RatNavSettings.Load(_dir).Overlay;

        Assert.Equal(1.0, o.MarkerScale);
        Assert.Equal(1.0, o.TextScale);
        Assert.Equal(1.0, o.PlaceNameScale);
        Assert.Equal(1.0, o.PlayerScale);
        Assert.Null(o.UiScale);
    }

    /// <summary>And having moved, a zero somebody chose stays zero rather than going negative.</summary>
    [Fact]
    public void A_look_ahead_already_on_the_new_scale_is_left_alone()
    {
        WriteSettings(new { revision = 5, hideoutLookAhead = 0 });

        Assert.Equal(0, RatNavSettings.Load(_dir).HideoutLookAhead);
    }

    /// <summary>One key moved by hand makes the whole set somebody's own arrangement.</summary>
    [Fact]
    public void A_set_with_one_key_moved_by_hand_is_left_alone()
    {
        WriteSettings(new
        {
            revision = 3,
            hotkeys = new
            {
                toggleOverlay = "F5",
                toggleInteract = "F6",
                toggleMode = "F7",
                identifyItem = "F8",
                readExtracts = "F9",
                centerMap = "F10",
                toggleFollow = "F4",
            },
        });

        var keys = RatNavSettings.Load(_dir).Hotkeys;

        Assert.Equal("F4", keys.ToggleFollow);
        Assert.Equal("F8", keys.IdentifyItem);
        Assert.Equal("F10", keys.CenterMap);
    }

    /// <summary>And having been through round 3, choosing the other way round sticks.</summary>
    [Fact]
    public void The_round_two_pair_chosen_deliberately_afterwards_is_kept()
    {
        WriteSettings(new
        {
            revision = 3,
            hotkeys = new { toggleMode = "F6", toggleInteract = "F7" },
        });

        var settings = RatNavSettings.Load(_dir);

        Assert.Equal("F6", settings.Hotkeys.ToggleMode);
        Assert.Equal("F7", settings.Hotkeys.ToggleInteract);
    }

    private void WriteHotkeys(object hotkeys, int? revision = null) =>
        WriteSettings(revision is { } r ? new { revision = r, hotkeys } : (object)new { hotkeys });

    private void WriteSettings(object document)
    {
        Directory.CreateDirectory(_dir);

        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
