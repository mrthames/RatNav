# Contributing to RatNav

RatNav reads two things Escape from Tarkov writes to your own disk: log files, and the names of
screenshots. It does not read game memory, inject code, hook rendering, modify game files, or send
input to the game — and it never will. **A contribution that changes that will not be accepted**,
however useful the feature. The value of this project rests on being safe to run, and that is not
tradeable. [How it works](docs/SAFETY.md) sets out where the line is and why it is there.

## How this project is run

RatNav is one person's side project, kept free under
[PolyForm Noncommercial](LICENSE). Two things follow from that, and both are worth knowing before
you spend an evening on something.

**Forking is welcome; there is no review queue here.** Pull requests are not the way in — not
because contributions are unwelcome, but because running a review process is a commitment this
project cannot make. If you want a change, the licence lets you fork and make it. Give your version
its own name.

**Issues are read.** Bug reports especially: a wrong marker, a map that will not load, a quest that
reads incorrectly. Those are how anything gets found. A reply is not guaranteed and a fix is not
promised, which is the honest position for a project of one.

The most useful thing anybody can send is in [Sharing a map calibration](#sharing-a-map-calibration)
below — one screenshot settles a map for everyone.

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

- **`docs/GUIDE.md`** and **`docs/FAQ.md`** are what users read. If you change behaviour someone
  would notice, change these in the same commit — a guide that describes an older product than the
  one people download is worse than no guide.
- **`docs/backlog.md`** is the running record of what was asked for, what was built, and what was
  investigated and found impossible. The `[?]` entries are the interesting ones: they are questions
  with recorded findings rather than open work.
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
page for settling it — though **Maps → Settle it** in the app does the same job without leaving it.

Either way, open an issue with the map and the position you used. A calibration solved once ships
for everyone.
