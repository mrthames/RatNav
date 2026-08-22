using System.Net;
using System.Text;
using RatNav.Core.Data;

namespace RatNav.Core.Tests;

/// <summary>
/// Transits belong on the map, and they are not extracts.
///
/// <para>tarkov.dev keeps them in a list of their own and RatNav read only the extracts, so every
/// transit was missing from the map entirely — the first user test noticed the count was wrong
/// before anyone noticed the pins were absent.</para>
///
/// <para>They are also named differently: an extract carries its name outright, a transit carries
/// a localization key that has to be looked up. A transit whose key does not resolve would be an
/// unnamed pin, which is worse than no pin, so it is dropped.</para>
/// </summary>
public sealed class MapTransitTests
{
    [Fact]
    public async Task Transits_are_read_onto_the_map_alongside_extracts()
    {
        var maps = await new TarkovDevClient(new HttpClient(new MapsHandler())).GetMapsAsync();

        var map = Assert.Single(maps);

        Assert.Equal(3, map.Extracts.Count);

        var exit = Assert.Single(map.Extracts, e => e.Name == "SE Exfil");
        Assert.False(exit.IsTransit);
        Assert.Equal("pmc", exit.Faction);

        var transit = Assert.Single(map.Extracts, e => e.IsTransit);
        Assert.Equal("Transit to Customs", transit.Name);

        // Open to whoever is standing at one, so the faction dial has nothing to say about it.
        Assert.Equal("shared", transit.Faction);
    }

    [Fact]
    public async Task A_transit_whose_name_will_not_resolve_is_left_off()
    {
        var maps = await new TarkovDevClient(new HttpClient(new MapsHandler())).GetMapsAsync();

        // INT_TRANSIT_9_DESC is in the map document and not in the translation table.
        Assert.DoesNotContain(Assert.Single(maps).Extracts, e => e.Name.Contains("_DESC"));
    }

    /// <summary>One map with two extracts and two transits, one of which cannot be named.</summary>
    private sealed class MapsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            // Anything that is not the map document or its translations — the calibration fetched
            // from GitHub, for instance — is not what these tests are about.
            if (!path.Contains("/maps"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = path.EndsWith("_en", StringComparison.Ordinal)
                ? """
                    {
                      "data": {
                        "INT_TRANSIT_6_DESC": "Transit to Customs",
                        "Interchange": "Interchange"
                      }
                    }
                    """
                : """
                    {
                      "data": {
                        "maps": {
                          "interchange": {
                            "name": "Interchange",
                            "normalizedName": "interchange",
                            "nameId": "Interchange",
                            "extracts": [
                              { "name": "SE Exfil", "faction": "pmc",
                                "position": { "x": -321.5, "y": 24.1, "z": 266.7 } },
                              { "name": "NW Exfil", "faction": "shared",
                                "position": { "x": 120.0, "y": 20.0, "z": -80.0 } }
                            ],
                            "transits": [
                              { "id": "6", "description": "INT_TRANSIT_6_DESC",
                                "position": { "x": 274.3, "y": 23.2, "z": 395.9 } },
                              { "id": "9", "description": "INT_TRANSIT_9_DESC",
                                "position": { "x": 12.0, "y": 5.0, "z": 7.0 } }
                            ]
                          }
                        }
                      }
                    }
                    """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
