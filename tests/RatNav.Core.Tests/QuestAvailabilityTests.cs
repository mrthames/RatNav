using RatNav.Core.Model;
using RatNav.Core.Progress;

namespace RatNav.Core.Tests;

/// <summary>
/// Which quests you could actually accept.
///
/// <para>Prerequisites alone are not the answer: most quests also gate on character level, and a
/// planner that offers one you cannot take is worse than one that offers fewer. But RatNav cannot
/// see your level — nothing the game writes to disk reports it — so the level filter has to be
/// optional, and absent has to mean "do not filter" rather than "assume level 1".</para>
/// </summary>
public class QuestAvailabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ProgressStore Progress() => new(_dir);

    private static TaskDef Task(string id, int? level = null, params string[] needs) => new()
    {
        Id = id,
        Name = id,
        MinPlayerLevel = level,
        PrerequisiteTaskIds = needs,
    };

    private static readonly TaskDef[] Tasks =
    [
        Task("debut"),
        Task("checking", level: 5),
        Task("shootout", level: 20, "debut"),
    ];

    [Fact]
    public void A_quest_above_your_level_is_not_available()
    {
        var available = Progress().AvailableNow(Tasks, playerLevel: 5).Select(t => t.Id);

        // "shootout" is also blocked by its prerequisite; "checking" is blocked by level alone.
        Assert.Equal(["debut", "checking"], available);
    }

    [Fact]
    public void Not_knowing_your_level_shows_everything_rather_than_nothing()
    {
        // Absent must not mean "level 1". Hiding quests because RatNav was never told is worse
        // than listing one or two you cannot take yet.
        var available = Progress().AvailableNow(Tasks).Select(t => t.Id);

        Assert.Equal(["debut", "checking"], available);
    }

    [Fact]
    public void Finishing_a_prerequisite_opens_what_it_gated()
    {
        var progress = Progress();
        progress.SetManual("debut", QuestState.Completed);

        Assert.Contains("shootout", progress.AvailableNow(Tasks, playerLevel: 30).Select(t => t.Id));
    }

    [Fact]
    public void Completed_quests_imply_a_level_floor()
    {
        var progress = Progress();
        progress.SetManual("checking", QuestState.Completed);

        // Not your real level — RatNav cannot see that — but a quest needing level 5 cannot have
        // been finished below it, which beats offering an empty box.
        Assert.Equal(5, progress.LevelImpliedBy(Tasks));
    }

    [Fact]
    public void Trader_levels_start_at_one_and_are_remembered()
    {
        var progress = Progress();
        Assert.Equal(1, progress.TraderLevelOf("Prapor"));

        progress.SetTraderLevel("Prapor", 3);

        // A fresh store is empty until it reads the file — that is deliberate, so a test or a
        // tool can work in memory without touching disk.
        var reopened = new ProgressStore(_dir);
        reopened.Load();

        Assert.Equal(3, reopened.TraderLevelOf("Prapor"));
    }
}
