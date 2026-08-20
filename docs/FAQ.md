# Questions, and things that go wrong

- [Is this safe? Will I get banned?](#is-this-safe-will-i-get-banned)
- [What does it actually read?](#what-does-it-actually-read)
- [Does it cost anything? Is there an account?](#does-it-cost-anything-is-there-an-account)
- [Why do I have to press a key to see where I am?](#why-do-i-have-to-press-a-key-to-see-where-i-am)
- [Nothing appears over the game](#nothing-appears-over-the-game)
- [The overlay is there but never notices a raid](#the-overlay-is-there-but-never-notices-a-raid)
- [I press my screenshot key and nothing happens](#i-press-my-screenshot-key-and-nothing-happens)
- [A map I play is not in the list](#a-map-i-play-is-not-in-the-list)
- [My marker is in the wrong place](#my-marker-is-in-the-wrong-place)
- [Everything is tiny on my 4K screen](#everything-is-tiny-on-my-4k-screen)
- [I pressed the interact key and there are no map controls](#i-pressed-the-interact-key-and-there-are-no-map-controls)
- [RatNav says it is on a different port](#ratnav-says-it-is-on-a-different-port)
- [A hotkey does nothing](#a-hotkey-does-nothing)
- [The items list is enormous](#the-items-list-is-enormous)
- [Why do I have to type my quests in?](#why-do-i-have-to-type-my-quests-in)
- [Why can't it read my stash?](#why-cant-it-read-my-stash)
- [Why can't it read my trader loyalty or my level?](#why-cant-it-read-my-trader-loyalty-or-my-level)
- [`F8` says it cannot read the screen](#f8-says-it-cannot-read-the-screen)
- [The data looks out of date](#the-data-looks-out-of-date)
- [Does it work on Steam? On Linux? On a second PC?](#does-it-work-on-steam-on-linux-on-a-second-pc)
- [Can my friend and I see each other on the map?](#can-my-friend-and-i-see-each-other-on-the-map)
- [Can I see it on my phone or tablet?](#can-i-see-it-on-my-phone-or-tablet)
- [I switched character and everything is empty](#i-switched-character-and-everything-is-empty)
- [How do I move to a new PC?](#how-do-i-move-to-a-new-pc)
- [How do I uninstall it completely?](#how-do-i-uninstall-it-completely)
- [Something is broken. Where do I report it?](#something-is-broken-where-do-i-report-it)

---

## Is this safe? Will I get banned?

RatNav does not touch the game. It reads two things Escape from Tarkov already writes to your own
disk, and draws an ordinary window on top.

It does **not** read or write game memory, inject code, hook DirectX or the game's rendering,
modify any game file, or send synthetic keyboard or mouse input to the game. It registers its
hotkeys with Windows the ordinary way rather than watching your keyboard, and it will not catch
mouse clicks over other applications, which is why identifying an item is a keypress rather than a
shift-click.

Nobody can promise you what a third party will do about anything — Battlestate Games set the rules
for their own game and can change them whenever they like. What can be told to you is exactly what
RatNav does, and every one of those claims is checkable in the source.

**RatNav is used at your own risk, and its author is not liable for what follows** — including
action taken against a game account. That is in the [licence](../LICENSE) too.

The full account, including why this is the established way to build a tool like this, is in
[how it works](SAFETY.md).

## What does it actually read?

Two things, both already on your disk, both written by the game:

- **Log files** — `<EFT install>\Logs\log_<date>_<version>\*application.log`. These give the map
  you loaded into, raid start and end, and quest events.
- **Screenshot filenames** — the game encodes your world coordinates and camera rotation into the
  name of every screenshot it saves. That is where your position comes from.

Plus, over the network: quest and item data from [tarkov.dev](https://tarkov.dev), map drawings
from the community projects that host them, and — only when you ask for a quest's photos — image
links from the Escape from Tarkov Wiki.

## Does it cost anything? Is there an account?

No, and no. There is no telemetry, no account, no server of ours, and nothing phones home. The
service it runs binds to `127.0.0.1` — your own machine — and nothing else can reach it unless you
turn on [network access](#can-i-see-it-on-my-phone-or-tablet), which is off until you do and reaches
your local network only.

## Why do I have to press a key to see where I am?

Because the only position Tarkov voluntarily writes to disk is in the filename of a screenshot.

Continuous position would mean reading the game's memory, which is precisely what anti-cheat exists
to catch. Every tool in this space that is safe to use works the same way — including the paid
ones.

So the design leans into it: one thumb button, and every press does as much as possible. Your
marker snaps, the remaining route re-orders from where you actually are, and — in the centred view
— the map turns to face the way you were looking. Between presses nothing moves, and the overlay
says how old the reading is rather than pretending.

## Nothing appears over the game

Almost always one of these:

1. **The game is in exclusive fullscreen.** Switch it to **Borderless** or **Windowed** in
   *Settings → Graphics*. Exclusive fullscreen draws above every overlay in Windows; no tool can
   work around it.
2. **The overlay is hidden.** Press `F5`.
3. **The overlay is off-screen**, usually after a monitor change. Open Setup and press **Put the
   overlay back**.

## The overlay is there but never notices a raid

RatNav is almost certainly watching the wrong game folder. Old installs on other drives look
identical to live ones, and it will happily read year-old logs forever.

Open **Setup** and check the **Escape from Tarkov folder**. It shows what it found and, when it
found several, why it picked that one — the install that wrote a log most recently. Point it at the
folder holding `EscapeFromTarkov.exe` if it chose wrong.

If the folder is right and it still sees nothing: the launcher's **Clear Cache** deletes log files,
so there may genuinely be nothing to read until your next raid.

## I press my screenshot key and nothing happens

- **Is the key bound in Tarkov?** *Settings → Controls → Screenshot*. RatNav never presses it; the
  game has to be the one writing the file.
- **Is the screenshot folder right?** Setup shows the one it is watching. The usual culprit is
  OneDrive having moved your `Documents` folder somewhere else.
- **Steam users:** `F12` is Steam's own screenshot key and will not reach the game. Bind something
  else.
- **Check the folder itself.** If a `.png` with coordinates in the name appears there when you
  press the key, the game is doing its part and the problem is on RatNav's side.

## A map I play is not in the list

RatNav offers the maps it can put a marker on and be right about. Two things keep one off the list.

**Its layout is not settled.** The drawing exists, but which way round it goes cannot be worked out
from published data — on some maps every extract sits inside the border, so a mirrored layout looks
exactly as valid as the real one. Telling the mirror from the truth takes somebody who stood
somewhere and can point at it, which is work done here rather than by you. Those maps are listed
under **Coming soon** on the **Maps** page, and they arrive finished.

**No drawing exists.** The Lab, The Labyrinth and Icebreaker have no community map with coordinates
— what is published for them is a flat picture, which cannot place a marker anywhere. RatNav draws
its maps from [tarkov.dev](https://tarkov.dev), and nothing in RatNav can conjure one that is not
there.

That is why those maps are not listed as "coming soon" anywhere in the app. Calling them coming
soon would be a promise resting entirely on somebody else drawing something, and a promise that
cannot be kept is worse than an honest gap. If a drawing appears, the map appears with it.

## My marker is in the wrong place

A map marked `[WIP]` is still being worked on, and that is where this is most likely. Otherwise it
is a bug worth reporting: the map name and the screenshot filename are enough, because the
coordinates are in the name.

The screenshot filename is the whole of what is needed to reproduce it, so a report naming the map
and the file is enough to get it fixed properly rather than worked around.

## Everything is tiny on my 4K screen

Press `F7`, then turn **Size** up in the row along the top of the overlay. RatNav's defaults are
drawn for 1080p; a 4K screen has four times the pixels in the same physical space, so everything
lands at a quarter of the area until you say otherwise.

The map's own markers and captions have their own dials behind the gear — **Pins** for the
markers, **Waypoints** for the captions on stops, extracts and marks, **Map labels** for the place
names the map itself carries, and **You** for your own marker.

Waypoints and map labels are separate because they do opposite jobs: the place names are the
backdrop you read to know which end of the map you are on, and a waypoint's caption is the thing
you are walking to.

## I pressed the interact key and there are no map controls

That is deliberate. `F7` hands the overlay your mouse and shows the **handles** — the grab bar, the
drawer buttons, the gear. The map settings live behind the **gear**, because a stack of settings
over the map every time you reach for the mouse is not what you reached for the mouse to do.

Press the gear once and it stays open until you fold it away again.

## RatNav says it is on a different port

Something else on your machine had the one it wanted, so it took the next free one rather than
refusing to start. The tray balloon names it. Everything RatNav opens itself already follows —
what does not is a bookmark you saved, or the address you typed on a phone.

**Setup → Port** pins a specific one if you would rather choose.

## A hotkey does nothing

Another application already owns that combination. Windows gives a hotkey to whoever asks first,
and RatNav tells you when it loses — check Setup, which lists the ones it could not get.

Rebind either that application or RatNav. Changes take effect immediately.

## The items list is enormous

Turn the **Look ahead** dial down. At 1 it shows what you could finish today; higher, it counts
further along the hideout build order and the quest chain, which is a much longer list. The line
under the tabs says which you are looking at.

The filter row (found in raid / for quests / for the hideout / for a trade / keys) is the other
half of the answer.

## Why do I have to type my quests in?

Because the game does not write your quest list anywhere readable. It writes *events* — accepted,
completed, failed — so RatNav can keep up once it knows where you started, but it cannot see the
board you already have.

So you set them once. After that, anything the log reports is applied automatically, and any
correction you make by hand **wins permanently** — a later log replay can never undo it.

## Why can't it read my stash?

Stash contents appear in no log file. The only interface that reports them is an unofficial
endpoint that needs your live account credentials, violates the game's terms, and puts your account
at real risk. RatNav will not ask you for those and will not use it.

So have-counts are typed, and the app is built around that being true — the watchlist keeps its own
count separately, so what is promised to the hideout is not silently counted as available for a
goal.

`F8` over an item is the fast way to check one thing without going near the number.

RatNav did once read a container off a screenshot. It worked — the game prints each item's short
name on its cell, so it was reading a printed label rather than guessing at a picture — but it
existed to fill in have-counts, and have-counts are not what the list is for. What matters is what
is still needed, so the feature was taken out rather than kept for its own sake.

## Why can't it read my trader loyalty or my level?

Same reason. Loyalty depends on reputation, level and spend; none of it is on disk. RatNav asks you
for it because guessing would mean offering you quests the game will not give you — 109 quests gate
on loyalty, and ignoring that made "ready" a lie.

For your level it does offer a suggestion: a quest that needs level 15 cannot have been finished
below it, so the quests you have marked complete imply a floor.

## `F8` says it cannot read the screen

Windows has no OCR language pack installed. It ships with Windows 10 version 2004 and later; on
older builds, or a stripped install, the component is missing. Everything else in RatNav works
without it.

## The data looks out of date

Press **Refresh** in the panel. RatNav refreshes on launch, on a timer, and immediately when it
notices the game has patched — it reads the version out of the log folder name.

When tarkov.dev is unreachable it keeps serving the last good data and says on screen that it is
stale, rather than leaving you with nothing mid-session.

## Does it work on Steam? On Linux? On a second PC?

- **Steam:** Tarkov is not on Steam, but if you run it through Steam's overlay for some other
  reason, avoid `F12` for your screenshot key.
- **Linux:** No. It is a Windows desktop application and uses Windows' own OCR and windowing.
- **A second PC:** Not usefully — it has to read the log and screenshot folders of the machine
  running the game. But the panel is a web page, so a second *screen* works fine (see below).

## Can my friend and I see each other on the map?

No, and that is on purpose. Live position sharing needs a server and NAT traversal, which is a
different kind of project and a different kind of trust.

Plan sharing gets you most of the benefit with none of that: swap codes before you queue and you
each get a merged plan showing whose objective is whose, what you are both hunting, and which keys
only one of you needs to carry.

## Can I see it on my phone or tablet?

Yes. **Setup → Reach RatNav from a phone or tablet**, then type the address it shows into a browser
on the other device.

Nothing is installed on the phone — it is a browser pointed at your PC. A plan you build there
reaches the overlay in game immediately, because both are looking at the same RatNav.

Three things worth knowing:

- **It is off until you turn it on.** RatNav listens on `127.0.0.1` — your own machine — by
  default, and opening it to the network is a deliberate change.
- **Nothing outside your network can reach it.** Port forwarding is a router-to-internet thing and
  is not part of this. Without it, your router is still the wall, whatever RatNav is doing.
- **There is no password.** Anyone already on your wifi can open RatNav and change its settings.
  On a home network that is you and your own devices; on a shared or building-wide network, think
  about whether that is what you want.

Windows Firewall will usually block the port until you allow it. Setup notices, offers to add the
rule — which needs a permission prompt, because opening a port always does — and prints the command
if you would rather run it yourself.

For a second monitor on the same machine none of this is needed: `http://localhost:8722/` in any
browser is the whole answer.

## I switched character and everything is empty

That is what switching to a character RatNav has not seen before looks like — each one keeps its
own quests, hideout, loyalty and level, so a fresh one starts fresh. Switch back from the menu at
the right of the top navigation and your progress is where you left it.

If you upgraded from a version before characters existed, everything you had was adopted into
**PvP**. If what you were actually playing was PvE or seasonal, switch to that one and set it up
there — the old profile can be cleared from **Setup → Start a character over** once you have.

## How do I move to a new PC?

Copy `%LOCALAPPDATA%\RatNav\settings.json`, `progress.json`, `tracking.json`, `waypoints.json` and
the `plans` folder. Everything else is cache and re-fetches itself.

## How do I uninstall it completely?

Uninstall from *Settings → Apps* as usual. That removes the program and its caches but
**deliberately leaves your progress, counts and plans** — reinstalling after a patch should not
cost you what you tracked.

To remove those too, delete `%LOCALAPPDATA%\RatNav`.

## Something is broken. Where do I report it?

[Open an issue](https://github.com/mrthames/RatNav/issues). The most useful things to include:

- What you expected and what happened.
- Whether the checks in **Setup** are green.
- For a wrong marker: the map, and ideally the screenshot filename that produced it.
- For a crash: `%LOCALAPPDATA%\RatNav\ratnav-error.log`.
