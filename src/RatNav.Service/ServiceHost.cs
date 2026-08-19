using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Progress;
using RatNav.Core.Sharing;
using RatNav.Core.Tracking;

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

        builder.Services.AddSingleton(_ =>
        {
            var tracker = new ItemTracker(dataDirectory);
            tracker.Load();
            return tracker;
        });

        builder.Services.AddSingleton(_ =>
        {
            var progress = new ProgressStore(dataDirectory);
            progress.Load();
            return progress;
        });

        builder.Services.AddSingleton<RatNavState>();
        builder.Services.AddSingleton(_ => new PlanStore(dataDirectory));
        builder.Services.AddSingleton(_ => RatNavSettings.Load(dataDirectory));
        builder.Services.AddSingleton<RaidHub>();

        builder.Services.AddSingleton(sp =>
        {
            var session = new RaidSession(
                sp.GetRequiredService<RatNavState>(),
                sp.GetRequiredService<ProgressStore>());

            // Every change reaches every surface, so the overlay and the browser cannot disagree.
            var hub = sp.GetRequiredService<RaidHub>();
            session.Changed += (_, view) => hub.Broadcast(view);

            return session;
        });

        builder.Services.AddSingleton<RaidHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RaidHost>());

        var app = builder.Build();

        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapRatNavApi();

        // Live raid state, pushed. Loopback only, like everything else here.
        app.Map("/ws/raid", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var hub = context.RequestServices.GetRequiredService<RaidHub>();
            var session = context.RequestServices.GetRequiredService<RaidSession>();

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.AcceptAsync(socket, session, context.RequestAborted);
        });

        // Anything not an API route falls through to the SPA, so client-side routes survive a
        // refresh. Harmless when wwwroot is empty, which it is until the web app is built.
        app.MapFallbackToFile("index.html");

        return app;
    }
}
