namespace RatNav.Core.Tests;

using System.Net;
using System.Text;
using RatNav.Core.Data;

/// <summary>
/// A quest's keys have to survive being read, whichever part of the quest records them.
///
/// <para>This is a regression test for a bug that was invisible from the inside and expensive from
/// the outside: <b>29 of the 57 key-requiring quests never mentioned their key</b>, so RatNav drew
/// a waypoint and let you queue without the one item that would have let you reach it.</para>
///
/// <para>The cause was reading keys from objectives. Only an objective with a <c>zone</c> becomes a
/// waypoint — a zone is what gives it a position — and the plan's "bring these" list is built from
/// waypoints. <i>Farming</i> is the clean case, reproduced below: the objective that names the key
/// has possible locations rather than a zone, and the objective that has a position needs no key.
/// So the key belonged to nothing that was drawn, and nothing that was drawn mentioned it.</para>
/// </summary>
public sealed class QuestKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Farming, reduced to the shape that broke: the key is on the objective with no zone.</summary>
    private const string Translations = """
    {
      "data": {
        "task-name": "Farming",
        "obj-find": "Find them",
        "obj-visit": "Locate it"
      }
    }
    """;

    private const string Payload = """
    {
      "data": {
        "tasks": {
          "farming": {
            "id": "farming",
            "name": "task-name",
            "trader": "mechanic",
            "neededKeys": [{ "map": "customs", "keys": ["warehouse-key"] }],
            "objectives": [
              {
                "id": "find-them",
                "type": "findQuestItem",
                "description": "obj-find",
                "requiredKeys": [["warehouse-key"]]
              },
              {
                "id": "locate-it",
                "type": "visit",
                "description": "obj-visit",
                "zones": [{ "map": "customs", "position": { "x": 1, "y": 2, "z": 3 } }]
              }
            ]
          }
        }
      }
    }
    """;

    private TarkovDevClient Client() =>
        new(new HttpClient(new StubHandler(Payload, Translations)));

    [Fact]
    public async Task A_quests_keys_are_read_from_the_quest()
    {
        var tasks = await Client().GetTasksAsync();
        var farming = Assert.Single(tasks);

        Assert.Equal("Farming", farming.Name);
        Assert.Equal(["warehouse-key"], farming.NeededKeyItemIds);
    }

    /// <summary>
    /// The objective that carries a position is not the one that carries the key, which is the
    /// whole reason the quest-level list has to exist.
    /// </summary>
    [Fact]
    public async Task The_positioned_objective_does_not_know_about_the_key()
    {
        var farming = Assert.Single(await Client().GetTasksAsync());

        var positioned = Assert.Single(farming.Objectives.Where(o => o.Position is not null));
        Assert.Empty(positioned.NeededKeyItemIds);

        // And the one that does know has nowhere to be drawn.
        var keyed = Assert.Single(farming.Objectives.Where(o => o.NeededKeyItemIds.Count > 0));
        Assert.Null(keyed.Position);
    }

    /// <summary>A quest that needs nothing says so, rather than inventing an empty key.</summary>
    [Fact]
    public async Task A_quest_with_no_keys_has_none()
    {
        const string none = """
        { "data": { "tasks": { "t": { "id": "t", "name": "n", "objectives": [] } } } }
        """;

        const string names = """{ "data": { "n": "No keys here" } }""";

        var client = new TarkovDevClient(new HttpClient(new StubHandler(none, names)));
        var task = Assert.Single(await client.GetTasksAsync());

        Assert.Empty(task.NeededKeyItemIds);
    }

    /// <summary>
    /// Answers both halves of a fetch: the document, and the parallel one that maps its translation
    /// keys to text. The client asks for the second at <c>{path}_{language}</c>.
    /// </summary>
    private sealed class StubHandler(string body, string translations) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var wantsText = request.RequestUri!.AbsolutePath.EndsWith("_en", StringComparison.Ordinal);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    wantsText ? translations : body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
