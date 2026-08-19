# Backlog

Requests are written down here **before** they are worked on.

This exists because a long working session gets summarised as it runs, and anything only held in
conversation can be lost when that happens. A request that made it to this file survives; one that
did not, might not. So: capture first, then execute, then tick.

Status: `[ ]` not started · `[~]` in progress · `[x]` done · `[?]` needs a decision from Justin

---

## Round 6 — 2026-08-19

**Decisions taken before execution:**

- **Quests tabs: Active / Ready / Done / All.** Active is what you accepted; Ready is reachable but
  not accepted; Done covers completed and failed; All includes locked. "Available" survives as
  "Ready", which is what it always meant.
- **Quest wiki images: fetch through Fandom's API and cache locally**, shown as a carousel with the
  captions, attributed CC-BY-SA. The API is a supported route rather than HTML scraping, which is
  what makes this acceptable against the no-scraping principle.

**Order of execution:** investigations first, since two of them can change what the rest should
look like; then bugs; then features.

**Pass 1 shipped.** Everything ticked below is done. What remains, in rough order: off-screen edge
markers, the vertical control stack, spawn locations, the Quests tab rebuild, barter and craft
tracking with the items-list restructure, buddy-app map controls, items filters, bottom placement
for the list, plan-page key warnings, and the quest wiki carousel.

### Overlay

- [x] **Map artifacts are far too small, and need to be scalable.** Waypoints especially.
  - Default: **×3** the current size, as a starting point to judge from.
  - Add a **scale control** covering everything drawn on the map — waypoints, extracts, and the
    like — because players run different resolutions and one fixed size cannot suit all of them.

- [x] **The popped-out items list is a one-way trip.** Once torn off, there is no way to put it
  back. It needs a close control that returns it into the map overlay, **collapsed**.
- [ ] **Position the list at the bottom left or bottom right of the map**, as a toggle.
  - Note: a left/right side swap (`⇄`) already exists but is only visible in interact mode (`F6`),
    so it may simply not have been findable. Worth confirming whether this replaces that or is a
    separate bottom-corner placement. `[?]`
- [x] **The popped-out window should open at the height of its contents** — quests, hideout and
  watchlist combined — rather than needing to be dragged to size every single time it is opened.
- [ ] **All of the above persists across sessions**, so relaunching RatNav or the game picks up
  where it left off. Stored **separately from the F9 view**, like the F5/F9 split already is.

### Items list

- [x] **The red and yellow numbers are unexplained.** Needs a legend or helper text.
  - Cause, for whoever picks this up: the colour is the count, set in `ItemRow.From`. Red means an
    active quest or upgrade wants it **found in raid**; amber means it is wanted but can be bought;
    grey means you already have enough. Nothing on screen says so.
- [x] **Item names are shorthand and unreadable.** "Chek. 15" is not a name anyone recognises.
  - Cause: the overlay uses tarkov.dev's `shortName` because it fits a narrow column. That works
    for "LEDX" and "GPU" and fails badly for keys — "Chek. 15" is *Chekannaya 15 apartment key*.
  - Wanted: human-readable names throughout, not backend shorthand.

### Interact mode (F6)

- [x] **The detached items window cannot be dragged or resized.** `F6`'s move and resize behaviour
  is not reaching it once it is off the map overlay. It needs the same easy drag and resize.
  - Note: it *was* given whole-surface `DragMove` and a resize grip, so either they are not
    working or they are not discoverable. Check before rewriting.
- [x] **The map settings strip is horizontal and too long.** Reaching fade, zoom and the rest means
  widening the map first, which is backwards.
  - Wanted: a **vertical stack**, shown by default on `F6`, with an option to **collapse** it.
  - It must live **inside the map overlay** so that the items list — left, right or bottom — is not
    pushed around by the controls.
  - Controls should **scale or scroll** within whatever overlay size the player has chosen, rather
    than dictating a minimum width.

### Floors and building interiors

- [x] **Building interiors are missing on Streets.** Walking through a door, no internal layout
  appeared on either the ground or second floor — no hallways, no walkable areas, nothing to say
  where to go or where a dead end is.
  - **Investigated: the data is fine, and Justin's instinct was right.** Streets' `Ground_Level`
    holds 484 paths but its `buildings` group is footprints only — no interiors. The interiors
    live one level up, in `Second_Floor` → `Floor-2` (98 paths). Standing inside a building at
    street level puts you at a height that resolves to `Ground_Level`, which has nothing indoors
    to draw. So combining the base pair is the fix, not a workaround.
- [x] **Ground and the first indoor floor should probably be drawn together.** Done, then
  corrected: drawn as a *distinct ghost layer* rather than merged into your floor, and behind the
  ghost toggle. Merged at full strength it read as your floor — a stairwell one storey up looked
  like a room beside you — and it ignored the ghost toggle entirely, which a toggle called "ghost"
  should not be able to do.
