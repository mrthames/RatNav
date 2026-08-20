# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## 0.3.0-alpha.1 — 2026-08-20

The first public release. Alpha: it works and is used daily, but it is early.


### Maps

- **One Ground Zero, not two.** Ground Zero 21+ is the same buildings, the same streets, the same
  drawing and the same six extracts as Ground Zero — the game splits the location to decide who
  you meet there, not where anything is. It folded into Ground Zero, along with the tutorial
  variant and Night Factory into Factory. Quests attached to any of them still arrive on the one
  map, and a plan saved against an old one still opens.
- **"Coming soon" means a map that can actually arrive.** A map is listed only when a drawing
  exists and the sole thing missing is which way round it goes — Factory, Reserve and Terminal
  today, each one position away from being finished. The Lab, The Labyrinth and Icebreaker have no
  community drawing with coordinates, so they are not listed and not promised. The FAQ says why.
- **`[WIP]` beside a map still being worked on.** A stable release only ever contains finished ones.
- **Settle a map yourself** from **Maps → Settle it**: take a screenshot somewhere you recognise and
  click the spot.

### Reading a quest

- **Click a waypoint to open its quest** — on the overlay and on the Maps page. What it wants,
  which step this pin serves, a link to the wiki, and the wiki's screenshots of the place.
- **An info control on every item row**, saying which quests and which hideout levels want it.
  Quest names open the quest.

### Reading your stash

- **Scan a container from a screenshot** — a scav junk box, or a block of stash. Items are named by
  matching against the icons of what you already track, and nothing is written until you confirm
  it.
- **A row of one repeated item reads as a divider**, so a block taller than the screen can be shot
  in pieces without counting the overlap twice.

### Goals

- **Name what you are collecting for**, and list what it takes. This replaces the searchable
  catalogue of 789 barters and 214 crafts: finding the one you meant needed you to already know
  which of Therapist's four Dorm 303 trades it was.

### The overlay

- **A Size control**, because the defaults are drawn for 1080p and a 4K screen lands them at a
  quarter of the area.
- **The gear stays put** when the control stack opens, and closes it again on a second click.
- **Panel opacity stopped resizing the window.**
- **Hover labels are drawn into the scene**, so they no longer flash and vanish.
- **An open planner button** in the control drawer, and the buddy app opens with RatNav.
- **Spawn locations removed.** Built twice, as regions and as points, and neither read well on a
  map you are navigating.

### The items list

- **Items are named in full.** "Elite" is elite cutters, "Access" is a TerraGroup Labs access
  keycard, "Chek. 15" is the Chekannaya 15 apartment key. The game prints those short names on a
  stash cell, where they are exactly right and where RatNav still reads them; in a list you are
  scanning for a name you have in mind, they are not.
- **One alphabetical list**, on the overlay and in the buddy app. Found-in-raid items are no longer
  lifted into their own block — the colour says which they are wherever they sit, and two alphabets
  meant looking twice.

### The buddy app

- **Character level in the top navigation**, where it can keep up with you.
- **Traders as cards** with their portraits and one level control each.
- **The hideout as a grid** with the game's own station icons, an **Upgrade** control, and **max**
  where there is nothing left to do.
- **The Maps page matches the overlay** — same four draw levels under the same names, same
  waypoint, extract and mark symbols, same place labels.
- **Your marks can join a plan**, and come in two kinds: a place, or something to pick up.

### Setup, cut back

- The banner that said RatNav could see the game while the game was closed.
- The multiple-install section; one folder, detected, with a **Browse…** picker.
- The second-screen instructions, which were useless read from inside the thing they describe.
- The quest and item counts in the navigation.
- **Hotkeys are set by pressing the key**, not by typing its name. Two are gone — opening the panel
  and ticking an objective off — and what is left runs `F5` to `F9`.

## 0.2.0 — 2026-08-20

### Barters and crafts

- **Say which trades you are working towards**, and what they cost joins your list. Therapist's
  Dorm 303 barter wants seven T-shaped plugs and three rolls of insulating tape; RatNav knew that
  and had no way for you to act on it.
- **Hideout crafts too**, which were never fetched at all — 214 recipes and the station level each
  one needs.
