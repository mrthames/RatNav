using System.Net;
using System.Text;
using RatNav.Core.Data;

namespace RatNav.Core.Tests;

/// <summary>
/// Quests and items come from tarkov.dev; map calibration comes from tarkovdata on GitHub.
/// Two independent hosts, so one being down must not blank what the other returned.
/// </summary>
public class PartialOutageTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Maps_still_load_when_tarkov_dev_is_down()
    {
        // This is not hypothetical: tarkov.dev was returning "GraphQL server unavailable" while
        // this was written, and the app stayed usable because of exactly this path.
        var http = new HttpClient(new TarkovDevDownHandler());
        var cache = new GameDataCache(new TarkovDevClient(http), new MapAssets(http, _dir), _dir);

        var result = await cache.RefreshAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data.Tasks);
        Assert.Empty(result.Data.Items);

        // The map came from the source that was up, and it is calibrated enough to plot on.
        var map = Assert.Single(result.Data.Maps);
        Assert.Equal("customs", map.Name);
        Assert.NotNull(map.Image);
        Assert.Equal(180, map.Image.CoordinateRotation);
    }

    [Fact]
    public async Task Named_places_and_floors_survive_the_outage_too()
    {
        // Map metadata carries more than geometry — landmark names and floor definitions come
        // with it, and they are just as usable without the game-data half.
        var http = new HttpClient(new TarkovDevDownHandler());
        var cache = new GameDataCache(new TarkovDevClient(http), new MapAssets(http, _dir), _dir);

        var map = Assert.Single((await cache.RefreshAsync()).Data.Maps);

        Assert.Equal("customs", map.Id);
        Assert.Equal("Big Red", Assert.Single(map.Labels).Text);
        Assert.Equal("Shebuka", map.Image?.Author);
        Assert.Equal("Ground_Level", map.Image?.DefaultFloor);
    }

    /// <summary>tarkov.dev errors exactly as it does in production; GitHub serves normally.</summary>
    private sealed class TarkovDevDownHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var isGitHub = request.RequestUri!.Host.Contains("githubusercontent");

            var body = isGitHub
                ? """
                    [
                      {
                        "normalizedName": "customs",
                        "maps": [
                          {
                            "key": "customs",
                            "projection": "interactive",
                            "svgPath": "https://assets.tarkov.dev/maps/svg/Customs.svg",
                            "svgLayer": "Ground_Level",
                            "coordinateRotation": 180,
                            "bounds": [[698, -307], [-372, 237]],
                            "author": "Shebuka",
                            "labels": [{ "text": "Big Red", "position": [10, 20] }]
                          }
                        ]
                      }
                    ]
                    """
                : """{"errors":["GraphQL server unavailable. Try again later."]}""";

            return Task.FromResult(new HttpResponseMessage(isGitHub ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
