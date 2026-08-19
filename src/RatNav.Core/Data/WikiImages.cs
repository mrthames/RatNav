namespace RatNav.Core.Data;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>One picture from a quest's wiki article.</summary>
public sealed record WikiImage
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// The screenshots on a quest's wiki article — the ones showing which building and which door.
///
/// <para>A pin says where to walk. What turns that into "find this room" is a picture of the room,
/// and the community wiki has them for most quests. They are fetched rather than shipped: they are
/// other people's work under CC BY-SA, and redistributing them in a release would be both a
/// licensing question and a hundred megabytes.</para>
///
/// <para>Cached hard. Wiki articles change on patch days and not otherwise, so a month-old answer
/// is almost always the right one, and a tool that re-asks on every hover is one the wiki would be
/// right to block.</para>
/// </summary>
public sealed class WikiImages(HttpClient http, string cacheDirectory)
{
    private const string Api = "https://escapefromtarkov.fandom.com/api.php";

    /// <summary>Wiki articles change on patch days. Anything fresher than this is good enough.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Below this a picture is furniture — a trader portrait, an item icon, a banner — rather than
    /// a screenshot of somewhere. Every real guide shot on the wiki is a full-resolution capture.
    /// </summary>
    private const int SmallestUsefulWidth = 600;

    private static readonly string[] NotGuidance =
        ["banner", "icon", "logo", "portrait", "trader"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The pictures for a quest, from its wiki link. Empty when there is no link, no article, or
    /// the wiki is unreachable — this illustrates a quest and is never worth failing one over.
    /// </summary>
    public async Task<IReadOnlyList<WikiImage>> ForAsync(
        string taskId, string? wikiUrl, CancellationToken ct = default)
    {
        if (PageTitle(wikiUrl) is not { Length: > 0 } page) return [];

        if (Cached(taskId) is { } cached) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            // Checked again inside the gate: several waypoints on one quest would otherwise each
            // fetch the same article while the first was still in flight.
            if (Cached(taskId) is { } now) return now;

            var names = await NamesAsync(page, ct);
            var images = names.Count == 0 ? [] : await ResolveAsync(names, ct);

            Save(taskId, images);
            return images;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"…/wiki/Glory_to_CPSU" becomes "Glory_to_CPSU".</summary>
    public static string? PageTitle(string? wikiUrl)
    {
        if (wikiUrl is not { Length: > 0 }) return null;
        if (!Uri.TryCreate(wikiUrl, UriKind.Absolute, out var uri)) return null;

        var last = uri.Segments.LastOrDefault()?.Trim('/');

        return last is { Length: > 0 } ? Uri.UnescapeDataString(last) : null;
    }

    private async Task<IReadOnlyList<string>> NamesAsync(string page, CancellationToken ct)
    {
        var url = $"{Api}?action=parse&page={Uri.EscapeDataString(page)}&prop=images&format=json";
        var parsed = await http.GetFromJsonAsync<ParseEnvelope>(url, ct);

        return
        [
            .. (parsed?.Parse?.Images ?? [])
                .Where(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .Where(name => !NotGuidance.Any(
                    bad => name.Contains(bad, StringComparison.OrdinalIgnoreCase)))

                // The article's own order, which is the order someone wrote the guide in — first
                // picture first. Capped because a long article can list thirty.
                .Take(12)
        ];
    }

    private async Task<IReadOnlyList<WikiImage>> ResolveAsync(
        IReadOnlyList<string> names, CancellationToken ct)
    {
        var titles = string.Join("|", names.Select(n => "File:" + n));

        var url = $"{Api}?action=query&titles={Uri.EscapeDataString(titles)}"
            + "&prop=imageinfo&iiprop=url%7Csize&format=json";

        var payload = await http.GetFromJsonAsync<QueryEnvelope>(url, ct);
        var pages = payload?.Query?.Pages?.Values.ToList() ?? [];

        var found = pages
            .Select(p => (p.Title, Info: p.ImageInfo?.FirstOrDefault()))
            .Where(p => p.Info?.Url is { Length: > 0 })
            .Where(p => p.Info!.Width >= SmallestUsefulWidth)
            .Select(p => new WikiImage
            {
                Title = (p.Title ?? "").Replace("File:", "", StringComparison.OrdinalIgnoreCase),
                Url = p.Info!.Url!,
                Width = p.Info.Width,
                Height = p.Info.Height,
            })
            .ToList();

        // The API returns pages in its own order, so the article's order is restored here — the
        // sequence a guide was written in is most of what makes it readable.
        var order = names
            .Select((name, index) => (name, index))
            .ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);

        return [.. found.OrderBy(image => order.GetValueOrDefault(image.Title, int.MaxValue))];
    }

    private string PathFor(string taskId) =>
        Path.Combine(cacheDirectory, "wiki", $"{Sanitize(taskId)}.json");

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private IReadOnlyList<WikiImage>? Cached(string taskId)
    {
        try
        {
            var path = PathFor(taskId);
            if (!File.Exists(path)) return null;
            if (DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge) return null;

            return JsonSerializer.Deserialize<List<WikiImage>>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void Save(string taskId, IReadOnlyList<WikiImage> images)
    {
        try
        {
            var path = PathFor(taskId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // An empty answer is cached too. "This quest has no pictures" is a real answer, and
            // not storing it means asking the wiki again every single time.
            File.WriteAllText(path, JsonSerializer.Serialize(images));
        }
        catch (IOException)
        {
            // Costs a re-fetch, nothing more.
        }
    }

    private sealed record ParseEnvelope([property: JsonPropertyName("parse")] ParseBody? Parse);

    private sealed record ParseBody([property: JsonPropertyName("images")] List<string>? Images);

    private sealed record QueryEnvelope([property: JsonPropertyName("query")] QueryBody? Query);

    private sealed record QueryBody(
        [property: JsonPropertyName("pages")] Dictionary<string, PageBody>? Pages);

    private sealed record PageBody(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("imageinfo")] List<ImageInfoBody>? ImageInfo);

    private sealed record ImageInfoBody(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height);
}