- [ ] **Streets has no interior geometry for most buildings.** `[?]` Measured at a real position
  inside a room off the street (0.811, 0.907): exactly one ground-level shape covers that point and
  it is large — a footprint. No room-sized geometry exists there on *any* floor. The small squares
  visible nearby are the handful of buildings that do have interiors, which live in `Floor-2`.
  - This is the map data, not RatNav. tarkov.dev's Streets drawing simply does not include most
    building interiors.
  - Options: leave it and say so; look for a different Streets drawing that has them; or draw
    something honest at the point — "no interior mapped here" — so it does not read as a bug.
  - Original wording for the record: On Streets, stepping
  off the street into a building puts you on the second level, but nothing about that transition is
  visually significant — so the pair should read as one prominent layer.
  - Higher floors should still hide the ones below, so this is the *base pair* being combined
    rather than a rule applied to every level.
  - Exactly which two combine is map-dependent — ground plus first on some, ground plus second on
    others. `[?]` Needs a rule that works from the map's own floor list rather than a hardcoded pair.
- [x] **Ghosting needs to be brighter.** At its current strength it does not carry, and the whole
  point is seeing walkable areas when you go inside a building.

### Off-screen markers

- [x] **Anything outside the visible area needs an edge indicator.** Zooming or panning pushes
  waypoints and extracts off the view, and they currently vanish with no trace.
  - Wanted: a marker pinned to the **edge of the map view**, positioned in the direction of the
    real thing as the crow flies, so you can walk that way until it comes into view.
  - Applies to **waypoints and extracts** both — knowing which direction an exit is in while
    navigating is the same need.
  - Should keep whatever the marker already means: waypoint numbering, and the green/yellow
    PMC/Scav distinction for extracts.

### Hover and cursor reliability

These three are probably one underlying problem. Worth investigating together before fixing any of
them separately.

- [ ] **Tooltips flash and vanish.** Hovering a waypoint in the F5 view shows a tooltip for an
  instant, then it disappears. Same unresponsiveness in F9.
  - Hypothesis: the overlay is `Topmost` with `WS_EX_NOACTIVATE`, and a WPF `ToolTip` is a popup
    owned by a window that never activates — so it opens and is immediately dismissed. If that is
    it, tooltips cannot be made reliable and the label has to be drawn **into the canvas** instead.
- [x] **The cursor vanishes and stutters over the map in F6.** It flickers as it crosses lines
  versus open areas, and near the edges — exactly where you need it for dragging and resizing — it
  disappears often enough to be hard to locate.
  - Hypothesis: cursor resolution is falling through inconsistently. Drawn shapes are
    hit-test-invisible, so the cursor comes from whatever is beneath them, and a `Cursors.None`
    set for click-through mode may be winning in places. Needs one explicit, consistent cursor
    across every element while interactive.
- [x] **F9 has no visible border or drag handle.** There is nothing to grab or even see for
  selection. It should behave the way F5 does.
  - And when `F6` is toggled off, those controls must disappear again — that part works today and
    should stay that way.

### Map text, spawns, and quest imagery

- [ ] **Text on the map needs its own scale control**, alongside the artifact scale from the first
  item of this round. Covers street names, building names, area names, and waypoint names.
  - `[?]` One scale for everything drawn, or separate dials for text and iconography? The first
    item asked for ×3 on icons specifically, which suggests they may want to move independently.
- [x] **Spawn locations, toggleable on the map.** `SPAWNS` in the control stack cycles
  off / pmc / scav / both.
  - tarkov.dev's REST maps document does carry them, and distinguishes both things that matter:
    `sides` (pmc / scav / all) and `categories` (player / bot / boss). Bot-only points are dropped
    — most of the list, and none of them say where a *player* is coming from.
  - Drawn as **areas, not points**. The source lists 140 PMC spawns on Woods and 196 on Streets;
    plotted literally that is a rash of dots that answers no question. They cluster at a 100 m
    radius into 12–26 regions a map, drawn at the size of the ground they cover with the number of
    spawn points in the tooltip.
  - Fell out of it: **the game data cache now carries a schema number**. Adding a field used to
    mean every existing install served a cache missing it until the six-hour age check happened to
    fire — so a new layer read as broken rather than pending, for exactly the person who had just
    updated and gone looking for it.
- [ ] **Quest images from the wiki, reachable from the waypoint hover.** For a quest like *Glory to
  CPSU*, the wiki has screenshots showing the building and the room to look for; those are what
  turn "walk to this pin" into "find this door".
  - Wanted: a clickable action opening a **carousel** of the quest's wiki images, with the
    descriptions that accompany them.
  - **Settled:** fetch through **Fandom's API** and cache locally, with CC-BY-SA attribution
    shown. The API is a supported route rather than HTML scraping, which is what makes this sit
    acceptably against the no-scraping principle.
  - Also depends on the tooltip work above: if hover labels have to be drawn into the canvas, the
    "clickable" part needs a click target that survives an overlay that never takes focus.

### Buddy app parity

- [x] **Factory reports "calibration unverified".** Believed to have been sorted already — needs a
  review that the map data is current with everything since.
  - **Investigated. The badge is right and the guess is doubtful.** Factory resolves to `(z, x)`
    at `Weak` confidence — the only map not resolving to `(x, z)`. It is nearly square, so the
    aspect check cannot settle the axis order, and its extracts sit inside the border, so the
    signs cannot be settled either.
  - Four maps are `Weak`: **Factory, Reserve, Terminal**, and Lighthouse. Lighthouse is now marked
    verified from the Northern Checkpoint screenshot (0.3pp fit, runner-up 21.6pp out).
  - **Reserve looks actively wrong.** The screenshot taken at Checkpoint Fence reported a best fit
    of `(-x, -z)` at 12.7pp — a poor fit even as a winner — while the solver settles on `(x, z)`.
    Worth a fresh screenshot at a known extract to settle it.
  - Factory and Terminal need the same: one screenshot each, standing somewhere identifiable.
