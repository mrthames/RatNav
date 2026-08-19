using RatNav.Core.Game;
using RatNav.Core.Progress;
using RatNav.Core.Watchers;

namespace RatNav.Core.Tests;

public class ScreenshotWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public ScreenshotWatcherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Write(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not really a png");
        return path;
    }

    private const string InRaid =
        "2026-08-18[20-11]_-14.44, 1.44, -139.32_-0.02523, 0.05025, -0.00127, -0.99842_13.31 (0).png";

    [Fact]
    public void The_newest_existing_screenshot_can_be_read_without_waiting_for_a_new_one()
    {
        // The first fix of a raid is usually taken before anyone thinks to start the app.
        Write(InRaid);

        using var watcher = new ScreenshotWatcher(_dir) { Disposal = ScreenshotDisposal.Keep };
        var fix = watcher.ReadLatestExisting();

        Assert.NotNull(fix);
        Assert.Equal(-14.44, fix.Position.X, 2);
        Assert.Equal(-139.32, fix.Position.Z, 2);
    }

    [Fact]
    public void A_screenshot_with_no_coordinates_is_ignored_and_left_alone()
    {
        // Menu and hideout screenshots carry no position. Not an error, and not ours to delete.
        var path = Write("2026-08-18[20-11]_menu screenshot.png");

        using var watcher = new ScreenshotWatcher(_dir) { Disposal = ScreenshotDisposal.Delete };

        Assert.Null(watcher.ReadLatestExisting());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_processed_screenshot_is_archived_rather_than_left_to_pile_up()
    {
        // A folder with thousands of multi-megabyte PNGs is the real cause of the slowdown people
        // blame on screenshot tracking.
        var path = Write(InRaid);

        using var watcher = new ScreenshotWatcher(_dir) { Disposal = ScreenshotDisposal.Archive };
        watcher.ReadLatestExisting();

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "RatNav archive")));
    }

    [Fact]
    public void Deleting_is_available_for_players_who_do_not_want_the_images_at_all()
    {
        var path = Write(InRaid);

        using var watcher = new ScreenshotWatcher(_dir) { Disposal = ScreenshotDisposal.Delete };
        watcher.ReadLatestExisting();

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(_dir, "RatNav archive")));
    }

    [Fact]
    public void The_same_screenshot_is_never_read_twice()
    {
        // The watcher fires on create and again on write; a duplicate fix would re-run
        // housekeeping on a file that is already gone.
        Write(InRaid);

        using var watcher = new ScreenshotWatcher(_dir) { Disposal = ScreenshotDisposal.Keep };

        var fixes = 0;
        watcher.PositionFixed += (_, _) => fixes++;

        watcher.ReadLatestExisting();
        watcher.ReadLatestExisting();

        Assert.Equal(1, fixes);
    }

    [Fact]
    public void A_missing_screenshot_folder_is_created_rather_than_fatal()
    {
        // It does not exist until the player's first in-game screenshot, and a watcher cannot be
        // pointed at a directory that is not there.
        var missing = Path.Combine(_dir, "not', yet");

        using var watcher = new ScreenshotWatcher(missing);

        Assert.True(Directory.Exists(missing));
    }
}

public class LogWatcherTests : IDisposable
{
    private readonly string _install = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    private readonly string _session;

    public LogWatcherTests()
    {
        _session = Path.Combine(_install, "Logs", "log_2026.08.18_19-42-49_1.1.0.1.46777");
        Directory.CreateDirectory(_session);
    }

