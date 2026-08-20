namespace RatNav.Core.Tests;

using RatNav.Core.Stash;

/// <summary>
/// Reading a container from the labels the game prints on it.
///
/// <para>The layouts here are modelled on real 4K screenshots: a junk box open on its own, a stash
/// with a row of bandages at each end of the tracked block, and an inventory screen with worn gear
/// down the left and carried containers in the middle.</para>
/// </summary>
public class LabelReaderTests
{
    /// <summary>Short names to items, the way the item index does it.</summary>
    private static (string, string)? Resolve(string label) => label switch
    {
        "Bandage" => ("bandage", "Bandage"),
        "Sodium" => ("sodium", "Pack of sodium"),
        "T-Plug" => ("plug", "T-Shaped plug"),
        "Wires" => ("wires", "Bundle of wires"),
        "GPhone" => ("phone", "Golden phone"),
        "AS VAL" => ("asval", "AS VAL"),
        _ => null,
    };

    private static TextBlock At(string text, double x, double y) => new(text, x, y, 60, 14);

    // ---- a container on its own

    [Fact]
    public void Every_label_in_a_container_is_counted()
    {
        var reading = LabelReader.Read(
            [At("Sodium", 100, 100), At("Sodium", 200, 100), At("Wires", 300, 100)],
            ImportKind.Container,
            Resolve);

        Assert.Equal(2, reading.Items.Single(i => i.ItemId == "sodium").Count);
        Assert.Equal(1, reading.Items.Single(i => i.ItemId == "wires").Count);
    }

    [Fact]
    public void The_most_of_anything_leads()
    {
        var reading = LabelReader.Read(
            [At("Wires", 100, 100), At("Sodium", 200, 100), At("Sodium", 300, 100)],
            ImportKind.Container,
            Resolve);

        Assert.Equal("sodium", reading.Items[0].ItemId);
    }

    /// <summary>The game prints it in the corner, so naming a box need not be typed twice.</summary>
    [Fact]
    public void A_containers_own_name_is_read_off_its_title_bar()
    {
        var reading = LabelReader.Read(
            [At("Junk", 60, 40), At("Junk 1", 1500, 40), At("Sodium", 100, 120)],
            ImportKind.Container,
            Resolve);

        Assert.Equal("Junk 1", reading.ContainerName);
    }

    [Fact]
    public void Text_that_matches_nothing_is_reported_rather_than_dropped()
    {
        var reading = LabelReader.Read(
            [At("Sodium", 100, 100), At("Krasavch", 200, 100)],
            ImportKind.Container,
            Resolve);

        Assert.Equal(["Krasavch"], reading.Unrecognised);
    }

    [Fact]
    public void A_stack_count_is_not_mistaken_for_a_name()
    {
        var reading = LabelReader.Read(
            [At("Sodium", 100, 100), At("4/4", 140, 150), At("400/400", 240, 150)],
            ImportKind.Container,
            Resolve);

        Assert.Empty(reading.Unrecognised);
        Assert.Single(reading.Items);
    }

    // ---- the stash, bounded by bandages

    [Fact]
    public void Only_what_sits_between_the_bandage_rows_counts()
    {
        var reading = LabelReader.Read(
            [
                At("Wires", 100, 50),      // above the block
                At("Bandage", 100, 100),   // the top boundary
                At("Bandage", 200, 100),
                At("Sodium", 100, 200),    // the block itself
                At("T-Plug", 200, 300),
                At("Bandage", 100, 400),   // the bottom boundary
                At("Bandage", 200, 400),
                At("GPhone", 100, 500),    // below the block
            ],
            ImportKind.Stash,
            Resolve);

        Assert.Equal(["plug", "sodium"], reading.Items.Select(i => i.ItemId).OrderBy(i => i));
    }

    /// <summary>Bandages put there on purpose are not loot, and twenty of them on a shopping list
    /// would be its own small betrayal.</summary>
    [Fact]
    public void The_bandages_themselves_are_never_counted()
    {
        var reading = LabelReader.Read(
            [
                At("Bandage", 100, 100), At("Bandage", 200, 100),
                At("Sodium", 100, 200),
                At("Bandage", 100, 300), At("Bandage", 200, 300),
            ],
            ImportKind.Stash,
            Resolve);

        Assert.DoesNotContain(reading.Items, i => i.ItemId == "bandage");
    }

    /// <summary>
    /// One row of bandages bounds nothing. Counting everything would be the wrong kind of helpful:
    /// it would silently import the whole stash off a screenshot meant to import part of it.
    /// </summary>
    [Fact]
    public void Without_both_boundaries_nothing_is_counted()
    {
        var reading = LabelReader.Read(
            [At("Bandage", 100, 100), At("Sodium", 100, 200)],
            ImportKind.Stash,
            Resolve);

        Assert.Empty(reading.Items);
    }

    // ---- an inventory screen

    /// <summary>
    /// The rule that keeps worn equipment out. A weapon sits under its own header, and a header
    /// that is not one of the carried ones takes everything below it out of the count.
    /// </summary>
    [Fact]
    public void Only_what_is_carried_counts_and_never_what_is_worn()
    {
        var reading = LabelReader.Read(
            [
                At("ON SLING", 50, 380),   // worn
                At("AS VAL", 60, 400),
                At("POCKETS", 510, 70),    // carried
                At("Sodium", 520, 90),
                At("BACKPACK", 510, 200),  // carried
                At("Wires", 620, 220),
                At("POUCH", 510, 530),     // carried
                At("T-Plug", 620, 550),
            ],
            ImportKind.Carried,
            Resolve);

        var found = reading.Items.Select(i => i.ItemId).ToList();

        Assert.Contains("sodium", found);
        Assert.Contains("wires", found);
        Assert.Contains("plug", found);
        Assert.DoesNotContain("asval", found);
    }

    /// <summary>
    /// The stash sits to the right of the carried sections and shares their vertical space, so a
    /// section bounded only above and below would swallow it — and a backpack that reports the
    /// whole stash is worse than one that reports nothing.
    ///
    /// <para>What stops it is the stash panel's own heading, which is on the real screen.</para>
    /// </summary>
    [Fact]
    public void A_carried_section_does_not_reach_across_into_the_stash()
    {
        var reading = LabelReader.Read(
            [
                At("SORT TABLE", 1100, 40),  // the stash panel begins here
                At("BACKPACK", 510, 200),
                At("Wires", 620, 220),
                At("GPhone", 1400, 220),     // over in the stash
            ],
            ImportKind.Carried,
            Resolve);

        Assert.Contains(reading.Items, i => i.ItemId == "wires");
        Assert.DoesNotContain(reading.Items, i => i.ItemId == "phone");
    }

    [Fact]
    public void A_screen_with_no_carried_sections_counts_nothing()
    {
        var reading = LabelReader.Read(
            [At("ON SLING", 50, 380), At("AS VAL", 60, 400)],
            ImportKind.Carried,
            Resolve);

        Assert.Empty(reading.Items);
    }

    [Fact]
    public void Interface_text_is_never_an_item()
    {
        var reading = LabelReader.Read(
            [At("SEARCH", 100, 40), At("SORT TABLE", 200, 40), At("HANDBOOK", 300, 900)],
            ImportKind.Container,
            Resolve);

        Assert.Empty(reading.Items);
        Assert.Empty(reading.Unrecognised);
    }

    [Fact]
    public void An_empty_picture_is_not_an_error()
    {
        var reading = LabelReader.Read([], ImportKind.Container, Resolve);

        Assert.Empty(reading.Items);
        Assert.Null(reading.ContainerName);
    }
}