- [ ] **The buddy app should get the same map controls as the overlay** — ink, fade, ghosting,
  place names, floor selection, artifact and text scale, zoom, right-drag pan.
  - **Not** "follows you": there is no you in a browser.
  - Extends the Round 5 item "Map controls to match the overlay", which is still open — do them
    together rather than twice.
- [x] **Extracts need a "both" option**, showing PMC and Scav together, in **both** the overlay and
  the buddy app. Today the overlay cycles pmc → scav → off with no combined view.

- [x] **The hideout look-ahead slider does not drag.** Clicking steps it, but holding and dragging
  does nothing.
  - Likely cause: every change fires a save and a reload, and the re-render replaces the input
    mid-drag so the browser loses the grab. Debouncing while dragging would fix it.
  - If dragging cannot be made to feel right, **replace it with arrows** — stepping is the whole
    interaction anyway, and a slider that only works by clicking is worse than a pair of buttons.
  - The Items tab has the same control and will have the same problem.

- [x] **"By amount" / "What's next" appears to do nothing** on the Items page. It did do
  something — "what's next" leads with the nearest hideout wave — but it is a *sort*, and a
  reordering of two hundred rows is a quiet change you have to read the whole list to notice. It
  now says `Sort` beside it and names the two orders for what they are; the thing that was
  actually wanted is the filter row below.
  - Verify it is actually being applied first — the request only sends `sort` for one of the two,
    so a bug is plausible. If it *is* applied, the two orderings may simply be too alike to notice:
    both fall back to found-in-raid and then quantity, and if most rows are wave 1 the leading key
    barely separates anything.
  - **If the distinction is not meaningful, remove the toggle.** Leave the tabs as needed /
    watchlist / search, and let the table be sorted by clicking its columns instead.
- [x] **Filters on the items table**, which is the thing actually wanted here: show only what is
  needed now, or only hideout build items.

### Plan persistence

- [x] **The Plan tab forgets which map you were on.** After a Streets session it opened on Factory.
  It should reopen on the map of the plan you last used, showing whatever was left unfinished.
  - Cause: the active plan *is* restored on the service side, but the Plan view's map picker is
    seeded independently — it just takes the first map in the list, which is Factory. The picker
    needs to start from the active plan.
- [ ] **Switching maps should not discard the objectives you picked.** Choosing a different map,
  then coming back, should still have your selections for the first one, and the last map used
  stays remembered.
  - `[?]` This implies keeping a selection per map rather than one selection at a time. Worth
    confirming whether that means several saved plans (one per map, switched between) or one plan
    that remembers its map-by-map picks.

### Quests tab, rebuilt

- [x] **Traders belong on the Quests tab**, with their current level and up/down adjustment.
  - A separate Traders tab was just built (Round 5). This supersedes it — fold it into Quests
    rather than keeping both.
  - Auto-track if possible. Already established it is not: loyalty depends on rep, level and spend,
    none of which the game writes to disk. Manual stays.
- [x] **Locked section for quests not yet reachable** — held there until the level is reached or
  the prerequisite quests are done.
- [x] **Cut the tabs down to Active / Ready / Done / Locked / All.** Locked earns its place: it is
  where a quest goes when it is waiting on something, and each row names what — "needs level 20",
  "needs Debut" — rather than showing a padlock that says nothing you can act on.
  - Remove **Available** — redundant.
  - Remove **To-do** — redundant to Active.
  - **All** shows everything: active, completed, and not started.
  - **Settled:** four tabs — **Active** (accepted), **Ready** (reachable, not accepted), **Done**
    (completed and failed), **All** (everything including locked). "Available" was not redundant,
    it was badly named.
- [x] **Every quest offers four states: not started, active, done, failed.** All four are on every
  row. Failed now carries its own red tag and a count beside the list — the Complete tab holds both
  finished states, and a failed quest that reads as done is one you never go back and look at.
- [x] **Setting a quest active does nothing.** Fixed by the tab rebuild. Verified live: marking
  *Stick to It* active took the Active tab from 52 quests to 53 and the quest appeared in it.
- [ ] **Storyline quests are missing entirely.** `[?]` **The distinction is not in the data.**
  tarkov.dev's tasks document carries no category field of any kind — the 24 keys on a task are
  trader, level, requirements, rewards, objectives, and flags for `kappaRequired` (13 quests),
  `lightkeeperRequired` (7), `factionName` (BEAR 6 / USEC 6) and `restartable` (16). Nothing names
  a storyline.
  - So this needs a source before it needs a design. Options: the wiki's own categorisation, a
    hand-kept list in the repo, or waiting for tarkov.dev to model it.
  - The nearest thing that *is* derivable and worth having meanwhile: filters for Kappa,
    Lightkeeper and faction-locked quests — the real "which of these actually matter" axes.

### Overlay freshness

