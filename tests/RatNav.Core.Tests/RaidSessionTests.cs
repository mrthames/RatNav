using System.Net;
using System.Text.Json;
using RatNav.Core.Data;
using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Progress;
using RatNav.Service;

namespace RatNav.Core.Tests;

using RatNav.Core;

/// <summary>
/// The startup race, which cost a real raid before it was found. RatNav's watchers begin the
/// instant the app opens, but the game data they are matched against is downloaded — so when the
/// app is started while a raid is already running, the raid start arrives first and there is
/// nothing yet to resolve "TarkovStreets" against.
///
/// The session used to resolve the map once, at that moment, and cache the null. Every later
/// request then reported "not in raid" for the rest of the session, no matter how many position
/// fixes the player took, because the data arriving a second later changed nothing.
/// </summary>
public class RaidSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static GameData Streets() => new()
    {
        FetchedAt = DateTimeOffset.UtcNow,
        GameVersion = "1.0.0",
        Maps =
        [
            new MapDef
            {
                Id = "streets",
                Name = "Streets of Tarkov",
                LogAliases = ["TarkovStreets"],
                Image = new MapImage
                {
                    SourceUrl = "https://example.invalid/streets.svg",
                    CoordinateRotation = 0,
                    Bounds = [[-400, -400], [400, 400]],
                    PixelWidth = 1000,
                    PixelHeight = 1000,
                },
            },
        ],
    };

    /// <summary>A cache that will serve <paramref name="data"/> once told to, and never touch the network.</summary>
    private GameDataCache CacheFor(GameData data)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, $"gamedata-{data.GameVersion}.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var http = new HttpClient(new UnreachableHandler());
        return new GameDataCache(new TarkovDevClient(http), new MapAssets(http, _dir), _dir);
    }

    [Fact]
    public async Task Finds_the_map_when_the_raid_starts_before_the_data_loads()
    {
        var cache = CacheFor(Streets());
        var session = new RaidSession(new RatNavState(cache), new ProgressStore(new RatNavProfile(_dir)));

        // The order that actually happens on a mid-raid start: log first, data second.
        session.OnRaidStarted("TarkovStreets");
        Assert.False(session.View().InRaid);

        await cache.EnsureFreshAsync("1.0.0");

        var view = session.View();
        Assert.True(view.InRaid);
        Assert.Equal("Streets of Tarkov", view.MapName);
    }

    [Fact]
    public async Task Keeps_the_fix_when_the_log_reports_the_same_map_again()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(new RatNavProfile(_dir)));
        session.OnRaidStarted("TarkovStreets");
        session.OnPositionFixed(new PositionFix
        {
            Position = new GamePosition(100, 5, 200),
            Rotation = new Quaternion(0, 0.7071, 0, 0.7071),
            HeadingDegrees = 90,
            TakenAt = DateTimeOffset.UtcNow,
        });

        // The log is re-read on every poll; seeing the same raid again is not a new raid, and
        // treating it as one would silently discard the fix the player just took.
        session.OnRaidStarted("TarkovStreets");

        Assert.NotNull(session.View().X);
    }

    /// <summary>
    /// Finishing a quest finishes its stops.
    ///
    /// <para>This is the only route a plan has to being cleared now. The in-raid strip used to
    /// carry a checkbox per stop, and ticking one meant alt-tabbing out of a raid — so it never
    /// happened, and the plan stayed lit through raids it had nothing to do with. Marking the
    /// quest done afterwards is the move that actually gets made.</para>
    /// </summary>
    [Fact]
    public async Task Completing_every_objective_of_a_quest_strikes_its_stops()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var progress = new ProgressStore(new RatNavProfile(_dir));
        var session = new RaidSession(new RatNavState(cache), progress);

        session.OnRaidStarted("TarkovStreets");
        session.UsePlan(PlanWith(("obj-1", "task-a"), ("obj-2", "task-a"), ("obj-3", "task-b")), StreetsMap());

        session.CompleteAll(["obj-1", "obj-2"]);

        var stops = session.View().Stops.ToDictionary(s => s.ObjectiveId);
        Assert.True(stops["obj-1"].Done);
        Assert.True(stops["obj-2"].Done);
        Assert.False(stops["obj-3"].Done);
    }

    /// <summary>
    /// A quest is finished in objectives that were never planned, too, and the next plan must not
    /// route back through any of them.
    /// </summary>
    [Fact]
    public async Task Objectives_outside_the_plan_are_still_recorded()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var progress = new ProgressStore(new RatNavProfile(_dir));
        var session = new RaidSession(new RatNavState(cache), progress);

        session.OnRaidStarted("TarkovStreets");
        session.UsePlan(PlanWith(("obj-1", "task-a")), StreetsMap());

        session.CompleteAll(["obj-1", "never-planned"]);

        Assert.True(progress.IsObjectiveComplete("never-planned"));
    }

    /// <summary>Un-marking a quest puts its stops back, or a misclick is permanent.</summary>
    [Fact]
    public async Task Un_completing_a_quest_puts_its_stops_back()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var progress = new ProgressStore(new RatNavProfile(_dir));
        var session = new RaidSession(new RatNavState(cache), progress);

        session.OnRaidStarted("TarkovStreets");
        session.UsePlan(PlanWith(("obj-1", "task-a")), StreetsMap());

        session.CompleteAll(["obj-1"]);
        session.CompleteAll(["obj-1"], done: false);

        Assert.False(session.View().Stops.Single().Done);
        Assert.False(progress.IsObjectiveComplete("obj-1"));
    }

    /// <summary>Nothing to do is not the same as a plan to wipe.</summary>
    [Fact]
    public async Task A_quest_with_no_objectives_changes_nothing()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(new RatNavProfile(_dir)));

        session.OnRaidStarted("TarkovStreets");
        session.UsePlan(PlanWith(("obj-1", "task-a")), StreetsMap());

        session.CompleteAll([]);

        Assert.False(session.View().Stops.Single().Done);
    }

    private static MapDef StreetsMap() => Streets().Maps[0];

    private static RaidPlan PlanWith(params (string ObjectiveId, string TaskId)[] stops) => new()
    {
        MapId = "streets",
        MapName = "Streets of Tarkov",
        Waypoints = [.. stops.Select(s => new Waypoint
        {
            ObjectiveId = s.ObjectiveId,
            TaskId = s.TaskId,
            TaskName = s.TaskId,
            Description = s.ObjectiveId,
            Position = new GamePosition(0, 0, 0),
        })],
    };

    [Fact]
    public async Task Reports_no_raid_for_a_map_it_has_no_image_for()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(new RatNavProfile(_dir)));
        session.OnRaidStarted("laboratory");

        Assert.False(session.View().InRaid);
    }

    [Fact]
    public async Task Ending_a_raid_returns_the_overlay_to_idle()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(new RatNavProfile(_dir)));
        session.OnRaidStarted("TarkovStreets");
        Assert.True(session.View().InRaid);

        session.OnRaidEnded();

        var view = session.View();
        Assert.False(view.InRaid);
        Assert.Null(view.X);
        Assert.Empty(view.Trail);
    }

    [Fact]
    public async Task Objectives_cleared_in_a_raid_survive_the_raid()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var progress = new ProgressStore(new RatNavProfile(_dir));
        var session = new RaidSession(new RatNavState(cache), progress);

        session.OnRaidStarted("TarkovStreets");
        session.Complete("objective-1");
        session.OnRaidEnded();

        // Walking to a stop is a fact about the world. Losing it on the way back to the menu
        // would send the player there again next raid.
        Assert.True(progress.IsObjectiveComplete("objective-1"));
    }

    [Fact]
    public async Task An_objective_finished_last_raid_starts_ticked_off()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var progress = new ProgressStore(new RatNavProfile(_dir));
        progress.CompleteObjective("objective-1");

        var session = new RaidSession(new RatNavState(cache), progress);
        var map = cache.Current!.Maps[0];

        session.OnRaidStarted("TarkovStreets");
        session.UsePlan(PlanWith("objective-1", "objective-2"), map);

        Assert.Contains("objective-1", session.View().CompletedObjectiveIds);
        Assert.DoesNotContain("objective-2", session.View().CompletedObjectiveIds);
    }

    private static RaidPlan PlanWith(params string[] objectiveIds) => new()
    {
        MapId = "streets",
        MapName = "Streets of Tarkov",
        Waypoints =
        [
            .. objectiveIds.Select((id, i) => new Waypoint
            {
                ObjectiveId = id,
                TaskId = $"task-{i}",
                TaskName = $"Task {i}",
                Description = $"Objective {i}",
                Position = new GamePosition(i * 10, 0, i * 10),
            }),
        ],
    };

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network in tests");
    }
}