- **Counted apart** from quests and the hideout, in their own sections on the overlay and their own
  line on a buddy-app row. An item wanted three times for a quest and seven for a barter is two
  reasons, not a single ten, and one number would hide that finishing the quest leaves seven still
  to find.
- The picker offers only what your traders and hideout can actually do, with a checkbox for the
  rest.

### On the map

- **Marks of your own.** Click a spot on the Maps page, name it, and it draws in raid — purple and
  diamond-shaped, so it never reads as a quest objective. They are not part of a plan, so they
  outlive every plan.
- **Only the extracts you can use.** Double-tap `O` in game and press `F11`: RatNav reads the list
  off the screen and draws only those.
- **Hazard zones read as hazards.** The stylesheet reader took a map's colours and dropped their
  opacity, so Streets' sniper zones drew as solid red blocks over the streets underneath.
- **Hover labels are drawn into the map** rather than popped. A tooltip belonging to a window that
  never takes focus opened and vanished in the same frame, which is exactly what hovering a
  waypoint looked like.
- The raster tile layer, added and then removed. It extended well past the playable area and read
  as a photograph under a diagram, and what this map is for is structures, landmarks and roads.

### Planning

- **Stops run in the order you ticked them**, and drag to change it. They were held in a set, which
  has no order — so what you got was whatever order the objectives happened to load in, presented
  as a route.
- **The keys are named**, red when you do not have one, with a tick to say you do. "Bring 3 keys"
  was true and useless.
- **Finish a quest from the plan**, without a trip to the Quests view. It asks first, because
  completing a quest retires its item needs.
- **Picks are kept per map**, so glancing at Woods halfway through building a Customs run does not
  throw the Customs run away.
- Ticked stops fall to the bottom, struck through, and numbering counts only what is left.

### Quests and items

- **Look-ahead follows the quest chain**, not just the hideout build order — and both surfaces now
  say where the dial is set, because the same list means different things at depth 1 and depth 4.
- **The wiki's screenshots for a quest**, from a `photos` button: the ones showing which building
  and which door. Loaded from the wiki and credited to it.
- **Filters on the items list** — found in raid, for quests, for the hideout, for a trade, keys —
  each carrying its count.
- **Failed quests are told apart from finished ones.** Both are finished states, but a failed quest
  that reads as done is one you never go back and look at.
- **The loot card answers instead of listing.** It used to recite every quest that had ever wanted
  an item, every hideout level and every barter; it now leads with Keep, Keep — found in raid, Not
  now or Leave it, and everything you are not working on gets one counted line.

### Elsewhere

- **RatNav has a mark**: a navigation arrow and a rat in one shape, on the executable, the
  installer, the tray and the favicon. The tray used to show the generic Windows icon.
- **A full guide and an FAQ** in `docs/`, for people who did not build this.
- **A way back from a lost overlay** — a window dragged onto a monitor you no longer have cannot
  be dragged back, and the only fix was editing settings.json by hand.
- **The game data cache carries a schema number.** Adding a field used to mean every existing
  install served a cache missing it until the six-hour age check happened to fire.
- Place names draw on the buddy app's map, and a search takes you to one by name.


### The map

- **Drag it.** Right-click and hold to move the map, so you can look at one corner while zoomed
  in. Dragging switches following off — looking somewhere else is a decision — and a crosshair
  appears to put the map back on you and lock it there, the way every mapping app works.
  Panning needs interact mode (`F6`): right-click is aim-down-sights, and an overlay that
  swallowed it mid-raid would be worse than one that cannot pan.
- **Following is per presentation.** The corner box follows you by default — it is too small to
  hold a map usefully. The centred map holds still by default — it is big enough to read as a map,
  and one that re-centres on every fix puts the same building somewhere new each time you look.
- **A halo behind every line.** This is what makes a translucent map readable over Tarkov, whose
  backgrounds run from snowfield to unlit basement — no single line colour survives both, and
  turning the opacity up until it does buries the game instead. With a **Line** weight control
  beside it.

### The items list

