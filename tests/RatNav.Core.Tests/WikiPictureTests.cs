namespace RatNav.Core.Tests;

using RatNav.Core.Data;

/// <summary>
/// Serving a wiki picture through RatNav rather than letting the page load it.
///
/// <para>The page cannot load them: the wiki's CDN answers a request carrying a foreign
/// <c>Referer</c> with a 404 and a placeholder, so the carousel drew correct titles over broken
/// pictures.</para>
///
/// <para>What is tested here is the part that must not be got wrong — an endpoint that fetches a
/// URL from its query string is an open proxy unless something says which hosts are allowed. These
/// all refuse before any request is made, so none of them touches the network.</para>
/// </summary>
public class WikiPictureTests
{
    private static WikiImages Subject() =>
        new(new HttpClient(), Path.Combine(Path.GetTempPath(), "ratnav-tests", Guid.NewGuid().ToString()));

    [Theory]
    [InlineData("https://example.com/evil.png")]
    [InlineData("http://127.0.0.1:8722/api/settings")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("file:///C:/Windows/System32/config/SAM")]
    [InlineData("https://wikia.nocookie.net.evil.com/x.png")]
    [InlineData("https://fandom.com.attacker.example/x.png")]
    public async Task Only_the_wikis_own_hosts_are_fetched(string url) =>
        Assert.Null(await Subject().PictureAsync(url));

    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("/relative/path.png")]
    public async Task Nonsense_is_refused_rather_than_thrown_on(string url) =>
        Assert.Null(await Subject().PictureAsync(url));
}
