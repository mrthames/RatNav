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
| **Hotkeys** | Click a field and press the key you want. Defaults are `F5`–`F9`. Changes take effect at once, and RatNav says if another application already owns a combination. |
| **Game edition** | Sets your starting stash level. It never lowers a stash you have already raised. |
| **Your name on shared plans** | Only matters if you swap plans with someone. |

Setup re-checks itself every few seconds, so you can leave it open, launch the game, and watch the
checks go green.

---

## Planning a raid

**Plan** is the page you use before you queue.

1. **Pick a map.** Your picks are kept per map, so you can look at Woods halfway through building a
   Customs run and come back to find it as you left it.
2. **Tick the objectives you are pushing.** They are grouped by the place players actually call it
   — Depot, Dorms, Old Construction — with the quest and trader under each.
3. **Watch the panel on the right.** Stops run **in the order you ticked them**. Drag a row to
   change it, or use the arrows. The number on each row is the number the overlay draws on the map.
4. **Check the keys.** Any key an objective needs is named, red when you do not have one, with a
   tick to say you do. This is the one thing you cannot fix once the raid starts.
5. **Plan this raid.** The plan goes to the overlay and stays there until you clear it — it
   survives closing the game, and closing RatNav.

Once you are in raid, the stops **you have not reached** re-order around wherever you actually are,
the first time you take a position fix. Ones you have ticked off stay put.

### Finishing things

- **The tick on a stop** marks that objective done. It strikes through and drops to the bottom.
- **Quest done** marks the whole quest complete, which retires its item needs. It asks first,
  because that is not a thing you want to do by accident.
- When every planned objective of a quest is done, a **turn-in prompt** appears saying which trader
  to hand it to — and warns you if the plan only covered part of the quest.

---

## The overlay

| Key | What it does |
|---|---|
| *your screenshot key* | Take a position fix |
| `F5` | Show or hide the overlay |
| `F6` | Let the mouse reach it — move, resize, and use the map controls |
| `F7` | Switch between the corner box and the centred map |
| `F8` | Say what the item under your cursor is for |
| `F9` | Read the game's extract list |

**Two presentations, remembered separately.** `F5` is a small panel in a corner, out of the way.
`F7` is the map itself, large and translucent over the centre. Position, size, zoom, pan and
opacity are kept per presentation, so setting up one does not disturb the other.

**Nothing animates.** The overlay is a still image between fixes. Your marker snaps when you take
one and at no other time, and the line at the bottom says how long ago that was — because a marker
that slid around pretending to know where you are is how an overlay gets someone killed.

**Two drawers.** The buttons at the bottom-left open the **quest log** (your plan's quests,
numbered as on the map) and the **items list**. Either can be moved to the other side, collapsed,
or torn off into its own window for a second monitor. When they share a side the quest log sits on
top.

---

## Reading the map

Press `F6` and a control stack appears down the side.

| Control | What it does |
|---|---|
| **Floor** | Steps through the map's levels. A position fix picks the floor from the height you are at; choosing one by hand lasts until your next fix, and the name turns amber while you are looking at a level you are not standing on. Other floors draw solid where nothing on yours sits above them, dashed and dim only where they genuinely stack. |
| **Draw** | `graphical` uses the map's own palette — the way its author drew it. `full`, `structure` and `outline` progressively drop categories of detail rather than fading everything, which is what you want over a firefight. |
| **Ghost / names / halo** | Whether other floors show through, whether place names are drawn, and whether text gets a dark backing. |
| **Fade** | How strongly the whole overlay is drawn over the game. |
| **Line** | Stroke weight of the map itself. |
| **Pins / Text / You** | Separate size dials for markers, captions, and your own marker. |
| **Shrink** | How much all of those ease off as you zoom out. At zero they stay the size you set; at one they scale with the map. |
| **Map** | `still` holds the map and lets your marker travel across it. `follows you` keeps you centred. A recentre button appears once you have dragged away. |
| **Exits** | `pmc`, `scav`, `both`, or `off`. Shared extracts show under either faction. |

Right-drag pans. The wheel zooms. Anything off the visible area gets an arrow at the edge pointing
at where it really is, with the name abbreviated.

Hovering a pin, an extract or a mark names it — drawn into the map rather than as a tooltip,
because a tooltip belonging to a window that never takes focus opens and vanishes in the same
frame.

### Only the extracts you can actually use

A map has every extract it has ever had — seventeen on Streets — and a raid offers a handful.
Double-tap `O` in game to bring up the list, then press `F9`. RatNav reads the names off the
screen and draws only those. A `showing N · all` button puts everything back.

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

On the **Maps** page: choose **A place** or **An item** under *Mark*, click the spot, and name it.
A place draws as a diamond and an item as a box, both purple so neither reads as a quest. It appears on the
overlay from then on, in purple and diamond-shaped so it never reads as a quest objective.

Marks live per map and draw whenever that map is on screen, whether or not a plan is loaded — "car
batteries behind the garage" is true every raid.

They can also **join a plan**. On the Plan page they sit above the quest objectives under *Your
marks*, and tick, number and reorder in the same list, so a run can be "this quest step, then my
stash, then that one".

The Maps page also has **search** — type a place name and it takes you there — the same ink and
floor controls the overlay has, and every quest objective and extract for the map.

---

## Items, and why they are needed

**Items** answers "should I pick this up".

- **Needed** — what active quests and the hideout still want, minus what you have. The **Look
  ahead** dial decides how far out to count: 1 is what you could do today, higher follows the
  hideout build order *and* the quest chain further out. A line under the tabs says which you are
  looking at.
- **Watchlist** — anything else worth collecting, with **your own** target and count. Kept apart
  from your stash number on purpose: twenty bundles of wires with fifteen promised to the hideout
  is not twenty available for something you are tracking.
- **Custom** — see below.
- **Search** — every item in the game, with why it is wanted.

The filter row shows only what is found-in-raid, or for quests, or for the hideout, or for a trade,
or keys. Each filter carries its count, so the row doubles as a summary before you touch it.

Have-counts are typed by hand — your stash is not in any file on disk, and RatNav will not guess.
The point of the list is what is still *needed*, so the number you touch most is the one that goes
down as you find things.

---

## Your three characters

Escape from Tarkov gives you a PvE character, a PvP character, and a seasonal PvP one. They share
nothing — different quests accepted, different hideout, different trader loyalty, different level —
so RatNav keeps a separate set of everything for each.

Switch between them from the **menu at the right of the top navigation**. RatNav opens on whichever
you chose last.

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

**Look ahead** decides how far it counts: 1 is what you could build tonight, 3 is what to stop
vendoring. On a real hideout that is about 17 items rather than the several hundred an unfiltered
list gives you.

Star the upgrades you actually want and the list narrows to those.

---

## Quests and traders

**Quests** has three tabs — **Active**, **Complete**, and **All** — plus a search that reaches
every quest in the game.

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

Hover an item in game so its tooltip is showing and press `F8`.

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
