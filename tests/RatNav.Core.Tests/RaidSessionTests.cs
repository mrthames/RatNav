using System.Net;
using System.Text.Json;
using RatNav.Core.Data;
using RatNav.Core.Model;
using RatNav.Core.Progress;
using RatNav.Service;

namespace RatNav.Core.Tests;

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
        var session = new RaidSession(new RatNavState(cache), new ProgressStore(_dir));

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

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(_dir));
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

    [Fact]
    public async Task Reports_no_raid_for_a_map_it_has_no_image_for()
    {
        var cache = CacheFor(Streets());
        await cache.EnsureFreshAsync("1.0.0");

        var session = new RaidSession(new RatNavState(cache), new ProgressStore(_dir));
        session.OnRaidStarted("laboratory");

        Assert.False(session.View().InRaid);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network in tests");
    }
}
