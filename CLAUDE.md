# RatNav

A raid planner and navigation overlay for Escape from Tarkov. WPF overlay, ASP.NET Core service,
React web app. Everyone working on this uses AI assistance, so this file is the context your agent
gets before it touches anything.

## The line this project rests on

RatNav reads **two things the game already writes to your own disk**: log files, and the
coordinates the game encodes into screenshot filenames. It does **not**:

- read or write game memory
- inject code into the game process
- hook DirectX, Direct3D, the game's rendering, or the keyboard
- modify any game file
- send synthetic keyboard or mouse input to the game

**This is not a preference and it is not negotiable.** People run RatNav because it cannot get them
banned; a feature that crosses this line is worth less than the trust it costs, however good it is.
`tools/check-the-safety-line.sh` enforces it in CI, and `docs/SAFETY.md` sets out where the line
sits and why.

If a task seems to need something on that list, stop and say so rather than finding a way.

## Nothing personal goes in the repository

This is a public repository. **No secrets and no personal data**, and the two fail for different
reasons.

**Secrets are an incident.** Keys, tokens, private keys, connection strings, passwords with a value
beside them. A key that reaches a public repository is compromised the moment it lands — scrapers
watch the event firehose — so rotation is the fix, not deletion. They go nowhere at all: not to a
public repository, not to a private one.

**Personal data is somebody's, not the project's.** Email addresses, phone numbers, government or
payment identifiers, anything from a real account, and the machine somebody works on — a real user
profile directory, a private network address, an SSH login.

**Names are the one place with a distinction.** People contributing here under their own GitHub
account are named by git itself, and that is theirs to decide. Anybody who has not signed up for
that — a tester, a bug reporter, somebody mentioned in passing — is not named. A regex cannot tell
those apart, so `privacy-auditor` is the check for it.

`tools/check-for-personal-data.sh` runs in CI and fails the build. It reads **tracked** files, so
run it after `git add` — it will tell you what it did not check. It cannot read a screenshot at
all, and a screenshot of the Setup page prints a profile directory in twelve-point type.

## Branches

| | |
|---|---|
| `next` | Where work lands. Open a PR into it; merge your own once checks are green. |
| `main` | What the world downloads. Only the maintainer merges here. |

Never commit directly to either. Every change is a PR, and every PR into `next` needs a
**CHANGELOG.md** entry under **Unreleased** saying what a tester should go and look at — an alpha
nobody knows how to test is an alpha nobody tests.

## Before you open a PR

Run the review personas in `.claude/agents/`. `review-coordinator` reads the diff and runs the ones
that apply; the others can be summoned directly when you know what you are touching. Put what they
surface in the PR description — including the things you decided not to act on, and why.

The deterministic checks (`dotnet test`, the web build, the two `tools/` scripts, the changelog)
run in CI and block the merge. The personas catch what a script cannot.

## What will bite you

These are not hypothetical. Each one shipped.

- **Positions are device-independent pixels.** Windows has already divided out display scaling.
  Multiplying by it again is the 1.95-scale bug, and it looks plausible on one monitor.
- **WPF `StaticResource` resolves at parse time and only looks backwards.** A template referencing
  a style declared below it throws when the template *expands* — so the app starts fine and dies
  when the first row is drawn. Build succeeding is not evidence.
- **Detaching an element has two cases.** A `Panel` has `Children`; a `ContentControl` or `Window`
  has `Content`. Handling only the first throws from inside `WmDestroy`.
- **`RatNav.App` uses WPF and WinForms together**, so `Panel`, `Size`, `Point`, `Brush`, `Color`
  and `Brushes` are ambiguous and need aliases.
- **`npx tsc --noEmit` is a no-op here** — the solution-style tsconfig has `"files": []`. Use
  `npm run build` in `web/`.
- **React registers `onWheel` passively**, so `preventDefault()` in one is silently ignored. A
  hand-attached listener with `{ passive: false }` is the fix.
- **The maps are the hard part.** `docs/calibration.md` records three wrong answers that shipped
  before the right one. Read it before touching coordinates. `docs/game-logs.md` does the same for
  the log reader.

## House style

Long-form, and deliberately so — see `CONTRIBUTING.md`.

- **Comments explain why, not what.** The code says what. Comments carry the reason, especially
  where the obvious approach was tried and failed.
- **Tests describe behavior.** `Replaying_the_logs_cannot_undo_a_correction`, not `TestStore3`.
- **A skipped test is not a passing test.** Delete one that is no longer needed rather than
  skipping it; the release build refuses to ship with any test skipped.
- **Test against reality.** The log parser is tested with a notification copied verbatim from a
  live client. A test written from the implementation only proves the implementation agrees with
  itself.
- **Do not ship what cannot be trusted.** A map whose position cannot be established is held back
  rather than drawn with a warning. A pin that might be wrong is worse than no pin.
- **American English**, in prose, comments and identifiers alike.
- **If you change behavior a user would notice, change `docs/GUIDE.md` and `docs/FAQ.md` in the
  same commit.** The README and the docs described features that did not exist for several
  releases because nobody checks prose for truth.

## Running it

```
cd web && npm install && npm run build && cd ..   # builds into the service's wwwroot
dotnet test
dotnet run --project src/RatNav.App                # overlay, service and tray
```

`RATNAV_DATA_DIR` moves cached data, plans and settings somewhere else, which keeps experiments
away from your real progress. `RatNav.App` is `net8.0-windows` and Windows-only; Core, Service, the
tests and the web app build anywhere.
