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
- [ ] **The map settings strip is horizontal and too long.** Reaching fade, zoom and the rest means
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

- [ ] **Anything outside the visible area needs an edge indicator.** Zooming or panning pushes
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
- [ ] **Spawn locations, toggleable on the map.** Useful for knowing where players who loaded in at
  the same time as you may be coming from.
  - Check whether tarkov.dev's maps document carries spawns, and what it distinguishes — PMC,
    Scav, boss. If it separates them, the toggle probably should too.
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

- [ ] **"By amount" / "What's next" appears to do nothing** on the Items page.
  - Verify it is actually being applied first — the request only sends `sort` for one of the two,
    so a bug is plausible. If it *is* applied, the two orderings may simply be too alike to notice:
    both fall back to found-in-raid and then quantity, and if most rows are wave 1 the leading key
    barely separates anything.
  - **If the distinction is not meaningful, remove the toggle.** Leave the tabs as needed /
    watchlist / search, and let the table be sorted by clicking its columns instead.
- [ ] **Filters on the items table**, which is the thing actually wanted here: show only what is
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

- [ ] **Traders belong on the Quests tab**, with their current level and up/down adjustment.
  - A separate Traders tab was just built (Round 5). This supersedes it — fold it into Quests
    rather than keeping both.
  - Auto-track if possible. Already established it is not: loyalty depends on rep, level and spend,
    none of which the game writes to disk. Manual stays.
- [ ] **Locked section for quests not yet reachable** — held there until the level is reached or
  the prerequisite quests are done.
- [ ] **Cut the tabs down to Active / Done / All.**
  - Remove **Available** — redundant.
  - Remove **To-do** — redundant to Active.
  - **All** shows everything: active, completed, and not started.
  - **Settled:** four tabs — **Active** (accepted), **Ready** (reachable, not accepted), **Done**
    (completed and failed), **All** (everything including locked). "Available" was not redundant,
    it was badly named.
- [ ] **Every quest offers four states: not started, active, done, failed.**
  - Quests start as **not started**.
  - **Done** moves it off Active and into Done.
  - **Failed** is also a finished state, but tracked separately so failures can be seen.
- [ ] **Setting a quest active does nothing.** It stays where it was — the status change is not
  moving it between tabs.
- [ ] **Storyline quests are missing entirely** — a recent addition to the game and not modelled
  at all. Needs tracking, likely as its own top-level section.
  - And a matching one for **side quests**.
  - Check whether tarkov.dev's tasks document distinguishes these already; if it does not, work out
    where the distinction comes from before designing tabs around it.

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

### Quest log pane

- [ ] **A quest log on the overlay**, listing the active quests being worked in this raid.
  - A **collapsible pane** alongside the items list. Either can be collapsed, and their order is
    yours — so whichever matters more that raid sits where you want it.
  - **Poppable out** into its own window, exactly like the items list.
  - Each quest **links to its wiki article**.
  - Shares the plumbing with the items list rather than duplicating it: the same section, collapse,
    dock-back, pop-out and placement machinery.

### Items panel, second pass

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

- [ ] **Barters you are working towards, tracked separately from quests and the hideout.**
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

- [ ] **Hideout crafts, tracked the same way as barters.** Pick a craft — a Toolset on the
  Workbench — and the items needed to start it appear on the list.
  - Gated by station level, so only crafts your hideout can actually run should be offerable.
  - tarkov.dev has a `crafts` document alongside `barters`; it is not fetched yet.
- [ ] **Restructure the items list around this.** Justin's proposed shape, which supersedes the
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

- [ ] **Map controls to match the overlay** — zoom, right-drag pan, layer control.
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
