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
    /// Default port. Loopback only unless the LAN setting says otherwise — RatNav has no accounts
    /// and no auth, so reaching it from the network is an explicit opt-in.
    /// </summary>
    public const int DefaultPort = 8722;

    /// <summary>
    /// The port actually in use this run, once <see cref="Build"/> has settled it.
    ///
    /// <para>Everything in the app that talks to the service reads this rather than the constant.
    /// A configurable port that eleven call sites ignore is not configurable.</para>
    /// </summary>
    public static int Port { get; private set; } = DefaultPort;

    /// <summary>
    /// Where the service is, from this machine. Always loopback: the app talks to its own service
    /// over 127.0.0.1 whether or not the network can reach it too.
    /// </summary>
    public static string Root => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// The port that was asked for, when something else already had it and RatNav moved.
    ///
    /// <para>Null when it got what it wanted, which is almost always. Worth surfacing when it is
    /// not: a bookmark to the old port stops working, and silently answering somewhere else is
    /// how you spend an evening debugging a browser tab.</para>
    /// </summary>
    public static int? MovedFrom { get; private set; }

    /// <summary>
    /// The first port at or after <paramref name="wanted"/> that nothing else is using.
    ///
    /// <para>Tested by binding it, because that is the only question that matters and the only
    /// one with a reliable answer — a port can be free a moment before it is not. The window
    /// between the test and Kestrel's own bind is small and the alternative is not starting.</para>
    /// </summary>
    private static int FirstFree(int wanted, bool anyAddress)
    {
        for (var port = wanted; port < wanted + 24 && port <= 65535; port++)
        {
            var listener = new System.Net.Sockets.TcpListener(
                anyAddress ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback,
                port);

            try
            {
                listener.Start();
                return port;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Taken. Try the next one.
            }
            finally
            {
                try { listener.Stop(); } catch (System.Net.Sockets.SocketException) { }
            }
        }

        // Two dozen consecutive ports in use is not a conflict, it is something else wrong. Let
        // Kestrel fail on the one that was asked for, so the error names the real port.
        return wanted;
    }

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

        var dataDirectory = RatNavPaths.EnsureDataDirectory();

        // Loaded here rather than only in the container, because where to listen is the one
        // decision that cannot be taken later: Kestrel binds at start-up and does not move.
        var settings = RatNavSettings.Load(dataDirectory);
        var lan = settings.Lan;

        // Moved out of the way if something already has it, rather than refusing to start.
        //
        // This is what makes the port worth putting on the Setup page at all: the page lives
        // inside the service, so a port conflict that stops the service is a conflict you cannot
        // reach the setting to fix. RatNav would be telling you to edit settings.json by hand,
        // which is the thing the setting was added to avoid.
        var wanted = lan.Port > 0 ? lan.Port : port;

        Port = FirstFree(wanted, lan.Enabled);
        MovedFrom = Port == wanted ? null : wanted;

        builder.WebHost.ConfigureKestrel(options =>
        {
            // One or the other on the same port, never both: every address includes loopback, so
            // asking for loopback as well is asking for the same socket twice.
            //
            // Every address is what a phone or an iPad on the same wifi types in. No port
            // forwarding is involved and none should be — that is a router-to-internet thing, and
            // without it the router is still the wall.
            if (lan.Enabled) options.ListenAnyIP(Port);
            else options.ListenLocalhost(Port);
        });

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

        // The same instance the port was read from. Loading twice would work — migrations are
        // gated on a revision and would not run again — but two objects for one settings file is
        // two things that can disagree.
        builder.Services.AddSingleton(settings);

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