- [x] **Adding an item to the watchlist does not reach the overlay.** It stayed stale well past any
  reasonable wait. Must work **mid-raid and between raids** alike.
  - Cause: the overlay only reloads its items when the *raid* changes — a position fix, a plan
    change, a raid starting or ending. Watchlist and have-count edits go through the tracker, which
    announces nothing, so the overlay is never told.
  - Push is the better answer than polling and the plumbing already exists: the raid session
    publishes to every surface over a WebSocket. Item changes should publish the same way, rather
    than the overlay waking up on a timer to ask.
  - Justin's stated fallback: a periodic refresh, or a toggle for one.

### Item identification (F10) card

- [ ] **The card shows far too much.** It lists every quest, every hideout level, every barter it
  can find, which is not what the question is.
  - What is actually wanted, in this order:
    1. **Is this needed for anything I am working on** — an active quest, a hideout upgrade in
       view, or a barter being tracked?
    2. **Is it on my watchlist?**
    3. **A link to the item's wiki page.**
  - Everything else is background. A card you have to read while standing over loot has to answer
    in one glance, and right now it answers by listing.
  - Note: "barters being tracked" depends on the barter-tracking feature, which is still open. The
    other two do not.

### Traders and quest gating

- [x] **Traders listed in the game's own order** — Prapor, Therapist, Fence, Skier, Peacekeeper,
  Mechanic, Ragman, Jaeger, Ref, Lightkeeper, BTR Driver. Alphabetical put Fence third and BTR
  Driver first, matching nothing anyone sees while playing.
- [x] **Loyalty levels a character level cannot have reached are shown as out of reach**, with what
  they cost, rather than merely unselected.
- [x] **Quest availability now checks trader loyalty.** 109 quests carry a loyalty gate that RatNav
  never read, which is exactly why Ready was overstating. At level 22 this moved 19 quests out of
  Ready and into Locked; 68 locked quests name a loyalty gate as their reason.
- [x] **"Done" renamed "Complete"**, matching the game's wording.

### Search

- [x] **Searching a quest by its own name found nothing.** "What's on the Flash Drive?" returned no
  results because the game writes a typographic apostrophe (`’`) where anyone typing uses a
  straight one — so a quest sitting in the Ready list could not be found by its name, which reads
  as the quest being missing. Search now folds punctuation away on both sides, and an apostrophe
  joins a word rather than splitting it, so "whats" finds it too. Applies to items as well; they
  have the same typeset names.

### Floors, overlap only

- [x] **Other floors are only faded where they actually overlap the one you are on.** Ghosting a
  whole floor treats every part of it as a conflict, but floors only conflict where they stack — a
  stairwell above a corridor is ambiguous, a warehouse at the other end of the map is not, and
  fading that too turned the map to frosted glass for the sake of a few square metres. Anything
  with nothing above or below it now draws in full, solid rather than dashed.

### Raid detection

- [x] **Raid detection only worked for transit raids.** `Locations:` comes from a `[Transit]` line,
  which the game writes only when you arrive by transit — which is why Streets was detected and
  Interchange silently never was. Every raid writes `Location: Interchange` inside a
  `TRACE-NetworkGameCreate` line; both are read now, and the map is matched on its aliases, its
  name and its normalized name, since the two lines spell it differently (`TarkovStreets` against
  `Interchange`).

### Show a map without a raid

- [x] **The overlay went blank with no plan and no raid.** It drew only when in a raid, so there
  was nothing on screen while a raid was loading — the game writes its `Locations:` line only once
  the map has loaded — or while looking a map over beforehand. Any calibrated map can now be put on
  the overlay from the buddy app's Maps tab, no plan required. The game's map still wins when a
  raid starts.

### Sharing by code

- [x] **Share a plan as a pasteable code rather than a file.** A real 4-stop Streets plan comes to
  556 characters. Deflate then base64url — no `+`, `/` or `=`, so URLs and chat clients leave it
  alone — and tolerant of the whitespace and line wrapping that pasted text picks up. The buddy app
  gained a Share section: get a code, copy it, paste a friend's, and it imports **and merges** in
  one step. The `.ratnav` file export stays. Paste it in and RatNav imports and
  merges exactly as the `.ratnav` file does today.
  - Note on wording: a *hash* is one-way and cannot be turned back into a plan. What is wanted is
    an encoded plan — compressed and text-safe — which is what makes pasting it work.
  - Must round-trip into the same document the file produces, so import and merge stay one path
    rather than two that can disagree.
  - Keep the file export as well: a code is for chat, a file is for keeping.
  - Size matters — it has to survive being pasted into Discord without wrapping into nonsense.
    Names are already resolved locally at import, so they need not travel.

### Your marker

- [x] **Your marker and facing cone keep one size at every zoom**, with their own **YOU** control.
  They were shrinking along with the pins, which is wrong for the one thing that matters at every
  zoom: you pull the map back precisely to ask where you are and which way you are pointing, and a
  marker that shrank with everything else stops answering exactly when it is asked.

### Map labels

- [x] **Place names are white**, not muted grey. At full ink the map's own roads and rock are pale
  enough that a grey caption disappears into them.
- [x] **Waypoints draw over place names**, not under. Labels were painted last and sat on top of
  the thing you were navigating to.
- [x] **Captions claim their space.** Extracts cluster along a map's edges and their names piled
  into something unreadable; one that cannot find room is dropped, and off-view extracts are
  shortened to their first word — "RAIL", "NORTH".
