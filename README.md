# RatNav

A raid planner and navigation overlay for Escape from Tarkov.

Plan your raid before you queue — pick the map, check off the quest objectives you're pushing, and RatNav
builds a route with the keys you need to bring and the items you're hunting. In raid, a hotkey-toggled
overlay shows that route on the map, tells you the bearing and distance to your next stop, and re-plans from
wherever you actually are.

**Status: in development. Not yet usable.**

## Is this safe to use?

Yes, and here is exactly why.

RatNav reads **two things the game already writes to your own disk**:

- **Log files** (`<EFT install>\Logs\log_<date>_<version>\*application.log`) — for the map you loaded into,
  raid start and end, and quest accept/complete events.
- **Screenshot filenames** (`Documents\Escape from Tarkov\Screenshots`) — Escape from Tarkov encodes your
  world coordinates and camera rotation into the name of every screenshot it saves. That is where your
  position on the map comes from.

RatNav does **not**:

- read or write game memory
- inject code into the game process
- hook DirectX, Direct3D, or the game's rendering
- modify any game file
- send synthetic keyboard or mouse input to the game
- collect telemetry, require an account, or phone home

The overlay is an ordinary top-level Windows window composited by the desktop compositor, exactly like any
other application window. Nothing attaches to the game.

The only network requests RatNav makes are to [tarkov.dev](https://tarkov.dev) for game data and to
community map image hosts.

## How position works

Escape from Tarkov writes your coordinates into the filename of every screenshot you take. So your
screenshot key **is** RatNav's "where am I" key:

1. Bind a screenshot key in **Tarkov → Settings → Controls → Screenshot** (a mouse thumb button works well;
   Steam users should avoid F12).
2. Tap it in raid.
3. RatNav parses the filename, snaps your marker to that spot with your facing, re-plans the remaining route
   from where you now stand, and tells you the bearing and distance to your next objective.

Position updates when you tap, and only when you tap. There is no continuous tracking, because reading
position continuously would require reading game memory — which is what anti-cheat exists to catch. Every
ban-safe tool in this space works the same way.

## Requirements

- Windows 10/11
- Escape from Tarkov running in **Borderless** or **Windowed** mode. Exclusive fullscreen renders above all
  overlays; that is an operating system limitation, not something any tool can work around.

## Credits

RatNav is built on data generously maintained by the community:

- [tarkov.dev](https://tarkov.dev) and [the-hideout/tarkov-api](https://github.com/the-hideout/tarkov-api) —
  quests, items, hideout requirements, prices
- [TarkovTracker/tarkovdata](https://github.com/TarkovTracker/tarkovdata) — map coordinate bounds
- The map makers whose work those projects distribute
- [the-hideout/TarkovMonitor](https://github.com/the-hideout/TarkovMonitor) — prior art for reading game
  logs safely

Escape from Tarkov is a trademark of Battlestate Games. RatNav is an unofficial fan project with no
affiliation.

## License

MIT — see [LICENSE](LICENSE).
