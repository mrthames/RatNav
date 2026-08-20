namespace RatNav.Core.Stash;

/// <summary>A piece of text read off the screen, and where it was.</summary>
public readonly record struct TextBlock(string Text, double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public double Right => X + Width;
    public double MiddleY => Y + (Height / 2);
}

/// <summary>Which part of the screen a screenshot is of, and so which rules apply to it.</summary>
public enum ImportKind
{
    /// <summary>A container's own window, open on its own. The whole of it counts.</summary>
    Container,

    /// <summary>The stash, where a row of one repeated item marks each end of the tracked block.</summary>
    Stash,

    /// <summary>An inventory screen, where only what is carried counts and never what is worn.</summary>
    Carried,
}

/// <summary>One item found, and how many of it.</summary>
public sealed record CountedItem(string ItemId, string Name, int Count);

/// <summary>What one screenshot turned out to say.</summary>
public sealed record LabelReading
{
    /// <summary>The container's own name, when the picture showed one.</summary>
    public string? ContainerName { get; init; }

    public IReadOnlyList<CountedItem> Items { get; init; } = [];

    /// <summary>Text that looked like a label and matched nothing, so it can be said out loud.</summary>
    public IReadOnlyList<string> Unrecognised { get; init; } = [];
}

/// <summary>
/// Reads what is in a container from the labels the game prints on it.
///
/// <para>Escape from Tarkov writes each item's short name across the top of its cell — "Sodium",
/// "OScope", "T-Plug", "Duct tape". That is <c>ItemDef.ShortName</c>, which RatNav already holds
/// for every item in the game. So this is reading a printed label, not comparing pictures: more
/// accurate, nothing to download, and it fails into "I could not read that" rather than into a
/// confident wrong answer.</para>
///
/// <para>Which labels count depends on what the screenshot is of, and that is the caller's to say
/// rather than this code's to guess — see <see cref="ImportKind"/>.</para>
/// </summary>
public static class LabelReader
{
    /// <summary>
    /// Headers marking something you are carrying. Everything under one of these counts; anything
    /// under a slot that is not here — a weapon, armour, a helmet — does not.
    /// </summary>
    private static readonly string[] Carried =
        ["POCKETS", "BACKPACK", "POUCH", "TACTICAL RIG", "RIG", "SECURE CONTAINER"];

    /// <summary>
    /// Text that marks the top of the stash panel.
    ///
    /// <para>This is what stops a carried section reaching across the screen. The stash sits to the
    /// right of pockets, backpack and pouch and shares their vertical space, so a section bounded
    /// only above and below swallows it — and a backpack that reports the whole stash is worse than
    /// one that reports nothing.</para>
    /// </summary>
    private static readonly string[] StashPanel = ["SEARCH", "SORT TABLE"];

    /// <summary>
    /// Text on the screen that is furniture rather than an item: interface chrome, tab names,
    /// and the headers themselves.
    /// </summary>
    private static readonly string[] Furniture =
    [
        "SEARCH", "SORT TABLE", "BACK", "OVERALL", "GEAR", "HEALTH", "CUSTOMIZATION", "SKILLS",
        "MAP", "TASKS", "ACHIEVEMENTS", "QUICK USE", "MAIN MENU", "HIDEOUT", "TRADERS",
        "FLEA MARKET", "BUILDS", "HANDBOOK", "MESSENGER", "SURVEY", "EXPANSIONS", "SPECIAL SLOTS",
        "EARPIECE", "HEADWEAR", "FACE COVER", "ARMBAND", "BODY ARMOR", "EYEWEAR", "DOGTAG",
        "ON SLING", "HOLSTER", "ON BACK", "SHEATH", "NEW PRESET",
    ];

    /// <param name="blocks">Every piece of text read off the picture, with where it was.</param>
    /// <param name="kind">What the picture is of.</param>
    /// <param name="resolve">A label to an item, or null when nothing matches it.</param>
    /// <param name="separatorLabel">
    /// The short name of the item used to mark the ends of a stash block — bandages, by
    /// convention. Only consulted for <see cref="ImportKind.Stash"/>.
    /// </param>
    public static LabelReading Read(
        IReadOnlyList<TextBlock> blocks,
        ImportKind kind,
        Func<string, (string Id, string Name)?> resolve,
        string separatorLabel = "Bandage")
    {
        var wanted = kind switch
        {
            ImportKind.Stash => BetweenSeparators(blocks, separatorLabel),
            ImportKind.Carried => UnderCarriedHeaders(blocks),
            _ => blocks,
        };

        var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.Ordinal);
        var missed = new List<string>();

        foreach (var block in wanted)
        {
            var text = block.Text.Trim();

            if (text.Length == 0 || IsFurniture(text)) continue;

            if (resolve(text) is not { } item)
            {
                if (!missed.Contains(text)) missed.Add(text);
                continue;
            }

            var found = counts.TryGetValue(item.Id, out var existing) ? existing.Count : 0;

            counts[item.Id] = (item.Name, found + 1);
        }