- [x] **Markers and text ease off as you zoom out**, with a **Shrink** control (0 = fixed size,
  1 = scales with the map, 0.55 default). Sized for reading a building they became a wall of
  overlapping furniture across a whole map.
- [ ] **Finer place names are not available to us.** `[?]` Map Genie shows "Warehouse 2", "Guard
  House" and the like on Woods; tarkov.dev gives 16 area labels and no more, the SVG carries no
  text elements at all, and Map Genie's are their own editorial work in a commercial product.
  - What tarkov.dev *does* carry, positioned and unused: 428 loot containers, 387 loose loot
    points, 327 spawns, 64 hazards, 8 BTR stops, 4 locked doors with their keys. Named by type
    rather than by place, so they would give "Weapon box" and "sniper" rather than "Warehouse 2".
  - The realistic answer is **custom waypoints**, already on this list: mark the spot yourself and
    label it. Worth deciding whether that closes this or whether it is worth hunting further.

### Quick controls, and what fade means

- [x] **Panel opacity is its own thing.** "Fade" was inking the map; how solid the *window* is —
  how much of the game the bordered corner panel covers while you run around — is a separate
  question, and the centred view has no border to fade at all. Per presentation, like everything
  else about placement.
- [x] **A quick bar for the three reached for constantly**: panel opacity, zoom, and still/follow.
  The stack can stay folded and those are still to hand.
- [x] **Ink is arrows, not a cycling button.** A single control reading "full" looks like a switch
  that is on — nothing said three other levels existed, or that the drawn map is on one of them.

### Raster tiles as a base layer — built, then removed

**Reverted at Justin's call.** The tiles extended well past the playable area and read as a
photograph under a diagram; what the map is for is structures, landmarks and roads, and the vector
says those precisely. The graphical ink level stays — that is the map's *own* palette, which is
what made Woods legible — but the tile layer, its endpoint and its cache are gone.

The research is kept below in case it is ever wanted again.

- [~] **A real raster base under the vector.** Asked for after seeing Map Genie.
  - **Map Genie's maps cannot be used.** They are a commercial product sold behind a subscription;
    their tiles are their own work. RatNav is MIT and public, so borrowing them would put a
    licensing problem into every copy anyone downloads. Their approach is fair to learn from,
    their artwork is not.
  - **tarkov.dev serves tiles, from the same project as the SVGs already in use.** Found in
    `the-hideout/tarkov-dev`'s `maps.json`, which the calibration already reads:
    - `tilePath: https://assets.tarkov.dev/maps/woods/main_0.16/{z}/{x}/{y}.png`
    - `minZoom: 2`, `maxZoom: 6`, standard slippy scheme, verified fetching
    - `transform: [0.1855, 112.95, 0.1855, 167.85]` — a Leaflet transformation mapping game
      coordinates into the tile CRS, which is what places them
    - Author **Shebuka**, already credited per map.
  - Alignment: the tile pyramid is square and does not share the SVG's extent, so the raster's own
    world bounds are derived from the transform and then placed through the existing, verified
    coordinate transform. Same normalized space as everything else.
  - **Needs eyeballing once built** — alignment is the kind of thing that looks right in numbers
    and wrong on screen.

### Graphical maps, second pass

- [x] **Buildings were invisible in graphical mode.** They were being drawn — Woods has 111 of
  them — in the map's own `.building { fill:#1a2632 }` against `.land { fill:#1f5054 }`. Near-black
  on dark teal, which at any sensible opacity over a game reads as nothing, so Sawmill looked like
  roads through empty ground. Structures and boundaries are now traced over the graphical base, so
  the base gives you the place and the tracing gives you the buildings and rooms.
- [x] **Ink is per presentation.** The centred map is for crossing ground and wants outlines only;
  the corner map is for when you have arrived and wants the full picture. They were sharing one
  setting, so choosing for one spoiled the other. Defaults: `outline` for F9, `graphical` for F5.

### Graphical maps

- [x] **Maps can now be drawn in their own colours.** Asked for as "a graphical base layer instead
  of the vector"; the answer turned out to need no raster at all.
  - **There is no calibrated raster to use.** tarkov.dev serves no map imagery — the maps document
    has no image field, `assets.tarkov.dev/maps/*.png|webp|jpg` all 404, and `tarkov.dev/maps/woods.jpg`
    returns the site's HTML shell rather than a picture. Community rasters exist but are not
    calibrated to these bounds, so each would need aligning by hand.
  - **The vector already carries the imagery.** Each map ships a stylesheet of ~15 colours —
    `.trees { fill:#144043 }`, `.water { fill:#4a6b96 }` — and RatNav was discarding all of it and
    recolouring every shape by role. Woods has 481 shapes across forest, water, rock and road; it
    looked minimal because most of it had been thrown away, not because it was not drawn.
  - A fourth ink level, **graphical**, draws the map in its own palette and line weights. The
    role-based levels stay: they read better over a firefight, this reads better as a map.

### Overlay layout, third pass

- [x] **A collapsed items drawer still reserved its width**, so with the list set to the left the
  map sat 259px off-centre with nothing in the gap. A collapsed drawer costs nothing now.
- [x] **The F6 grab bar and the map controls overlapped.** Both were anchored to the top-left of
  the same grid, so the bar's text ran under the first control. They get a row each.
