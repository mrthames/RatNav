using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Progress;
using RatNav.Core.Tracking;

namespace RatNav.Core.Tests;

public class ItemTrackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ItemTracker Tracker() => new(_dir);
    private ProgressStore Progress() => new(_dir);

    private static ItemNeeds Watch() => new()
    {
        Item = new ItemDef { Id = "watch", Name = "Roler Submariner", ShortName = "Roler" },
        Quests =
        [
            new QuestNeed { TaskId = "active", TaskName = "Ice Cream Cones", ObjectiveId = "o1", Count = 2, FoundInRaid = true },
            new QuestNeed { TaskId = "done", TaskName = "Debut", ObjectiveId = "o2", Count = 5, FoundInRaid = false },
        ],
        Hideout =
        [
            new HideoutNeed { StationId = "workbench", StationName = "Workbench", Level = 1, Count = 3 },
            new HideoutNeed { StationId = "workbench", StationName = "Workbench", Level = 2, Count = 7 },
        ],
    };

    /// <summary>What the planner decided the hideout wants, in the shape the tracker takes.</summary>
    private static Dictionary<string, HideoutDemand> Wants(int count, int wave = 1, string upgrade = "Workbench 2") =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["watch"] = new HideoutDemand
            {
                Count = count,
                Wave = wave,
                UpgradeName = upgrade,
                StationId = "workbench",
            },
        };

    [Fact]
    public void Only_active_quests_and_upgrades_in_view_count_toward_what_you_need()
    {
        var progress = Progress();
        progress.SetManual("active", QuestState.Active);
        progress.SetManual("done", QuestState.Completed);

        var tracked = Tracker().Track(Watch(), progress, Wants(7));

        // 2 from the active quest, 7 from the upgrade the planner put in view. The finished
        // quest's 5 is no longer anyone's problem.
        Assert.Equal(2, tracked.QuestNeeded);
        Assert.Equal(7, tracked.HideoutNeeded);
        Assert.Equal(9, tracked.Needed);
    }

    [Fact]
    public void The_hideout_wants_nothing_until_the_planner_says_what()
    {
        var progress = Progress();
        progress.SetManual("active", QuestState.Active);

        // Counting every un-built level is what made the items list unusable: hundreds of items
        // for upgrades gated behind three others you have not started. With no planner supplied,
        // the hideout claims nothing rather than claiming all of it.
        var tracked = Tracker().Track(Watch(), progress);

        Assert.Equal(0, tracked.HideoutNeeded);
        Assert.Null(tracked.HideoutUpgrade);
    }

    [Fact]
    public void A_row_says_which_upgrade_wants_it_and_how_soon()
    {
        // A bare number does not tell you whether to keep something. "Medstation 3" does.
        var tracked = Tracker().Track(Watch(), Progress(), Wants(4, wave: 2, upgrade: "Medstation 3"));

        Assert.Equal("Medstation 3", tracked.HideoutUpgrade);
        Assert.Equal(2, tracked.HideoutWave);
    }

    [Fact]
    public void A_quest_you_have_not_started_does_not_make_you_hoard_for_it()
    {
        var tracked = Tracker().Track(Watch(), Progress());

        Assert.Equal(0, tracked.QuestNeeded);
        Assert.False(tracked.FoundInRaid);
    }

    [Fact]
    public void Found_in_raid_only_matters_while_the_quest_asking_for_it_is_active()
    {
        var progress = Progress();
        progress.SetManual("active", QuestState.Active);

        Assert.True(Tracker().Track(Watch(), progress).FoundInRaid);

        progress.SetManual("active", QuestState.Completed);
        Assert.False(Tracker().Track(Watch(), progress).FoundInRaid);
    }

    /// <summary>Just the quests, for tests where the hideout would only be noise.</summary>
    private static ItemNeeds QuestOnly() => Watch() with { Hideout = [] };

    [Fact]
    public void What_you_have_comes_off_what_you_still_need()
    {
        var progress = Progress();
        progress.SetManual("active", QuestState.Active);

        var tracker = Tracker();
        tracker.SetHave("watch", 1);

        var tracked = tracker.Track(QuestOnly(), progress);
        Assert.Equal(2, tracked.Needed);
        Assert.Equal(1, tracked.Remaining);
        Assert.False(tracked.Done);

        tracker.AdjustHave("watch", 1);
        Assert.True(tracker.Track(QuestOnly(), progress).Done);
    }

    [Fact]
    public void Counts_never_go_negative()
    {
        var tracker = Tracker();
        tracker.SetHave("watch", 1);

        Assert.Equal(0, tracker.AdjustHave("watch", -5));
        Assert.Equal(0, tracker.GetHave("watch"));
    }

    [Fact]
    public void Have_counts_and_the_watchlist_survive_a_restart()
    {
        // These are hand-entered over weeks of raids; losing them would be the worst bug in the app.
        var first = Tracker();
        first.SetHave("watch", 4);
        first.Watch("bolts", note: "for the workbench", target: 2);

        var second = Tracker();
        second.Load();

        Assert.Equal(4, second.GetHave("watch"));
        var entry = Assert.Single(second.Watchlist);
        Assert.Equal("bolts", entry.ItemId);
        Assert.Equal("for the workbench", entry.Note);
        Assert.Equal(2, entry.Target);
    }

    [Fact]
    public void Watching_an_item_twice_edits_it_rather_than_duplicating_it()
    {
        var tracker = Tracker();
        tracker.Watch("bolts", note: "first");
        tracker.Watch("bolts", note: "second", target: 3);

        var entry = Assert.Single(tracker.Watchlist);
        Assert.Equal("second", entry.Note);
        Assert.Equal(3, entry.Target);
    }

    [Fact]
    public void A_watchlist_target_adds_to_what_you_need()
    {
        var tracker = Tracker();
        tracker.Watch("watch", note: "spare for a barter", target: 4);

        var tracked = tracker.Track(QuestOnly(), Progress());

        Assert.True(tracked.Watched);
        Assert.Equal(4, tracked.Needed);
        Assert.Equal("spare for a barter", tracked.WatchNote);
    }

    [Fact]
    public void A_corrupt_tracking_file_does_not_stop_the_app_starting()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "tracking.json"), "{ not json at all");

        var tracker = Tracker();
        tracker.Load();

        Assert.Equal(0, tracker.GetHave("watch"));
        Assert.Empty(tracker.Watchlist);
    }
}

