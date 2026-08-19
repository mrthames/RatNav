# Contributing to RatNav

RatNav reads two things Escape from Tarkov writes to your own disk: log files, and the names of
screenshots. It does not read game memory, inject code, hook rendering, modify game files, or send
input to the game — and it never will. **A contribution that changes that will not be accepted**,
however useful the feature. The value of this project rests on being safe to run, and that is not
tradeable.

## Getting it running

```
git clone https://github.com/mrthames/RatNav
cd RatNav

cd web && npm install && npm run build && cd ..   # builds into the service's wwwroot
dotnet test
dotnet run --project src/RatNav.App                # the overlay, service and tray
```

For UI work, `dotnet run --project src/RatNav.Service` and `npm run dev` in `web/` gives you hot
reload against a live service.

`RATNAV_DATA_DIR` overrides where cached data, plans and settings go, which keeps experiments away
from your real progress.

## What is worth knowing before you start

Two documents will save you days:

- **`docs/calibration.md`** — how world coordinates become a position on a map, and the three
  wrong answers that were shipped and corrected before the right one. If you are touching maps,
  read it first.
- **`docs/game-logs.md`** — what the game writes, where, and the traps: files renamed between
  versions, held open and reported as zero bytes, and notifications pretty-printed across many
  lines.

Both exist because the same mistake kept repeating: a rule fitted the evidence available, and the
evidence was wrong.

## House style

**Tests describe behaviour, not methods.** `Replaying_the_logs_cannot_undo_a_correction` says what
must remain true. `TestProgressStore3` does not.

**Test against reality where reality is available.** The log parser is tested with a notification
copied verbatim from a live client rather than one written to match the parser. A test written
from the implementation only proves the implementation agrees with itself.

**Comments explain why, not what.** The code says what. Comments are for the reason a decision was
made, especially where the obvious approach was tried and failed.

**Say when something might be wrong.** A map whose calibration could not be established says so
in the UI. Confidently displaying a pin that might be 75 metres out is worse than admitting doubt.

## Data sources

Game data comes from [tarkov.dev](https://tarkov.dev), maps and their calibration from the
[tarkov-dev repo](https://github.com/the-hideout/tarkov-dev), both maintained by volunteers.
Cache politely, do not poll hard, and credit the map authors — every map carries its author in the
data and RatNav shows it.

## Sharing a map calibration

If you have a map RatNav marks as uncertain, `tools/make-calibration-page.py` builds a one-click
page for settling it. Take a screenshot somewhere you can point at on the map, click that spot,
and open a pull request with the result. A calibration solved once ships for everyone.