- [x] **The Share section did not notice a plan being built.** It kept its own copy of the saved
  plans and had no way to hear a new one existed — so building a Woods plan and then being told
  there was none to share was the next thing to happen.

### Sharing, per map

- [x] **The Share section offered whichever plan happened to be first**, so it read "get code for
  Streets of Tarkov" while Customs was selected. Sharing is a per-map act — a Customs code is no
  use to someone queueing Streets — so it now follows the map you have selected, clears a stale
  code when you switch, and merges an imported plan with your own for **that** map rather than
  picking some other one.

### Maps tab, and custom waypoints

- [ ] **Keep the Maps tab, but bring it up to the overlay's standard.** *(Revised — the first
  instinct was to drop it.)* It is the weakest surface in the app: the overlay's map has had
  several passes of work and this has had none, so it reads as unfinished beside it.
  - **Not** aiming at Map Genie's level of detail. That is a different product and does it better;
    the expected workflow is finding a spot there and marking it here.
  - What it should be: a **rough wireframe** — the same ink treatment the overlay uses — with
    **search for a named place**, and the ability to **mark a spot** from what you find.
  - Concretely: the overlay's ink, halo, ghosting, place names and scale controls are all missing
    here, and the map already knows 46 named places on Streets that nothing searches.
- [x] **Custom waypoints.** Mark a spot on the Maps tab and it draws on the overlay, in raid,
  labelled.
  - Placed by clicking, which settled the open question: Map Genie's positions are in its own
    coordinate space, so clicking RatNav's own map is the only reliable route. Coordinates are
    stored **normalised** rather than in game units, so a mark also survives a change to a map's
    calibration.
  - Purple and a diamond — its own colour *and* its own shape. Colour alone fails for anyone who
    cannot separate the two hues, and a navigation overlay is a bad place to find that out.
  - **Not** part of a plan, which is the one deliberate departure from the ask. A plan is for one
    raid and gets cleared; "car batteries behind the garage" is true every raid, and having to
    re-add it each time is how a feature stops being used. They persist per map instead, and draw
    whenever that map is on screen.
  - Adding one in the buddy app pushes straight to the overlay rather than waiting for the next
    position fix.

### Floors, third pass

- [x] **Stack everything except where floors actually overlap** — already how it works: other
  floors draw solid where nothing on yours sits above or below them, dashed and dimmed only where
  they genuinely stack.
- [x] **The floor follows your elevation automatically** — already how it works: a position fix
  picks the floor from the height band your Y lands in, and a floor chosen by hand lasts only
  until the next fix.
- [ ] **More room-level detail inside structures**, where the source has it. `[?]` Measured
  earlier: on Streets most buildings have no interior geometry at all in tarkov.dev's drawing —
  only footprints on the ground floor and interiors for a handful of buildings one level up. Worth
  checking whether other maps are better served before deciding this is achievable.

### Overlay chrome, fourth pass

- [x] **The F6 grab bar covered the drawer handles.** Interact mode pushes the content clear of it —
  a handle you can see and cannot press is worse than no handle.
- [x] **The map and quest-name line is gone.** The drawers say what it was trying to.
- [x] **The settings handle moved to the bottom corner** as a gear, beside the counter. In the
  header it sat on the drawer handles, and it is the least-reached-for of the three.
- [x] **Drawer handles hide when interact mode is off**, with the rest of the furniture.
- [x] **The quest log said nothing when empty**, which read as a broken button rather than an
  empty plan.
- [x] **The items list is alphabetical.** Sorting by urgency reads well as a ranking and badly as a
  list — you come to it looking for one name.
- [x] **The watchlist sits above quests and hideout.** It is the short list you chose by hand; the
  other is worked out and far longer.
- [x] **Share is a modal**, behind two buttons, rather than a panel taking a third of the Plan page.

### Plan ordering and the quest log drawer

- [x] **Stops keep the order you picked them in.** The planner was solving a shortest route and
  renumbering what you had chosen — a confident answer to a question you had already answered. The
  optimiser still exists and is still tested; it is opt-in now.
- [x] **A chosen order survives a position fix.** Rerouting used to re-sort on every screenshot,
  undoing an arrangement seconds after it was made and moving the numbers while you walked to one.
- [x] **A merged plan keeps both orders, yours first**, with a shared objective holding *your*
  place for it rather than moving to theirs.
- [x] **Drag to reorder in the buddy app**, with the number shown on each row — the same number the
  map and overlay use.
- [x] **The F5 title shows one quest name of several**, picked because it happened to be first,
  which says less than it implies. Replace with just "RATNAV", set to the right so the drawer
  handles have room on the left. The quest log replaces what it was trying to say.
- [x] **A quest log drawer on the F5 overlay**, beside the items list.
  - Lists the started quests in the plan: **waypoint number, title, and a brief objective**.
  - Extremely minimal.
  - Same machinery as the items list: left or right of the map, swappable, poppable out, closing
    the popped-out window reattaches it.
  - A quick toggle for the items view too, alongside it.
- [x] **A shortcut on the Plan page to complete a quest**, updating quest state without going to
  the Quests tab.

### Extracts actually available to you