public class ProgressStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_hand_correction_beats_what_the_logs_said()
    {
        var store = new ProgressStore(_dir);
        store.RecordFromLogs("task", QuestState.Active);
        store.SetManual("task", QuestState.Completed);

        Assert.Equal(QuestState.Completed, store.StateOf("task"));
    }

    [Fact]
    public void Replaying_the_logs_cannot_undo_a_correction()
    {
        // The whole reason the two layers are separate. The game does not always write in-raid
        // quest changes, so corrections are unavoidable — and a later import must not revert them.
        var store = new ProgressStore(_dir);
        store.SetManual("task", QuestState.Completed);

        store.RecordFromLogs("task", QuestState.Active);
        store.RecordFromLogs("task", QuestState.NotStarted);

        Assert.Equal(QuestState.Completed, store.StateOf("task"));
    }

    [Fact]
    public void Clearing_a_correction_hands_the_quest_back_to_the_logs()
    {
        var store = new ProgressStore(_dir);
        store.RecordFromLogs("task", QuestState.Active);
        store.SetManual("task", QuestState.Failed);
        store.ClearManual("task");

        Assert.Equal(QuestState.Active, store.StateOf("task"));
    }

    [Fact]
    public void Progress_survives_a_restart()
    {
        var first = new ProgressStore(_dir);
        first.SetManual("task", QuestState.Active);
        first.SetHideoutLevel("workbench", 3);

        var second = new ProgressStore(_dir);
        second.Load();

        Assert.Equal(QuestState.Active, second.StateOf("task"));
        Assert.True(second.IsHideoutLevelBuilt("workbench", 3));
        Assert.False(second.IsHideoutLevelBuilt("workbench", 4));
    }

    [Fact]
    public void A_station_built_to_a_level_counts_every_level_below_it()
    {
        var store = new ProgressStore(_dir);
        store.SetHideoutLevel("workbench", 3);

        Assert.True(store.IsHideoutLevelBuilt("workbench", 1));
        Assert.True(store.IsHideoutLevelBuilt("workbench", 3));
        Assert.False(store.IsHideoutLevelBuilt("workbench", 4));
    }

    [Fact]
    public void Available_quests_are_those_whose_prerequisites_are_done()
    {
        TaskDef[] tasks =
        [
            new() { Id = "first", Name = "Debut" },
            new() { Id = "second", Name = "Checking", PrerequisiteTaskIds = ["first"] },
            new() { Id = "third", Name = "Shootout", PrerequisiteTaskIds = ["second"] },
        ];

        var store = new ProgressStore(_dir);
        store.SetManual("first", QuestState.Completed);

        var available = store.AvailableNow(tasks).Select(t => t.Id).ToArray();

        Assert.Equal(["second"], available);
    }

    [Fact]
    public void Summary_counts_every_quest_exactly_once()
    {
        TaskDef[] tasks =
        [
            new() { Id = "a", Name = "A" }, new() { Id = "b", Name = "B" }, new() { Id = "c", Name = "C" },
        ];

        var store = new ProgressStore(_dir);
        store.SetManual("a", QuestState.Active);
        store.SetManual("b", QuestState.Completed);

        var summary = store.Summarize(tasks);

        Assert.Equal(1, summary[QuestState.Active]);
        Assert.Equal(1, summary[QuestState.Completed]);
        Assert.Equal(1, summary[QuestState.NotStarted]);
        Assert.Equal(tasks.Length, summary.Values.Sum());
    }
}
