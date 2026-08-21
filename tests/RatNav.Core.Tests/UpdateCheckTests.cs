using System.Net;
using System.Text;
using RatNav.Core.Updates;

namespace RatNav.Core.Tests;

/// <summary>
/// Comparing versions, and failing quietly.
///
/// <para>The comparison is the part worth testing hardest: it decides whether somebody is told
/// there is an update, and both ways of being wrong are bad. Missing a release means nobody
/// upgrades; inventing one means a notice that never goes away however many times it is followed.</para>
/// </summary>
public sealed class UpdateCheckTests
{
    [Theory]
    // Ordinary.
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.1.0", "0.1.0", false)]

    // Part by part as numbers. As text, "0.10.0" sorts before "0.9.0" and the update is missed.
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("0.9.0", "0.10.0", false)]
    [InlineData("1.0.0", "0.99.99", true)]

    // A missing part is a zero, so 0.2 and 0.2.0 are the same version written two ways.
    [InlineData("0.2", "0.2.0", false)]
    [InlineData("0.2.1", "0.2", true)]

    // A release beats the prerelease it came from — the case that matters for anyone running an
    // alpha when the stable it became is published.
    [InlineData("0.2.0", "0.2.0-alpha.1", true)]
    [InlineData("0.2.0-alpha.1", "0.2.0", false)]
    [InlineData("0.3.0-alpha.1", "0.2.0", true)]

    // The tag carries a v and the assembly does not.
    [InlineData("v0.2.0", "0.1.0", true)]

    // Unparseable is never newer. A bad compare should say nothing rather than nag.
    [InlineData("banana", "0.1.0", false)]
    [InlineData("0.2.0", "banana", false)]
    [InlineData("", "0.1.0", false)]
    public void Newer_is_decided_by_number_not_by_text(string candidate, string current, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(candidate, current));

    [Fact]
    public async Task A_newer_release_is_reported_with_where_to_get_it()
    {
        var check = new UpdateCheck(new HttpClient(new GitHub(HttpStatusCode.OK,
            """{"tag_name":"v0.3.0","html_url":"https://example.invalid/r/0.3.0","prerelease":false}""")));

        var status = await check.CheckAsync("0.2.0");

        Assert.True(status.Available);
        Assert.Equal("0.3.0", status.Latest);
        Assert.Equal("https://example.invalid/r/0.3.0", status.Url);
        Assert.Null(status.Problem);
    }

    [Fact]
    public async Task The_same_version_is_not_an_update()
    {
        var check = new UpdateCheck(new HttpClient(new GitHub(HttpStatusCode.OK,
            """{"tag_name":"v0.2.0","html_url":"https://example.invalid/r","prerelease":false}""")));

        Assert.False((await check.CheckAsync("0.2.0")).Available);
    }

    [Fact]
    public async Task GitHub_being_down_is_not_an_update_and_not_a_crash()
    {
        var check = new UpdateCheck(new HttpClient(new GitHub(HttpStatusCode.ServiceUnavailable, "nope")));

        var status = await check.CheckAsync("0.2.0");

        Assert.False(status.Available);
        Assert.Null(status.Latest);
        Assert.NotNull(status.Problem);
    }

    [Fact]
    public async Task An_unreachable_github_is_reported_rather_than_thrown()
    {
        var check = new UpdateCheck(new HttpClient(new Dead()));

        var status = await check.CheckAsync("0.2.0");

        Assert.False(status.Available);
        Assert.NotNull(status.Problem);
    }

    [Fact]
    public async Task A_fresh_answer_is_not_asked_for_twice()
    {
        var handler = new GitHub(HttpStatusCode.OK,
            """{"tag_name":"v0.3.0","html_url":"https://example.invalid/r","prerelease":false}""");

        var check = new UpdateCheck(new HttpClient(handler));

        await check.CheckIfDueAsync("0.2.0");
        await check.CheckIfDueAsync("0.2.0");

        // GitHub rate-limits anonymous callers by address, and the answer changes every few days
        // at its fastest.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_manual_check_asks_anyway()
    {
        var handler = new GitHub(HttpStatusCode.OK,
            """{"tag_name":"v0.3.0","html_url":"https://example.invalid/r","prerelease":false}""");

        var check = new UpdateCheck(new HttpClient(handler));

        await check.CheckIfDueAsync("0.2.0");
        await check.CheckAsync("0.2.0");

        // Somebody pressing the button has a reason to think the cached answer is stale.
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>
    /// A build nobody released is never told there is an update.
    ///
    /// <para>The tests run from a locally-built assembly, so this is the real case rather than a
    /// contrived one: it reports 0.0.0, every release is numerically newer, and without this it
    /// would be told to upgrade every day with nothing to do about it but install over its own
    /// build.</para>
    ///
    /// <para>The version it reports is also the point. It read 1.0.0 before — .NET's default for
    /// an unstamped assembly — which is not any RatNav that has ever existed and is *newer* than
    /// every real release, so the page said "you are on 1.0.0, which is the newest release".</para>
    /// </summary>
    [Fact]
    public async Task A_local_build_is_never_told_to_upgrade()
    {
        var check = new UpdateCheck(new HttpClient(new GitHub(HttpStatusCode.OK,
            """{"tag_name":"v99.0.0","html_url":"https://example.invalid/r","prerelease":false}""")));

        var status = await check.CheckAsync(RatNavVersion.Current);

        Assert.False(RatNavVersion.IsRelease);
        Assert.False(status.IsRelease);

        // The newest release is still reported — it is worth knowing what it is.
        Assert.Equal("99.0.0", status.Latest);

        // But this build is not behind it. It is not on the ladder at all.
        Assert.False(status.Available);
    }

    [Theory]
    // Both the deliberate local default and .NET's own: either means "nobody published this".
    [InlineData("0.0.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("", false)]
    [InlineData("0.2.0", true)]
    [InlineData("0.2.0-alpha.1", true)]
    public void A_version_nobody_stamped_is_not_a_release(string version, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsReleaseVersion(version));

    private sealed class GitHub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Dead : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network");
    }
}
