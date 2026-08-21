using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RatNav.Core.Updates;

/// <summary>What a check found, or why it found nothing.</summary>
public sealed record UpdateStatus
{
    /// <summary>The version running, as the assembly reports it.</summary>
    public required string Current { get; init; }

    /// <summary>The newest stable release, or null when the check could not be made.</summary>
    public string? Latest { get; init; }

    /// <summary>True when <see cref="Latest"/> is genuinely newer than what is running.</summary>
    public bool Available { get; init; }

    /// <summary>Where to go and get it.</summary>
    public string? Url { get; init; }

    /// <summary>When the check ran, so the answer can be cached and shown with its age.</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>Set when the check failed. Never a reason to interrupt anybody.</summary>
    public string? Problem { get; init; }
}

/// <summary>
/// Asks GitHub whether there is a newer RatNav, and says so. Nothing more.
///
/// <para><b>Tell, never do.</b> A tool that reads the game's files has no business downloading and
/// running an installer on its own. A line saying a newer version exists, with the link, is the
/// whole feature — anything past that is asking for a great deal more trust than this needs.</para>
///
/// <para><b>Prereleases do not count.</b> <c>/releases/latest</c> excludes them, which is the
/// entire reason to use it rather than <c>/releases</c>: alphas are published deliberately and
/// telling everybody about them would make the prerelease flag meaningless.</para>
///
/// <para><b>Failure is silence.</b> GitHub being unreachable, rate-limiting, or renaming a field
/// is not a thing to put in front of somebody who is trying to plan a raid.</para>
/// </summary>
public sealed class UpdateCheck(HttpClient http)
{
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/mrthames/RatNav/releases/latest";

    public const string ReleasesPage = "https://github.com/mrthames/RatNav/releases/latest";

    /// <summary>
    /// How long an answer is worth keeping.
    ///
    /// <para>Releases happen every few days at their fastest and GitHub rate-limits unauthenticated
    /// callers by IP. Asking once a day is far more often than the answer changes.</para>
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private UpdateStatus? _last;

    /// <summary>The last answer, however old, or null if nothing has been asked yet.</summary>
    public UpdateStatus? Last => _last;

    /// <summary>Asks only if the last answer has gone stale. Cheap when it has not.</summary>
    public async Task<UpdateStatus> CheckIfDueAsync(string current, CancellationToken ct = default)
    {
        if (_last is { CheckedAt: { } at } cached && DateTimeOffset.UtcNow - at < MaxAge)
            return cached;

        return await CheckAsync(current, ct);
    }

    /// <summary>Asks now, whatever the age of the last answer. This is what a manual check calls.</summary>
    public async Task<UpdateStatus> CheckAsync(string current, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);

            // GitHub refuses anonymous requests without one, and says so in a way that looks like
            // an outage rather than a missing header.
            request.Headers.UserAgent.ParseAdd("RatNav");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Failed(current, $"GitHub answered {(int)response.StatusCode}.");
            }

            var release = await response.Content.ReadFromJsonAsync<ReleaseDto>(ct);

            if (release?.TagName is not { Length: > 0 } tag)
                return Failed(current, "GitHub did not name a release.");

            var latest = tag.TrimStart('v', 'V');

            return _last = new UpdateStatus
            {
                Current = current,
                Latest = latest,
                Available = IsNewer(latest, current),
                Url = release.HtmlUrl ?? ReleasesPage,
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or System.Text.Json.JsonException)
        {
            return Failed(current, "Could not reach GitHub.");
        }
    }

    private UpdateStatus Failed(string current, string problem) =>
        _last = new UpdateStatus
        {
            Current = current,
            CheckedAt = DateTimeOffset.UtcNow,
            Problem = problem,
        };

    /// <summary>
    /// Whether one version string is newer than another.
    ///
    /// <para>Compared part by part as numbers rather than as text, because "0.10.0" is newer than
    /// "0.9.0" and sorts before it in every string comparison ever written.</para>
    ///
    /// <para>A prerelease suffix loses to the same version without one — 0.2.0 is newer than
    /// 0.2.0-alpha.1 — which matters for somebody running an alpha when the stable it became is
    /// published. Anything unparseable is treated as not newer: the failure mode of a bad compare
    /// should be saying nothing, not nagging about an update that does not exist.</para>
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        var (theirs, theirPre) = Split(candidate);
        var (ours, ourPre) = Split(current);

        if (theirs.Length == 0 || ours.Length == 0) return false;

        for (var i = 0; i < Math.Max(theirs.Length, ours.Length); i++)
        {
            var a = i < theirs.Length ? theirs[i] : 0;
            var b = i < ours.Length ? ours[i] : 0;

            if (a != b) return a > b;
        }

        // Same numbers: a release beats the prerelease that led to it, and nothing beats a release.
        return ourPre && !theirPre;
    }

    private static (int[] Parts, bool Prerelease) Split(string version)
    {
        var text = version.Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-');
        var prerelease = dash >= 0;

        if (prerelease) text = text[..dash];

        var parts = new List<int>();

        foreach (var piece in text.Split('.'))
        {
            if (!int.TryParse(piece, out var value)) return ([], prerelease);
            parts.Add(value);
        }

        return ([.. parts], prerelease);
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
