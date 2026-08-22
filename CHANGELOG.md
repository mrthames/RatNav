# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## Unreleased

What has landed on `next` and has not been released yet, in two parts, because they are read by
different people.

**Everything that changes RatNav** goes first, written for somebody about to install an alpha:
what changed, and what to go and try. **Repository and process** goes second, for anybody working
on it. Both are required — every pull request adds a line — but only the first belongs in what a
user reads to decide whether to update.

### Changed for you

<!-- Add anything a person running RatNav would notice. Say what to go and try. -->

*Nothing yet.*

### Repository and process

<!-- Build, CI, docs, tooling. Real work, but nobody installs it. -->

*Nothing yet.*

## 0.4.1 — 2026-08-22

**Stable.** Nothing RatNav does has changed. What changed is that it now says what running it
means, on the page you are looking at while you install it, rather than in a document you would
have to go and find.

### Changed for you

- **Setup says what you are accepting by running RatNav**, in plain words, on the page you are
  looking at while you install it. Battlestate have not approved it and have not been asked to;
  avoiding the things anti-cheat looks for is not the same as being allowed; the uncertainty is
  yours and so is your account. Also what it reads, that nothing is sent anywhere, and that it is
  alpha.

  All of this was already written down, in the README and in `docs/SAFETY.md`. That is not the same
  as anybody having read it — somebody who has just run an installer is on the Setup page, not on
  GitHub. It folds away once read and stays one click from the top, because a warning that cannot
  be dismissed stops being read within a week.

  Raised by a reader of a post about RatNav, who pointed out that most people installing it will
  not audit anything and that assuming otherwise is the author's problem, not theirs. Fair.

  *To try: open Setup. It should be there and open the first time, gone after **Understood**, and
  reachable from the line at the top afterwards.*

### Repository and process

- **The README's version line is updated when a version is closed, not after it ships.** A workflow
  used to rewrite it once a release was published. It cannot any more: `main` is protected, and
  GitHub will not let a workflow bypass a ruleset on a personal repository — "the Actions
  integration must be part of the ruleset source or owner organization". So the push was refused
  and the front page went on naming the previous version. **Latest stable** now checks the line
  and fails loudly if it is stale, which is the useful half of what it was doing.

## 0.4.0 — 2026-08-22

**Stable.** Setting up no longer means fifty trips to the mouse, network access can no longer reach
back out of the browser, and a great deal changed underneath: the repository opened to
contributors, the web app got tests for the first time, and the release machinery stopped being
able to publish two different builds under one filename.

Released as `0.4.0-alpha.1` first; everything in that alpha is here.

### Changed for you

- **Network access can no longer reach back out of the browser.** RatNav can answer on your local
  network so a tablet can read a plan, and there is no password — the network is the whole
  boundary, which Setup says out loud. That is a fair bargain for reading a quest list. It was
  also, until now, enough to change your settings, wipe a character, quit RatNav, or put a folder
  picker on your screen from another device.

  Those five are now refused to anything that is not the machine RatNav is running on, whether or
  not network access is switched on. Reading and planning are unchanged, because that is what the
  feature is for.

  Raised by a reader of a post about RatNav, and they were right.

  *To try: nothing changes unless you use **Setup → Reach RatNav from a phone or tablet**. If you
  do, the tablet should still build plans and mark quests, and Setup's Save should refuse.*

- **Mark a quest active without touching the mouse.** On the Quests page, type part of a name and
  press **Enter**. The box clears itself, so the next name can be typed straight away — type,
  Enter, type, Enter. **↑** and **↓** move the highlight without leaving the box, **Esc** clears
  it, and what just happened is said next to the search so you do not have to go looking for the
  row to check.

  Setting up a fresh install means marking fifty or more quests active, read off the game on the
  other monitor. Done with the mouse that is fifty round trips before you have seen a single thing
  RatNav is good for.

  *To try: open Quests, switch to **All**, type part of a quest name, press Enter. It should turn
  active, the box should empty, and the line beside the search should say which one it was. Then
  try arrowing down to the second match before pressing Enter.*

### Repository and process

- **The repository is open to invited collaborators**, working on `next`. Stable releases still
  come only from `main`. Nothing changes for anyone downloading RatNav.
