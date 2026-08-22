# Using RatNav

Everything RatNav does, in the order you would meet it. If you have just installed it, start at
[Your first raid](#your-first-raid).

- [Your first raid](#your-first-raid)
- [Setup](#setup)
- [Planning a raid](#planning-a-raid)
- [The overlay](#the-overlay)
- [Reading the map](#reading-the-map)
- [Reading a quest from its waypoint](#reading-a-quest-from-its-waypoint)
- [Marking your own spots](#marking-your-own-spots)
- [Items, and why they are needed](#items-and-why-they-are-needed)
- [Your three characters](#your-three-characters)
- [Tracking something yourself](#tracking-something-yourself)
- [The hideout as a build order](#the-hideout-as-a-build-order)
- [Quests and traders](#quests-and-traders)
- [Identifying loot](#identifying-loot)
- [Sharing a plan with a friend](#sharing-a-plan-with-a-friend)
- [Where your data lives](#where-your-data-lives)

---

## Your first raid

Five minutes, once.

1. **Bind a screenshot key in Tarkov** — *Settings → Controls → Screenshot*. A mouse thumb button
   is ideal, because you can press it without letting go of movement. Steam users should avoid
   `F12`.
2. **Set the game to Borderless or Windowed** — *Settings → Graphics*. Exclusive fullscreen draws
   above every overlay in Windows, so nothing can appear over it.
3. **Start RatNav** and open its panel (double-click the tray icon, or visit
   `http://localhost:8722/` in any browser).
4. **Work down Setup** until the checks are green. It tells you what to do about each one.
5. **Mark your quests.** Go to **Quests → All**, search for the ones you have picked up in game,
   and set them **Active**. Nothing else in RatNav means much until it knows what you are working
   on — quest state is not written to disk in a form anything can read reliably, so you tell it
   once and it keeps up from the logs after that.
6. **Set your character level** in the top navigation, your **trader levels** on the Quests page,
   and your **station levels** on Hideout. All three change what RatNav offers you.
7. **Build a plan** on the Plan page and press **Plan this raid**.
8. **Queue up.** When the raid loads, the overlay appears with your map. Press your screenshot key
   and your marker lands.

---

## Setup

Open **Setup** in the panel. Every field there is either detected or asked for — nothing is
hardcoded, and RatNav says which is which.

| Setting | What it is for |
|---|---|
| **Escape from Tarkov folder** | The folder holding `EscapeFromTarkov.exe`. RatNav looks for it and shows what it found; **Browse…** opens a folder picker when it looked in the wrong place. |
| **Screenshot folder** | Where the game saves screenshots. Defaults to `Documents\Escape from Tarkov\Screenshots`. If OneDrive has moved your Documents folder, this is the usual reason RatNav sees nothing. |
| **Your in-game screenshot key** | Whatever you bound in Tarkov. RatNav **never presses it**; this is so every prompt names the key *you* use. |
| **Hotkeys** | Click a field and press the key you want. Defaults are `F5`–`F11`. Changes take effect at once, and RatNav says if another application already owns a combination. |
| **Game edition** | Sets your starting stash level. It never lowers a stash you have already raised. |
| **Your name on shared plans** | Only matters if you swap plans with someone. |
| **Reach RatNav from a phone or tablet** | Off by default. On, RatNav answers on your machine's network address as well as its own, so another device on the same wifi can open it in a browser — nothing installed there, and nothing reachable from outside your network. There is no password: anyone on your wifi can open it. Setup shows the address to type, and offers to open the Windows Firewall if it is in the way. |
| **Quit RatNav** | Stops the overlay, the hotkeys and the app — the whole thing, not just the browser tab. Closing the tab stops nothing, because the tab is not the application. Nothing you have recorded is lost. |
| **Port** | Which port RatNav listens on. Leave it alone unless something else on the machine already wants 8722 — and if something does, RatNav takes the next free one and says so rather than refusing to start. |

Setup re-checks itself every few seconds, so you can leave it open, launch the game, and watch the
checks go green.

---

## Planning a raid

**Plan** is the page you use before you queue.

1. **Pick a map.** Your picks are kept per map, so you can look at Woods halfway through building a
   Customs run and come back to find it as you left it.
2. **Tick the objectives you are pushing.** They are grouped by the place players actually call it
   — Depot, Dorms, Old Construction — with the quest and trader under each.
3. **Check what it wants carried.** The strip above the list says how many objectives you have
   picked and what they need you to bring, counted — `3× MS2000 Marker` rather than a bare name —
   with keys in red. This is the one thing you cannot fix once the raid starts.
4. **Plan this raid.** The plan goes to the overlay and stays there until you clear it — it
   survives closing the game, and closing RatNav. The page folds down to the plan itself: the
   button becomes **End raid**, and **+ Add a stop to this plan** at the foot reopens the list.

Stops run **in the order you ticked them**, and the number on each row is the number the overlay
draws on the map. Nothing re-orders them: RatNav does not work out a route, because the order you
chose already carries what a solver cannot know — which end is quiet, what you want done early,
which two are on the way to a third. Change your mind and you change the ticks.

A position fix in raid moves your marker and drops the stops you have marked done. It leaves the
rest in the order you gave them.

### Finishing things

- **Quest done** marks the whole quest complete. It retires the quest's item needs *and* its
  stops — including objectives you never planned, so the next plan does not send you back through
  them. It does not ask: it is one click to undo, and un-marking the quest puts everything back.
- **There is no tick on a stop.** Ticking one meant alt-tabbing out of a raid to do it, so nobody
  did and the plan stayed lit through raids it had nothing to do with. Reconciling afterwards, from
  the quest, is the move that actually gets made. Ticking belongs where you *choose* — the list you
  pick a plan's stops from.
- When every planned objective of a quest is done, a **turn-in prompt** appears saying which trader
  to hand it to — and warns you if the plan only covered part of the quest.

---

## The overlay

| Key | What it does |
|---|---|
| *your screenshot key* | Take a position fix |
| `F5` | Show or hide the overlay |
| `F6` | Interact mode — let the mouse reach it, to move, resize, zoom and open the settings |
| `F7` | Switch between the corner panel and the centered map |
| `F8` | Follow you, or hold the map still |
| `F9` | Put the map back on you, without starting to follow |
| `F10` | Read the game's extract list |
| `F11` | Say what the item under your cursor is for |

They run `F5` to `F11` in the order you use them: show it, arrange it, choose the view, then the
two that move the map, then the two that read the screen.

**Two presentations, remembered separately.** The corner panel is small and out of the way; `F7`
swaps it for the map itself, over the center of the screen. Position, size, zoom, pan and opacity
are kept per presentation, so setting up one does not disturb the other.

The centered view has a **Coverage** dial. Turn it to 100% and it becomes a full-screen HUD: the
map drawn as glowing outlines over the game, dissolving toward the edges rather than stopping at a
border, and **turned so what is in front of you is up the screen**. Below 100% it is a window in
the middle of the screen, which is what it has always been. Clicks pass straight through the HUD
everywhere except on a control, so it can be up while you play.

The corner panel never turns. It is a small still map you glance at to orient against buildings,
and its cone already says which way you are facing.

**RatNav says when it does not know where you are.** Load into a raid and the map draws before you
have taken a position, so it says so over the map and names your screenshot key. It goes when the
first position lands.

**Nothing animates.** The overlay is a still image between fixes. Your marker snaps when you take
one and at no other time, and the line at the bottom says how long ago that was — because a marker
that slid around pretending to know where you are is how an overlay gets someone killed.

**Three drawers.** The buttons at the bottom-left open the **waypoints** list (your plan's stops,
numbered as on the map — quests and your own marks together) and the **items list**. Either can be
moved to the other side, collapsed, or torn off into its own window for a second monitor. When they
share a side the waypoints sit on top, and the divider between them drags.

The waypoints panel opens by itself when a plan exists and closes when one does not, since a list
of stops with no plan behind it is a heading over an empty box. The items list opens however you
left it — it is the shopping list, and it is worth reading between raids.

**map** is the third, and it folds the map itself away — leaving a narrow strip of just the two
lists, for when you are standing still reading what you still need rather than navigating. Nothing
about the map is lost while it is folded: the zoom, the floor, the ink and the rest come back the
way you left them, and the overlay remembers a width for each state so neither has to be dragged
back into shape.

Every panel edge that meets the map is draggable, on both sides.

---

## Reading the map

Press `F6` to let the mouse reach the overlay, then the **gear** for the settings. They open in a
window of their own — there is no room inside a small overlay for a panel that configures it, and
out here the map stays fully visible while you turn a dial. Drag it by its heading, resize it from
any edge, and close it with the ✕ or the gear. It never opens by itself.

Everything is grouped: **sizes**, **the map**, **what to draw**, and **the window**. Every size
dial reads `1.0×` on a fresh install and moves either way from there, because the numbers RatNav
ships were measured on a real screen rather than guessed — a dial that starts at its own floor is
not a default, it is a limit.

| Control | What it does |
|---|---|
| **Floor** | Every floor is drawn stacked unless you say otherwise. The dropdown on the quick panel lists **Stacked** first, then the map's own levels; a position fix never changes it underneath you. |
| **Draw** | `Graphical` uses the map's own palette — the way its author drew it. `Full`, `Structure` and `Outline` progressively drop categories of detail rather than fading everything, which is what you want over a firefight. |
| **Ghost / names / halo** | Whether other floors show through, whether place names are drawn, and whether text gets a dark backing. |
| **Fade** | How strongly the map is drawn over the game. The controls stay solid whatever you set. |
| **Line** | Stroke weight of the map itself. |
| **Waypoint pins / Waypoint labels / Map labels / You** | Separate size dials for the markers, the captions on stops and extracts, the map's own place names, and your own marker. |
| **Edge arrows / Edge labels** | The arrows around the edge pointing at what is off the view, and their captions. Their own sizes: an edge arrow stands in for a place you cannot see, where a pin marks one you can. |
| **Shrink** | How much all of those ease off as you zoom out. At zero they stay the size you set; at one they scale with the map. |
| **Map** | `still` holds the map and lets your marker travel across it. `follows you` keeps you centered — and in the centered view it is also what lets the map turn, since the turn pivots on the middle of the view and that is only *you* while it follows. |
| **Exits** | `Both`, `PMC`, `Scav`, or `Off`. Shared extracts show under either faction. Beside it, **transits** are their own on/off — anybody can take a transit whatever they queued as, so whose extracts you want has nothing to say about them. |
| **Quests** | `Active` is your plan's stops. `All` adds every other started quest's objective on this map, drawn hollow and unnumbered. `Off` leaves the map clean. |
| **Coverage / Edge fade / Glow** | The centered view only. How much of the screen it takes, where the drawing starts dissolving, and how much the lines bloom. |

These are the same controls under the same names as the app's **Maps** page, so what you can turn
on and off reads the same in both places.

Right-drag pans and the wheel zooms — in the corner panel and in the windowed centered view. In the
full-screen HUD the map passes clicks through to the game, so zoom from the quick panel instead.

**In the corner panel**, anything off the visible area gets an arrow at the edge pointing at where
it really is, with the name abbreviated. The centered view does not draw them: it is large enough to
show the ground you are crossing already, and a ring of arrows around the edge sits exactly where
the drawing is meant to be fading out.

Hovering a pin, an extract or a mark names it — drawn into the map rather than as a tooltip,
because a tooltip belonging to a window that never takes focus opens and vanishes in the same
frame.

### Only the extracts you can actually use

A map has every extract it has ever had — seventeen on Streets — and a raid offers a handful.
Double-tap `O` in game to bring up the list, then press `F10`. RatNav reads the names off the
screen and draws only those. **all exits / my exits** on the quick panel switches between the two,
both ways, so a reading is never a one-way trip.

It is a keypress rather than something detected, because knowing you pressed `O` would mean
watching your keyboard, which RatNav will not do.

---

## Reading a quest from its waypoint

Click any waypoint — on the overlay or on the Maps page — and the quest opens: what it wants, every
step with the one this pin serves marked and the finished ones struck through, a link to the wiki,
and the **wiki's screenshots of the place**.

Those pictures are the point. A pin tells you where to walk; a picture of the door tells you which
of six identical buildings you are looking for. They load from the wiki and are credited there.

## Marking your own spots

Find something worth remembering — a spawn, a stash, a good angle — and put it on the map.

Choose **A place** or **An item** under *Mark*, click the spot, and name it. The control is on the
**Maps** page and on the **Plan** page's map, because "I want to go here as well" is part of
building a raid and should not need two navigations.

A place draws as a diamond and an item as a box, both **orange** — its own color, so a mark never
reads as a quest objective, and its own shape, because color alone fails for anyone who cannot
separate the hues and a navigation overlay is a bad place to learn that. It appears on the overlay
from then on.

**Name it and nothing else.** The chips under the map list every mark on it — that is where you
rename and delete them. A label has to be short because it is drawn over a game, and short turned
out to be all anybody wanted to say: "Car batteries" says where, which is the whole job.

Marks live per map and draw whenever that map is on screen, whether or not a plan is loaded — "car
batteries behind the garage" is true every raid.

They can also **join a plan**. On the Plan page they sit above the quest objectives under *Your
marks*, and tick and number in the same list, so a run can be "this quest step, then my stash,
then that one".

The Maps page has the same ink and floor controls the overlay has, and draws every quest
objective and extract for the map.

---

## Items, and why they are needed

**Items** answers "should I pick this up".

- **Needed** — what active quests and the hideout still want, minus what you have. The **Look
  ahead** dial decides how far past today to count: 0 is what you could do now, higher follows the
  hideout build order *and* the quest chain further out. A line under the tabs says which you are
  looking at.
- **Watchlist** — anything else worth collecting, with **your own** target and count. Kept apart
  from your stash number on purpose: twenty bundles of wires with fifteen promised to the hideout
  is not twenty available for something you are tracking.
- **Custom** — see below.

**Search is a box on the page**, not a tab. Type and the list becomes results; clear it and your
list is where you left it. Starring a result puts it on the watchlist, which is usually why you
looked it up.

Sorted by **nearest upgrade** by default — what stands between you and finishing something. *Most
needed* answers the other question: what to grab if you happen to see it.

**Group by type** files the rows under what the handbook calls them — Electronics, Building
materials, Mechanical keys — alphabetical inside each group, and alphabetical overall when
grouping is off.

The filter row narrows to what is for quests or for the hideout. Keys count as being for a quest,
because they are: a key is recorded as something to *bring* rather than hand in, which is why it
once needed a filter of its own. Each filter carries its count, so the row doubles as a summary
before you touch it.

Have-counts are typed by hand — your stash is not in any file on disk, and RatNav will not guess.
The point of the list is what is still *needed*, so the number you touch most is the one that goes
down as you find things.

---

## Your three characters

Escape from Tarkov gives you a PvE character, a PvP character, and a seasonal PvP one. They share
nothing — different quests accepted, different hideout, different trader loyalty, different level —
so RatNav keeps a separate set of everything for each.

The header says which one you are looking at — **RatNav — PvP Seasonal** — and the caret beside it
switches. That answer changes what every page shows, so it is said rather than hidden behind an
icon. A fresh install opens on the seasonal character, since a season is the current wipe; after
that RatNav opens on whichever you chose last, and remembers it across updates.

What is shared, because none of it belongs to a character: where the game is installed, your
hotkeys, your screenshot key, and the cached copy of the game's data.

**Starting one over** is in **Setup**, under *Start a character over*. It clears everything recorded
for that one character — quests, hideout, loyalty, level, what you are tracking, plans and marks —
and leaves the others alone. It asks you to type the character's name first, because it cannot be
undone.

---

## Tracking something yourself

Therapist will trade a Dorm 303 key for seven T-shaped plugs and three rolls of insulating tape.
On **Items → Custom**, press **Add tracking**, call it "Document case", search for those items and
say how many — and they join what you are looking for.

It can be anything you are setting items aside for: a barter, a craft, a hideout upgrade, a kit you
build for yourself, a promise to a friend. Nothing checks it against the game's own trades, because
RatNav has no business having an opinion about which.

**Each item counts down.** A `+` and a `−` on every row, and the number the list leads with is what
is *left*: found four of six and it asks for two. That count belongs to the list rather than to a
stash total, which is the point — four plugs set aside for the document case are not also available
for the workbench, and a single number cannot say both.

These stay **apart** from quests and the hideout. An item wanted three times for a quest and seven
for something you are tracking is two reasons, not a single ten — and only the split tells you that
finishing the quest leaves seven still to find.

The overlay shows one foldable section for each, so the one you are working on can stay open while
the others are out of the way.

---

## The hideout as a build order

Tell RatNav the level of each station and it works out what that makes reachable next, following
the game's own prerequisites.

**Look ahead** decides how far past today it counts: 0 is what you could build tonight, 2 is what
to stop vendoring. On a real hideout that is about 17 items rather than the several hundred an
unfiltered list gives you.

It is one setting with three dials on it — the hideout page, the items page and the overlay all
read and write the same number, so turning it anywhere turns it everywhere.

---

## Quests and traders

**Quests** has three tabs — **Active**, **Complete**, and **All** — plus a search that reaches
every quest in the game.

### Setting up, without the mouse

The first thing a new install asks of you is marking every quest you have accepted as active, read
off the game. That is fifty or more, and doing it by hand is fifty round trips between keyboard and
mouse.

So the search box does the whole job. Switch to **All**, then:

| | |
|---|---|
| type | narrows the list — part of a name is enough |
| **Enter** | marks the highlighted quest active, and **empties the box** |
| **↑** **↓** | move the highlight without the caret leaving the text |
| **Esc** | clears the box |

The box emptying itself is the part that matters: type, Enter, type, Enter, without ever looking
down. What just happened is said beside the search, because the row usually scrolls out of view as
it changes and a change you cannot see is one you reach for the mouse to check.

A quest that is already active says so rather than doing nothing quietly.

Every quest carries all four states: not started, active, complete, failed. Failed is a finished
state like complete, but it is tagged and counted separately, because a failed quest that reads as
done is one you never go back and look at.

Rows say what is standing in the way in words — *needs level 20*, *needs Debut* — rather than a
padlock that tells you nothing you can act on.

**Traders** are listed with their loyalty levels, which you set by hand. Loyalty depends on
reputation, level and spend; none of that is on disk, and the endpoint that knows it needs your
account password, which RatNav will never ask for.

**Photos** on any quest row opens the screenshots from that quest's wiki article — the ones showing
which building and which door. They are loaded from the wiki and credited to it.

---

## Identifying loot

Hover an item in game so its tooltip is showing and press `F11`.

RatNav reads the tooltip **off the screen**, using the OCR built into Windows, and answers the
question you are actually asking: **Keep**, **Keep — found in raid**, **Not now**, or **Leave it**,
followed by the reasons that are things you are working on. Everything else — quests you have not
started, things you have not started tracking — gets one counted line rather than a recital.

It is a key rather than shift-click because catching a mouse click over another application needs a
system-wide mouse hook, which is the same machinery RatNav refuses to use for the keyboard.

OCR misreads. RatNav says how sure it is rather than presenting a guess as fact, and offers the
runners-up.

---

## Sharing a plan with a friend

**Share plan** on the Plan page gives you a code — a line of text, no file, no server. Send it
however you already talk.

They paste it into **Import a plan** and it merges with their own plan for that map:

- **Nothing is dropped.** Every objective survives and carries its owner.
- **Your stops keep your order**, and theirs follow in the order they sent them.
- It flags what actually changes the raid: **objectives to do together**, **items you are both
  hunting** (the map only spawns so many), and **keys only one of you needs to carry**.

Your own plan is untouched, so an updated code from them is a re-merge rather than a rebuild.

---

## Updates

**Setup → Updates** says which version you are running and whether there is a newer stable one.
RatNav asks GitHub once a day.

- **It tells, it does not do.** There is a link, and following it is your decision. Downloading and
  running an installer on your behalf is a great deal more trust than a map overlay needs.
- **Prereleases do not count.** Alphas are published deliberately; you are told about stable
  releases only. If you *are* running an alpha, you are told when the stable it became ships.
- **Turn it off** with the checkbox, and **Check now** still works — a switch that disabled the
  button too would mean "never tell me" rather than "do not go looking".
- **Failure is silence.** GitHub being unreachable is not worth interrupting a raid over.

Updating is the ordinary thing: download the installer, quit RatNav, run it over the top. Your
plans, progress and settings live outside the install and are left alone.

---

## Where your data lives

Everything RatNav knows about you is in `%LOCALAPPDATA%\RatNav`:

| File | What it holds |
|---|---|
| `settings.json` | Paths, hotkeys, and every overlay preference |
| `progress.json` | Quest states, hideout levels, trader levels |
| `tracking.json` | Have-counts, watchlist, the goals you are collecting for |
| `waypoints.json` | Spots you marked |
| `plans/` | Saved plans |
| `gamedata-*.json` | Cached quest and item data from tarkov.dev |
| `maps/`, `wiki/` | Cached map drawings and wiki image lists |

Copy the first five to move to another machine. Delete the last three and they are re-fetched.

Uninstalling deletes the caches and **leaves everything else alone** — reinstalling after a patch
should not cost you what you tracked. To start clean, delete `%LOCALAPPDATA%\RatNav` yourself.
