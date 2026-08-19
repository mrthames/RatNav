using System.Net;
using System.Text;
using System.Text.Json;
using RatNav.Core.Data;
using RatNav.Core.Model;

namespace RatNav.Core.Tests;

/// <summary>
/// The behaviour under test here is the one that matters most in the field: what happens when
/// tarkov.dev is unreachable. It was, in fact, down while this was being written — which is
/// exactly why "keep serving the last good data" is a tested guarantee rather than a good intention.
/// </summary>
public class GameDataCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private GameDataCache CacheWith(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new GameDataCache(new TarkovDevClient(http), new MapAssets(http, _dir), _dir);
    }

    private void SeedDisk(GameData data)
    {
        Directory.CreateDirectory(_dir);
        var name = $"gamedata-{data.GameVersion ?? "unknown"}.json";
        File.WriteAllText(
            Path.Combine(_dir, name),
            JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static GameData Cached(string? version = "0.14.0") => new()
    {
        FetchedAt = DateTimeOffset.UtcNow.AddDays(-3),
        GameVersion = version,
        Items = [new ItemDef { Id = "watch", Name = "Bronze pocket watch" }],
    };

    [Fact]
    public async Task Serves_cached_data_when_the_api_is_down()
    {
        SeedDisk(Cached());

        var cache = CacheWith(new AlwaysFailsHandler());
        var result = await cache.RefreshAsync("0.14.0");

        Assert.False(result.Succeeded);
        Assert.True(result.ServingStale);
        Assert.NotNull(result.Error);

        // The point: there is still a usable planner on the other side of a failed refresh.
        Assert.Single(result.Data.Items);
        Assert.Equal("Bronze pocket watch", result.Data.Items[0].Name);
    }

    [Fact]
    public async Task Reports_the_outage_rather_than_throwing_when_nothing_is_cached()
    {
        var cache = CacheWith(new AlwaysFailsHandler());
        var result = await cache.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Empty(result.Data.Items);
        Assert.Empty(result.Data.Tasks);
    }

    [Fact]
    public async Task Fresh_cached_data_is_served_without_calling_the_api()
    {
        SeedDisk(Cached() with { FetchedAt = DateTimeOffset.UtcNow });

        var handler = new AlwaysFailsHandler();
        var cache = CacheWith(handler);

        var result = await cache.EnsureFreshAsync("0.14.0");

        Assert.True(result.Succeeded);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_game_patch_forces_a_refresh_even_if_the_cache_is_young()
    {
        SeedDisk(Cached("0.14.0") with { FetchedAt = DateTimeOffset.UtcNow });

        var handler = new AlwaysFailsHandler();
        var cache = CacheWith(handler);

        // Same data, different game version: the client was patched, so what we hold is suspect
        // no matter how recently we fetched it.
        var result = await cache.EnsureFreshAsync("0.15.0");

        Assert.True(handler.Calls > 0);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_corrupt_cache_file_is_skipped_rather_than_fatal()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "gamedata-broken.json"), "{ this is not json");
        SeedDisk(Cached("0.14.0"));

        var loaded = CacheWith(new AlwaysFailsHandler()).LoadFromDisk();

        Assert.NotNull(loaded);
        Assert.Single(loaded.Items);
    }

    [Fact]
    public async Task A_successful_refresh_is_written_to_disk()
    {
        var cache = CacheWith(new CannedHandler());
        var result = await cache.RefreshAsync("0.14.0");

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(_dir, "gamedata-0.14.0.json")));

        var reloaded = cache.LoadFromDisk("0.14.0");
        Assert.NotNull(reloaded);
        Assert.Single(reloaded.Items);
        Assert.Equal("Bronze pocket watch", reloaded.Items[0].Name);
    }

    private sealed class AlwaysFailsHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("no network");
        }
    }

    /// <summary>Returns just enough of each response shape to exercise the happy path.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.Host.Contains("githubusercontent")
                ? "{}"
                : Canned(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        private static string Canned(HttpRequestMessage request)
        {
            var query = request.Content?.ReadAsStringAsync().Result ?? "";

            if (query.Contains("RatNavItems"))
                return """{"data":{"items":[{"id":"watch","name":"Bronze pocket watch","shortName":"Watch","width":1,"height":1}]}}""";
            if (query.Contains("RatNavTasks"))
                return """{"data":{"tasks":[]}}""";
            if (query.Contains("RatNavHideout"))
                return """{"data":{"hideoutStations":[]}}""";
            return """{"data":{"maps":[]}}""";
        }
    }
}