- **Review personas for AI-assisted work** in `.claude/agents/`, and a `CLAUDE.md` giving every
  agent the same starting context. Four auditors — safety, privacy, docs truth, code — with
  `review-coordinator` running whichever apply to a diff.
- **`/onboard` and `/review`**, two commands in `.claude/commands/`. `CLAUDE.md` can only be read;
  these do the things a file cannot — check the toolchain, prove the build and tests pass, run the
  auditors, and run every check that would block a merge before the pull request is opened.
- **The release list is tidied.** `v0.1.0` and `v0.2.0-alpha.1` are gone — neither had ever been
  downloaded, and the alpha shipped an installer with the stable release's exact filename. Their
  git tags are kept, so the history is still navigable. `v0.3.0` remains the current release and
  `v0.2.0` stays as a rollback.
- **Releases are named consistently, and the tag is checked before anything is built.** An alpha
  used to publish an installer with the stable release's filename, because the version pattern
  captured only the numeric part and dropped `-alpha.1` — so the two were indistinguishable in a
  downloads folder, and the alpha's binary reported itself as the stable version to the update
  check. Both artifacts are now `RatNav-<version>-<kind>` with the same version. A tag that is not
  `vMAJOR.MINOR.PATCH[-alpha.N]` fails the build, as does a release whose version has no
  `CHANGELOG.md` section, or any test that is skipped rather than passing.
- **The web app has tests.** 67 of them, across the Quests keyboard flow, onboarding and the setup
  gate, Setup's saving, the Plan page, Items, the Hideout and the API layer — where before there
  were none, and the app is a third of what people use. They run on every pull request and again
  before a release. `src/test/service.ts` holds the shared vocabulary so a contributor writes a
  test rather than a fixture.

  Writing them found two bugs that had been there for months: **the Hideout page had no error
  handling at all** and sat on "loading hideout…" forever when the service refused, and **a failed
  item count escaped as an unhandled rejection**, leaving the row looking ready while nothing had
  changed — so the honest reading was that the click had not registered.
- **A bad release tag fails in seconds, not minutes.** Tag validation and the changelog check ran
  after the web build and the whole test suite, so a malformed tag burned a full build before
  being rejected — and the comment above them claimed otherwise. They now run straight after
  checkout, which is what the comment always said.
- **`tools/check-the-safety-line.sh`**, which fails the build on any API that would read game
  memory, hook input or rendering, or send input to the game, and on any native call that is not
  on its allowlist. The promise on the front page is now checked rather than remembered.

## 0.3.0 — 2026-08-21

**Stable.** A day of using RatNav in live raids at 1080p rather than looking at it on a desktop,
and fixing what that turned up. The overlay follows the raid now instead of being told about it,
the Plan page is in the order the work actually happens, and the sizes it ships with were measured
in a raid rather than chosen.

### The overlay follows the raid

- **It shows itself when a raid starts and hides when one ends.** Between raids it was over the
  stash, the flea and the traders, which is where you sort inventory and click through menus — and
  it had nothing to say there anyway.
- **The map draws itself.** *Show on overlay* is gone from the Maps page: a raid already names the
  map it is loading, so the button covered a case that handles itself.
- **The waypoints panel opens only when there is a plan.** With none it stays closed rather than
  holding a side of the overlay for an empty box. The items list opens however you left it — it is
  the shopping list, and it is worth reading between raids.
- **The quest log is called WAYPOINTS**, because it stopped being only quests when marks of your
  own could join a plan.

### The sizes are measured

Every size dial reads **1.0×** on a fresh install and moves either way from there. The settings
hold a multiplier of a base that was tuned in a live raid, so "a bit smaller than default" is
something you can express — where `0.75` told nobody anything, and a dial that started at its own
floor was a limit rather than a default.

Screens taller than 1080p scale by their real height ratio, floored at 1.0 and capped well short of
double. An existing settings file comes onto the new numbers if it was on the old defaults, and
keeps the size it draws at if you had chosen one.

- **Edge arrows and their labels have their own sizes.** They followed the waypoint dials but not
  the zoom shrink, so the two drifted apart and neither dial appeared to control them.
