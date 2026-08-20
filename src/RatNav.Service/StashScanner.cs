using System.Drawing;
using System.Drawing.Imaging;
using RatNav.Core.Data;
using RatNav.Core.Model;
using RatNav.Core.Stash;
using RatNav.Core.Tracking;

namespace RatNav.Service;

/// <summary>One cell as it was read, with what it might be.</summary>
public sealed record ScannedCell
{
    public required int Column { get; init; }
    public required int Row { get; init; }

    /// <summary>What it might be, nearest first. Empty when nothing came close enough.</summary>
    public required IReadOnlyList<IconMatch> Matches { get; init; }
}

/// <summary>What one screenshot turned out to hold.</summary>
public sealed record StashScan
{
    public required bool Found { get; init; }

    /// <summary>Why nothing was found, in words, when that is the answer.</summary>
    public string? Problem { get; init; }

    public int Columns { get; init; }
    public int Rows { get; init; }

    /// <summary>Rows filled with one repeated item, which are dividers rather than loot.</summary>
    public IReadOnlyList<int> SeparatorRows { get; init; } = [];

    public IReadOnlyList<ScannedCell> Cells { get; init; } = [];
}

/// <summary>
/// Reads a container out of a screenshot.
///
/// <para>The whole feature rests on one constraint, and it is the user's rather than the code's:
/// shoot a <b>scav junk box</b>, which is a fixed grid, or a <b>defined block of your inventory</b>
/// with a row of bandages marking where it ends. Either way it is a known rectangle rather than a
/// scrolling page, and a known rectangle is something a computer can read.</para>
///
/// <para>Items are named by matching each cell against the icons of items you already track. Not
/// against all 5,312 — if nothing wants an item, RatNav has no reason to count it, and a few
/// hundred candidates is a different problem from five thousand, both in accuracy and in how many
/// icons have to be fetched before any of this works at all.</para>
///
/// <para>Nothing here writes a count. It produces a list to be looked at.</para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class StashScanner(HttpClient http, string cacheDirectory)
{
    private readonly Dictionary<string, IconSignature?> _signatures = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StashScan> ScanAsync(
        Stream screenshot,
        IReadOnlyList<ItemDef> candidates,
        CancellationToken ct = default)
    {
        using var bitmap = Load(screenshot);

        if (bitmap is null)
            return new StashScan { Found = false, Problem = "That file is not an image RatNav can read." };

        var grid = ContainerGrid.Detect(Brightness(bitmap), bitmap.Width, bitmap.Height);

        if (grid is null)
        {
            return new StashScan
            {
                Found = false,
                Problem = "No container grid in that picture. Take the shot with a scav box open, "
                    + "or with one block of your stash filling the frame.",
            };
        }

        // Every occupied cell, reduced to a signature. Done before matching so a separator row can
        // be found by comparing cells to each other rather than to the catalogue.
        var cells = new Dictionary<(int Column, int Row), IconSignature>();

        foreach (var cell in grid.Cells.Where(c => c.Occupied))
        {
            var (x, y, size) = grid.Bounds(cell);

            if (Signature(bitmap, x, y, size) is { } signature)
                cells[(cell.Column, cell.Row)] = signature;
        }

        var separators = RatNav.Core.Stash.SeparatorRows.Find(grid, cells);
        var known = await CatalogueAsync(candidates, ct);

        var scanned = new List<ScannedCell>();

        foreach (var ((column, row), signature) in cells)
        {
            // A divider is bandages somebody put there on purpose. Naming them would put forty on
            // a shopping list.
            if (separators.Contains(row)) continue;

            scanned.Add(new ScannedCell
            {
                Column = column,
                Row = row,
                Matches = IconMatcher.Rank(signature, known),
            });
        }

        return new StashScan
        {
            Found = true,
            Columns = grid.Columns,
            Rows = grid.Rows,
            SeparatorRows = separators,
            Cells = [.. scanned.OrderBy(c => c.Row).ThenBy(c => c.Column)],
        };
    }

    /// <summary>
    /// The signatures of the items worth comparing against, fetched once and kept.
    ///
    /// <para>Icons are cached on disk. They are small, they never change, and re-downloading a few
    /// hundred of them on every scan would be rude to a service that gives them away.</para>
    /// </summary>
    private async Task<List<(string ItemId, string Name, IconSignature Signature)>> CatalogueAsync(
        IReadOnlyList<ItemDef> candidates, CancellationToken ct)
    {
        var known = new List<(string, string, IconSignature)>();

        await _gate.WaitAsync(ct);
        try
        {
            foreach (var item in candidates)
            {
                if (item.IconUrl is not { Length: > 0 }) continue;

                if (!_signatures.TryGetValue(item.Id, out var signature))
                {
                    signature = await IconAsync(item, ct);
                    _signatures[item.Id] = signature;
                }

                if (signature is not null) known.Add((item.Id, item.Name, signature));
            }
        }
        finally
        {
            _gate.Release();
        }

        return known;
    }

    private async Task<IconSignature?> IconAsync(ItemDef item, CancellationToken ct)
    {
        var path = Path.Combine(cacheDirectory, "icons", $"{Sanitize(item.Id)}.img");

        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var bytes = await http.GetByteArrayAsync(item.IconUrl!, ct);
                await File.WriteAllBytesAsync(path, bytes, ct);
            }

            using var stream = File.OpenRead(path);
            using var bitmap = Load(stream);

            return bitmap is null ? null : Signature(bitmap, 0, 0, Math.Min(bitmap.Width, bitmap.Height));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // An icon that will not download costs that one item, not the scan.
            return null;
        }
    }

    /// <summary>The image as brightness, which is all the grid detection needs.</summary>
    private static double[] Brightness(Bitmap bitmap)
    {
        var values = new double[bitmap.Width * bitmap.Height];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);

                values[y * bitmap.Width + x] = (pixel.R + pixel.G + pixel.B) / 765.0;
            }
        }

        return values;
    }

    /// <summary>
    /// One cell, reduced to a signature.
    ///
    /// <para>Inset past the grid lines: they are the same on every cell and would be the loudest
    /// thing every signature had in common.</para>
    /// </summary>
    private static IconSignature? Signature(Bitmap bitmap, int x, int y, int size)
    {
        var inset = Math.Max(1, size / 12);

        var left = Math.Max(0, x + inset);
        var top = Math.Max(0, y + inset);
        var right = Math.Min(bitmap.Width, x + size - inset);
        var bottom = Math.Min(bitmap.Height, y + size - inset);

        var width = right - left;
        var height = bottom - top;

        if (width < IconSignature.Size || height < IconSignature.Size) return null;

        var rgb = new double[width * height * 3];

        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                var pixel = bitmap.GetPixel(left + px, top + py);
                var at = (py * width + px) * 3;

                rgb[at] = pixel.R / 255.0;
                rgb[at + 1] = pixel.G / 255.0;
                rgb[at + 2] = pixel.B / 255.0;
            }
        }

        return IconSignature.From(rgb, width, height);
    }

    private static Bitmap? Load(Stream stream)
    {
        try
        {
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
