using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RatNav.Core;
using RatNav.Core.Data;
using RatNav.Core.Game;
using RatNav.Core.Maps;
using RatNav.Core.Model;
using RatNav.Core.Planning;
using RatNav.Core.Progress;
using RatNav.Core.Sharing;
using RatNav.Core.Tracking;
using RatNav.Core.Watchers;

namespace RatNav.Service;

public static class ApiEndpoints
{
    /// <summary>
    /// Raised when hotkeys change, so the desktop app can rebind them without a restart. An event
    /// rather than a dependency, because the service knows nothing about windows.
    /// </summary>
    public static event Action<RatNavSettings>? HotkeysChanged;

    /// <summary>
    /// Raised when anything the items list is built from changes — a watchlist entry, a have
    /// count, a hideout level or target, a quest state.
    ///
    /// <para>The overlay used to reload its items only when the <i>raid</i> changed, so starring
    /// something in the buddy app never reached it. Pushed rather than polled: the plumbing to
    /// tell every surface at once already exists, and a timer would be slower and busier.</para>
    /// </summary>
    public static event Action? ItemsChanged;

    public static void MapRatNavApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---- status and refresh

        api.MapGet("/status", async (RatNavState state) =>
        {
            var refresh = await state.Cache.EnsureFreshAsync();
            return Results.Ok(state.Status(refresh));
        });

        api.MapPost("/refresh", async (RatNavState state) =>
        {
            var refresh = await state.Cache.RefreshAsync();

            // A failed refresh is not an HTTP error: we still have data, and the UI needs to
            // render it alongside an honest note about why it is old.
            return Results.Ok(state.Status(refresh));
        });

        // ---- items

        // Search returns the same shape as the needed and watchlist views, so one table
        // component renders all three and a row behaves identically wherever you found it.
        api.MapGet("/items/search", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            string q, int? limit) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var hideout = HideoutPlanner.Demand(state.Upcoming(progress, settings.HideoutLookAhead));

            var results = index.Search(q, limit ?? 25)
                .Select(item => index.GetNeeds(item.Id) ?? new ItemNeeds { Item = item })
                .Select(needs => TrackedItemView.From(tracker.Track(needs, progress, hideout)));