- **The dials are named for what they draw** — waypoint pins, waypoint labels, map labels — rather
  than for the machinery behind them, and step in tenths down to 0.25.

### Added

- **The settings are a window of their own**, grouped into sizes, the map, what to draw, and the
  window. There is no room inside a small overlay for a panel that configures it: centered it
  covered the map, and against an edge it collided with the quick controls.
- **The centered view keeps a rectangle.** Size and place it where you like; the coverage dial still
  puts a centered one back. Its default was measured rather than derived from the dial.
- **Transits have their own on/off**, separate from whose extracts to show — anybody can take a
  transit whatever they queued as.
- **Bring lists say how many.** `3× MS2000 Marker` rather than a bare name, summed across the
  objectives you picked. Keys stay uncounted: a key is one key however many doors it opens.
- **The character is in the header** — *RatNav — PvP Seasonal* with a caret — rather than behind an
  icon that gave no clue it was about that. A fresh install opens on the seasonal character.
- **Resize the overlay from any edge or corner**, in both views.

### The Plan page

- **In the order the work happens**: pick a map, see what it will cost you, then the plan, then the
  list you pick from. The plan used to sit above the thing that builds it.
- **It folds down to the plan once one exists**, with **+ Add a stop to this plan** at the foot and
  **End raid** on the bar. Updating mid-raid rebuilds the plan from what is ticked, so the overlay
  follows.
- **Quest done no longer asks**, and now does both halves of "done" — the stop strikes through and
  moves to the bottom, and undoing puts it back where it was.
- **No map preview and no drag-to-reorder list.** The Maps page is where a map is looked at, and
  the order is the order you tick things in — nothing re-orders it afterwards.

### Fixed

- **A build nobody released claimed to be the newest release.** It read 1.0.0 — .NET's default for
  an unstamped assembly, which is newer than every real release by every comparison — so Setup
  said "you are on 1.0.0, which is the newest release". The version also came from the entry
  assembly, which is the test runner under a test runner.
- **The map zoomed and scrolled the page at once.** React registers its wheel listener passively
  and a passive listener cannot cancel the event, so `preventDefault` was quietly ignored.
- **The exits filter read backwards.** It said what pressing it would do while every neighboring
  control said what it *was*.
- **Transits leaked into the list of extracts the game offered**, because the reader matched
  OCR'd names against a list that now includes them.
- **Edge labels fell off the bottom of the map.** They were always drawn below their arrow, which
  is inside the view on the top edge and off the map on the bottom. They also had no backing, and
  edge markers crowd by nature.
- **The settings opened themselves** whenever you pressed the interact key, because the panel's
  open state was being saved to disk.
- **Closing a popped-out quest window took the app with it.**
- **The centered view did not center on you** after switching to it — the redraw ran before the
  layout had settled, so the map was centered on a rectangle that no longer existed.
- **Custom waypoints no longer ask for a note.** The name was all anybody wanted to say. Notes
  already saved are untouched.

## 0.2.0 — 2026-08-21

**Stable.** Everything from the 0.2.0 alpha, plus a day of sitting in front of the overlay at
1080p and fixing what that turned up. The sizes RatNav ships with are now measured rather than
guessed, and the two surfaces agree with each other in a lot of places they quietly did not.

### The defaults are measured now

The overlay was tuned on a 1920×1080 screen until it looked right, and those numbers are what
ships. **Every size dial reads 1.0** and moves either way from there — the settings hold a
multiplier of the tuned base rather than an absolute size, because "0.75" tells nobody anything
and "a bit smaller than default" tells everybody.

Screens taller than 1080p scale by their real height ratio, floored at 1.0 and capped well short
of double. A dial still reads 1.0 and still means "right for this screen".

An existing settings file comes along if it is still on an old shipped default, and keeps the size
it draws at if somebody chose one.

### Added

- **Update checking.** Once a day RatNav asks GitHub whether there is a newer stable release and
  says so on Setup, with a link. It never downloads or runs anything. Refusable, and **Check now**
  works with the daily check switched off. Prereleases do not count — but a release does beat the
  prerelease it came from, so an alpha tells you when its stable ships.
- **An installer that looks like the app** — RatNav's ground and its mark, rather than the stock
  gray wizard, with a welcome page that says what this is before asking anyone to agree to a
  license.