        return new LabelReading
        {
            ContainerName = kind == ImportKind.Container ? NameOfContainer(blocks) : null,
            Items =
            [
                .. counts
                    .Select(pair => new CountedItem(pair.Key, pair.Value.Name, pair.Value.Count))
                    .OrderByDescending(i => i.Count)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            ],
            Unrecognised = missed,
        };
    }

    /// <summary>
    /// The labels sitting between the first and last separator row.
    ///
    /// <para>A row filled with one cheap item is a boundary somebody put there on purpose, and the
    /// separators themselves are never counted — adding twenty bandages to a shopping list would
    /// be its own small betrayal.</para>
    /// </summary>
    private static IReadOnlyList<TextBlock> BetweenSeparators(
        IReadOnlyList<TextBlock> blocks, string separatorLabel)
    {
        var separators = blocks
            .Where(b => b.Text.Trim().Equals(separatorLabel, StringComparison.OrdinalIgnoreCase))
            .Select(b => b.MiddleY)
            .OrderBy(y => y)
            .ToList();

        // Fewer than two rows' worth is not a pair of boundaries, so nothing is bounded and the
        // honest answer is nothing rather than everything.
        if (separators.Count < 2) return [];

        var top = separators[0];
        var bottom = separators[^1];

        // A cell's height of tolerance, so a label on the same row as a separator is treated as
        // part of that row rather than as content.
        var margin = Math.Max(8, (bottom - top) * 0.01);

        return
        [
            .. blocks.Where(b => b.MiddleY > top + margin && b.MiddleY < bottom - margin)
        ];
    }

    /// <summary>
    /// The labels under a header naming something you carry.
    ///
    /// <para>This is what keeps worn equipment out. A weapon, armour, a helmet and a headset all
    /// sit under headers of their own, and a header that is not on the carried list takes
    /// everything under it out of the count.</para>
    /// </summary>
    private static IReadOnlyList<TextBlock> UnderCarriedHeaders(IReadOnlyList<TextBlock> blocks)
    {
        var headers = blocks
            .Select(b => (Block: b, Header: Carried.FirstOrDefault(
                h => b.Text.Trim().Equals(h, StringComparison.OrdinalIgnoreCase))))
            .Where(h => h.Header is not null)
            .Select(h => h.Block)
            .OrderBy(b => b.Y)
            .ToList();

        if (headers.Count == 0) return [];

        // Every header on the screen, carried or not, so a section ends where the next one starts
        // whatever that next one is.
        var boundaries = blocks
            .Where(b => IsFurniture(b.Text.Trim())
                || Carried.Any(h => b.Text.Trim().Equals(h, StringComparison.OrdinalIgnoreCase)))
            .Select(b => b.Y)
            .OrderBy(y => y)
            .ToList();

        // Where the stash begins, so a section stops before it. Without a marker there is nothing
        // to stop at, and reporting everything to the right of a backpack header would mean
        // importing the whole stash from a picture meant to import a backpack.
        var stashBegins = blocks
            .Where(b => StashPanel.Any(
                m => b.Text.Trim().Equals(m, StringComparison.OrdinalIgnoreCase)))
            .Select(b => b.X)
            .DefaultIfEmpty(double.MaxValue)
            .Min();

        var wanted = new List<TextBlock>();

        foreach (var header in headers)
        {
            var below = boundaries.FirstOrDefault(y => y > header.Y + 1, double.MaxValue);

            wanted.AddRange(blocks.Where(b =>
                b.Y > header.Y
                && b.Y < below

                // Right of where the header starts, which is where its own grid is drawn, and
                // left of the stash.
                && b.X >= header.X - 8
                && b.X < stashBegins));
        }

        return [.. wanted.Distinct()];
    }

    /// <summary>
    /// A container window's own name, from the top-right of it.
    ///
    /// <para>The game prints it there — "Junk 1" — which means naming a box does not have to be
    /// typed twice. Offered for confirmation rather than trusted: it is a guess about layout, and
    /// the person looking at the screenshot knows better than the rule does.</para>
    /// </summary>
    private static string? NameOfContainer(IReadOnlyList<TextBlock> blocks)
    {
        if (blocks.Count == 0) return null;

        var top = blocks.Min(b => b.Y);
        var strip = blocks.Where(b => b.Y <= top + Math.Max(12, b.Height * 1.5)).ToList();

        if (strip.Count == 0) return null;

        var name = strip.OrderByDescending(b => b.Right).First().Text.Trim();

        return name.Length is > 0 and <= 32 && !IsFurniture(name) ? name : null;
    }

    private static bool IsFurniture(string text) =>
        Furniture.Any(f => text.Equals(f, StringComparison.OrdinalIgnoreCase))

        // A pure number is a stack count, a durability figure, or a price — never a name.
        || text.All(c => char.IsDigit(c) || c is '/' or ',' or '.' or ' ' or '%');
}
