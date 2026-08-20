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

Nobody can promise you what a third party will do about anything. What we can tell you is exactly
what it does, and the answer is above; every one of those claims is something you can check in the
source.

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
service it runs binds to `127.0.0.1` — your own machine — and nothing else can reach it.

## Why do I have to press a key to see where I am?

Because the only position Tarkov voluntarily writes to disk is in the filename of a screenshot.

Continuous position would mean reading the game's memory, which is precisely what anti-cheat exists
to catch. Every tool in this space that is safe to use works the same way — including the paid
ones.

So the design leans into it: one thumb button, and every press does as much as possible. Your
marker snaps, the remaining route re-orders from where you actually are, and the bearing and
distance to your next stop update. Between presses nothing moves, and the overlay says how old the
reading is rather than pretending.

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
  press the key, the game is doing its part and the problem is on RatNav's side — please open an
  issue with the filename.

## A map I play is not in the list

RatNav offers the maps it can put a marker on and be right about. Two things keep one off the list,
and the **Maps** page shows which applies:

- **Its layout is not settled.** The drawing exists, but which way round it goes cannot be worked
  out from published data — on some maps every extract sits inside the border, so a mirrored layout
  looks exactly as valid as the real one. **You can settle this in thirty seconds:** take a
  screenshot in game somewhere you can recognise, open **Maps → Settle it**, and click that spot.
  The map joins the list immediately, and stays settled.
- **No drawing exists.** The Lab, The Labyrinth and Icebreaker have no community map yet. Nothing
  in RatNav can fix that.

If you settle one, please open an issue with the map and the position you used — it ships for
everyone in the next release.

## My marker is in the wrong place

That should not happen on a map RatNav offers; every one of them is calibrated. Please open an
issue with the map name and, if you can, the screenshot filename that produced the wrong marker —
the coordinates are in the name and that is enough to reproduce it exactly.

If you settled the map yourself from a position, **Maps → Settle it** can be redone: take a fresh
screenshot somewhere nearer an edge of the map and click again.

## Everything is tiny on my 4K screen

Press `F6`, then turn **Size** up in the row along the top of the overlay. RatNav's defaults are
drawn for 1080p; a 4K screen has four times the pixels in the same physical space, so everything
lands at a quarter of the area until you say otherwise.

The map's own markers and captions have their own dials — **Pins**, **Text** and **You** — in the
control stack.

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

By default the service listens on `127.0.0.1` only, so nothing outside the machine can reach it.
Opening it to your network is a deliberate change, not the default.

For a second monitor on the same machine, `http://localhost:8722/` in any browser is the whole
answer — the panel is built for a tall narrow window.

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
