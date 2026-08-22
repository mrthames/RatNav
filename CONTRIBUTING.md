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

**Pull requests come from invited collaborators only.** There is no open review queue, and a PR
from anyone else is **closed automatically** with a note saying why — a worse welcome than a
reply, and a better one than silence for six months. If you want RatNav to do something it does
not, the [license](LICENSE) lets you fork it for anything noncommercial — give your version its
own name.

If you have been invited, the section below is yours. The rest of the file is for anyone building
it themselves.

## If you have commit access

Two branches, and the difference between them is who decides.

| | |
|---|---|
| **`next`** | Where work lands. Open a PR into it, and merge your own once the checks are green. |
| **`main`** | What the world downloads. Only the maintainer merges here, and that is the whole point of the split. |

**Nothing goes to `main` directly.** It reaches `main` when `next` is promoted, which is a
deliberate act rather than a consequence of merging.

### Getting a change in

1. **Branch off `next`.**
2. **Test it.** `dotnet test` has to pass, and `npm run build` in `web/` if you touched the app.
   Run it against a real map or a real raid if that is what it touches — the tests cover the
   parsing and the maths, not whether a pin lands on the right building.
3. **Run `review-coordinator`.** See [the review below](#the-review).
4. **Add a changelog entry** under **Unreleased** in [CHANGELOG.md](CHANGELOG.md), in whichever
   of its two sections fits:

   - **Changed for you** — anything a person running RatNav would notice. Say what to go and try,
     not just what you did.
   - **Repository and process** — build, CI, docs, tooling. Real work, but nobody installs it.

   This is a required check, and it is required for a reason: an alpha is only worth installing if
   the person installing it can see what changed and knows what to look at. The split exists
   because those two lists are read by different people, and a user scanning for "should I
   update?" should not have to wade through CI changes to find out.
5. **Open the PR into `next`.** CI runs, the changelog check runs.
6. **Merge it yourself** once both are green. No approval needed — the gate is on `main`, not
   here.

### The review

Everyone working on RatNav uses AI assistance, so the review is written for that rather than
around it. Four auditors live in [`.claude/agents/`](.claude/agents), each owning one thing that
has actually gone wrong here:

| | |
|---|---|
| **`safety-auditor`** | The promise that RatNav never touches the game. Also reads every new dependency, because a package can cross the line on your behalf. |
| **`privacy-auditor`** | Personal data staying out of a public repository — including the screenshots, which no text search can read. |
| **`docs-truth-checker`** | Documentation describing the software that exists. The README once claimed a routing feature that had never been built, in six places. |
| **`code-reviewer`** | The traps this codebase has actually shipped: display scaling, WPF resource ordering, migrations, test discipline. |

**`review-coordinator` is the one to run**, or `/review`, which does the same and also runs every
check that would block the merge. It reads your diff, decides which of the four auditors apply,
runs them in parallel and reconciles the results into one ranked list.

**Starting fresh?** `/onboard` reads the project, checks your toolchain, proves the build and tests
pass, and puts you on a branch off `next`. Worth the two minutes on a machine you have not worked
on here before — a broken baseline makes every later result meaningless.

Put what it surfaced in the PR description, **including what you chose not to act on and why**.
That half is the point: it is the difference between a decision somebody made and a thing nobody
noticed. The auditors deliberately cannot edit anything — they report, you decide.

None of this replaces the checks that block the merge. `dotnet test`, the web build,
`tools/check-for-personal-data.sh`, `tools/check-the-safety-line.sh` and the changelog all run in
CI and are not opinions. The auditors exist for what a script cannot check: whether a claim is
true, whether a screenshot leaks a name, whether the obvious fix is the one that shipped a crash
last time.

Read [`CLAUDE.md`](CLAUDE.md) first, whatever you are doing. It is the context every agent working
on this repository starts from.

If a change genuinely has nothing to tell a tester — a typo in a comment, a CI tweak — label the
PR `no changelog` and say why in the description. It is a label rather than a silent exception so
that skipping it is visible.

### How a change reaches people

```
your PR ──▶ next ──▶ alpha (v0.4.0-alpha.1, a prerelease)
                       │
                       └──▶ main ──▶ stable (v0.4.0)
```

Alphas are cut from `next` whenever there is something worth trying, and are marked prerelease on
GitHub — the front page keeps pointing at the last stable one, so nobody gets an alpha by
accident. A stable release happens when the maintainer decides `next` is ready, which is the one
step that is not automatic and is not meant to be.

So a merge to `next` is not a small thing: people install alphas. But it is also not the last
word, which is why you can merge your own.

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

### Version numbers

`MAJOR.MINOR.PATCH`, and each part means something:

| | |
|---|---|
| **MAJOR** | A break. Somebody's saved plans, settings or progress do not survive the update, or the way RatNav is used changes shape. Still `0` — everything below is pre-1.0 and says so. |
| **MINOR** | A feature, or a change somebody would notice and might have to be told about. |
| **PATCH** | A fix. Nothing new, nothing moved. |

**Prereleases add `-alpha.N`**, counting from 1 for each version: `v0.4.0-alpha.1`, then
`-alpha.2`. `-beta.N` and `-rc.N` are accepted by the same rules if a version ever needs them.

**The tag is the only source of truth.** The build reads its version from the tag and stamps it
into the binary, both filenames and the release notes. Nothing is typed anywhere twice, and a tag
that does not match the shape above **fails the release build before anything is compiled** —
which is how a stable and an alpha once shipped installers with the same filename.

### Cutting a release

1. **Move the changelog on.** Rename `## Unreleased` to `## 0.4.0-alpha.1` — or the stable version —
   and date it. Add a fresh empty `## Unreleased` above it. The release build **fails if
   `CHANGELOG.md` has no section matching the tag**, because a build nobody can read notes for is
   a build nobody can decide whether to install.
2. **Tag it.** `git tag v0.4.0-alpha.1 && git push origin v0.4.0-alpha.1`, from `next` for an
   alpha, from `main` for a stable release.
3. **The build does the rest**: runs the tests, refuses to ship past a failing *or skipped* one,
   builds the installer and the portable zip, and publishes the release — marked prerelease if the
   tag has a suffix.
4. **For a stable release, run the Latest stable workflow** afterwards. It updates the README's
   install line, and it cannot fire on its own.

**Promoting `next` to `main` is the maintainer's, and only theirs.** A pull request into `main`
requires their review — [CODEOWNERS](.github/CODEOWNERS) says so and a ruleset enforces it — so a
collaborator cannot put anything in front of users on their own. The maintainer promotes either by
merging that pull request or by pushing `next` to `main` directly, which their admin bypass
allows.

Nothing ships from either branch until a `v*` tag exists, and **only an admin can create one**.
That is the real gate: even something merged to `main` reaches nobody until it is tagged
deliberately.

Both artifacts are named `RatNav-<version>-setup.exe` and `RatNav-<version>-win-x64.zip`, with the
same version in both, suffix included.

### How this appears to people

- **Alphas are cut from `next`** and marked prerelease. They never become "Latest", so the download
  on the front page keeps pointing at the last stable build and nobody gets an alpha by accident.
  Releases are driven by the tag rather than the branch, so an alpha can come off `next` at any
  point without disturbing `main`.
- **Promotion to stable is a decision, not a step.** When a version has been tried properly, it is
  promoted deliberately, because that changes what everybody downloads.
- **The README names the latest stable release**, updated by a workflow when one is promoted. A
  version written by hand goes stale in two releases, and a stale install step is worse than none —
  it sends somebody looking for a file that is not there.

## House style

**Tests describe behavior, not methods.** `Replaying_the_logs_cannot_undo_a_correction` says what
must remain true. `TestProgressStore3` does not.

**A skipped test is not a passing test.** If a test is no longer needed, delete it — git remembers
it, and a skipped test reads as coverage that is not there. The release build refuses to ship with
any test skipped, for the same reason it refuses to ship with one failing.

**Two suites, both run on every pull request.** `dotnet test` covers the service and the parsing;
`npm test` in `web/` covers the app people click. They are the shared safety net, and adding to
them is how a bug stays fixed.

**Writing a web test.** `src/test/service.ts` is the vocabulary: `serve({...})` stands in for the
RatNav service and records what was asked of it, `fails(500)` makes it refuse, and the fixtures —
`aQuest`, `aTrackedItem`, `anUpgrade`, `aMap`, `noRaid` — carry **every** field the app reads.
Build on those rather than hand-rolling an object: a view that maps over a field your fixture left
out throws during render, React unmounts the subtree, and what you see is an empty page and an
assertion failure that says nothing about the cause.

Anything not stubbed throws by name, on purpose — a view that quietly grows a new call should
break the test that did not know about it, rather than reaching a service that happens to be
running on your machine.

Two of these tests found real bugs while being written: the Hideout page had no error handling at
all and sat on "loading" forever when the service refused, and the Items row actions let a failed
save escape as an unhandled rejection. Both had been there for months.

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