- **`F8` follows or holds the map still**, and **`F9` puts the map back on you** without starting
  to follow — for a still map, which is still on purpose, when you want to know where you are
  once.
- **Transits are on the map**, in their own color with their own symbol, with their own on/off
  control. They were missing entirely: tarkov.dev keeps them in a separate list RatNav never read,
  so Interchange drew six of its eight ways off the map.
- **How far each exit is**, in meters under its name.
- **Tick a stop off from the overlay**, or drop it from the plan. Ending a raid marks a quest
  complete when every one of its objectives is.
- **Group the items list by type** — the handbook's categories, alphabetical inside each.
- **Setup is a prerequisite.** With the game folder unset, every other page says what is failing
  and offers the way there rather than showing nothing.
- **Resize the overlay from any edge or corner**, not just the one grip.
- **A toggle for the hotkey reminder strip.**

### Fixed

- **The game is found where the launcher put it.** Detection probed a fixed list of paths, so it
  only ever found an install nobody had moved — the first tester hit exactly that and spent
  minutes on empty pages with nothing saying why. It reads the installer's own record now.
- **`F5` to `F11` run in the order you use them**, rather than the order the keys were added.
- **Dragging a panel divider threw "Access to the path is denied"** and then would not move. Three
  faults: a settings save that let a transient file-move failure out, one save per drag event
  rather than one per drag, and a save running from a mouse handler so the exception unwound the
  drag. The divider's limits were fixed pixel numbers, which left thirty pixels of travel on a
  small overlay.
- **Torn-off panels swallowed every click** in their rectangle, whatever edit mode said — the
  overlay's click-through styles were never applied to them.
- **The hideout look-ahead counted from one**, so the dial read 1 above the words "showing only
  what you can finish now". Zero is now-only. Its three dials are one setting now; two of them
  were not saving.
- **A collection's heading counted the wrong thing** — "3 left" for six items, because it counted
  rows.
- **The quest brief covered the map and stopped at the lists**; it covers the whole panel now, its
  text and picture sit together rather than at opposite ends, and clicking outside dismisses it.
- **A popped-out quest log opened nothing** when you clicked a quest. It opens a window of its own.
- **The overlay showed a plan built for another map.**
- **The quest log's tick-off controls never appeared**, because rows are built with that decision
  baked in and entering edit mode did not rebuild them.

### Changed

- **The settings are a centered modal** in two columns, rather than a drawer down the left edge.
- **The quick controls carry only what changes mid-raid** — floor, follow, exits — with everything
  else behind the gear.
- **Search is a box on the items page**, not a tab. Type and the list becomes results; clear it and
  your list is where you left it.
- **The items page defaults to nearest upgrade**, which is the question it is usually open for.
- **Keys count as being for a quest**, so the separate Keys filter is gone. A key is recorded as
  something to bring rather than hand in, which is why it could not match the quests filter.
- **Say "center"**, not "center", and "press" rather than "tap".
- Edit mode's buttons are 15% smaller, the overlay's padding is tighter, the position readout moved
  to the header, and the "0 of 3 done" counter is gone.

## 0.2.0-alpha.1 — 2026-08-21

**An alpha.** The stable release is still 0.1.0 and the download on the front page still points at
it. Everything here comes out of the first real user test — four hours of someone installing 0.1.0
cold and playing with it — and it is a lot, so it wants using before it is promoted.

### Fixed

- **Rebinding to an arrow key did nothing.** The browser and WPF disagree about what keys are
  called: the browser says `ArrowLeft` where the enum says `Left`, and `1` where it says `D1`. So
  rebinding worked for letters and function keys and silently failed for the arrows, the whole
  number row, Backspace, Page Up, Page Down and the space bar. Both spellings are accepted now, and
  the keys are displayed the way a person writes them rather than the way the enum spells them.
- **Saving on the Setup page threw away the overlay's size and position.** The settings are a
  record, and the overlay changed its own copy — so the service's copy still held the arrangement
  from launch, and Save wrote it back over the file. Every drag, resize, zoom and panel choice since
  start-up went with it. There is one instance now rather than two racing to be last.
