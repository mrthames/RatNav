# How RatNav works, and why it is built this way

The first question anyone asks about a tool that runs alongside Escape from Tarkov is whether it is
safe. This is the long answer. It is worth reading once, and everything in it is checkable in the
source.

## What RatNav reads

Two things, both already on your own disk, both written there by the game itself:

- **Log files.** `<EFT install>\Logs\log_<date>_<version>\*application.log`. These give the map you
  loaded into, when a raid starts and ends, and quest events. RatNav opens them read-only, tolerates
  the game holding them open, and never writes to them.
- **Screenshot filenames.** When you take a screenshot, Escape from Tarkov puts your world
  coordinates and camera rotation into the name of the file it saves. RatNav reads the name. That is
  where your position on the map comes from — the name, not the picture.

Over the network it fetches quest and item data from [tarkov.dev](https://tarkov.dev), map drawings
from the community projects that publish them, and — only when you ask a quest for its photos —
image links from the Escape from Tarkov Wiki.

That is the complete list.

## What RatNav never does

- **It does not read or write the game's memory.** Not once, not for anything.
- **It does not inject code** into the game process, load a library into it, or attach a debugger.
- **It does not hook DirectX, Direct3D, or the game's rendering.** The overlay is an ordinary
  top-level Windows window, composited by the desktop the same way every other window on your screen
  is. Nothing of RatNav's runs inside the game.
- **It does not modify any game file.**
- **It does not send input to the game.** No synthetic keystrokes, no synthetic clicks, nothing that
  presses anything for you. Your screenshot key is pressed by you.
- **It does not watch your keyboard or mouse.** Its hotkeys are registered with Windows the ordinary
  way, using `RegisterHotKey` — the same mechanism any application uses to claim a shortcut. There is
  no low-level keyboard hook and no mouse hook anywhere in it.
- **It has no telemetry, no accounts, and nothing that phones home.** Its own service listens on
  `127.0.0.1` and nothing outside your machine can reach it.

Two design decisions follow from that list and are worth calling out, because both cost something:

**Your position updates only when you press your screenshot key.** Continuous position would mean
reading the game's memory, which is exactly what anti-cheat exists to catch. So RatNav does not have
it, and does not pretend to — the marker snaps when you take a fix and at no other time, and the
overlay says how long ago that was.

**Identifying an item under your cursor is a keypress, not a shift-click.** Catching a mouse click
over another application needs a system-wide mouse hook, which is the same machinery RatNav refuses
to use for the keyboard. A hotkey is registered with Windows and touches nothing.

## Why this is the established way to do it

Everything above amounts to one rule: **RatNav treats Escape from Tarkov as something to read files
from, never as something to reach into.**

That is the line anti-cheat is drawn around. BattlEye exists to detect software that attaches to,
inspects, or alters a protected process — memory reads, injected code, hooked rendering, synthetic
input. RatNav does none of those things, and the reason it can still be useful is that the game
already writes what it needs onto your disk, in plain files, for its own reasons.

It is also the mechanism every safe tool in this space uses, including the paid ones. Reading the
game's logs is what [TarkovMonitor](https://github.com/the-hideout/TarkovMonitor) does. Reading
position out of a screenshot filename is how every "where am I" overlay works, because it is the
only place the game puts that number.

Nothing here is clever or novel. It is deliberately the least invasive thing that could work.

## What nobody can promise you

Battlestate Games make the rules for their own game, and they can change them whenever they like.
Nobody outside that company can tell you what they will decide in future about any category of tool,
and anyone who says otherwise is guessing.

So: **RatNav is used at your own risk, and its author is not liable for anything that follows from
using it, including action taken against a game account.** That is in the [licence](../LICENSE) too.

What can be said is what RatNav actually does, which is above, and that all of it is in the source
in front of you. If you want to check any single claim on this page, the code is there —
`ScreenshotWatcher`, `LogWatcher`, and `GlobalHotKey` are the three files where all of it happens.

## If you are contributing

A change that crosses the line above will not be accepted, however useful the feature. That is
stated in [CONTRIBUTING.md](../CONTRIBUTING.md) as well. The value of this project rests entirely on
being safe to run, and that is not tradeable for a feature.
