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

    /// <summary>
    /// Raised when a mark of your own is added, renamed or removed, so the overlay redraws without
    /// waiting for a raid to change. Marking a spot in the buddy app and then having to take a
    /// position fix before it appeared would defeat the point of marking it.
    /// </summary>
    public static event Action? WaypointsChanged;

    /// <summary>Raised when someone asks for the overlay to be put back where it started.</summary>
    public static event Action? OverlayResetRequested;

    /// <summary>
    /// Opens a folder picker and returns what was chosen, or null if it was cancelled.
    ///
    /// <para>A hook rather than an event, because this one has an answer. Set by the desktop app,
    /// which is the only part of RatNav that can put a window on screen — a browser cannot ask
    /// the operating system for a path, and typing one is where people get it wrong.</para>
    ///
    /// <para>Null when nothing set it, which is what a headless run looks like.</para>
    /// </summary>
    public static Func<string?, string?>? BrowseForFolder { get; set; }

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
            var trades = state.TradeDemand(tracker);

            var results = index.Search(q, limit ?? 25)
                .Select(item => index.GetNeeds(item.Id) ?? new ItemNeeds { Item = item })
                .Select(needs => TrackedItemView.From(tracker.Track(needs, progress, hideout, trades)));

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

            var trades = state.TradeDemand(tracker);

            var rows = index.AllNeeded()
                .Select(n => tracker.Track(n, progress, hideout, trades))
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
            var trades = state.TradeDemand(tracker);

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

                var tracked = tracker.Track(needs, progress, hideout, trades);
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

        // ---- barters and crafts you are working towards

        // What is offerable, and what is already picked.
        //
        // Gated by what you can actually do: a barter needs the trader at that loyalty level, a
        // craft needs the station built to that level. There are 789 barters and 214 crafts, and
        // an ungated list is mostly things you cannot do — which is the same as no list.
        api.MapGet("/trades", (
            RatNavState state, ItemTracker tracker, ProgressStore progress,
            string? q, bool? all) =>
        {
            if (state.Cache.Current is not { } data) return Results.Ok(Array.Empty<object>());

            var picked = tracker.Trades.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var levels = progress.HideoutLevels;
            var traderLevels = progress.TraderLevels;
            var index = state.Index;

            string? NameOf(string id) => index?.GetItem(id)?.Name;

            var barters = data.Barters.Select(b => new TradeView
            {
                Id = b.Id,
                Kind = "barter",
                Source = b.TraderName ?? "a trader",
                Level = b.MinTraderLevel,
                Makes = b.OfferedItem is { } o ? NameOf(o.ItemId) ?? "something" : "something",
                MakesItemId = b.OfferedItem?.ItemId,
                Costs = [.. b.RequiredItems.Select(r => new TradeCost
                {
                    ItemId = r.ItemId,
                    Name = NameOf(r.ItemId) ?? r.ItemId,
                    Count = (int)Math.Ceiling(r.Count),
                    Have = tracker.GetHave(r.ItemId),
                })],

                // Trader loyalty is something the player tells us, so an unset trader reads as
                // level 1 rather than as locked. Guessing the other way would hide trades from
                // anyone who has not filled the Traders tab in.
                Available = b.MinTraderLevel
                    <= Math.Max(1, traderLevels.GetValueOrDefault(b.TraderName ?? "", 1)),
                Tracked = picked.TryGetValue(b.Id, out var pickedBarter),
                Times = pickedBarter?.Times ?? 1,
            });

            var crafts = data.Crafts.Select(c => new TradeView
            {
                Id = c.Id,
                Kind = "craft",
                Source = c.StationName ?? "a station",
                Level = c.StationLevel,
                Makes = c.ProducedItem is { } made ? NameOf(made.ItemId) ?? "something" : "something",
                MakesItemId = c.ProducedItem?.ItemId,
                Costs = [.. c.RequiredItems.Select(r => new TradeCost
                {
                    ItemId = r.ItemId,
                    Name = NameOf(r.ItemId) ?? r.ItemId,
                    Count = (int)Math.Ceiling(r.Count),
                    Have = tracker.GetHave(r.ItemId),
                })],
                Available = levels.GetValueOrDefault(c.StationId) >= c.StationLevel,
                Tracked = picked.TryGetValue(c.Id, out var pickedCraft),
                Times = pickedCraft?.Times ?? 1,
            });

            var rows = barters.Concat(crafts)
                .Where(t => all == true || t.Available || t.Tracked)
                .Where(t => q is not { Length: > 0 }
                    || SearchText.Contains(t.Makes, q)
                    || SearchText.Contains(t.Source, q)
                    || t.Costs.Any(c => SearchText.Contains(c.Name, q)))

                // Picked first, then by what it makes. Whatever you are working towards is the
                // reason you opened this.
                .OrderByDescending(t => t.Tracked)
                .ThenBy(t => t.Makes, StringComparer.OrdinalIgnoreCase)
                .Take(200);

            return Results.Ok(rows);
        });

        api.MapPost("/trades/{id}", (ItemTracker tracker, string id, TradeRequest request) =>
        {
            if (request.Tracked)
            {
                var kind = request.Kind?.Equals("craft", StringComparison.OrdinalIgnoreCase) == true
                    ? TradeKind.Craft
                    : TradeKind.Barter;

                tracker.TrackTrade(id, kind, request.Times ?? 1);
            }
            else
            {
                tracker.UntrackTrade(id);
            }

            ItemsChanged?.Invoke();
            return Results.Ok(new { id, tracked = request.Tracked });
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
        api.MapPost("/items/identify", (
            RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings,
            IdentifyRequest request) =>
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
                matches = matches.Select(m => Detail(index, tracker, m.Item, m.Confidence) with
                {
                    Verdict = Verdict(state, tracker, progress, settings, m.Item.Id),
                }),
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

            var known = (state.Cache.Current?.Traders ?? [])
                .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

            var traders =
                from task in tasks
                where task.TraderName is { Length: > 0 }
                group task by task.TraderName! into byTrader

                // The order the game lists them in. Alphabetical put Fence third and BTR Driver
                // first, which matches nothing anyone sees while playing.
                orderby TraderOrder(byTrader.Key)
                let def = known.GetValueOrDefault(byTrader.Key)
                select new
                {
                    name = byTrader.Key,

                    // What each loyalty level costs, so a level you cannot have reached can be
                    // shown as out of reach rather than merely unselected.
                    levels = (def?.Levels ?? []).Select(l => new
                    {
                        level = l.Level,
                        requiredPlayerLevel = l.RequiredPlayerLevel,
                        reachable = settings.PlayerLevel is null || l.RequiredPlayerLevel <= settings.PlayerLevel,
                    }),


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
            var trades = state.TradeDemand(tracker);
            var now = HideoutPlanner.Demand(upcoming.Where(u => u.Wave == 1));
            var later = HideoutPlanner.Demand(upcoming.Where(u => u.Wave > 1));

            var watched = tracker.Watchlist.Select(w => w.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Quests you could accept today, not every quest in the game. Without this the section
            // is several thousand rows — everything the whole wipe will ever ask for — which is not
            // something anyone can read, let alone act on.
            // The same dial that decides how far into the hideout build order to look decides how
            // far along the quest chain. Depth 1 is what you could accept today; past that it
            // follows what finishing those would unlock.
            var acceptable = progress
                .ReachableWithin(state.Cache.Current?.Tasks ?? [], depth)
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

                var tracked = tracker.Track(needs, progress, now, trades);

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

            // The barters and crafts being worked towards, as their own sections under the
            // watchlist.
            //
            // Their own rows rather than folded into the counts above, and deliberately so: an
            // item wanted three times for a quest and seven for a barter is two reasons, and one
            // row reading "10" would hide that finishing the quest leaves seven still to find.
            foreach (var (itemId, need) in trades)
            {
                var item = index.GetItem(itemId) ?? Unknown(itemId);
                var short_ = Readable(item);
                var left = Math.Max(0, need.Count - tracker.GetHave(itemId));

                var row = new PanelRow
                {
                    Id = itemId,
                    Name = short_,
                    FullName = item.Name,
                    Count = left,
                    Tracked = true,
                    Reason = string.Join(" · ", need.For),
                    FoundInRaid = false,
                };

                (need.CraftOnly ? panel.Crafting : panel.Barter).Add(row);
            }

            // Found-in-raid first: it is the one thing you cannot buy your way out of later.
            panel.Now.Sort(ByUrgency);
            panel.Barter.Sort(ByUrgency);
            panel.Crafting.Sort(ByUrgency);
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
                settings.PlayerLevel, names, progress.StateOf, progress.TraderLevelOf));

            // Three groups, because those are the three that are true.
            //
            // Grouping by "reachable" was tried and dropped. RatNav can see prerequisites, level
            // and recorded loyalty, but not reputation or spending — so it can never be certain a
            // quest is available, and a tab that claims to know sorts quests you *can* take into a
            // list called Locked. Better to show everything and let the player, who can see the
            // trader screen, decide what to activate.
            //
            // The gates are still worked out and still shown on each row. As information they are
            // useful; as a filter that hides things they were not.
            rows = filter?.ToLowerInvariant() switch
            {
                "active" => rows.Where(t => t.State == nameof(QuestState.Active)),

                // Failed sits with complete because both are finished — but it stays
                // distinguishable, so a wipe's failures can be seen rather than folded into wins.
                "complete" => rows.Where(t =>
                    t.State is nameof(QuestState.Completed) or nameof(QuestState.Failed)),

                _ => rows,
            };

            if (q is { Length: > 0 })
            {
                // Punctuation-insensitive: quest names use a typographic apostrophe that nobody
                // types, so a literal match on "What's on the Flash Drive?" found nothing and read
                // as the quest being missing.
                rows = rows.Where(t =>
                    SearchText.Contains(t.Name, q) || SearchText.Contains(t.TraderName, q));
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
                        ToggleMode = keys.ToggleMode ?? current.Hotkeys.ToggleMode,
                        IdentifyItem = keys.IdentifyItem ?? current.Hotkeys.IdentifyItem,
                        ReadExtracts = keys.ReadExtracts ?? current.Hotkeys.ReadExtracts,
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

        // Draw a map without a plan and without waiting for the game to say which raid you are
        // in — for looking one over between raids, or while a raid is still loading.
        api.MapPost("/raid/map/{id}", (RatNavState state, RaidSession session, string id) =>
        {
            var map = FindMap(state, id);
            if (map?.Image is null) return Results.NotFound(new { error = "No calibrated map by that name." });

            session.ShowMap(map);
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
            RatNavState state, PlanStore plans, RatNavSettings settings,
            CustomWaypointStore marks, BuildPlanRequest request) =>
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

            // Marks of your own, alongside the quest objectives.
            //
            // Their positions are stored against the image rather than the world — they were put
            // there by clicking a map, and that survives a change of calibration where world
            // coordinates would not — so they come back through the transform to get there.
            if (map.Image is { } image)
            {
                var transform = new CoordinateTransform(image);

                waypoints.AddRange(
                    from id in request.WaypointIds ?? []
                    let mark = marks.Get(id)
                    where mark is not null
                        && string.Equals(mark.MapId, map.Id, StringComparison.OrdinalIgnoreCase)
                    select new Waypoint
                    {
                        ObjectiveId = mark!.Id,

                        // No quest behind it, and nothing downstream should pretend there is: a
                        // mark cannot be turned in, and a turn-in prompt for one would be a lie.
                        TaskId = "",
                        TaskName = mark.Label,
                        Description = mark.Kind == MarkKind.Item
                            ? $"Pick up: {mark.Label}"
                            : $"Your mark: {mark.Label}",
                        Position = transform.ToGamePosition(
                            new MapPoint(mark.X * image.PixelWidth, mark.Y * image.PixelHeight)),
                    });
            }

            if (waypoints.Count == 0)
                return Results.BadRequest(new { error = "Nothing you picked has a position on this map." });

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

        // Maps RatNav can put a marker on and be right.
        //
        // Two things get a map excluded. Some of the game's locations have no community drawing at
        // all — The Lab, The Labyrinth, Icebreaker — and listing those gets you a picker with
        // entries that open onto nothing. And some have a drawing whose orientation cannot be
        // settled from the data, which is worse: a map that looks right and puts you on the wrong
        // side of it.
        //
        // Neither is a caveat to display. A marker you cannot trust is not a feature with an
        // asterisk, it is a defect, and shipping it with a warning attached just moves the
        // consequence onto the player.
        //
        // `?all=true` returns everything with its reason, which is how the Setup view can say what
        // is missing and what would settle it.
        api.MapGet("/maps", (RatNavState state, bool? all) =>
            Results.Ok((state.Cache.Current?.Maps ?? [])
                .Where(m => all == true || Trustworthy(m))
                .Select(MapSummary.From)));

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

        // The pictures on a quest's wiki article — which building, which door.
        //
        // Fetched rather than shipped: they are other people's work under CC BY-SA, so RatNav
        // links to them and credits the wiki rather than redistributing them in a release.
        api.MapGet("/tasks/{id}/images", async (
            RatNavState state, WikiImages wiki, string id, CancellationToken ct) =>
        {
            var task = state.Cache.Current?.Tasks.FirstOrDefault(t => t.Id == id);
            if (task is null) return Results.NotFound();

            return Results.Ok(new
            {
                taskName = task.Name,
                wikiUrl = task.WikiUrl,
                images = await wiki.ForAsync(id, task.WikiUrl, ct),
            });
        });

        // Which extracts the game is currently offering, from what was read off the screen.
        //
        // The text is supplied by the caller for the same reason item identification's is: every
        // pixel that touches the screen stays in the desktop app, and this side stays testable
        // with strings.
        api.MapPost("/raid/extracts/read", (
            RatNavState state, RaidSession session, RatNavSettings settings, IdentifyRequest request) =>
        {
            var map = session.View().MapId is { Length: > 0 } id ? FindMap(state, id) : null;

            if (map is null) return Results.BadRequest(new { error = "No map is loaded." });

            var lines = request.Lines is { Count: > 0 }
                ? request.Lines
                : (request.Text ?? "").Split(
                    ['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var offered = ExtractMatcher.Match(lines, map.Extracts);

            settings.Remember(s => s.Overlay = s.Overlay with { OfferedExtracts = offered });

            return Results.Ok(new { offered, readText = lines, of = map.Extracts.Count });
        });

        // Back to showing every extract the map has. What the game offered is forgotten with it —
        // last raid's answer is worse than no answer.
        api.MapDelete("/raid/extracts/read", (RatNavSettings settings) =>
        {
            settings.Remember(s => s.Overlay = s.Overlay with { OfferedExtracts = [] });

            return Results.Ok(new { offered = Array.Empty<string>() });
        });

        // Put the overlay back where it started.
        //
        // A window dragged onto a monitor that is no longer there cannot be dragged back — there
        // is nothing to grab. Without this, a change of monitors leaves someone with an overlay
        // they can neither see nor recover, and the only fix is editing settings.json by hand.
        api.MapPost("/settings/overlay/reset", (RatNavSettings settings) =>
        {
            var fresh = new RatNavSettings.OverlayBounds();

            settings.Remember(s => s.Overlay = s.Overlay with
            {
                // Position and size only. Everything else — ink, scales, which drawers are open —
                // is a preference someone chose on purpose, and resetting a window is not a
                // reason to throw those away.
                Box = s.Overlay.Box with
                {
                    Left = fresh.Box.Left,
                    Top = fresh.Box.Top,
                    Width = fresh.Box.Width,
                    Height = fresh.Box.Height,
                    Zoom = fresh.Box.Zoom,
                    PanX = 0,
                    PanY = 0,
                },
                Wireframe = s.Overlay.Wireframe with
                {
                    Left = fresh.Wireframe.Left,
                    Top = fresh.Wireframe.Top,
                    Width = fresh.Wireframe.Width,
                    Height = fresh.Wireframe.Height,
                    Zoom = fresh.Wireframe.Zoom,
                    PanX = 0,
                    PanY = 0,
                },
            });

            OverlayResetRequested?.Invoke();
            return Results.Ok(settings.Overlay);
        });

        // ---- settling a map that the data cannot

        // Asks the desktop app to open a folder picker.
        //
        // Detection handles the ordinary install. This is for the drive you keep games on, where
        // typing the path is the step people get subtly wrong and the failure arrives later as an
        // overlay that never notices a raid.
        api.MapPost("/settings/browse", (BrowseRequest request) =>
        {
            if (BrowseForFolder is not { } browse)
                return Results.BadRequest(new { error = "No window to open a picker in." });

            var chosen = browse(request.Start);

            return chosen is { Length: > 0 } ? Results.Ok(new { path = chosen }) : Results.NoContent();
        });

        // The last position read from a screenshot, before it is placed on any map.
        //
        // Placing it is precisely the step in question while a map's layout is being settled, so
        // this is the reading on its own.
        api.MapGet("/position/latest", (RaidSession session) =>
        {
            if (session.LastFix is not { } fix) return Results.NoContent();

            return Results.Ok(new
            {
                x = fix.Position.X,
                y = fix.Position.Y,
                z = fix.Position.Z,
                takenAt = fix.TakenAt,
                mapId = session.View().MapId,
            });
        });

        // The maps RatNav will not offer yet, and why.
        api.MapGet("/maps/held-back", (RatNavState state) =>
            Results.Ok(
                from map in state.Cache.Current?.Maps ?? []
                where !Trustworthy(map)
                orderby map.Name
                select new
                {
                    map.Id,
                    map.Name,
                    map.NormalizedName,

                    // The two reasons are not the same problem. One needs a drawing that does not
                    // exist; the other needs thirty seconds from somebody who plays the map.
                    hasDrawing = map.Image is not null,
                    confidence = map.Image?.Confidence.ToString() ?? "Unknown",
                    reason = map.Image?.CalibrationReason
                        ?? "No community drawing exists for this map yet.",
                    canBeSettled = map.Image is not null,
                }));

        // Settle one, from a position and the spot on the map where that position actually is.
        //
        // The margin here is enormous, which is what makes clicking a safe way to do it: a wrong
        // layout is a mirror image and misses by something like half the map, while a hurried
        // click misses by a few percent. Where the answers do come out close — a position near the
        // centre, where mirroring barely moves anything — it says so rather than choosing.
        api.MapPost("/maps/{id}/calibrate", (
            RatNavState state, RatNavSettings settings, string id, CalibrateRequest request) =>
        {
            var map = FindMap(state, id);

            if (map?.Image is not { } image)
                return Results.BadRequest(new { error = "There is no drawing for that map." });

            if (map.NormalizedName is not { Length: > 0 } key)
                return Results.BadRequest(new { error = "That map has no name to record against." });

            var solved = CalibrationFromPoint.Solve(
                image,
                new GamePosition(request.X, request.Y, request.Z),
                new MapPoint(request.ImageX, request.ImageY));

            if (!solved.Decisive)
            {
                return Results.Ok(new
                {
                    settled = false,
                    mapping = solved.Mapping.ToString(),
                    miss = solved.Miss,
                    runnerUpMiss = solved.RunnerUpMiss,
                    reason = solved.Miss > 0.12
                        ? "That spot is not where any layout puts you. Check the position, or click again."
                        : "Too near the middle of the map to tell the layouts apart. "
                            + "Take a position somewhere nearer an edge.",
                });
            }

            settings.Remember(s => s.ConfirmedMaps[key] = solved.Mapping.ToString());

            // Re-solved rather than patched in memory: the confirmed mapping is read at the same
            // point every other one is, so there is one path and not a special case.
            state.Cache.Reapply(settings.ConfirmedMaps);

            return Results.Ok(new
            {
                settled = true,
                mapping = solved.Mapping.ToString(),
                miss = solved.Miss,
                runnerUpMiss = solved.RunnerUpMiss,
                reason = $"Settled as {solved.Mapping}. The next-best layout missed by "
                    + $"{solved.RunnerUpMiss * 100:F0}% of the map.",
            });
        });

        // Forget one, for when it was settled from a position that turned out to be wrong.
        api.MapDelete("/maps/{id}/calibrate", (
            RatNavState state, RatNavSettings settings, string id) =>
        {
            var map = FindMap(state, id);
            if (map?.NormalizedName is not { Length: > 0 } key) return Results.NotFound();

            settings.Remember(s => s.ConfirmedMaps.Remove(key));
            state.Cache.Reapply(settings.ConfirmedMaps);

            return Results.Ok(new { settled = false });
        });

        // ---- marks of your own

        // Spots someone marked by hand, with a short name for each.
        //
        // Kept apart from plans deliberately. A plan is for one raid and gets cleared; "car
        // batteries behind the garage" is true every raid, and having to re-add it each time is
        // how a feature stops being used.
        api.MapGet("/maps/{id}/waypoints", (CustomWaypointStore marks, string id) =>
            Results.Ok(marks.For(id)));

        api.MapPost("/maps/{id}/waypoints", (
            CustomWaypointStore marks, string id, WaypointRequest request) =>
        {
            var kind = request.Kind?.Equals("item", StringComparison.OrdinalIgnoreCase) == true
                ? MarkKind.Item
                : MarkKind.Place;

            var mark = marks.Add(id, request.Label ?? "", request.X, request.Y, request.Floor, kind);

            WaypointsChanged?.Invoke();
            return Results.Ok(mark);
        });

        api.MapPost("/waypoints/{markId}/label", (
            CustomWaypointStore marks, string markId, WaypointRequest request) =>
        {
            if (!marks.Rename(markId, request.Label ?? "")) return Results.NotFound();

            WaypointsChanged?.Invoke();
            return Results.Ok(new { id = markId, label = request.Label });
        });

        api.MapDelete("/waypoints/{markId}", (CustomWaypointStore marks, string markId) =>
        {
            if (!marks.Remove(markId)) return Results.NotFound();

            WaypointsChanged?.Invoke();
            return Results.Ok(new { id = markId });
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
    /// Whether a map can be drawn and trusted: it has a drawing, and the drawing's orientation was
    /// either checked in game or established from the data rather than guessed at.
    /// </summary>
    private static bool Trustworthy(MapDef map) =>
        map.Image is { } image
        && image.Confidence is CalibrationConfidence.Verified or CalibrationConfidence.Derived;

    /// <summary>
    /// Looks a map up by tarkov.dev id or by its normalized name. Both work, because when
    /// tarkov.dev is down the only id a map has is its tarkovdata key — and a URL that stops
    /// working during someone else's outage is a URL that was wrong to begin with.
    /// </summary>
    /// <summary>
    /// Whether to pick something up, answered in the order the question is actually asked.
    ///
    /// <para>Built here rather than in the overlay because deciding what counts as "something you
    /// are working on" is domain logic — active quests, upgrades within the look-ahead, trades you
    /// picked — and the overlay should not be re-deriving it from a list of everything.</para>
    /// </summary>
    private static ItemVerdict? Verdict(
        RatNavState state,
        ItemTracker tracker,
        ProgressStore progress,
        RatNavSettings settings,
        string itemId)
    {
        if (state.Index is not { } index) return null;

        var needs = index.GetNeeds(itemId);
        if (needs is null) return null;

        var hideout = HideoutPlanner.Demand(state.Upcoming(progress, settings.HideoutLookAhead));
        var trades = state.TradeDemand(tracker);
        var tracked = tracker.Track(needs, progress, hideout, trades);

        var activeQuests = needs.Quests.Where(q => progress.IsActive(q.TaskId)).ToList();
        var nearestQuest = activeQuests.OrderByDescending(q => q.FoundInRaid).FirstOrDefault();

        var watch = tracker.Watchlist.FirstOrDefault(w => w.ItemId == itemId);
        var trade = trades.GetValueOrDefault(itemId);

        return LootVerdict.For(
            (tracked.QuestNeeded, nearestQuest?.TaskName),
            (tracked.HideoutNeeded, tracked.HideoutUpgrade),
            (tracked.TradeNeeded, trade?.For.FirstOrDefault()),
            watch is { Target: { } target } ? (target, watch.Have) : null,
            tracked.FoundInRaid,

            // Counted, not listed. A common item is wanted by half the quest tree eventually, and
            // the card has one glance to work with.
            needs.Quests.Count(q => !progress.IsActive(q.TaskId)),
            needs.Barters.Count);
    }

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

    /// <summary>
    /// Where a trader sits in the game's own list.
    ///
    /// <para>Hardcoded because the order is a fact about the game's interface rather than
    /// something the data carries, and it has been stable for years. Anything unrecognised sorts
    /// last rather than throwing the rest out of order.</para>
    /// </summary>
    private static int TraderOrder(string name) => name.ToLowerInvariant() switch
    {
        "prapor" => 0,
        "therapist" => 1,
        "fence" => 2,
        "skier" => 3,
        "peacekeeper" => 4,
        "mechanic" => 5,
        "ragman" => 6,
        "jaeger" => 7,
        "ref" => 8,
        "lightkeeper" => 9,
        "btr driver" => 10,
        _ => 99,
    };

    private static ItemDef Unknown(string itemId) => new() { Id = itemId, Name = "Unknown item" };

    private static object? Track(
        RatNavState state, ItemTracker tracker, ProgressStore progress, RatNavSettings settings, string itemId)
    {
        if (state.Index is not { } index) return null;

        var needs = index.GetNeeds(itemId)
            ?? new ItemNeeds { Item = index.GetItem(itemId) ?? Unknown(itemId) };

        var hideout = HideoutPlanner.Demand(state.Upcoming(progress, settings.HideoutLookAhead));

        return TrackedItemView.From(tracker.Track(needs, progress, hideout, state.TradeDemand(tracker)));
    }

    private static MapInkLevel ParseInk(string? ink) => ink?.ToLowerInvariant() switch
    {
        "outline" => MapInkLevel.Outline,
        "structure" => MapInkLevel.Structure,
        "full" => MapInkLevel.Full,
        _ => MapInkLevel.Graphical,
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
    IReadOnlyList<string>? ShoppingListItemIds = null,

    /// <summary>Marks of your own to run alongside the quest objectives, in the order given.</summary>
    IReadOnlyList<string>? WaypointIds = null);

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

    /// <summary>How many the barters and crafts being worked towards want.</summary>
    public int TradeNeeded { get; init; }

    /// <summary>Which trades want it — "Therapist LL2 · Dorm room 303 key".</summary>
    public IReadOnlyList<string> TradeFor { get; init; } = [];

    /// <summary>True when only crafts want it, which is what splits Barter from Crafting.</summary>
    public bool TradeIsCraftOnly { get; init; }

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
        TradeNeeded = t.TradeNeeded,
        TradeFor = t.TradeFor,
        TradeIsCraftOnly = t.TradeIsCraftOnly,
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
    public string? ToggleMode { get; init; }
    public string? IdentifyItem { get; init; }
    public string? ReadExtracts { get; init; }
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

    /// <summary>What the trader barters you are working towards want.</summary>
    public List<PanelRow> Barter { get; init; } = [];

    /// <summary>What the hideout crafts you are working towards want.</summary>
    public List<PanelRow> Crafting { get; init; } = [];

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

    /// <summary>
    /// Whether to pick it up, and why, strongest reason first.
    ///
    /// <para>Set when the question is "I am standing over this". The lists above are the whole
    /// truth about an item; this is the part of it that changes what you do next.</para>
    /// </summary>
    public ItemVerdict? Verdict { get; init; }

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

/// <summary>A barter or craft, as the picker shows it.</summary>
public sealed record TradeView
{
    public required string Id { get; init; }

    /// <summary>"barter" or "craft".</summary>
    public required string Kind { get; init; }

    /// <summary>The trader or station offering it.</summary>
    public required string Source { get; init; }

    /// <summary>Loyalty level for a barter, station level for a craft.</summary>
    public required int Level { get; init; }

    public required string Makes { get; init; }
    public string? MakesItemId { get; init; }
    public required IReadOnlyList<TradeCost> Costs { get; init; }

    /// <summary>Whether you can actually do it today.</summary>
    public required bool Available { get; init; }

    public required bool Tracked { get; init; }
    public required int Times { get; init; }
}

/// <summary>One input to a trade, with how many you already have.</summary>
public sealed record TradeCost
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required int Have { get; init; }
}

public sealed record TradeRequest(bool Tracked, string? Kind, int? Times);

public sealed record WaypointRequest(string? Label, double X, double Y, string? Floor, string? Kind = null);

/// <summary>Where you were, and where that is on the map image.</summary>
public sealed record BrowseRequest(string? Start);

public sealed record CalibrateRequest(
    double X, double Y, double Z, double ImageX, double ImageY);

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
        Func<string, QuestState>? stateOf,
        Func<string, int>? traderLevelOf)
    {
        if (playerLevel is { } level && task.MinPlayerLevel is { } needed && needed > level)
            yield return $"needs level {needed}";

        if (traderLevelOf is not null)
        {
            foreach (var requirement in task.TraderRequirements)
            {
                var trader = requirement.TraderName ?? requirement.TraderId;
                if (traderLevelOf(trader) >= requirement.Level) continue;

                yield return $"needs {requirement.TraderName ?? "trader"} LL{requirement.Level}";
            }
        }

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
        Func<string, QuestState>? stateOf = null,
        Func<string, int>? traderLevelOf = null) => new()
    {
        Blockers = state != QuestState.NotStarted || available
            ? []
            : [
                .. Reasons(task, playerLevel, taskNames, stateOf, traderLevelOf),
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