- **Half the key-requiring quests never mentioned their key.** Reported against *Farming*; it was
  **29 of 57**. Keys are recorded against objectives, and only an objective with coordinates becomes
  a waypoint — so a key belonging to a positionless objective was attached to nothing and shown
  nowhere. The quest's own key list is read instead, which is complete.
- **The quest log stayed empty unless the items list was open.** The refresh returned early when the
  items panel was closed, and filled the quest log after that return — so the log only ever
  refreshed as a side effect of the list wanting to.
- **A custom waypoint in a plan was drawn twice**, red as a quest stop and orange as a mark, at the
  same point. It is drawn once now, as the stop, in the waypoint color.
- **Clearing a plan wiped the map.** The chosen map is not part of the plan — it is what makes a map
  draw without one. It survives now, and goes only on a restart or a change of map.
- **The overlay scaled itself to about 2.0 on a 1440p screen.** The size was derived from the
  screen's height in device-independent pixels — which already has display scaling divided out, so
  the multiplier applied a correction twice. The derivation is gone and the default is 1.0.
- **Importing a plan code said "returned 400" and nothing else.** The service explains every refusal
  and the app threw the explanation away. It shows the reason now. Two of those reasons were also
  wrong: a code that had lost its prefix was reported as coming from a different version of RatNav,
  and a truncated one leaked the JSON parser's own output.

### Added

- **A guided first launch.** A new install opens on **Setup** when a required check is failing, and
  a checklist carries you through the four things that have to happen in order — point RatNav at the
  game, mark your quests, set your hideout levels, build a plan. Each step ticks itself off from
  real state rather than from a remembered position, so it is right whatever order you do them in.
- **Browse for the screenshot folder**, as the game folder already had. It is the one OneDrive
  moves, so it is the one people most need to go and find.
- **Reset hotkeys to defaults**, per section, so resetting keys does not touch folders.
- **Save sits on a sticky bar** at the bottom of Setup, with **Buy me a coffee** at the far end, and
  says whether there is anything unsaved.

### Changed

- **The size dial is called "UI scale"**, which is what it is.
- **What a raid needs you to bring is loud.** Keys are filled rather than tinted; quest items are
  outlined in the warning color rather than the muted gray of a disabled control. It is the one
  thing on the page that cannot be fixed once you have queued.
- **The Setup fields line up.** The Browse button sat below the box it belonged to, by however tall
  the hint underneath happened to be.

## 0.1.0 — 2026-08-20

The first release, and the numbering starts here. Everything below was built before it: the
versions published while RatNav was being figured out are gone, and this is the one to download.

**Alpha, and the version number says so.** RatNav works and is used daily. `v1` is for when it has
settled.

### Verified before release

- **A first install was actually run**, against an empty data directory, rather than read: the game
  folder is found, the logs are read, the screenshot folder is located, the screenshot key is
  recognized, and the game data downloads. The port fallback proved itself in the same run — 8722
  was already taken, so it took 8723 and said so.

### Plan

- **No checkbox on an in-raid stop, and Quest done clears the plan instead.** Ticking a stop meant
  alt-tabbing out of a raid to do it, so it never happened and a plan stayed lit through raids it
  had nothing to do with. **Quest done** on the right of the row now retires every objective of
  that quest — including the ones that were never planned, so the next plan does not route you back
  through them — and un-marking the quest puts them back. The number stays, because it is what ties
  the row to the overlay and to the map. Checkboxes remain where choosing happens: the list you
  pick a plan's stops from.

### App

- **The Maps page controls stop moving.** Half of them only appeared when they had something to
  offer — the floor picker on a map with floors, exits on a map with extracts — so changing map
  reflowed the row and whatever you were reaching for moved. A control with nothing to offer is
  dimmed in place now.
- **Custom waypoints, after using them for real.** *Mark* is **Custom waypoints**: a toggle for
  whether they are drawn and a **+ Waypoint** button, instead of choosing *a place* or *an item*
  before you have said what it is. Naming one uses RatNav's own dialog rather than the browser's
  prompt box, and asks for a name and nothing else — a note can follow from the waypoint's chip
  under the map. The pin is now the same shape a quest stop draws, in the waypoint color: what
  separates yours from a quest's is where it came from, which is what a color is for. "something
  to pick up" no longer appears under a waypoint's name, because nobody chooses that any more.