- Sits **beside the map** as a narrow strip rather than under it, on whichever side you put it.
- **Three foldable sections.** *Quests & hideout* is what active quests and buildable-now upgrades
  want. *Watchlist* is what you chose by hand. *Later* is upgrades gated behind something unbuilt
  and quests you could accept but have not — folded by default, and capped with the number of
  rows it left out rather than stopping silently.
- Rows are short names with the count in a fixed column, so the numbers line up. The full name
  and the reason are on hover.
- The pop-out window is **the overlay's list, parked elsewhere** — borderless, same ground, same
  rows, draggable anywhere, narrow enough for the edge of a screen.

### The hideout

- **Level controls that are actually controls**: `+` and `−` per station, so an upgrade marked by
  mistake can be put back. And a **Built it** button on each upgrade, because the moment you want
  to record one is while you are looking at it.

### Setup

- **Setup can now set things**, not just report them. The Escape from Tarkov folder, the
  screenshot folder, your in-game screenshot key, your hotkeys, and the name on shared plans are
  all editable, and every change takes effect immediately — including hotkeys, which rebind
  without a restart.
- The game folder is **detected, not assumed**. Setup says whether the path came from detection or
  from you, refuses a folder that is not an install rather than accepting it quietly, and takes an
  empty box as "go back to detecting it". A wrong folder used to look exactly like RatNav being
  broken.

### Fixed

- **The game version could not be read for ten hours a day.** Escape from Tarkov writes log folders
  with a non-padded hour — `log_2026.08.19_8-25-33` before 10am, `log_2026.08.18_23-01-52` after —
  and RatNav's parser required two digits. Any morning session failed to match, so patch detection
  quietly stopped working and Setup reported "no log sessions yet" over a folder full of them.
- Log sessions are ordered by the date in their own name rather than by filesystem timestamps,
  which copying an install or restoring a backup rewrites.

### The hideout

- A **Hideout** view: set where each station is, and see what that makes reachable next.
- A **Look ahead** control that means something. Every un-built level wants items, so the
  unfiltered answer is hundreds of them — most for upgrades gated behind three others you have
  not started. RatNav walks the game's own prerequisites instead: 1 is what you could build
  tonight, 3 is what to stop vendoring. On a fresh hideout that is 17 items rather than several
  hundred.
- **Target** the upgrades you actually want and the items list narrows to them, because widening
  a list is not what someone with a plan needs.
- Items say *which* upgrade wants them — "4 for Medstation 3" rather than "4 for hideout" — and
  can be ordered by what you are closest to finishing rather than by quantity.
- Trader and skill gates are shown but never hide an upgrade. RatNav cannot see your loyalty
  levels, and guessing would hide things you can in fact start.

### Plans

- **A plan outlives its raid**, and the app. Extracting no longer throws away what you were
  working towards; the stops stay, strikeable one by one, ready for the next time you queue that
  map. Restarting RatNav puts the plan back.
- A plan for another map is kept rather than applied. Queue Streets with a Customs plan loaded
  and the overlay shows Streets, says the plan is for Customs, and leaves it intact.
- **Turn-ins are confirmed, not assumed.** When every planned objective of a quest is ticked, the
  plan view offers to mark it turned in — and says so when you only planned part of the quest.
  Finishing objectives and handing a quest in are different events, the game does not reliably
  log the second, and a completed quest retires its item needs.

### The overlay

- The centred map view is now only the map. The title and the fix age were text over the game in
  the one mode that exists to keep the screen clear.

## 0.1.1 — 2026-08-19

### Fixed

- **Barters were empty.** The whole source failed to load because tarkov.dev records
  currency-priced trades with fractional counts — a barter costing "155.1" of something is real
  data, and 313 of the 789 have one. RatNav read them as whole numbers and threw away the
  document.
- **A dead source now says so.** The refresh above reported success every time while a source was
  broken, because a failed fetch was swallowed to keep the rest working. Failing soft is right;
  failing quietly is not. Setup now names any source that is down, and the check goes red.

## 0.1.0 — 2026-08-19

First release.

### Plan a raid

- Pick a map and see the objectives of your active quests grouped by the place players call it —
  Depot, Dorms, Old Construction — with the quest and trader for each.
- Tick what you are pushing and get a route ordered for you, plus the keys to bring. Keys are
  shown before you queue, because they are the one thing you cannot fix once the raid starts.

