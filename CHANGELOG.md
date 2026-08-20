# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## Unreleased

### Plan

- **No checkbox on an in-raid stop, and Quest done clears the plan instead.** Ticking a stop meant
  alt-tabbing out of a raid to do it, so it never happened and a plan stayed lit through raids it
  had nothing to do with. **Quest done** on the right of the row now retires every objective of
  that quest — including the ones that were never planned, so the next plan does not route you back
  through them — and un-marking the quest puts them back. The number stays, because it is what ties
  the row to the overlay and to the map. Checkboxes remain where choosing happens: the list you
  pick a plan's stops from.

### App

- **Reach RatNav from a phone or tablet.** **Setup → Reach RatNav from a phone or tablet** makes
  the service answer on your machine's network address as well as its own, so an iPad on the same
  wifi can open it in a browser. Nothing is installed on the other device, and a plan built there
  reaches the overlay in game immediately — both are looking at the same RatNav.

  Off until you turn it on, and **nothing outside your network can reach it**: port forwarding is a
  router-to-internet thing and is not part of this. There is no password either, which Setup says
  out loud — anyone already on your wifi can open it.

  Windows Firewall usually blocks the port. Setup checks, stays quiet when there is nothing to fix,
  and otherwise offers to add the rule — with a permission prompt, because opening a port always
  needs one — and prints the command for anyone who would rather run it themselves.
- **The port is configurable**, since 8722 can already be taken. Everything that talks to the
  service reads the port in use rather than the built-in constant, which eleven call sites were
  previously ignoring.
- **The header stops wrapping when the clock ticks.** Pressing refresh turned "updated never" into
  "updated just now" and pushed the character level, the timestamp, the refresh button and the
  profile menu onto a line of their own below the navigation. The timestamp now has a width that
  fits its longest form, and **Character level** sits above its controls rather than beside them.

### Overlay

- **The overlay's map controls read like the app's Maps page.** A **Quests** control — *Active*,
  *All*, *Off* — under the same name as the app's: active is the plan's stops, all adds every other
  started quest's objective on this map drawn hollow and unnumbered, off leaves the map clean. Draw
  levels, exits and quests are all written the way the app writes them — *Graphical*, *PMC*,
  *Active* — rather than as the raw lowercase setting, because two spellings of the same four
  choices read as two different sets of choices.
- **The map controls are reachable in the centred view.** The gear that unfolds them lived in the
  footer, which the centred view deliberately does not draw — so folding the controls away there
  left nothing on screen to bring them back. The centred view has its own gear now, floating over
  the map, and the controls open as a panel at the top rather than a column down the full height
  of the screen.
- **Edge arrows are drawn at full size.** The arrows that point at waypoints off the edge of the
  view were drawn at 0.7 with their text smaller again, on the theory that a direction should not
  be mistaken for a position. In practice it made the one marker whose position you cannot see the
  hardest thing on the map to read. An arrow is already unmistakably an arrow.
- **Every map caption is drawn, overlaps and all.** A caption that could not find clear space used
  to be dropped. Overlapping text is worse to look at, but a label that is not there cannot be read
  at all — and which ones vanished depended on draw order, so the same map lost different names at
  different zooms.
- **Two text dials instead of one.** **Map labels** sizes the place names the map itself carries;
  **Waypoints** sizes the captions on stops, extracts, marks and edge arrows. They are the backdrop
  and the destination respectively, and one control for both meant neither was ever right.
- **A floor dropdown on the quick panel**, per map, with **Stacked** first and default. Every floor
  is drawn over the other unless you say otherwise; isolating one is now one click away instead of
  buried in the controls drawer, and a position fix never changes it underneath you. The drawer's
  floor stepper can reach Stacked too — it could get into a single floor and not back out.
- **Fold the map away and keep the lists.** A **map** handle next to **quests** and **items**, in
  the panel view. Folding it leaves a narrow strip of exactly the two lists — for the part of a
  raid spent standing still reading what you still need, where the map is the biggest thing on
  screen and the least useful. Nothing about the map is given up: the zoom, ink, floor, follow and
  panel sides all come back as they were. Both widths are remembered — the one with the map and
  the one without — so neither has to be dragged back into shape. Folding the last list while the
  map is away brings the map back rather than leaving an empty window.
- **A divider wherever a panel meets the map — both sides.** There was one handle and it followed
  the items list, so with the list on the left and the quest log on the right only the left edge
  could be grabbed. Each side now has its own, and its own remembered width: pulling one edge in no
  longer pushes the other out.
- **A divider between the quest log and the items list** when they share a side. Drag it and both
  resize; whichever ends up too short to fit its contents scrolls. The quest log stays on top — it
  is the shorter of the two and the one you read rather than scan, so a long list above it buries
  it. The boundary is kept as a fraction, so resizing the overlay does not push one panel off the
  bottom.