- **The place search is gone** from the Maps page. The names are drawn on the map you are looking
  at.
- **Setup says that hotkey combinations work.** `Ctrl+Alt+T` has always been a valid binding and
  nothing on the page said so — which is the binding a Stream Deck macro wants, so it can send
  something the game will not fight over. It also says plainly that mouse buttons cannot be used:
  catching one over another application needs the system-wide mouse hook RatNav refuses, and a
  two-key combination covers the same ground. Your in-game screenshot key is unaffected — RatNav
  only names that one.
- **"Leave a folder empty to go back to detecting it"** moved from beside the Save button, where it
  read as an instruction about saving, onto the two folder fields it was describing.
- **Planning is closed while you are in a raid you already have a plan for.** The obvious next
  click on the Plan page was one that replaced the plan you were in the middle of walking. The
  quest list and **Plan this raid** are unavailable until you **End raid** or **Clear plan**, and
  the page says so where the controls were rather than just removing them. A plan with no raid is
  the ordinary between-raids case and stays fully editable.
- **"updated 2 hours ago" says what was updated.** It is when the quest, item and map catalog was
  last fetched from tarkov.dev — which only changes when the game does, so hours or days old is
  normal. With no subject it read as the whole app being stale, which sent people looking for a
  fault that was not there. It reads **game data** now, and both it and the refresh button explain
  themselves.
- **The map works by touch.** One finger pans and two pinch to zoom, about the point between them
  — which is what every map on a phone does and therefore what fingers already try. The mouse is
  untouched: left-drag on a desktop is a selection, and turning that into a pan would break the
  thing people do reach for.
- **The navigation wraps at phone width** instead of running past the edge, and the character-level
  steppers are finger-sized on a small screen. A navigation you have to scroll sideways to reach
  half of is not one.
- **Quit RatNav from the app.** Closing the browser tab stopped nothing — the tab is not the
  application, it is a page one process serves alongside the overlay and the hotkeys — and the only
  way out was the tray icon, which people do not look in. **Setup → Quit RatNav** stops the whole
  thing. It asks first, because there is no undo.
- **A mark can carry a note.** The label is drawn on a map over a game and has to be short — "car
  batteries". The note is read standing still and can be a sentence — "third shelf, behind the
  crates" — which is the part nobody remembers at the time. Add one from the mark's chip under the
  map; it shows on the pin, and against the stop in the overlay's quest log once the mark joins a
  plan.
- **Mark a spot from the Plan page.** The *Mark* control was on the Maps page only, so noting "I
  want to go here as well" while building a raid meant going to Maps to place it and back to Plan
  to add it — two navigations for one thought. It is on both maps now.
- **Your own marks are orange.** They were purple, which is a fine hue and the wrong one: it is the
  quest objectives they have to be told apart from at a glance, and orange against their red-orange
  reads faster. Yellow was the obvious alternative and is already what a scav extract is.
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
- **The port is configurable**, on the Setup page, since 8722 can already be taken. Everything that
  talks to the service reads the port in use rather than the built-in constant, which eleven call
  sites were previously ignoring. If the port RatNav wants is already in use it **moves to the next
  free one and says so** rather than refusing to start — the Setup page lives inside the service,
  so a conflict that stopped the service would be one you could not reach the setting to fix.
- **The header stops wrapping when the clock ticks.** Pressing refresh turned "updated never" into
  "updated just now" and pushed the character level, the timestamp, the refresh button and the
  profile menu onto a line of their own below the navigation. The timestamp now has a width that
  fits its longest form, and **Character level** sits above its controls rather than beside them.

### Overlay

- **The interact key no longer opens the map settings.** Pressing it hands over the mouse and shows
  the handles — the grab bar, the drawer chips, the gear. The stack of map settings behind the gear
  is a thing you go and get, not a thing you are handed every time you reach for the mouse. It is
  remembered once you open it. Existing settings still on the shipped value come along.
- **The controls stack stops colliding with its neighbors.** It ran from the top of the window to
  a few pixels off the bottom, overlapping the quick bar above it and the footer below. Its margins
  were fixed numbers that were right once — the footer has grown a second row since, and everything
  scales with the size dial. It measures the neighbors now.