### Navigate it

- A hotkey-toggled overlay over the game, showing the route, your position, and the distance and
  bearing to your next stop — "Dorms · 140 m · 30° right".
- Position comes from the coordinates Escape from Tarkov writes into screenshot filenames. Tap
  your in-game screenshot key and the marker snaps, the route re-orders from where you actually
  are, and the screenshot is archived so the folder never fills up.
- Nothing animates between fixes, and the overlay says how old your last one is rather than
  implying the marker is current.
- The map picks its own floor from the height your fix lands at, so walking upstairs and taking a
  screenshot switches levels without touching anything. Looking at another floor by hand lasts
  until your next fix, then the map comes back to where you are standing.
- The raid ends when the game returns to the menu: the overlay goes idle and the plan is put away.
  Objectives you ticked off are kept — walking to a stop is a fact worth remembering, and a plan
  re-run later starts with them already crossed out. There is an **End raid** button for the times
  the game's logs do not make the ending obvious.

### Know what your loot is for

- Hover an item in game and press `F10`: RatNav reads the tooltip off the screen with the OCR
  built into Windows and says which quests want it, which hideout station **and level** needs it,
  whether it opens a door, and which traders will take it in trade.
- A key rather than shift-click, deliberately — catching a click over another application needs a
  system-wide mouse hook, which is the machinery RatNav will not use.
- It says how sure it is. OCR misreads, and a guess presented as fact would eventually cost
  someone a quest item.

### Read the map

- Extracts, with a **PMC / Scav** switch. Shared extracts show under either, because they work
  whichever you queued as. Drawn as diamonds against the objectives' circles, so the two are
  tellable apart without relying on colour.
- The map can **hold still** while your marker travels across it, or follow you and slide
  underneath. Still is the default: a map that re-centres on every fix puts the same building
  somewhere new each time you look.
- No line drawn between stops. It implied a route through walls that does not exist; the order is
  carried by numbered pins instead, and hovering one says which quest it is for.
- An items panel on the overlay — the watchlist first, then what quests and the hideout still
  want — which collapses, and can be torn off into its own window for a second monitor.

### Make it yours

- Bindable hotkeys — `F5` shows and hides the overlay, `F6` lets you drag, resize, and zoom it,
  `F7` opens the full panel, `F8` ticks the current stop off, `F9` switches presentation.
- Two presentations of the same state: a **box** in a corner for staying out of the way, or a
  **wireframe** map drawn large and translucent over the centre, with terrain faded back so the
  game still reads through it. Opacity and scale are yours to set.
- Your screenshot key is a setting — middle mouse by default — so every prompt names the key you
  actually press instead of a key someone else chose.

### Track items

- What every active quest and un-built hideout module needs, minus what you have.
- Have-counts entered by hand, because the game puts stash contents in no file on disk and
  guessing would be worse than asking.
- A watchlist for anything else worth remembering, with notes and targets.

### Share and merge plans

- Export a plan as a `.ratnav` file and merge it with a friend's. Nothing is dropped: every
  objective survives carrying its owner, and one you both picked becomes a single shared stop.
- Merging flags what changes how you run the raid — objectives to do together, items you are both
  hunting, and keys only one of you needs to carry.

### Honest about what it does not know

- Maps whose calibration could not be established say so instead of showing a pin that might be
  75 metres out.
- Quest progress read from the game's logs sits *under* anything you correct by hand, so a later
  replay can never undo a correction.
- When tarkov.dev is unavailable, the last good data keeps being served and the app says it is
  stale rather than emptying.
- Quests, items, and maps are re-checked at launch and every six hours, so a patch does not leave
  you planning around quests the game no longer has.
- Starting RatNav while already in a raid picks the raid up from the log rather than waiting for
  the next one — and does not replay old raids over your current position.
- When something does go wrong at startup, RatNav says so and writes the detail to a file. "It
  opened and did nothing" is not a bug report anyone can act on.

### Installing it

- An installer, so nothing needs a command line. It installs for your user only — no
  administrator prompt — and puts RatNav in your Start Menu. The .NET runtime is included.
- A portable zip alongside it, for anyone who would rather not install anything.
