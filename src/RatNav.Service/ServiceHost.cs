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
        // The content root has to be the assembly's folder, not the working directory. `dotnet
        // run` sets it to wherever it was invoked from, and the WPF app hosts this service
        // in-process — so the default served 404 for every page while the API answered perfectly,
        // which is a confusing way to fail.
        //
        // It is passed in at construction rather than assigned afterwards: the static file
        // provider is built during CreateBuilder, so setting WebRootPath on the environment later
        // changes a property nothing reads again.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = FindWebRoot(),
        });

        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        var dataDirectory = RatNavPaths.EnsureDataDirectory();

        // Which character is being tracked. Everything belonging to one lives in its own
        // directory; the cached game data, the map images and the machine's own settings sit
        // outside and are shared, because none of that changes when you switch character.
        var profile = new RatNavProfile(dataDirectory);
        profile.AdoptLooseFiles();
        profile.Restore();

        builder.Services.AddSingleton(profile);

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
            var tracker = new ItemTracker(profile);
            tracker.Load();
            return tracker;
        });

        // Its own client: the wiki is a different host from tarkov.dev, and a tool that identifies
        // itself is one a wiki can talk to about rate limits rather than simply block.
        builder.Services.AddHttpClient<WikiImages>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "RatNav/1.0 (+https://github.com/mrthames/RatNav)");
        })
        .AddTypedClient((http, _) => new WikiImages(http, dataDirectory));

        // Its own client, because item icons come from a different host from the game data and
        // there are a few hundred of them on the first scan.
        builder.Services.AddSingleton(_ =>
        {
            var marks = new CustomWaypointStore(profile);
            marks.Load();
            return marks;
        });

        builder.Services.AddSingleton(_ => RatNavSettings.Load(dataDirectory));

        builder.Services.AddSingleton(sp =>
        {
            var progress = new ProgressStore(profile);
            progress.Load();

            // Character level used to live in settings, which are shared across characters. On
            // the first launch after it moved, carry the old value into whichever profile is
            // open — otherwise upgrading silently drops the level and quietly narrows every
            // list that depends on it, with nothing on screen saying why.
            if (progress.PlayerLevel is null
                && sp.GetRequiredService<RatNavSettings>().PlayerLevel is { } inherited)
            {
                progress.SetPlayerLevel(inherited);
            }

            return progress;
        });

        builder.Services.AddSingleton<RatNavState>();
        builder.Services.AddSingleton(_ => new PlanStore(profile));
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

        // The stores are singletons that keep their state in memory, so switching character has
        // to tell each one to look at the new directory. Wired here rather than inside the stores
        // because this is where the instances exist.
        profile.Changed += () =>
        {
            app.Services.GetRequiredService<ItemTracker>().Load();
            app.Services.GetRequiredService<ProgressStore>().Load();
            app.Services.GetRequiredService<CustomWaypointStore>().Load();
        };

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

    /// <summary>
    /// Looks for the built web app beside the assembly first, then in the service project — so a
    /// published build and a developer running from source both work without configuration.
    /// </summary>
    private static string? FindWebRoot()
    {
        var candidates = new[]
        {
            // Published, or built with the web app copied alongside.
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),

            // Running from source before `npm run build` has copied anything across.
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RatNav.Service", "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RatNav.Service", "wwwroot"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }

        return null;
    }
}