- **Say what to press when RatNav does not know where you are.** Load into a raid and the map draws
  centered on the middle of itself, which looks like a map that has decided something when what it
  has done is wait. It now says so over the map, naming your own screenshot key, until the first
  position lands.
- **No more "455 m · 43° right".** Gone from the overlay's header and from the Plan page's in-raid
  strip. It read as useful and was not: a straight line to a pin through whatever walls are between
  you and it, on a bearing relative to a heading that was current at your last fix. Precise about
  something nobody can walk, and stale the moment you turn — and the map answers the same question
  honestly.
- **A short quest log takes the room it needs and no more.** It used to hold open a fixed share of
  the side whatever it had in it, so three quests sat in a section sized for ten and the items list
  below was squeezed for nothing. The divider is a **ceiling** now rather than a split: drag it
  below the contents and the log scrolls, and dragging it above them does nothing, because there is
  nothing to reveal.
- **Scroll bars are a thumb and nothing else** — no track, no arrows, four pixels wide, and only
  when something is actually hidden. WPF's own is a chunky light column with a button at each end,
  which over a dark translucent overlay reads as a control from another application. **They also
  disappear with the rest of the controls**: with the mouse back in the game a bar cannot be
  dragged and the wheel is not the overlay's, so it was a mark sitting over a raid for no reason.
  The wheel still works the instant interact mode returns.
- **Popped-out panels carry the main overlay's handles** — the same solid light-blue bar to drag
  and the same light-blue corner to pull, instead of a faint strip and an invisible edge. The
  edges still resize; what was missing was something to see.
- **The centered map turns with you.** Your heading points up the screen, so what is drawn at the
  top is what is in front of you — no more reading a route that runs behind you off the top of a
  monitor that is in front of you. Your cone points straight up, because up is now where you are
  looking, and the captions and place names stay upright while their positions turn. **north up**
  in the controls keeps the old behavior. The corner panel never turns: it is a small still map
  you glance at to orient against buildings, and one that spun every time you turned would be
  unreadable.
- **The fade stops dimming the controls.** In the centered view, turning the map down to a faint
  wash took the control panel with it — so the setting you most want was the one that made the
  controls hardest to use. The map carries the fade there; the controls stay solid.
- **No edge arrows in the centered view.** They stay in the corner panel, which is small enough
  that something just outside it is genuinely lost. The centered view is large and already shows the
  ground you are crossing, so the same arrows ringed the edge of the thing you were looking
  through — at exactly the point where the drawing is deliberately fading out.
- **The centered view opens where it is useful** — zoomed in and following you, rather than fully
  zoomed out and still. `outline` stays its default.
- **The centered map turns into a full-screen HUD.** A **Coverage** dial in the controls: at 100%
  the centered view takes the whole screen, the drawing **dissolves toward the edges** instead of
  stopping at a border, and the map lines **glow** rather than sit in ink. Below 100% it is the
  centered window it has always been — one dial rather than a third view to configure, since `Box`
  and `Wireframe` already carry separate settings. **Edge fade** and **Glow** are their own dials
  and appear only at full coverage.

  **Clicks pass through the HUD except on a control.** Click-through is a whole-window setting,
  which is fine until the window *is* the screen — then reaching a control means the whole screen
  stops passing clicks, so you cannot shoot, and an interact key hit by accident mid-raid leaves
  you unable to click at anything. At full coverage the window answers per point instead.

  The centered view is also now genuinely centered: its position and size come from the dial, so it
  no longer offers a drag handle and a resize corner that the next layout would undo.
- **The overlay's map controls read like the app's Maps page.** A **Quests** control — *Active*,
  *All*, *Off* — under the same name as the app's: active is the plan's stops, all adds every other
  started quest's objective on this map drawn hollow and unnumbered, off leaves the map clean. Draw
  levels, exits and quests are all written the way the app writes them — *Graphical*, *PMC*,
  *Active* — rather than as the raw lowercase setting, because two spellings of the same four
  choices read as two different sets of choices.