- [ ] **Read the in-raid extract list and show only what you can use.** Pressing `O` twice in raid
  lists the extracts open to you this run; the map shows every extract the map has, and several are
  never available on a given raid — wrong side, wrong conditions, wrong faction.
  - Toggleable in the overlay: all extracts, or only the ones offered to you.
  - The reading is the same technique as item identification — capture the screen, OCR it, match
    the names against the map's extracts. `ScreenTextReader` and the fuzzy matcher both exist.
  - `[?]` Triggered how? A hotkey pressed just after the game's list is up is the obvious route,
    since RatNav cannot know you pressed `O` without watching the keyboard, which it will not do.

### Bulk item import from a screenshot

- [ ] **Read a stash or scav-box screenshot and set have-counts from it**, as a first import rather
  than typing thirty numbers in by hand.
  - This is the **inventory OCR** that was deliberately left out of the original plan, now being
    asked for. Worth re-reading that reasoning before starting: it is grid detection plus icon
    template matching against tarkov.dev's item images — a different discipline from everything
    else here, needing iterative tuning against real screenshots, and accuracy is never perfect
    (stacks, mods, and lookalike icons all trip it).
  - **Must ship behind a review-before-apply screen** whatever the accuracy. Silently overwriting
    counts someone spent weeks accumulating is far worse than making them confirm a list.
  - "Initial import" is the right framing and makes it far more tractable: getting from nothing to
    roughly right is useful even at 80% accuracy, where continuously trusting it would not be.
  - Some of the machinery already exists: `ScreenTextReader` captures the screen and runs Windows'
    own OCR, and `ItemMatcher` fuzzy-matches text to items. A stash screenshot may carry readable
    stack counts and, on hover, names — worth checking whether OCR alone gets far enough before
    reaching for icon matching.

### Quest log pane

- [ ] **A quest log on the overlay**, listing the active quests being worked in this raid.
  - A **collapsible pane** alongside the items list. Either can be collapsed, and their order is
    yours — so whichever matters more that raid sits where you want it.
  - **Poppable out** into its own window, exactly like the items list.
  - Each quest **links to its wiki article**.
  - Shares the plumbing with the items list rather than duplicating it: the same section, collapse,
    dock-back, pop-out and placement machinery.

### Items panel, second pass

- [x] **No way to remove a watchlist item.** The star toggle was there and worked, but un-watching
  only swapped the row for an updated copy of itself — so it sat on the list with a hollow star and
  read as a button that did nothing. It is a ✕ on the watchlist now, and the row leaves.

- [x] **No way to set a watchlist target in the buddy app.** The Need field was only editable for
  items nothing else wanted, so anything a quest or the hideout also needed could not be given a
  target. On the watchlist it is always editable now — it is your number.
- [x] **The watchlist keeps its own have-count**, separate from the stash total. Twenty bundles of
  wires with fifteen earmarked for the hideout is not twenty available for a barter, and one shared
  number said it was — which is how you spend something already promised elsewhere.

- [x] **The watchlist was counting quest and hideout need, not your target.** Which is why one item
  read 19 on the overlay and 60/49 in the buddy app — neither figure was the watchlist's. The
  watchlist now counts the number you set; quests and the hideout keep their own section. A row
  with no target set shows a dash rather than a tick, because there was never an amount to finish.
- [x] **Section headings say what they are counting** — "buildable now" versus "+2 ahead" — so the
  same list at two look-ahead depths is tellable apart.

- [x] **The items list must never attach to the map in the F9 view.** That view exists to be a map;
  a panel over the middle of the screen is the thing it is for avoiding. Attached is now F5 only,
  and the button pops the list out instead when pressed in F9.
- [x] **A list popped out from F5 stays popped out across a switch to F9.** It is its own window;
  the presentation has nothing to do with it.
- [x] **A collapse control on the list itself**, visible whenever the list is — folding it away is
  an ordinary thing to want mid-raid, and reaching for `F6` first is not.

- [x] **Scrolling over the items list scrolls the map instead.** The wheel handler is on the window,
  so it wins before the list's scroll viewer ever sees it.
- [x] **The divider between map and list should be draggable.** First attempt used a GridSplitter
  sharing a column with the panel, which left its resize target ambiguous — it did nothing. Now a
  Thumb, which reports how far it moved and nothing else. Default width raised 170 → 235 so a name
  like "Chekannaya 15 apartment key" is not trimmed.
- [x] **The swap-sides arrow does nothing.** Both it and the pop-out button were wired correctly —
  the frame drawn behind F9's chrome earlier this round had a background, which made it
  hit-testable and put it over every control in the panel. It is decoration; it no longer takes
  the mouse.
- [x] **The pop-out button does nothing.** Same cause.
- [x] **Right-drag jumps the map to centre before dragging.** Cause: dragging switches following
  off, and following off means "centre on the middle of the map" — so the view snapped there the
  instant the drag began, and you dragged from the wrong place. Turning following off now converts
  where you were into an equal pan offset, so the view does not move at all.
- [x] **"Can buy" is the wrong words.** It suggests the flea market, which is not what is meant.
  - Wanted: either say it plainly — *does not need to be found in raid* — or drop the second entry
    entirely and let the legend explain only the red, with everything else reading as an ordinary
    needed item.

### Barter tracking