- **Popped-out panels resize.** The quest log and the items list, torn off into windows of their
  own, can be pulled by any edge or corner. They are borderless so that they look like part of the
  overlay rather than like a dialog, and a borderless window has no frame for Windows to size it
  by — so the outer few pixels now answer as an edge, and the ordinary resize takes over from
  there. All four sides, because these sit down the side of a screen and the edge you want to pull
  is as often the left as the right.

### Hotkeys

- **`F6` and `F7` swapped.** `F6` now switches between the corner panel and the centred map, and
  `F7` is the one that hands the overlay your mouse. The view you flip between constantly belongs
  next to show/hide; the key you press to go and adjust something belongs further out. `F5`, `F8`
  and `F9` have not moved.
- **Your settings file comes with it.** A file still carrying the old pair is swapped once, on the
  next launch, and stamped so it is never rearranged again — which means you can now deliberately
  bind the old arrangement back and keep it. A file where only one of the two had been rebound is
  somebody's own choice and is left alone.

## 0.4.0 — 2026-08-20

Still alpha in the sense that matters — it is early, and it is being built as it is used — but this
is the version to install.

A large batch. Three characters are tracked separately, the Plan page was rebuilt around a strip
that does not move, reading a stash from a screenshot was removed, and the app stopped being called
the buddy app. Several things found while testing are fixed.


### Three characters


- **PvE, PvP and PvP Seasonal, tracked separately.** The game gives you three characters that share
  nothing — different quests accepted, different hideout, different loyalty — and RatNav was
  tracking them against one set of files, so quests finished on one read as done on the others.
  Switch between them from the menu at the right of the navigation, and RatNav opens on whichever
  you chose last.
- **Character level moved with them.** It belongs to a character, not to a machine. Your game's
  install path, your hotkeys and the cached copy of tarkov.dev stay shared, because none of that
  changes when you switch.
- **Start a character over**, from Setup. It names the profile, says what goes, and will not act
  until you type the name back — a confirmation you can dismiss by reflex is not one.
- **An existing install keeps its progress**, adopted into PvP. The files are copied rather than
  moved, so the originals are still there if anything goes wrong.

### Plan


- **One strip that does not move.** How many objectives are picked, what they need you to bring —
  keys in red — an explanation of the ordering, and **Plan this raid**, all in a row that stays
  exactly where it is as you tick things. The three panels used to resize as you worked, so
  whatever you were about to click had gone somewhere else.
- **The map is on the Plan page**, folded away by default and showing the waypoints of the quests
  you have ticked. Zoom and right-drag to pan; click a waypoint to read the quest.
- **Sharing and importing moved into a menu**, so the maps stay one click away and two occasional
  controls stop looking as important as the choice you make first.

### Items


- **Each item counts down.** Every item in a collection has a `+`/`−` of its own, and the number
  it shows is what is **left** — found four of six and the list asks for two. The count belongs to
  that collection rather than to a stash total, so two collections wanting the same item are two
  separate answers: plugs set aside for the document case are not also available for the workbench.
- **The overlay shows a foldable section for each**, so the one you are working on stays open
  while the rest are out of the way.

- **Reading a stash from a screenshot is gone.** It was built to fill in have-counts quickly, and
  have-counts are not the point of the list — what is still *needed* is. A feature kept because it
  works, rather than because it earns its place, is one more thing to explain and maintain.

### Tracking something yourself


- **Items → Custom → Add tracking.** Name it, search for the items, say how many. This replaces the
  searchable catalogue of 789 barters and 214 crafts: finding the one you meant needed you to
  already know which of Therapist's four Dorm 303 trades it was.

### The items list


- **Items are named in full.** "Elite" is elite cutters, "Access" is a TerraGroup Labs access
  keycard, "Chek. 15" is the Chekannaya 15 apartment key. The game prints those short names on a
  stash cell, where they are exactly right and where RatNav still reads them; in a list you are
  scanning for a name you have in mind, they are not.
- **One alphabetical list**, on the overlay and in the app. Found-in-raid items are no longer
  lifted into their own block — the colour says which they are wherever they sit, and two alphabets
  meant looking twice.

### Quests and maps


- **Maps can show active quests, all quests, or none.** Streets with every quest in the game pinned
  on it is a map you cannot read.
- **The hideout page drops the watchlist star.** Watchlisting belongs on Items; this page is for
  what to build.

- **What to bring, named.** The quest modal lists what a quest needs carried in, keys first — and
  the Plan page says it on the row you tick, before you queue rather than after. It used to read
  "needs a key", which told you there was a problem; "Dorm room 314 marked key" tells you whether
  you already have it.
- **The steps in a quest modal are clickable**, so reading a neighbouring step no longer means
  closing it, going back to the map and hunting for that waypoint.