- **The map controls are reachable in the centered view.** The gear that unfolds them lived in the
  footer, which the centered view deliberately does not draw — so folding the controls away there
  left nothing on screen to bring them back. The centered view has its own gear now, floating over
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
- **Folding the map no longer hides the handle that brings it back.** At the narrow width a folded
  map leaves, "position 24 minutes ago" ran over the **map** chip — the one control that unfolds
  it, which made folding very nearly a one-way trip. The footer chips wrap now instead of
  overflowing, which settles the same collision at large UI scales.
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

- **`F6` and `F7` swapped.** `F6` now switches between the corner panel and the centered map, and
  `F7` is the one that hands the overlay your mouse. The view you flip between constantly belongs
  next to show/hide; the key you press to go and adjust something belongs further out. `F5`, `F8`
  and `F9` have not moved.
- **Your settings file comes with it.** A file still carrying the old pair is swapped once, on the
  next launch, and stamped so it is never rearranged again — which means you can now deliberately
  bind the old arrangement back and keep it. A file where only one of the two had been rebound is
  somebody's own choice and is left alone.

---

## Before the first release

The versions below were published while RatNav was being worked out, and were removed when the
numbering restarted at 0.1.0. **All of this is in 0.1.0** — the sections are kept because they say
*why* things are the way they are, which is worth more than the version numbers they were filed
under.

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
  searchable catalog of 789 barters and 214 crafts: finding the one you meant needed you to
  already know which of Therapist's four Dorm 303 trades it was.

### The items list

- **Items are named in full.** "Elite" is elite cutters, "Access" is a TerraGroup Labs access
  keycard, "Chek. 15" is the Chekannaya 15 apartment key. The game prints those short names on a
  stash cell, where they are exactly right and where RatNav still reads them; in a list you are
  scanning for a name you have in mind, they are not.
- **One alphabetical list**, on the overlay and in the app. Found-in-raid items are no longer
  lifted into their own block — the color says which they are wherever they sit, and two alphabets
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
- **The steps in a quest modal are clickable**, so reading a neighboring step no longer means
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
- **Custom tracking can mark an item found-in-raid**, which colors its number on the overlay the
  same red the quest and hideout lists use. RatNav cannot know: a barter may demand it where a kit
  you are building for yourself does not.

- **The overlay draws Customs in its own colors again.** The **Graphical** level is meant to use
  the map's own palette, and it was drawing the flat wireframe one instead — the overlay was
  fetching a map the service had already restyled, so the palette it read back was RatNav's, not
  the map author's.
- **Money reads as money.** 400,000 roubles rather than 400000, anywhere an amount appears.
- **No "Built it" on an upgrade you cannot have built.** It appeared under *After one more upgrade*
  too, where taking it would record a level you do not have.

- **F8 reads the tooltip, not the cell.** Hovering a compass in a backpack reported a golden neck
  chain: six cells beside it were labeled "GoldChain" and its own cell was truncated to "Compa".
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
- **Hazard zones read as hazards.** The stylesheet reader took a map's colors and dropped their
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
  hold a map usefully. The centered map holds still by default — it is big enough to read as a map,
  and one that re-centers on every fix puts the same building somewhere new each time you look.
- **A halo behind every line.** This is what makes a translucent map readable over Tarkov, whose
  backgrounds run from snowfield to unlit basement — no single line color survives both, and
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

- The centered map view is now only the map. The title and the fix age were text over the game in
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
  tellable apart without relying on color.
- The map can **hold still** while your marker travels across it, or follow you and slide
  underneath. Still is the default: a map that re-centers on every fix puts the same building
  somewhere new each time you look.
- No line drawn between stops. It implied a route through walls that does not exist; the order is
  carried by numbered pins instead, and hovering one says which quest it is for.
- An items panel on the overlay — the watchlist first, then what quests and the hideout still
  want — which collapses, and can be torn off into its own window for a second monitor.

### Make it yours

- Bindable hotkeys — `F5` shows and hides the overlay, `F6` lets you drag, resize, and zoom it,
  `F7` opens the full panel, `F8` ticks the current stop off, `F9` switches presentation.
- Two presentations of the same state: a **box** in a corner for staying out of the way, or a
  **wireframe** map drawn large and translucent over the center, with terrain faded back so the
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
  75 meters out.
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
