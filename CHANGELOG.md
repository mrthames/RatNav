# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## Unreleased

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
