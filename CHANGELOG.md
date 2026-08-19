# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## Unreleased

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
