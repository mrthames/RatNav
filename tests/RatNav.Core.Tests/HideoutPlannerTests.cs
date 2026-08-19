using RatNav.Core.Model;
using RatNav.Core.Planning;

namespace RatNav.Core.Tests;

/// <summary>
/// The build order.
///
/// <para>The thing being tested is that the look-ahead number means something. If waves were just
/// "show more rows", the control would be decoration; the point is that wave 2 is genuinely
/// unreachable until wave 1 is built, so 1 answers "what can I build tonight" and 3 answers "what
/// should I stop vendoring".</para>
/// </summary>
public class HideoutPlannerTests
{
    /// <summary>Generator gates Medstation, which gates Lavatory. Three steps, deliberately.</summary>
    private static readonly HideoutStation[] Stations =
    [
        new()
        {
            Id = "generator",
            Name = "Generator",
            Levels =
            [
                Level("generator", 1, [Item("fuel", 2)]),
                Level("generator", 2, [Item("wires", 4)]),
            ],
        },
        new()
        {
            Id = "medstation",
            Name = "Medstation",
            Levels =
            [
                Level("medstation", 1, [Item("bandage", 3)], station: ("generator", 1)),
                Level("medstation", 2, [Item("ledx", 1)], station: ("generator", 2)),
            ],
        },
        new()
        {
            Id = "lavatory",
            Name = "Lavatory",
            Levels = [Level("lavatory", 1, [Item("bolts", 5)], station: ("medstation", 1))],
        },
    ];

    private static HideoutLevel Level(
        string stationId,
        int level,
        ObjectiveItem[] items,
        (string Station, int Level)? station = null) => new()
    {
        Id = $"{stationId}-{level}",
        Level = level,
        ItemRequirements = items,
        StationRequirements = station is { } required
            ? [new StationLevelRequirement(required.Station, required.Level)]
            : [],
    };

    private static ObjectiveItem Item(string id, int count) => new() { ItemId = id, Count = count };

    private static Dictionary<string, int> Built(params (string Station, int Level)[] levels) =>
        levels.ToDictionary(l => l.Station, l => l.Level, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Wave_one_is_only_what_can_be_built_right_now()
    {
        var upcoming = HideoutPlanner.Upcoming(Stations, Built(), lookAhead: 1);

        // Medstation needs Generator 1 and Lavatory needs Medstation 1, so neither is reachable
        // from an empty hideout however much you want them.
        Assert.Equal(["generator"], upcoming.Select(u => u.StationId));
        Assert.Equal(1, upcoming[0].Level);
    }

    [Fact]
    public void Looking_further_reaches_what_the_first_wave_unlocks()
    {
        var upcoming = HideoutPlanner.Upcoming(Stations, Built(), lookAhead: 3);

        var lavatory = upcoming.Single(u => u.StationId == "lavatory");

        // Generator 1 → Medstation 1 → Lavatory 1. The number is a real distance through the
        // build order, not a row count.
        Assert.Equal(3, lavatory.Wave);
        Assert.Equal(2, upcoming.Single(u => u is { StationId: "medstation", Level: 1 }).Wave);

        // Medstation 2 needs Generator 2, which itself only lands in wave 2 — so the second level
        // of a station is a step further out than the first, not the same step.
        Assert.Equal(3, upcoming.Single(u => u is { StationId: "medstation", Level: 2 }).Wave);
    }

    [Fact]
    public void What_is_already_built_moves_everything_closer()
    {
        var upcoming = HideoutPlanner.Upcoming(Stations, Built(("generator", 1)), lookAhead: 1);

        // With the gate already met, Medstation 1 is tonight's job rather than next week's.
        Assert.Contains(upcoming, u => u is { StationId: "medstation", Level: 1, Wave: 1 });
    }

    [Fact]
    public void A_finished_station_stops_appearing()
    {
        var upcoming = HideoutPlanner.Upcoming(
            Stations, Built(("generator", 2), ("medstation", 2), ("lavatory", 1)), lookAhead: 5);

        Assert.Empty(upcoming);
    }

    [Fact]
    public void Trader_gates_are_reported_but_do_not_hide_the_upgrade()
    {
        HideoutStation[] gated =
        [
            new()
            {
                Id = "workbench",
                Name = "Workbench",
                Levels =
                [
                    new HideoutLevel
                    {
                        Id = "workbench-1",
                        Level = 1,
                        ItemRequirements = [Item("bolts", 2)],
                        TraderRequirements = [new TraderLevelRequirement("prapor", "Prapor", 2)],
                    },
                ],
            },
        ];

        var upcoming = HideoutPlanner.Upcoming(gated, Built(), lookAhead: 1);

        // RatNav cannot see your loyalty levels. Treating the gate as a blocker would hide an
        // upgrade you may well be able to start, so it is shown with the reason attached.
        Assert.Single(upcoming);
        Assert.Contains(upcoming[0].Blockers, b => b is { Kind: "trader", Text: "Prapor LL2" });
    }

    [Fact]
    public void Demand_names_the_nearest_upgrade_that_wants_an_item()
    {
        HideoutStation[] shared =
        [
            new()
            {
                Id = "a",
                Name = "Alpha",
                Levels = [Level("a", 1, [Item("bolts", 2)])],
            },
            new()
            {
                Id = "b",
                Name = "Beta",
                Levels = [Level("b", 1, [Item("bolts", 3)], station: ("a", 1))],
            },
        ];

        var demand = HideoutPlanner.Demand(HideoutPlanner.Upcoming(shared, Built(), lookAhead: 2));

        // Both want bolts. The count is the total, but the name is the one you will reach first —
        // that is what makes the row tell you whether to keep the thing.
        Assert.Equal(5, demand["bolts"].Count);
        Assert.Equal("Alpha 1", demand["bolts"].UpgradeName);
        Assert.Equal(1, demand["bolts"].Wave);
    }

    [Fact]
    public void Targeting_narrows_the_list_to_what_you_picked()
    {
        var upcoming = HideoutPlanner.Upcoming(
            Stations, Built(), lookAhead: 3, targeted: new HashSet<string> { "medstation:1" });

        var demand = HideoutPlanner.Demand(upcoming);

        // Picking three things and still being shown the shopping list for all eleven would
        // defeat the point of picking.
        Assert.Equal(["bandage"], demand.Keys);
    }
}