- [x] **Barters you are working towards, tracked separately from quests and the hideout.**
  - Example: Therapist's Dorm 303 key barter wants 7 plugs and 3 blue tapes. Picking that barter
    should put those on your list.
  - **Counted apart.** Those finds must not be folded into quest or hideout totals — an item wanted
    3× for a quest and 7× for a chosen barter is two separate reasons, not a single 10.
  - Shown as its own header or subsection in the items list, on the **overlay and the buddy app**.
    Exact UX is open. `[?]`
  - The data is already there: barters are fetched and indexed by what they cost, and each carries
    its trader, loyalty level, and what it hands back. What is missing is the ability to *choose*
    one and have it feed the list — the same shape as targeting a hideout upgrade.

### Crafts, and the shape of the items list

- [x] **Hideout crafts, tracked the same way as barters.** Pick a craft — a Toolset on the
  Workbench — and the items needed to start it appear on the list.
  - Gated by station level, so only crafts your hideout can actually run should be offerable.
  - tarkov.dev has a `crafts` document alongside `barters`; it is not fetched yet.
- [x] **Restructure the items list around this.** Built as: watchlist, then Barter, then Crafting
  (both appearing only once you have picked something), then quests & hideout, then Later. Justin's proposed shape, which supersedes the
  three-section split built in Round 5:
  - **Quests & hideout** — as now.
  - **Watchlist** — items of interest, with **subsections for Barter and Crafting**. Each names the
    barter or craft being worked towards and lists what it needs.
  - **Later** — still there, but focused purely on quests and hideout.
- [ ] **Look-ahead applies to quests as well as the hideout**, and is a toggle, not a fixed depth:
  immediate need, or one or two levels ahead.
  - **Helper text beside each section title** saying which it is currently showing — immediate
    need, or looking ahead. Without that the same list means two different things on different
    days and nothing on screen says which.
  - Note: a hideout look-ahead already exists. This extends it to quests and surfaces what it is
    set to, rather than leaving it implicit.

---

## Round 5 — 2026-08-19

### Overlay

- [x] **Separate saved state for F5 and F9.** Arranging the centred map overwrote the corner
  panel's position and size. Placement, zoom, pan, and follow are now per presentation; ink,
  opacity, halo, line weight and the items panel stay shared.
- [x] **Floor ghosting.** Draw the floors below the active one faintly underneath it. On Streets,
  ground and second floor need to be readable together — walking off a street into a building, the
  room means nothing without the street it came off.
- [x] **Extracts: green for PMC, yellow for Scav.** Bigger, using an extract icon, with the extract
  name drawn on the overlay. Currently too small to read.
- [x] **Place names on the map**, toggleable — "Old Gas", "New Gas", "Dorms".
- [x] **Waypoints bigger**, using a waypoint icon. Hover tooltip in the F5 view saying which
  objective it is; F9 needs the marker only, no tooltip.
- [x] **Cursor vanishes over the map in F6.** Should stay visible while positioning.
- [x] **F5 should hide the popped-out items window too** — it toggles every overlay component, not
  just the map.

### Items

- [x] **Drop money from item tracking** — roubles, dollars, euros. Overlay and buddy app both.
  These are not things you find in raid; they come from selling and quests.
- [x] **Set how many you need**, not only how many you have. The Needed view behaves correctly;
  the watchlist needs the same controls.
- [x] **Remove the flea price column.**
- [x] **Wiki link per item**, plus whatever indicates likely spawn locations.
- [~] **Items tab: sections like the overlay** — quests/hideout available now, collapsible, plus a
  look-ahead control so it is not 561 rows of the whole wipe.

### Hideout

- [x] **Game edition sets the starting stash level.** A Setup dropdown; Edge of Darkness and
  Unheard start at Stash 4, Prepare for Escape at 3, Left Behind at 2. Never lowers a stash you
  have already upgraded past.
- [x] **Check why Stash cannot be built** under "Buildable now" — confirmed correct, not a bug.
  Stash is 4/4 on your profile, so there is no next level to offer. Nothing was gating it; it is
  finished.

### Buddy app

- [x] **Map controls to match the overlay** — zoom, right-drag pan, layer control.
- [ ] **Plan page: call out required keys.** Show which key each objective needs, in red when not
  marked as held, and let it be marked found/held.
- [x] **Character level**, configurable in Setup, and now filtering which quests count as
  available. **Cannot be read automatically** — nothing the game writes to disk reports it, and
  the only endpoint that does needs your account password. Setup suggests a floor from the quests
  you have marked complete instead.
- [x] **Traders and their loyalty levels.** Own tab: loyalty 1–4 per trader, quests done and
  active, and what each will give you right now. **Loyalty cannot be derived** — it depends on
  rep, level and spend, none of which are on disk — so it is set by hand.

---

## Deferred, with reasons

- **Inventory OCR of the stash.** A different discipline — grid detection and icon matching — that
  needs tuning against real screenshots, and would ship behind a review-before-apply screen even
  then. Manual counts are reliable; this is an accelerator, not a foundation.
- **Live squad position sharing.** Needs hosting and NAT traversal. Plan merging delivers the
  coordination benefit with no infrastructure.
- **The Lab calibration.** Keycard-gated; needs a screenshot from inside.
- **ratnav.dev.** GitHub Pages site, `ratnav.app` redirect via CloudFront.
- **the tester's design pass.**