            return Results.Ok(results);
        });

        // What to actually pick up: only what active quests and un-built modules want, minus
        // what you already have. The unfiltered version is every item the game will ever ask
        // for, which is not a shopping list.
        api.MapGet("/items/needed", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            int? lookAhead, string? sort) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var hideout = HideoutPlanner.Demand(
                state.Upcoming(progress, lookAhead ?? settings.HideoutLookAhead));

            var rows = index.AllNeeded()
                .Select(n => tracker.Track(n, progress, hideout))
                .Where(t => t.Remaining > 0);

            // "next" answers a different question from the default. The default is what to grab if
            // you see it; "next" is what stands between you and finishing something, so it leads
            // with the nearest hideout wave and only then with quantity.
            rows = sort?.ToLowerInvariant() == "next"
                ? rows.OrderBy(t => t.HideoutWave ?? int.MaxValue)
                      .ThenByDescending(t => t.FoundInRaid)
                      .ThenByDescending(t => t.Remaining)
                : rows.OrderByDescending(t => t.FoundInRaid)
                      .ThenByDescending(t => t.Remaining);

            return Results.Ok(rows.Select(TrackedItemView.From));
        });

        api.MapGet("/items/watchlist", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings) =>
        {
            if (state.Index is not { } index) return Results.Ok(Array.Empty<object>());

            var hideout = HideoutPlanner.Demand(state.Upcoming(progress, settings.HideoutLookAhead));

            // The watchlist counts *your* number.
            //
            // It used to report quest and hideout need, which the Needed tab already reports — so
            // the same item read one figure here and another there, and neither was the target you
            // set. Quest and hideout counts stay on the row so it can still say why the item
            // matters elsewhere; they just stop deciding how many you are short.
            var results = tracker.Watchlist.Select(entry =>
            {
                var needs = index.GetNeeds(entry.ItemId)
                    ?? new ItemNeeds { Item = index.GetItem(entry.ItemId) ?? Unknown(entry.ItemId) };

                var tracked = tracker.Track(needs, progress, hideout);
                var target = entry.Target ?? 0;

                return TrackedItemView.From(tracked) with
                {
                    Needed = target,

                    // The watchlist's own count, not the stash total. What is set aside for the
                    // hideout is not available for this, and one shared number cannot say so.
                    Have = entry.Have,
                    Remaining = Math.Max(0, target - entry.Have),
                    WatchTarget = entry.Target,
                };
            });

            return Results.Ok(results);
        });

        api.MapPost("/items/{id}/have", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            string id, HaveRequest request) =>
        {
            if (request.Delta is { } delta) tracker.AdjustHave(id, delta);
            else if (request.Count is { } count) tracker.SetHave(id, count);
            else return Results.BadRequest(new { error = "Send either a count or a delta." });

            ItemsChanged?.Invoke();
            return Results.Ok(Track(state, tracker, progress, settings, id));
        });

        api.MapPost("/items/{id}/watch", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            string id, WatchRequest request) =>
        {
            if (request.Watch) tracker.Watch(id, request.Note, request.Target, request.Have);
            else tracker.Unwatch(id);

            ItemsChanged?.Invoke();
            return Results.Ok(Track(state, tracker, progress, settings, id));
        });

        api.MapGet("/items/{id}", (RatNavState state, ItemTracker tracker, string id) =>
        {
            if (state.Index is not { } index) return Results.NotFound();

            var item = index.GetItem(id);
            if (item is null) return Results.NotFound();

            return Results.Ok(Detail(index, tracker, item, null));
        });

        // Identifies an item from text read off the screen — the "what is this junk for?" question,
        // answered without leaving the game.
        //
        // The text is supplied by the caller rather than captured here, which keeps every pixel
        // that touches the screen in the desktop app and leaves this side testable with strings.
        api.MapPost("/items/identify", (RatNavState state, ItemTracker tracker, IdentifyRequest request) =>
        {
            if (state.Index is not { } index) return Results.NotFound();

            var lines = request.Lines is { Count: > 0 }
                ? request.Lines
                : (request.Text ?? "").Split(
                    ['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var matches = ItemMatcher.Identify(lines, state.Cache.Current?.Items ?? [], limit: 5);

            return Results.Ok(new
            {
                // Every candidate, not just the winner. OCR misreads, and being able to say "no,
                // the one below it" is the difference between a useful tool and a frustrating one.
                matches = matches.Select(m => Detail(index, tracker, m.Item, m.Confidence)),
                readText = lines,
            });
        });

        // ---- traders

        // Traders were missing entirely, and they are half of how quests are organised — you go
        // to a trader, not to a quest list.
        //
        // Loyalty is set by hand. Nothing the game writes to disk reports it, and the endpoint
        // that would needs your account credentials, which RatNav will not ask for.
        api.MapGet("/traders", (RatNavState state, ProgressStore progress, RatNavSettings settings) =>
        {
            var tasks = state.Cache.Current?.Tasks ?? [];

            var available = progress
                .AvailableNow(tasks, settings.PlayerLevel)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var traders =
                from task in tasks
                where task.TraderName is { Length: > 0 }
                group task by task.TraderName! into byTrader
                orderby byTrader.Key
                select new
                {
                    name = byTrader.Key,

                    // Keyed by name rather than id: tasks carry the resolved name, and a separate
                    // id would mean a second lookup for no gain.
                    level = progress.TraderLevelOf(byTrader.Key),

                    total = byTrader.Count(),
                    completed = byTrader.Count(t => progress.StateOf(t.Id) == QuestState.Completed),
                    active = byTrader.Count(t => progress.StateOf(t.Id) == QuestState.Active),
                    availableNow = byTrader.Count(t => available.Contains(t.Id)),

                    // What you could pick up from them right now, which is the reason to look.
                    next = byTrader
                        .Where(t => available.Contains(t.Id))
                        .OrderBy(t => t.MinPlayerLevel ?? 0)
                        .Take(5)
                        .Select(t => new { t.Id, t.Name, t.MinPlayerLevel, t.WikiUrl }),
                };

            return Results.Ok(traders);
        });

        api.MapPost("/traders/{name}/level", (ProgressStore progress, string name, TraderLevelRequest request) =>
        {
            progress.SetTraderLevel(name, request.Level);
            return Results.Ok(new { name, level = progress.TraderLevelOf(name) });
        });

        // ---- hideout

        // What the hideout is, what it could become next, and what that costs.
        //
        // The look-ahead is the whole point. Every un-built level wants items, so the unfiltered
        // answer is hundreds of items for upgrades gated behind three others you have not started
        // — a list nobody can shop from. Waves make the number mean something: 1 is what you could
        // build tonight.
        api.MapGet("/hideout", (
            RatNavState state, ProgressStore progress, RatNavSettings settings, ItemTracker tracker, int? lookAhead) =>
        {
            var data = state.Cache.Current;
            if (data is null) return Results.Ok(new { stations = Array.Empty<object>(), upcoming = Array.Empty<object>() });

            var depth = lookAhead ?? settings.HideoutLookAhead;
            var upcoming = state.Upcoming(progress, depth);
            var index = state.Index;

            var stations = data.HideoutStations
                .OrderBy(st => st.Name, StringComparer.OrdinalIgnoreCase)
                .Select(st => new
                {
                    id = st.Id,
                    name = st.Name,
                    builtLevel = progress.HideoutLevelOf(st.Id),
                    maxLevel = st.Levels.Count == 0 ? 0 : st.Levels.Max(l => l.Level),
                });

            return Results.Ok(new
            {
                lookAhead = depth,
                stations,
                upcoming = upcoming.Select(u => new
                {
                    stationId = u.StationId,
                    stationName = u.StationName,
                    level = u.Level,
                    wave = u.Wave,
                    targeted = u.Targeted,
                    description = u.Description,
                    constructionTimeSeconds = u.ConstructionTimeSeconds,
                    blockers = u.Blockers.Select(b => new { kind = b.Kind, text = b.Text }),

                    // Costs carry what you already have, so the view can show what is left rather
                    // than what it wants in total — the useful number when shopping.
                    items = u.ItemRequirements.Select(r => new
                    {
                        itemId = r.ItemId,
                        name = index?.GetItem(r.ItemId)?.Name ?? "Unknown item",
                        shortName = index?.GetItem(r.ItemId)?.ShortName,
                        count = r.Count,
                        have = tracker.GetHave(r.ItemId),
                        foundInRaid = r.FoundInRaid,
                    }),
                }),
            });
        });

        api.MapPost("/hideout/look-ahead", (RatNavSettings settings, LookAheadRequest request) =>
        {
            settings.Remember(s => s.HideoutLookAhead = Math.Clamp(request.Levels, 1, 10));

            ItemsChanged?.Invoke();
            return Results.Ok(new { lookAhead = settings.HideoutLookAhead });
        });

        // Picking upgrades out narrows the items list to them. Without this the look-ahead can
        // only ever widen the list, and widening is not what someone with a plan wants.
        api.MapPost("/hideout/{stationId}/levels/{level:int}/target", (
            ProgressStore progress, string stationId, int level, TargetRequest request) =>
        {
            progress.TargetHideoutLevel(stationId, level, request.Targeted);

            ItemsChanged?.Invoke();
            return Results.Ok(new { stationId, level, targeted = request.Targeted });
        });

        // The overlay's items list, already grouped.
        //
        // Grouping lives here rather than in the overlay because the rule for which section a row
        // belongs to is real domain logic — active quests and buildable-now upgrades are things
        // you can finish this raid; everything else is things you cannot — and the overlay should
        // not be re-deriving that from a flat list.
        api.MapGet("/items/panel", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            int? lookAhead) =>
        {
            if (state.Index is not { } index)
                return Results.Ok(new ItemPanel());

            var depth = lookAhead ?? settings.HideoutLookAhead;
            var upcoming = state.Upcoming(progress, depth);

            // Split by reachability, not by depth. Wave 1 is what nothing is standing in the way
            // of; the rest is gated behind an upgrade you have not built.
            var now = HideoutPlanner.Demand(upcoming.Where(u => u.Wave == 1));
            var later = HideoutPlanner.Demand(upcoming.Where(u => u.Wave > 1));

            var watched = tracker.Watchlist.Select(w => w.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Quests you could accept today, not every quest in the game. Without this the section
            // is several thousand rows — everything the whole wipe will ever ask for — which is not
            // something anyone can read, let alone act on.
            var acceptable = progress
                .AvailableNow(state.Cache.Current?.Tasks ?? [])
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var panel = new ItemPanel();

            foreach (var needs in index.AllNeeded())
            {
                var id = needs.Item.Id;

                // The watchlist is what you chose by hand, so it wins the row wherever else the
                // item might also belong — otherwise unchecking it appears to do nothing.
                if (watched.Contains(id)) continue;

                var tracked = tracker.Track(needs, progress, now);

                if (tracked.Remaining > 0 && (tracked.QuestNeeded > 0 || tracked.HideoutNeeded > 0))
                {
                    panel.Now.Add(PanelRow.From(tracked, Why(tracked, needs, progress)));
                    placed.Add(id);
                    continue;
                }

                // Everything else: gated hideout upgrades, and quests you have not accepted. Worth
                // knowing about before you vendor something, not worth reading mid-raid.
                var gated = later.GetValueOrDefault(id);
                var future = needs.Quests.Where(q => acceptable.Contains(q.TaskId)).ToList();

                if (gated is null && future.Count == 0) continue;

                var reason = gated is not null
                    ? gated.UpgradeName
                    : $"{future[0].TaskName}{(future.Count > 1 ? $" +{future.Count - 1}" : "")}";

                panel.Later.Add(new PanelRow
                {
                    Id = id,
                    Name = Short(needs.Item),
                    FullName = needs.Item.Name,
                    Count = gated?.Count ?? future.Sum(q => q.Count),
                    Reason = reason,
                    FoundInRaid = gated?.FoundInRaid ?? future.Any(q => q.FoundInRaid),
                });
            }

            foreach (var entry in tracker.Watchlist)
            {
                var item = index.GetItem(entry.ItemId) ?? Unknown(entry.ItemId);
                var have = entry.Have;

                // Your number, not the game's. The watchlist is what *you* decided to collect —
                // quests and the hideout have their own section, and borrowing their totals here
                // made the same item read one figure in one place and another figure below it.
                var wanted = entry.Target;

                panel.Watchlist.Add(new PanelRow
                {
                    Id = entry.ItemId,
                    Name = Readable(item),
                    FullName = item.Name,
                    Count = wanted is { } target ? Math.Max(0, target - have) : 0,

                    // Without a target there is nothing to finish, so nothing to tick off. A row
                    // that says "done" when you never said how many you wanted is a lie.
                    Tracked = wanted is not null,

                    Reason = entry.Note is { Length: > 0 } note
                        ? note
                        : wanted is { } t ? $"{have} of {t}" : "watching",

                    FoundInRaid = false,
                });
            }

            // Found-in-raid first: it is the one thing you cannot buy your way out of later.
            panel.Now.Sort(ByUrgency);
            panel.Later.Sort(ByUrgency);

            // A glanceable panel has a length past which it stops being glanceable. Cut, and said
            // so — a list that silently stops reads as "that is everything".
            const int mostRowsWorthShowing = 60;

            if (panel.Later.Count > mostRowsWorthShowing)
            {
                panel.LaterHidden = panel.Later.Count - mostRowsWorthShowing;
                panel.Later.RemoveRange(mostRowsWorthShowing, panel.LaterHidden);
            }

            panel.LookAhead = depth;
            return Results.Ok(panel);
        });

        // ---- progress

        api.MapGet("/progress", (RatNavState state, ProgressStore progress, RatNavSettings settings) =>
        {
            var tasks = state.Cache.Current?.Tasks ?? [];
            var summary = progress.Summarize(tasks);

            return Results.Ok(new
            {
                notStarted = summary[QuestState.NotStarted],
                active = summary[QuestState.Active],
                completed = summary[QuestState.Completed],
                failed = summary[QuestState.Failed],
                availableNow = progress.AvailableNow(tasks, settings.PlayerLevel).Count(),
            });
        });

        api.MapPost("/progress/tasks/{id}", (ProgressStore progress, string id, TaskStateRequest request) =>
        {
            if (!Enum.TryParse<QuestState>(request.State, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Unknown quest state '{request.State}'." });

            progress.SetManual(id, parsed);

            ItemsChanged?.Invoke();
            return Results.Ok(new { id, state = parsed.ToString() });
        });

        api.MapPost("/progress/hideout/{id}", (ProgressStore progress, string id, HideoutLevelRequest request) =>
        {
            progress.SetHideoutLevel(id, request.Level);

            ItemsChanged?.Invoke();
            return Results.Ok(new { id, level = progress.HideoutLevelOf(id) });
        });

        // ---- tasks

        api.MapGet("/tasks", (
            RatNavState state, ProgressStore progress, RatNavSettings settings, string? filter, string? q) =>
        {
            var tasks = state.Cache.Current?.Tasks ?? [];

            var available = progress.AvailableNow(tasks, settings.PlayerLevel)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var names = tasks
                .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

            var rows = tasks.Select(t => TaskSummary.From(
                t, progress.StateOf(t.Id), available.Contains(t.Id),
                settings.PlayerLevel, names, progress.StateOf));

            // Four groups, and each answers a different question.
            //
            // "Available" was not redundant with "active", it was badly named: one is what you
            // accepted, the other is what you could accept. Renamed to "ready", which is what it
            // always meant, and "todo" is gone because it genuinely was redundant.
            rows = filter?.ToLowerInvariant() switch
            {
                "active" => rows.Where(t => t.State == nameof(QuestState.Active)),
                "ready" => rows.Where(t => t.Available),

                // Failed sits with done because both are finished — but it stays distinguishable,
                // so a wipe's failures can be seen rather than quietly folded into successes.
                "done" => rows.Where(t =>
                    t.State is nameof(QuestState.Completed) or nameof(QuestState.Failed)),

                // Neither started, nor reachable: waiting on a level or on another quest.
                "locked" => rows.Where(t => t.State == nameof(QuestState.NotStarted) && !t.Available),

                _ => rows,
            };

            if (q is { Length: > 0 })
            {
                rows = rows.Where(t =>
                    t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.TraderName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            // Active first — those are the ones with items worth picking up tonight — then what
            // is unlocked, then the rest, each alphabetical inside its group.
            return Results.Ok(rows
                .OrderByDescending(t => t.State == nameof(QuestState.Active))
                .ThenByDescending(t => t.Available)
                .ThenBy(t => t.TraderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        });

        // Objectives of the quests you have active on a given map — what the plan builder offers.
        api.MapGet("/maps/{id}/plannable", (RatNavState state, ProgressStore progress, string id) =>
        {
            var data = state.Cache.Current;
            var map = FindMap(state, id);
            if (data is null || map?.Image is null) return Results.NotFound();

            var transform = new CoordinateTransform(map.Image);

            var rows =
                from task in data.Tasks
                where progress.StateOf(task.Id) == QuestState.Active
                from objective in task.Objectives
                where objective.Position is not null && objective.MapIds.Contains(map.Id)
                let position = objective.Position.GetValueOrDefault()
                let point = transform.ToNormalized(position)
                select new
                {
                    objectiveId = objective.Id,
                    taskId = task.Id,
                    taskName = task.Name,
                    traderName = task.TraderName,
                    description = objective.Description,
                    optional = objective.Optional,
                    x = point.X,
                    y = point.Y,
                    place = map.NearestLabel(position)?.Text,
                    neededKeyItemIds = objective.NeededKeyItemIds,
                    itemIds = objective.Items.Select(i => i.ItemId),
                };

            return Results.Ok(rows);
        });

        api.MapGet("/tasks/{id}", (RatNavState state, string id) =>
        {
            var task = state.Cache.Current?.Tasks.FirstOrDefault(t => t.Id == id);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        // ---- settings

        // What Setup edits. Everything here is either detected or chosen by a person — none of it
        // is baked in, because someone else's install is not going to look like the developer's.
        api.MapGet("/settings", (RatNavSettings settings, RatNavState state, ProgressStore progress) =>
            Results.Ok(SettingsView.From(settings) with
            {
                SuggestedPlayerLevel = progress.LevelImpliedBy(state.Cache.Current?.Tasks ?? []),
            }));

        api.MapPost("/settings", (
            RatNavSettings settings, RatNavState state, ProgressStore progress, RaidHost host,
            SettingsUpdate update) =>
        {
            // A folder that is not an install is worth refusing rather than accepting quietly:
            // the symptom of a wrong path is an overlay that shows nothing, which looks identical
            // to RatNav being broken.
            if (update.GameDirectory is { Length: > 0 } directory
                && GameInstallFinder.Describe(directory) is null)
            {
                return Results.BadRequest(new
                {
                    error = "That folder does not look like an Escape from Tarkov install — "
                          + "there is no Logs folder inside it. Pick the folder containing "
                          + "EscapeFromTarkov.exe.",
                });
            }

            var watchersAffected =
                update.GameDirectory != settings.GameDirectory
                || update.ScreenshotDirectory != settings.ScreenshotDirectory
                || (update.ScreenshotDisposal is { Length: > 0 } d
                    && !string.Equals(d, settings.ScreenshotDisposal.ToString(), StringComparison.OrdinalIgnoreCase));

            settings.Remember(current =>
            {
                // Empty means "go back to detecting it", which is a different answer from "leave
                // it alone" — so blank clears rather than being ignored.
                if (update.GameDirectory is not null)
                    current.GameDirectory = Blank(update.GameDirectory);

                if (update.ScreenshotDirectory is not null)
                    current.ScreenshotDirectory = Blank(update.ScreenshotDirectory);

                if (update.ScreenshotKey is { Length: > 0 } key) current.ScreenshotKey = key;
                if (update.PlayerLevel is { } level) current.PlayerLevel = Math.Clamp(level, 1, 79);
                if (update.GameEdition is { Length: > 0 } edition) current.GameEdition = edition;
                if (update.Owner is not null) current.Owner = Blank(update.Owner);

                if (update.ScreenshotDisposal is { Length: > 0 } disposal
                    && Enum.TryParse<ScreenshotDisposal>(disposal, ignoreCase: true, out var parsed))
                {
                    current.ScreenshotDisposal = parsed;
                }

                if (update.Hotkeys is { } keys)
                {
                    current.Hotkeys = new RatNavSettings.HotKeySettings
                    {
                        ToggleOverlay = keys.ToggleOverlay ?? current.Hotkeys.ToggleOverlay,
                        ToggleInteract = keys.ToggleInteract ?? current.Hotkeys.ToggleInteract,
                        ExpandPanel = keys.ExpandPanel ?? current.Hotkeys.ExpandPanel,
                        CompleteObjective = keys.CompleteObjective ?? current.Hotkeys.CompleteObjective,
                        ToggleMode = keys.ToggleMode ?? current.Hotkeys.ToggleMode,
                        IdentifyItem = keys.IdentifyItem ?? current.Hotkeys.IdentifyItem,
                    };
                }
            });

            // Applied immediately. Being told to restart the app is a poor answer to "RatNav
            // cannot see my game" — that is the moment someone is least willing to be patient.
            // The edition decides the stash you start with, which is not an upgrade you built and
            // should not be sitting in "buildable now" waiting for you to notice.
            if (update.GameEdition is { Length: > 0 })
                SeedStash(state, progress, settings.GameEdition);

            if (watchersAffected) host.Rewatch();
            if (update.Hotkeys is not null) HotkeysChanged?.Invoke(settings);

            return Results.Ok(SettingsView.From(settings));
        });

        // ---- setup

        api.MapGet("/diagnostics", (RatNavSettings settings, RatNavState state, RaidHost host) =>
            Results.Ok(Diagnostics.Build(settings, ServiceHost.DefaultPort, state.Status(host.LastRefresh))));

        // ---- the live raid

        api.MapGet("/raid", (RaidSession session) => Results.Ok(session.View()));

        api.MapPost("/raid/objectives/{id}", (RaidSession session, string id, DoneRequest request) =>
        {
            session.Complete(id, request.Done);
            return Results.Ok(session.View());
        });

        // A plan outlives the raid it was built for, so it is editable between raids. Striking off
        // what is no longer worth doing and keeping the rest is the usual move after extracting.
        api.MapDelete("/raid/stops/{id}", (RaidSession session, string id) =>
        {
            session.RemoveStop(id);
            return Results.Ok(session.View());
        });

        api.MapDelete("/raid/plan", (RaidSession session, RatNavSettings settings) =>
        {
            session.ClearPlan();
            settings.Remember(s => s.ActivePlanId = null);
            return Results.Ok(session.View());
        });

        // Quests whose every planned objective is done, waiting on a trader.
        //
        // RatNav will not mark these complete on its own. Finishing the objectives and handing the
        // quest in are different events, the game does not reliably log the second, and a quest
        // marked complete retires its item needs — so guessing wrong quietly deletes a shopping
        // list. It asks instead.
        api.MapGet("/raid/turn-ins", (RatNavState state, RaidSession session, ProgressStore progress) =>
        {
            var view = session.View();
            var tasks = (state.Cache.Current?.Tasks ?? []).ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

            var done = view.CompletedObjectiveIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var ready =
                from stop in view.Stops
                group stop by stop.TaskId into byTask
                where byTask.All(s => done.Contains(s.ObjectiveId))
                let task = tasks.GetValueOrDefault(byTask.Key)
                where progress.StateOf(byTask.Key) != QuestState.Completed
                select new
                {
                    taskId = byTask.Key,
                    taskName = byTask.First().TaskName,
                    traderName = task?.TraderName,
                    objectiveCount = byTask.Count(),

                    // Objectives you did not plan are not evidence of anything. Saying how many of
                    // the quest you actually covered is what makes "turned in?" answerable.
                    totalObjectiveCount = task?.Objectives.Count ?? byTask.Count(),
                    wikiUrl = task?.WikiUrl,
                };

            return Results.Ok(ready);
        });

        // Ends the raid by hand. The log watcher normally does this on its own, but the game does
        // not write a "raid over" line — it writes that it is re-preparing your profile — and a
        // log that rolls over or a launcher cache wipe can lose the moment. A raid you cannot
        // dismiss is worse than one RatNav never noticed.
        api.MapPost("/raid/end", (RaidSession session) =>
        {
            session.OnRaidEnded();
            return Results.Ok(session.View());
        });

        // ---- plans

        api.MapGet("/plans", (PlanStore plans) =>
            Results.Ok(plans.All().Select(p => new
            {
                p.Id,
                p.Document.MapId,
                p.Document.MapName,
                p.Document.Owner,
                p.Document.CreatedAt,
                stops = p.Document.Stops.Count,
                keys = p.Document.RequiredKeyItemIds.Count,
            })));

        api.MapGet("/plans/{id}", (PlanStore plans, string id) =>
        {
            var saved = plans.Get(id);
            return saved is null ? Results.NotFound() : Results.Ok(saved.Document);
        });

        // Builds a route from the objectives you ticked, and saves it.
        api.MapPost("/plans", (
            RatNavState state, PlanStore plans, RatNavSettings settings, BuildPlanRequest request) =>
        {
            var map = FindMap(state, request.MapId);
            if (map is null) return Results.NotFound(new { error = $"No map called '{request.MapId}'." });

            var chosen = new HashSet<string>(request.ObjectiveIds, StringComparer.OrdinalIgnoreCase);
            var data = state.Cache.Current;

            var waypoints =
                (from task in data?.Tasks ?? []
                 from objective in task.Objectives
                 where chosen.Contains(objective.Id) && objective.Position is not null
                 select new Waypoint
                 {
                     ObjectiveId = objective.Id,
                     TaskId = task.Id,
                     TaskName = task.Name,
                     Description = objective.Description,
                     Position = objective.Position.GetValueOrDefault(),
                     TraderName = task.TraderName,
                     Optional = objective.Optional,
                     NeededKeyItemIds = objective.NeededKeyItemIds,
                 }).ToList();

            if (waypoints.Count == 0)
                return Results.BadRequest(new { error = "None of those objectives have a position on this map." });

            var plan = RaidPlanner.Plan(map, waypoints);
            var document = PlanDocument.From(plan, settings.Owner, request.ShoppingListItemIds);

            return Results.Ok(new { id = plans.Save(document), plan = document });
        });

        // Makes a saved plan the one the overlay follows.
        api.MapPost("/plans/{id}/activate", (
            RatNavState state, PlanStore plans, RaidSession session, RatNavSettings settings, string id) =>
        {
            var saved = plans.Get(id);
            if (saved is null) return Results.NotFound();

            var map = FindMap(state, saved.Document.MapId);
            if (map is null) return Results.NotFound(new { error = "That plan's map is not loaded." });

            session.UsePlan(PlanConversion.ToPlan(saved.Document, map, state.Cache.Current), map);

            // Remembered so the plan is still there after a restart. Rebuilding it every evening
            // would make the planner a chore rather than a tool.
            settings.Remember(s => s.ActivePlanId = id);

            return Results.Ok(session.View());
        });

        api.MapDelete("/plans/{id}", (PlanStore plans, string id) =>
        {
            plans.Delete(id);
            return Results.NoContent();
        });

        // Exports exactly what is stored, so sharing cannot drift from saving.
        api.MapGet("/plans/{id}/export", (PlanStore plans, string id) =>
        {
            var saved = plans.Get(id);
            return saved is null
                ? Results.NotFound()
                : Results.Text(saved.Document.ToJson(), "application/json");
        });

        api.MapPost("/plans/import", (PlanStore plans, ImportRequest request) =>
        {
            var document = PlanDocument.FromJson(request.Json, out var problem);
            return document is null
                ? Results.BadRequest(new { error = problem })
                : Results.Ok(new { id = plans.Save(document), plan = document });
        });

        // The same plan as a line of text you can paste into a chat window.
        //
        // A file is for keeping; a code is for sending. "Download this, send it, save it, import
        // it" is four steps where one will do, and the code carries the plan itself — there is no
        // server to look anything up in, which is the point.
        api.MapGet("/plans/{id}/code", (PlanStore plans, string id) =>
        {
            var saved = plans.Get(id);

            return saved is null
                ? Results.NotFound()
                : Results.Ok(new { code = PlanCode.Encode(saved.Document) });
        });

        api.MapPost("/plans/import-code", (PlanStore plans, ImportCodeRequest request) =>
        {
            var document = PlanCode.Decode(request.Code, out var problem);

            // Saved exactly as an imported file is, so importing and merging stay one path rather
            // than two that can drift apart.
            return document is null
                ? Results.BadRequest(new { error = problem })
                : Results.Ok(new { id = plans.Save(document), plan = document });
        });

        // Combines saved plans into one squad plan. Nothing is dropped; what it adds is the
        // overlap — shared objectives, contested items, keys only one of you needs to carry.
        api.MapPost("/plans/merge", (RatNavState state, PlanStore plans, RaidSession session, MergeRequest request) =>
        {
            var documents = request.PlanIds
                .Select(plans.Get)
                .Where(p => p is not null)
                .Select(p => p!.Document)
                .ToList();

            if (documents.Count < 2)
                return Results.BadRequest(new { error = "Merging needs at least two plans." });

            var map = FindMap(state, documents[0].MapId);
            if (map is null) return Results.NotFound(new { error = "That plan's map is not loaded." });

            if (documents.Any(d => !string.Equals(d.MapId, map.Id, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = "Those plans are for different maps." });

            var itemsByTask = (state.Cache.Current?.Tasks ?? []).ToDictionary(
                t => t.Id,
                t => (IReadOnlyList<string>)[.. t.Objectives.SelectMany(o => o.Items).Select(i => i.ItemId).Distinct()],
                StringComparer.OrdinalIgnoreCase);

            var squad = PlanMerger.Merge(map, documents, itemsByTask);
            session.UsePlan(squad.Plan, map);

            return Results.Ok(new
            {
                squad.Owners,
                squad.Overlap,
                raid = session.View(),
            });
        });

        // ---- maps

        api.MapGet("/maps", (RatNavState state) =>
            Results.Ok((state.Cache.Current?.Maps ?? []).Select(MapSummary.From)));

        // The map image, restyled for wherever it is about to be drawn.
        //
        // `ink` is the Diablo-style dial: `full` on a second monitor, `structure` or `outline`
        // over the game, where a solid map would bury the thing you are trying to see. The
        // restyle happens here rather than in each surface so the overlay and the web app can
        // never disagree about what "outline at 40%" looks like.
        api.MapGet("/maps/{id}/image", async (
            RatNavState state, MapAssets assets, string id, string? ink, double? opacity, string? accent) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            var path = await assets.EnsureImageAsync(map.Image);
            if (path is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var markup = await File.ReadAllTextAsync(path);

            var options = new MapInkOptions
            {
                Level = ParseInk(ink),
                Opacity = opacity ?? 1.0,
                Accent = string.IsNullOrWhiteSpace(accent) ? new MapInkOptions().Accent : accent,
            };

            return Results.Text(MapInk.Apply(markup, options), "image/svg+xml");
        });

        // Every objective that can be pinned on this map, already converted to image
        // coordinates. Doing the transform server-side is what keeps the WPF overlay and the
        // web app from drifting apart: there is one implementation, and both surfaces call it.
        api.MapGet("/maps/{id}/objectives", (RatNavState state, string id) =>
        {
            var data = state.Cache.Current;
            if (data is null) return Results.NotFound();

            var map = FindMap(state, id);
            if (map is null) return Results.NotFound();
            if (map.Image is null) return Results.Ok(Array.Empty<object>());

            var transform = new CoordinateTransform(map.Image);

            var pins =
                from task in data.Tasks
                from objective in task.Objectives
                // Match on the resolved map's own id, not the URL segment — the route accepts a
                // normalized name too, and comparing that against tarkov.dev ids silently
                // matched nothing.
                where objective.Position is not null && objective.MapIds.Contains(map.Id)
                let position = objective.Position.GetValueOrDefault()
                let point = transform.ToNormalized(position)
                select new ObjectivePin
                {
                    TaskId = task.Id,
                    TaskName = task.Name,
                    TraderName = task.TraderName,
                    ObjectiveId = objective.Id,
                    Description = objective.Description,
                    Type = objective.Type,
                    Optional = objective.Optional,
                    X = point.X,
                    Y = point.Y,
                    Elevation = position.Y,
                    NeededKeyItemIds = objective.NeededKeyItemIds,
                };

            return Results.Ok(pins);
        });

        // Where you can leave from, in image coordinates. Faction is carried raw rather than
        // filtered here — which extracts matter depends on what you queued as, and that is a
        // choice each surface offers rather than one the service makes for it.
        api.MapGet("/maps/{id}/extracts", (RatNavState state, string id) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            var transform = new CoordinateTransform(map.Image);

            return Results.Ok(
                from extract in map.Extracts
                where extract.Position is not null
                let point = transform.ToNormalized(extract.Position.GetValueOrDefault())
                select new ExtractPin
                {
                    Name = extract.Name,
                    Faction = extract.Faction ?? "shared",
                    X = point.X,
                    Y = point.Y,
                    Elevation = extract.Position.GetValueOrDefault().Y,
                });
        });

        // The names players use for places — "Old Gas", "Dorms". Drawn on the map so it reads the
        // way people talk about it rather than as anonymous geometry.
        api.MapGet("/maps/{id}/places", (RatNavState state, string id) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            var transform = new CoordinateTransform(map.Image);

            return Results.Ok(
                from label in map.Labels
                let point = transform.ToNormalized(label.Position)
                select new PlaceLabel
                {
                    Text = label.Text,
                    X = point.X,
                    Y = point.Y,
                });
        });

        // Converting a raw screenshot filename into a map position. This is the Pass 1
        // checkpoint's endpoint, and it stays useful afterwards as the overlay's fix path.
        api.MapPost("/maps/{id}/locate", (RatNavState state, string id, LocateRequest request) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound();

            if (!ScreenshotFilename.TryParse(request.Filename, out var fix))
                return Results.BadRequest(new { error = "That filename carries no coordinates." });

            var transform = new CoordinateTransform(map.Image);
            var point = transform.ToNormalized(fix.Position);

            return Results.Ok(new LocateResponse
            {
                X = point.X,
                Y = point.Y,
                HeadingDegrees = fix.HeadingDegrees,
                ImageHeadingDegrees = transform.ToImageHeading(fix.HeadingDegrees),
                Position = fix.Position,
                TakenAt = fix.TakenAt,
            });
        });
    }

    /// <summary>
    /// Looks a map up by tarkov.dev id or by its normalized name. Both work, because when
    /// tarkov.dev is down the only id a map has is its tarkovdata key — and a URL that stops
    /// working during someone else's outage is a URL that was wrong to begin with.
    /// </summary>
    /// <summary>An item card with everything a player asks about it in one shape.</summary>
    private static ItemDetail Detail(ItemIndex index, ItemTracker tracker, ItemDef item, double? confidence)
    {
        var watch = tracker.Watchlist.FirstOrDefault(w => w.ItemId == item.Id);

        return ItemDetail.From(item, index.GetNeeds(item.Id)) with
        {
            Have = tracker.GetHave(item.Id),
            Watched = watch is not null,
            WatchNote = watch?.Note,
            Confidence = confidence,
        };
    }

    private static MapDef? FindMap(RatNavState state, string id)
    {
        var maps = state.Cache.Current?.Maps;
        if (maps is null) return null;

        return maps.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? maps.FirstOrDefault(m => string.Equals(m.NormalizedName, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Empty is a real answer — it means "detect it" — so it is stored as null, not "".</summary>
    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Why a row is on the list, in as few words as a narrow panel can show.</summary>
    private static string Why(TrackedItem tracked, ItemNeeds needs, ProgressStore progress)
    {
        if (tracked.HideoutUpgrade is { Length: > 0 } upgrade) return upgrade;

        var quest = needs.Quests.FirstOrDefault(q => progress.IsActive(q.TaskId));
        if (quest is not null) return quest.TaskName;

        return needs.AsKey.Count > 0 ? "key" : "";
    }

    /// <summary>
    /// The name to show.
    ///
    /// <para>The short name only when it is one people actually say — "LEDX", "GPU", "Bolts". It
    /// is tarkov.dev's abbreviation, and for keys it produces things like "Chek. 15", which nobody
    /// recognises as <i>Chekannaya 15 apartment key</i>. So it is used when it reads as a word and
    /// the full name is used when it does not.</para>
    /// </summary>
    private static string Short(ItemDef item) => Readable(item);

    internal static string Readable(ItemDef item)
    {
        var shortName = item.ShortName;

        if (shortName is not { Length: > 0 } || shortName == "?") return item.Name;

        // A dot anywhere is an abbreviation announcing itself — "Chek. 15", "M.parts".
        if (shortName.Contains('.', StringComparison.Ordinal)) return item.Name;

        // A single word lifted out of the middle of a longer name loses what the thing is:
        // "Access" for a TerraGroup Labs access keycard could be anything. Acronyms are fine —
        // "GPU", "LEDX" — because they are what players say, and they are all upper case.
        var words = item.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var acronym = shortName.All(c => !char.IsLetter(c) || char.IsUpper(c));

        if (!acronym && words >= 3 && !item.Name.StartsWith(shortName, StringComparison.OrdinalIgnoreCase))
            return item.Name;

        // Short enough to fit and long enough to mean something.
        return shortName.Length is >= 2 and <= 18 && shortName.Count(char.IsLetter) >= 2
            ? shortName
            : item.Name;
    }

    private static int ByUrgency(PanelRow a, PanelRow b) =>
        a.FoundInRaid != b.FoundInRaid
            ? b.FoundInRaid.CompareTo(a.FoundInRaid)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the stash to whatever the chosen edition ships with, if it is not already higher.
    ///
    /// <para>Edge of Darkness starts at Stash 4. Without this it reads as three un-built upgrades
    /// sitting at the top of "buildable now", wanting items nobody needs to find.</para>
    ///
    /// <para>Never lowers it: someone who has upgraded past their edition's starting point should
    /// not lose that by naming their edition afterwards.</para>
    /// </summary>
    private static void SeedStash(RatNavState state, ProgressStore progress, string edition)
    {
        var level = edition.ToLowerInvariant() switch
        {
            "edge-of-darkness" or "unheard" => 4,
            "prepare-for-escape" => 3,
            "left-behind" => 2,
            _ => 1,
        };

        var stash = (state.Cache.Current?.HideoutStations ?? [])
            .FirstOrDefault(s => string.Equals(s.NormalizedName, "stash", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(s.Name, "Stash", StringComparison.OrdinalIgnoreCase));

        if (stash is null || progress.HideoutLevelOf(stash.Id) >= level) return;

        progress.SetHideoutLevel(stash.Id, level);
    }

    private static ItemDef Unknown(string itemId) => new() { Id = itemId, Name = "Unknown item" };

    private static object? Track(
        RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings, string itemId)
    {
        if (state.Index is not { } index) return null;

        var needs = index.GetNeeds(itemId)
            ?? new ItemNeeds { Item = index.GetItem(itemId) ?? Unknown(itemId) };

        var hideout = HideoutPlanner.Demand(state.Upcoming(progress, settings.HideoutLookAhead));

        return TrackedItemView.From(tracker.Track(needs, progress, hideout));
    }

    private static MapInkLevel ParseInk(string? ink) => ink?.ToLowerInvariant() switch
    {
        "outline" => MapInkLevel.Outline,
        "structure" => MapInkLevel.Structure,
        _ => MapInkLevel.Full,
    };
}

// ---- Wire shapes. Deliberately flatter than the domain model: these exist to be read by a
// ---- React table and an XAML canvas, not to round-trip.

public sealed record LocateRequest(string Filename);
public sealed record DoneRequest(bool Done);
public sealed record ImportRequest(string Json);
public sealed record MergeRequest(IReadOnlyList<string> PlanIds);

public sealed record BuildPlanRequest(
    string MapId,
    IReadOnlyList<string> ObjectiveIds,
    IReadOnlyList<string>? ShoppingListItemIds = null);

/// <summary>Either an absolute count or a nudge. The +/- buttons send a delta.</summary>
public sealed record HaveRequest(int? Count, int? Delta);

/// <summary>
/// A change to a watchlist entry. Every field but <c>Watch</c> is optional and absent means "leave
/// it alone", so setting a target does not blank a note typed earlier.
/// </summary>
public sealed record WatchRequest(bool Watch, string? Note, int? Target, int? Have);

/// <summary>A plan someone pasted in, as a share code.</summary>
public sealed record ImportCodeRequest(string? Code);
public sealed record TaskStateRequest(string State);
public sealed record HideoutLevelRequest(int Level);

/// <summary>An item row with progress folded in — what the Items view renders.</summary>
public sealed record TrackedItemView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public string? IconUrl { get; init; }
    public string? WikiUrl { get; init; }
    public int? Avg24hPrice { get; init; }
    public int QuestNeeded { get; init; }
    public int HideoutNeeded { get; init; }

    /// <summary>The nearest hideout upgrade wanting this — "Medstation 3".</summary>
    public string? HideoutUpgrade { get; init; }

    /// <summary>How far out that upgrade is. 1 means you could build it today.</summary>
    public int? HideoutWave { get; init; }

    public int Needed { get; init; }
    public int Have { get; init; }
    public int Remaining { get; init; }
    public bool FoundInRaid { get; init; }
    public bool IsKey { get; init; }
    public bool Watched { get; init; }
    public string? WatchNote { get; init; }
    public int? WatchTarget { get; init; }

    public static TrackedItemView From(TrackedItem t) => new()
    {
        Id = t.Item.Id,
        Name = t.Item.Name,
        ShortName = t.Item.ShortName,
        IconUrl = t.Item.IconUrl,
        WikiUrl = t.Item.WikiUrl,
        Avg24hPrice = t.Item.Avg24hPrice,
        QuestNeeded = t.QuestNeeded,
        HideoutNeeded = t.HideoutNeeded,
        HideoutUpgrade = t.HideoutUpgrade,
        HideoutWave = t.HideoutWave,
        Needed = t.Needed,
        Have = t.Have,
        Remaining = t.Remaining,
        FoundInRaid = t.FoundInRaid,
        IsKey = t.IsKey,
        Watched = t.Watched,
        WatchNote = t.WatchNote,
        WatchTarget = t.WatchTarget,
    };
}

public sealed record LocateResponse
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double HeadingDegrees { get; init; }
    public required double ImageHeadingDegrees { get; init; }
    public required GamePosition Position { get; init; }
    public required DateTimeOffset TakenAt { get; init; }
}

public sealed record ObjectivePin
{
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public string? TraderName { get; init; }
    public required string ObjectiveId { get; init; }
    public required string Description { get; init; }
    public string? Type { get; init; }
    public bool Optional { get; init; }

    /// <summary>Position on the map image as a fraction of its size, so any render scale works.</summary>
    public required double X { get; init; }
    public required double Y { get; init; }

    /// <summary>World height, for maps with floors.</summary>
    public double Elevation { get; init; }

    public IReadOnlyList<string> NeededKeyItemIds { get; init; } = [];
}

public sealed record ItemSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public string? IconUrl { get; init; }
    public int? Avg24hPrice { get; init; }
    public int Needed { get; init; }
    public bool FoundInRaid { get; init; }
    public int QuestCount { get; init; }
    public int HideoutCount { get; init; }
    public bool IsKey { get; init; }

    public static ItemSummary From(ItemDef item, ItemNeeds? needs) => new()
    {
        Id = item.Id,
        Name = item.Name,
        ShortName = item.ShortName,
        IconUrl = item.IconUrl,
        Avg24hPrice = item.Avg24hPrice,
        Needed = needs?.TotalNeeded ?? 0,
        FoundInRaid = needs?.AnyFoundInRaid ?? false,
        QuestCount = needs?.Quests.Count ?? 0,
        HideoutCount = needs?.Hideout.Count ?? 0,
        IsKey = needs?.AsKey.Count > 0,
    };
}

/// <summary>What Setup can change. Every field is optional; absent means "leave it alone".</summary>
public sealed record SettingsUpdate
{
    public string? GameDirectory { get; init; }
    public string? ScreenshotDirectory { get; init; }
    public string? ScreenshotKey { get; init; }
    public string? ScreenshotDisposal { get; init; }
    public string? Owner { get; init; }
    public HotKeyUpdate? Hotkeys { get; init; }
    public int? PlayerLevel { get; init; }
    public string? GameEdition { get; init; }
}

public sealed record HotKeyUpdate
{
    public string? ToggleOverlay { get; init; }
    public string? ToggleInteract { get; init; }
    public string? ExpandPanel { get; init; }
    public string? CompleteObjective { get; init; }
    public string? ToggleMode { get; init; }
    public string? IdentifyItem { get; init; }
}

/// <summary>
/// Settings as Setup shows them: what is set, and what RatNav is actually using.
///
/// <para>Those are different when a field is left to detection, and the difference is the whole
/// point of the screen — "detected: F:\Escape From Tarkov" tells you something that an empty box
/// does not.</para>
/// </summary>
public sealed record SettingsView
{
    public string? GameDirectory { get; init; }
    public string? ScreenshotDirectory { get; init; }
    public required string ScreenshotKey { get; init; }
    public required string ScreenshotDisposal { get; init; }
    public string? Owner { get; init; }
    public required RatNavSettings.HotKeySettings Hotkeys { get; init; }
    public int? PlayerLevel { get; init; }
    public required string GameEdition { get; init; }

    /// <summary>
    /// The lowest level consistent with the quests marked complete, offered when nothing is set.
    /// Not your real level — nothing on disk reports that — but a floor beats an empty box.
    /// </summary>
    public int? SuggestedPlayerLevel { get; init; }

    /// <summary>The install in use, whether set by hand or detected.</summary>
    public string? ResolvedGameDirectory { get; init; }

    /// <summary>The screenshot folder in use.</summary>
    public required string ResolvedScreenshotDirectory { get; init; }

    /// <summary>True when the folder in use was found rather than chosen.</summary>
    public bool GameDirectoryDetected { get; init; }

    public static SettingsView From(RatNavSettings settings)
    {
        var resolved = settings.GameDirectory ?? GameInstallFinder.Find()?.Directory;

        return new SettingsView
        {
            GameDirectory = settings.GameDirectory,
            ScreenshotDirectory = settings.ScreenshotDirectory,
            ScreenshotKey = settings.ScreenshotKey,
            ScreenshotDisposal = settings.ScreenshotDisposal.ToString(),
            Owner = settings.Owner,
            Hotkeys = settings.Hotkeys,
            PlayerLevel = settings.PlayerLevel,
            GameEdition = settings.GameEdition,
            ResolvedGameDirectory = resolved,
            ResolvedScreenshotDirectory =
                settings.ScreenshotDirectory ?? RatNavPaths.DefaultScreenshotDirectory,
            GameDirectoryDetected = settings.GameDirectory is null && resolved is not null,
        };
    }
}

/// <summary>
/// The overlay's items list, in the three groups it shows.
///
/// <para>The split is by what you can act on. <b>Now</b> is active quests and upgrades nothing is
/// standing in the way of. <b>Watchlist</b> is what you chose by hand. <b>Later</b> is upgrades
/// gated behind something unbuilt and quests you have not accepted — worth knowing before you
/// vendor something, not worth reading mid-raid, which is why it starts collapsed.</para>
/// </summary>
public sealed record ItemPanel
{
    public List<PanelRow> Now { get; init; } = [];
    public List<PanelRow> Watchlist { get; init; } = [];
    public List<PanelRow> Later { get; init; } = [];

    /// <summary>How many rows were cut from <see cref="Later"/> to keep the panel readable.</summary>
    public int LaterHidden { get; set; }

    /// <summary>
    /// How far into the hideout build order the list is looking. Reported so a heading can say so:
    /// the same list means different things at depth 1 and depth 4, and nothing on screen said
    /// which you were looking at.
    /// </summary>
    public int LookAhead { get; set; } = 1;
}

/// <summary>One line: what it is, how many more, and why — nothing else fits.</summary>
public sealed record PanelRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required int Count { get; init; }
    public required string Reason { get; init; }
    public bool FoundInRaid { get; init; }

    /// <summary>
    /// True when there is a number to reach. False for a watchlist entry with no target, where
    /// zero means "no amount was ever named" rather than "you have enough".
    /// </summary>
    public bool Tracked { get; init; } = true;

    public static PanelRow From(TrackedItem tracked, string reason) => new()
    {
        Id = tracked.Item.Id,
        Name = ApiEndpoints.Readable(tracked.Item),
        FullName = tracked.Item.Name,
        Count = tracked.Remaining,
        Reason = reason,
        FoundInRaid = tracked.FoundInRaid,
    };
}

/// <summary>Sets how far you have levelled a trader.</summary>
public sealed record TraderLevelRequest
{
    public int Level { get; init; } = 1;
}

/// <summary>How many hideout upgrades deep the items list should reach.</summary>
public sealed record LookAheadRequest
{
    public int Levels { get; init; } = 1;
}

/// <summary>Marks a hideout upgrade as one being worked towards.</summary>
public sealed record TargetRequest
{
    public bool Targeted { get; init; } = true;
}

/// <summary>Text read off the screen, awaiting identification.</summary>
public sealed record IdentifyRequest
{
    /// <summary>Lines as the reader found them. Preferred — line structure helps.</summary>
    public IReadOnlyList<string>? Lines { get; init; }

    /// <summary>The whole capture as one string, for callers that have no line breaks.</summary>
    public string? Text { get; init; }
}

public sealed record ItemDetail
{
    public required ItemDef Item { get; init; }
    public IReadOnlyList<QuestNeed> Quests { get; init; } = [];
    public IReadOnlyList<HideoutNeed> Hideout { get; init; } = [];
    public IReadOnlyList<QuestNeed> AsKey { get; init; } = [];

    /// <summary>Trades that take this item — the reason plenty of worthless-looking loot is not.</summary>
    public IReadOnlyList<BarterNeed> Barters { get; init; } = [];

    public int TotalNeeded { get; init; }
    public bool AnyFoundInRaid { get; init; }

    /// <summary>How much of this you have said you hold, so the answer can be "you have enough".</summary>
    public int Have { get; init; }

    /// <summary>On the watchlist, with whatever note was left on it.</summary>
    public bool Watched { get; init; }
    public string? WatchNote { get; init; }

    /// <summary>Set when this came from reading the screen: 0 to 1, so the UI can hedge honestly.</summary>
    public double? Confidence { get; init; }

    public static ItemDetail From(ItemDef item, ItemNeeds? needs) => new()
    {
        Item = item,
        Quests = needs?.Quests ?? [],
        Hideout = needs?.Hideout ?? [],
        AsKey = needs?.AsKey ?? [],
        Barters = needs?.Barters ?? [],
        TotalNeeded = needs?.TotalNeeded ?? 0,
        AnyFoundInRaid = needs?.AnyFoundInRaid ?? false,
    };
}

/// <summary>A named place on the map image.</summary>
public sealed record PlaceLabel
{
    public required string Text { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

/// <summary>An extract, placed on the map image.</summary>
public sealed record ExtractPin
{
    public required string Name { get; init; }

    /// <summary>"pmc", "scav", or "shared". Shared works whatever you queued as.</summary>
    public required string Faction { get; init; }

    public required double X { get; init; }
    public required double Y { get; init; }
    public double Elevation { get; init; }
}

public sealed record TaskSummary
{
    /// <summary>Why a quest cannot be started yet, in words rather than a padlock.</summary>
    private static IEnumerable<string> Reasons(
        TaskDef task,
        int? playerLevel,
        IReadOnlyDictionary<string, string>? taskNames,
        Func<string, QuestState>? stateOf)
    {
        if (playerLevel is { } level && task.MinPlayerLevel is { } needed && needed > level)
            yield return $"needs level {needed}";

        if (stateOf is null) yield break;

        foreach (var id in task.PrerequisiteTaskIds)
        {
            if (stateOf(id) == QuestState.Completed) continue;
            yield return $"needs {taskNames?.GetValueOrDefault(id) ?? "an earlier quest"}";
        }
    }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TraderName { get; init; }
    public int? MinPlayerLevel { get; init; }
    public bool Kappa { get; init; }
    public string? WikiUrl { get; init; }
    public int ObjectiveCount { get; init; }
    public IReadOnlyList<string> MapIds { get; init; } = [];

    public required string State { get; init; }

    /// <summary>Not started, but every prerequisite is done — the quests you could pick up now.</summary>
    public bool Available { get; init; }

    /// <summary>
    /// What is standing in the way, when something is. Named rather than implied: "locked" tells
    /// you nothing you can act on, "needs level 20" and "needs Debut" both do.
    /// </summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Objectives that can be pinned, which is what makes a quest worth planning around.</summary>
    public int PositionedObjectiveCount { get; init; }

    public static TaskSummary From(
        TaskDef task,
        QuestState state,
        bool available,
        int? playerLevel = null,
        IReadOnlyDictionary<string, string>? taskNames = null,
        Func<string, QuestState>? stateOf = null) => new()
    {
        Blockers = state != QuestState.NotStarted || available
            ? []
            : [
                .. Reasons(task, playerLevel, taskNames, stateOf),
            ],

        Id = task.Id,
        Name = task.Name,
        TraderName = task.TraderName,
        MinPlayerLevel = task.MinPlayerLevel,
        Kappa = task.Kappa,
        WikiUrl = task.WikiUrl,
        ObjectiveCount = task.Objectives.Count,
        MapIds = [.. task.Objectives.SelectMany(o => o.MapIds).Distinct()],
        State = state.ToString(),
        Available = available,
        PositionedObjectiveCount = task.Objectives.Count(o => o.Position is not null),
    };
}

public sealed record MapSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NormalizedName { get; init; }

    /// <summary>False when we have no calibrated image, so the UI can say why pins are missing.</summary>
    public bool Calibrated { get; init; }

    public string? ImageUrl { get; init; }
    public int CoordinateRotation { get; init; }
    public int ExtractCount { get; init; }

    /// <summary>False where the mapping has not been established with confidence.</summary>
    public bool CalibrationVerified { get; init; }

    /// <summary>"Verified", "Derived", "Weak" or "Unknown".</summary>
    public string Confidence { get; init; } = "Unknown";

    /// <summary>How the mapping was arrived at, in words, for the UI to show.</summary>
    public string? CalibrationReason { get; init; }

    /// <summary>The solved mapping itself, e.g. "(-z, x)".</summary>
    public string? Mapping { get; init; }

    /// <summary>
    /// The map's levels, bottom to top. A position fix picks one on its own; this is what lets a
    /// surface offer to look at another without guessing what the map contains.
    /// </summary>
    public IReadOnlyList<MapFloorSummary> Floors { get; init; } = [];

    /// <summary>The level drawn when nothing has chosen one.</summary>
    public string? DefaultFloor { get; init; }

    public static MapSummary From(MapDef map) => new()
    {
        Id = map.Id,
        Name = map.Name,
        NormalizedName = map.NormalizedName,
        Calibrated = map.Image is not null,
        CalibrationVerified = map.Image?.CalibrationVerified ?? false,
        Confidence = map.Image?.Confidence.ToString() ?? "Unknown",
        CalibrationReason = map.Image?.CalibrationReason,
        Mapping = map.Image?.Mapping.ToString(),
        ImageUrl = map.Image?.SourceUrl,
        CoordinateRotation = map.Image?.CoordinateRotation ?? 0,
        ExtractCount = map.Extracts.Count,
        DefaultFloor = map.Image?.DefaultFloor,
        // Bottom to top, by the elevation each level actually covers. The source order is a
        // drawing order — it puts Underground last on most maps — and a floor control that steps
        // "up" into the basement is worse than no floor control. Sorted here rather than in the
        // model, because MapDef.FloorAt relies on that drawing order to resolve overlapping bands.
        Floors = [.. (map.Image?.Floors ?? [])
            .OrderBy(f => f.MinHeight ?? double.NegativeInfinity)
            .ThenBy(f => f.MaxHeight ?? double.PositiveInfinity)
            .Select(f => new MapFloorSummary
            {
                Name = f.Name,
                Layer = f.Layer,
                MinHeight = f.MinHeight,
                MaxHeight = f.MaxHeight,
            })],
    };
}

/// <summary>One level of a multi-storey map, named the way a player would say it.</summary>
public sealed record MapFloorSummary
{
    public required string Name { get; init; }
    public required string Layer { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxHeight { get; init; }
}
