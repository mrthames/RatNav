# Contributing to RatNav

RatNav reads two things Escape from Tarkov writes to your own disk: log files, and the names of
screenshots. It does not read game memory, inject code, hook rendering, modify game files, or send
input to the game — and it never will. **A contribution that changes that will not be accepted**,
however useful the feature. The value of this project rests on being safe to run, and that is not
tradeable. [How it works](docs/SAFETY.md) sets out where the line is and why it is there.

## How this project is run

RatNav is one person's side project, kept free under [PolyForm Noncommercial](LICENSE).

**Bug reports are read.** A marker in the wrong place, a map that will not load, a quest that reads
incorrectly — those are worth knowing about, and an issue is the way to say so. A reply is not
guaranteed and a fix is not promised, which is the honest position for a project of one.

**There is no review queue.** Pull requests are not the way in, and feature requests are not being
gathered. A PR opened here is **closed automatically**, with a note saying why — which is a worse
welcome than a reply, and a better one than silence for six months. If you want RatNav to do
something it does not, the [license](LICENSE) lets you fork it for anything noncommercial — give
your version its own name.

The rest of this file is for anyone building it themselves.

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

## The other documents

- **`docs/GUIDE.md`** and **`docs/FAQ.md`** are what users read. If you change behavior someone
  would notice, change these in the same commit — a guide that describes an older product than the
  one people download is worse than no guide.
- **`CHANGELOG.md`** is what changed and why, version by version. Findings that shaped a decision
  live in the commit that made it — `git log` is the record.
- **`brand/README.md`** covers the mark, and `brand/render.ps1` rebuilds the `.ico` and the PNGs
  from it. The path coordinates are deliberately duplicated between the SVG and the renderer —
  change one, change the other.

## Releases

Work is grouped into a **version**, not shipped a commit at a time.

- **Alphas are tagged with a suffix** — `v0.3.0-alpha.1` — and GitHub marks them prerelease. They do
  not become "Latest", so the download on the front page keeps pointing at the last stable build.
- **Promotion to stable is a decision, not a step.** When a version has been tried properly, it is
  promoted deliberately, because that changes what everybody downloads.
- **The README names the latest stable release**, updated by a workflow when one is promoted. A
  version written by hand goes stale in two releases, and a stale install step is worse than none —
  it sends somebody looking for a file that is not there.

## House style

**Tests describe behavior, not methods.** `Replaying_the_logs_cannot_undo_a_correction` says what
must remain true. `TestProgressStore3` does not.

**Test against reality where reality is available.** The log parser is tested with a notification
copied verbatim from a live client rather than one written to match the parser. A test written
from the implementation only proves the implementation agrees with itself.

**Comments explain why, not what.** The code says what. Comments are for the reason a decision was
made, especially where the obvious approach was tried and failed.

**Do not ship what cannot be trusted.** A map whose position cannot be established is held back
rather than shown with a warning. A pin that might be wrong is worse than no pin.

## Data sources

Game data comes from [tarkov.dev](https://tarkov.dev), maps and their calibration from the
[tarkov-dev repo](https://github.com/the-hideout/tarkov-dev), both maintained by volunteers.
Cache politely, do not poll hard, and credit the map authors — every map carries its author in the
data and RatNav shows it.
