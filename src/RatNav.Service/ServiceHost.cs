using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RatNav.Core;
using RatNav.Core.Data;

namespace RatNav.Service;

/// <summary>
/// Builds the local service. The WPF app hosts this in-process so there is one executable and
/// one tray icon; <c>dotnet run --project src/RatNav.Service</c> starts the same thing
/// standalone for web development.
/// </summary>
public static class ServiceHost
{
    /// <summary>
    /// Default port. Bound to loopback only — RatNav has no accounts and no auth, so it must
    /// not be reachable from the network. Sharing to a phone is an explicit opt-in, later.
    /// </summary>
    public const int DefaultPort = 8722;

    public static WebApplication Build(string[]? args = null, int port = DefaultPort)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        var dataDirectory = RatNavPaths.EnsureDataDirectory();

        builder.Services.AddHttpClient<TarkovDevClient>(http =>
        {
            // Identifying ourselves is basic courtesy to a free community API, and it gives
            // them something to point at if RatNav ever misbehaves.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RatNav/0.1 (+https://ratnav.dev)");
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddHttpClient<MapAssets>(http =>
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RatNav/0.1 (+https://ratnav.dev)");
            http.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddTypedClient((http, _) => new MapAssets(http, dataDirectory));

        builder.Services.AddSingleton(sp => new GameDataCache(
            sp.GetRequiredService<TarkovDevClient>(),
            sp.GetRequiredService<MapAssets>(),
            dataDirectory));

        builder.Services.AddSingleton<RatNavState>();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapRatNavApi();

        // Anything not an API route falls through to the SPA, so client-side routes survive a
        // refresh. Harmless when wwwroot is empty, which it is until the web app is built.
        app.MapFallbackToFile("index.html");

        return app;
    }
}