    public void Dispose()
    {
        if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Append(string kind, string text)
    {
        // The 1.1.0 naming, which is not what older clients used.
        var path = Path.Combine(_session, $"2026.08.18_19-42-49_1.1.0.1.46777 {kind}_000.log");
        File.AppendAllText(path, text + "\n");
    }

    [Fact]
    public void A_raid_start_reports_the_map_the_game_loaded()
    {
        Append("application",
            "2026-08-18 19:59:44.088|1.1.0.1.46777|Info|application|" +
            "[Transit] Flag:None, RaidId:6a851c1b0af206edae04813b, Count:0, Locations:bigmap ->");

        using var watcher = new LogWatcher(_install);
        RaidStarted? started = null;
        watcher.RaidStarted += (_, e) => started = e;

        watcher.Poll();

        Assert.NotNull(started);
        Assert.Equal("bigmap", started.LocationId);
        Assert.Equal("bigmap", watcher.CurrentLocationId);
    }

    [Fact]
    public void Starting_mid_raid_still_knows_which_map_you_are_in()
    {
        // The normal case for anyone who forgets to launch RatNav first. The raid start is
        // already in the log by the time we look.
        Append("application", "Locations:bigmap ->");
        Append("application", "some other line");

        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.RaidStarted += (_, e) => seen.Add(e.LocationId);

        watcher.Poll();

        Assert.Equal(["bigmap"], seen);
        Assert.Equal("bigmap", watcher.CurrentLocationId);
    }

    [Fact]
    public void Only_the_most_recent_raid_in_the_log_counts()
    {
        // Earlier entries are raids already finished. Replaying them all would end on whichever
        // happened to be last in the file and wipe the fix the player just took.
        Append("application", "Locations:factory4_day ->");
        Append("application", "Locations:bigmap ->");
        Append("application", "Locations:TarkovStreets ->");

        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.RaidStarted += (_, e) => seen.Add(e.LocationId);

        watcher.Poll();
        watcher.Poll();

        Assert.Equal(["TarkovStreets"], seen);
    }

    [Fact]
    public void Going_back_to_the_menu_ends_the_raid()
    {
        Append("application", "Locations:bigmap ->");

        using var watcher = new LogWatcher(_install);
        RaidEnded? ended = null;
        watcher.RaidEnded += (_, e) => ended = e;

        watcher.Poll();

        // The game writes no "raid over" line. What it writes is that it is re-preparing your
        // profile, which is what happens on the way back to the menu however the raid finished.
        Append("application",
            "2026-08-19 00:01:52.024|1.1.0.1.46777|Info|application|" +
            "PrepareSelectedProfileLocally ProfileId:6a73aed57233ed3aa206e778 AccountId:633603");

        watcher.Poll();

        Assert.NotNull(ended);
        Assert.Equal("bigmap", ended.LocationId);
        Assert.Null(watcher.CurrentLocationId);
    }

    [Fact]
    public void Launching_the_game_does_not_end_a_raid_that_never_started()
    {
        // The same line appears at launch, before any raid. Treating it as an ending there would
        // fire a raid-over for a raid that never happened.
        Append("application", "PrepareSelectedProfileLocally ProfileId:abc AccountId:1");

        using var watcher = new LogWatcher(_install);
        var ended = 0;
        watcher.RaidEnded += (_, _) => ended++;

        watcher.Poll();
        Append("application", "CompleteSelectedProfile ProfileId:abc AccountId:1");
        watcher.Poll();

        Assert.Equal(0, ended);
    }

    [Fact]
    public void A_raid_that_already_finished_is_not_resumed_on_startup()
    {
        // Launching RatNav after playing should not drop you into the raid you left. The map
        // load is in the log, but so is the trip back to the menu that followed it.
        Append("application", "Locations:bigmap ->");
        Append("application", "PrepareSelectedProfileLocally ProfileId:abc AccountId:1");

        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.RaidStarted += (_, e) => seen.Add(e.LocationId);

        watcher.Poll();

        Assert.Empty(seen);
        Assert.Null(watcher.CurrentLocationId);
    }

    [Fact]
    public void Quests_already_in_the_log_are_not_re_announced()
    {
        // A quest accepted an hour ago is not news, and re-announcing it on every launch would
        // undo any correction made since.
        Append("notifications",
            """{"type":"new_message","message":{"type":10,"templateId":"old-task description"}}""");

        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.QuestChanged += (_, e) => seen.Add(e.TaskId);

        watcher.Poll();

        Assert.Empty(seen);
    }

    [Fact]
    public void The_game_version_is_read_from_the_session_folder()
    {
        using var watcher = new LogWatcher(_install);
        watcher.Poll();

        // This is what patch detection compares against.
        Assert.Equal("1.1.0.1.46777", watcher.GameVersion);
    }

    [Theory]
    [InlineData(10, QuestState.Active)]
    [InlineData(11, QuestState.Failed)]
    [InlineData(12, QuestState.Completed)]
    public void Quest_changes_are_read_from_the_notifications_log(int messageType, QuestState expected)
    {
        // Not application.log — quest state arrives as a chat notification, and the task id is
        // the first word of the message template.
        using var watcher = new LogWatcher(_install);
        QuestEvent? change = null;
        watcher.QuestChanged += (_, e) => change = e;

        watcher.Poll();   // catch up on what is already there

        Append("notifications",
            $$$"""{"type":"new_message","message":{"type":{{{messageType}}},"templateId":"5967733e86f774602332fc84 successMessageText"}}""");

        watcher.Poll();

        Assert.NotNull(change);
        Assert.Equal("5967733e86f774602332fc84", change.TaskId);
        Assert.Equal(expected, change.State);
    }

    [Fact]
    public void Other_notifications_sharing_the_stream_are_ignored()
    {
        // Flea sales, player chat and group invites all arrive the same way.
        using var watcher = new LogWatcher(_install);
        var changes = 0;
        watcher.QuestChanged += (_, _) => changes++;

        watcher.Poll();

        Append("notifications", """{"type":"new_message","message":{"type":1,"templateId":"someone said hello"}}""");
        Append("notifications", """{"type":"new_message","message":{"type":4,"templateId":"5bdabfb886f7743e152e867e 0"}}""");

        watcher.Poll();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Only_new_lines_are_reported_on_a_second_look()
    {
        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.QuestChanged += (_, e) => seen.Add(e.TaskId);

        watcher.Poll();

        Append("notifications",
            """{"type":"new_message","message":{"type":10,"templateId":"task-one successMessageText"}}""");

        watcher.Poll();
        watcher.Poll();

        Append("notifications",
            """{"type":"new_message","message":{"type":12,"templateId":"task-two successMessageText"}}""");
        watcher.Poll();

        Assert.Equal(["task-one", "task-two"], seen);
    }

    [Fact]
    public void The_real_pretty_printed_format_the_game_writes_is_parsed()
    {
        // Copied verbatim from a live client's notifications log. The game does not write one
        // JSON object per line — it pretty-prints across a dozen — and a line-at-a-time parser
        // reads this file forever and finds nothing, which looks exactly like "the game doesn't
        // log quests".
        using var catchUp = new LogWatcher(_install);
        catchUp.Poll();

        Append("notifications", """
            2025-01-15 07:57:15.224 -08:00|Info|push-notifications|Got notification | ChatMessageReceived
            {
              "type": "new_message",
              "eventId": "6787dadc2e5977903a06968b",
              "dialogId": "54cb50c76803fa8b248b4571",
              "message": {
                "_id": "6787dadc3653a8f8450c31f8",
                "uid": "54cb50c76803fa8b248b4571",
                "type": 10,
                "dt": 1736956636,
                "text": "",
                "templateId": "657315df034d76585f032e01 description",
                "hasRewards": false,
                "maxStorageTime": 604800
              }
            }
            2025-01-15 07:57:15.224 -08:00|Info|push-notifications|NotificationManager.ProcessMessage
            """);

        var watcher = catchUp;
        QuestEvent? change = null;
        watcher.QuestChanged += (_, e) => change = e;

        watcher.Poll();

        Assert.NotNull(change);
        Assert.Equal("657315df034d76585f032e01", change.TaskId);
        Assert.Equal(QuestState.Active, change.State);
    }

    [Fact]
    public void An_object_still_being_written_is_finished_on_the_next_poll()
    {
        // The game is writing while we read, so catching an object mid-print is normal rather
        // than exceptional.
        using var watcher = new LogWatcher(_install);
        var seen = new List<string>();
        watcher.QuestChanged += (_, e) => seen.Add(e.TaskId);

        watcher.Poll();

        Append("notifications", """
            {
              "type": "new_message",
              "message": {
                "type": 10,
            """);

        watcher.Poll();
        Assert.Empty(seen);

        Append("notifications", """
                "templateId": "task-later description"
              }
            }
            """);

        watcher.Poll();
        Assert.Equal(["task-later"], seen);
    }

    [Fact]
    public void A_half_written_line_is_skipped_rather_than_fatal()
    {
        // The game is writing while we read, so a truncated final line is normal.
        using var watcher = new LogWatcher(_install);
        watcher.Poll();

        Append("notifications", """{"type":"new_message","message":{"type":10,"templ""");

        watcher.Poll();   // must not throw
    }

    [Fact]
    public void An_install_with_no_logs_is_simply_unavailable()
    {
        var empty = Path.Combine(_install, "nothing-here");
        using var watcher = new LogWatcher(empty);

        watcher.Poll();
        Assert.Null(watcher.CurrentLocationId);
    }

    [Fact]
    public void A_session_directory_name_yields_its_version()
    {
        Assert.Equal("1.1.0.1.46777",
            GameInstallFinder.VersionFrom("log_2026.08.18_19-42-49_1.1.0.1.46777"));
        Assert.Null(GameInstallFinder.VersionFrom("not-a-session"));
    }
}