- **The Quests list says less, on purpose.** The "available" tag and the "needs Prapor LL2" gate
  reasons are gone. Both were RatNav guessing at a screen you are looking at — you can see the
  trader, RatNav cannot. **All** is a searchable list of every quest; make one active and it moves
  to **Active**, complete it and it moves to **Complete**.
- **Coming soon is a list, not a job.** Settling a map's orientation is work for whoever builds
  RatNav, not for somebody who installed it, so the control asking you to do it is gone. Those maps
  arrive finished.

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

### Reading a quest


- **Click a waypoint to open its quest** — on the overlay and on the Maps page. What it wants,
  which step this pin serves, a link to the wiki, and the wiki's screenshots of the place.
- **An info control on every item row**, saying which quests and which hideout levels want it.
  Quest names open the quest.

### The overlay


- **Click a quest in the quest log to read it, in raid.** What it wants, which step this stop is,
  and the wiki's pictures of the place — the same panel a waypoint on the map opens. Press your
  interact key, click the stop, read it, dismiss it. On a single screen this was the one thing you
  had to leave the game for.

- **A key-bind reminder along the bottom**, up with the controls rather than over the game all
  raid. It names the keys you actually bound, including your in-game screenshot key — the one
  people forget. The app carries the same strip stuck to the bottom of the window, from the
  same source, so the two cannot drift.
- **The item card takes itself away after five seconds.** You read it in a moment and then it is
  over the game, and dismissing it by hand is a keypress nobody wants to spend mid-raid.

- **The strip above the map is half as tall.** The name and the bearing were stacked on two lines
  for two short pieces of text, costing the top of the overlay twice what the strip at the bottom
  costs. They share a line now, and every pixel saved is one the map gets.

- **A Size control**, because the defaults are drawn for 1080p and a 4K screen lands them at a
  quarter of the area.
- **The gear stays put** when the control stack opens, and closes it again on a second click.
- **Panel opacity stopped resizing the window.**
- **Hover labels are drawn into the scene**, so they no longer flash and vanish.
- **An open planner button** in the control drawer, and the app opens with RatNav.
- **Spawn locations removed.** Built twice, as regions and as points, and neither read well on a
  map you are navigating.

### The app


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

### Fixed


- **The Maps controls stay where you left them.** They shared one wrapping row with the map's own
  size deciding how it broke, so changing map moved everything. Search now has a row of its own —
  it is the one control you reach for by aiming rather than by reading.
- **Custom tracking can mark an item found-in-raid**, which colours its number on the overlay the
  same red the quest and hideout lists use. RatNav cannot know: a barter may demand it where a kit
  you are building for yourself does not.

- **The overlay draws Customs in its own colours again.** The **Graphical** level is meant to use
  the map's own palette, and it was drawing the flat wireframe one instead — the overlay was
  fetching a map the service had already restyled, so the palette it read back was RatNav's, not
  the map author's.
- **Money reads as money.** 400,000 roubles rather than 400000, anywhere an amount appears.
- **No "Built it" on an upgrade you cannot have built.** It appeared under *After one more upgrade*
  too, where taking it would record a level you do not have.

- **F8 reads the tooltip, not the cell.** Hovering a compass in a backpack reported a golden neck
  chain: six cells beside it were labelled "GoldChain" and its own cell was truncated to "Compa".
  Only the game's tooltip names what the cursor is on, so that is the only thing matched now.
- **"needs Ragman LL0" is gone.** Loyalty starts at 1, so a requirement of 0 is the source saying
  "no trader gate" in a field that has to hold a number. It read as a gate you could not be below.
- **The hideout's required items no longer slide under the Built it control.**

- **Zoom follows the pointer on the Maps page.** Scrolling zoomed about the top-left corner, so
  whatever you were pointing at slid away from under the cursor.

- **Quest photos load.** Both carousels showed the right titles over broken pictures: the wiki's
  CDN answers a request carrying a foreign address with a placeholder rather than the screenshot,
  so loading them straight from the page could never have worked. They come through RatNav now,
  which also means each one is fetched once and kept instead of pulled again on every view.
- **Quest photos fit their window.** They are 7000-pixel screenshots, and the picture was asking
  for the whole width of the box *and* a share of what was left over, so it pushed past the edge.

## 0.2.0 — 2026-08-20

### Barters and crafts

- **Say which trades you are working towards**, and what they cost joins your list. Therapist's
  Dorm 303 barter wants seven T-shaped plugs and three rolls of insulating tape; RatNav knew that
  and had no way for you to act on it.
- **Hideout crafts too**, which were never fetched at all — 214 recipes and the station level each
  one needs.
- **Counted apart** from quests and the hideout, in their own sections on the overlay and their own
  line on a row in the app. An item wanted three times for a quest and seven for a barter is two
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
- Place names draw on the app's map, and a search takes you to one by name.


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
