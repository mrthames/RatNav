---
description: Ground this session in RatNav — read the project, check the environment, explain the rules
---

You are starting work on RatNav with somebody who may not have worked on it before. Orient
yourself, orient them, and prove the environment works before either of you writes anything.

`CLAUDE.md` is already in your context. This is the active half: reading the things it points at,
and checking the things it cannot.

## 1. Read, before saying anything

- `CLAUDE.md` — the safety line, the branches, the traps, the house style.
- `CONTRIBUTING.md` — how a change gets in, and how a release is cut.
- `docs/SAFETY.md` — where the line sits and why it is not tradeable.
- `.claude/agents/` — the four auditors and the coordinator that runs them.

If they said what they intend to work on, read the relevant one too: `docs/calibration.md` for
anything touching maps or coordinates, `docs/game-logs.md` for anything reading the game's files.
Both exist because the same mistake kept repeating.

## 2. Check the environment, do not assume it

Run these and report what actually happened:

```
git --version
dotnet --version          # 8.0.x
node --version            # 22 or later
git remote -v
git branch --show-current
```

Then prove it builds, because "it should work" is not the same as it working:

```
cd web && npm install && npm run build && cd ..
dotnet test
```

`dotnet test` should report a few hundred passing and **zero skipped**. If anything fails, stop and
work out why with them — a broken baseline makes every later result meaningless, and they will not
know whether their first change caused it.

On Windows, offer them a shortcut so launching it is not a command they have to remember:

```
pwsh tools/make-dev-shortcut.ps1
```

It puts **RatNav (dev)** on the Desktop and in the Start Menu, pointed at the build they just made.
It warns if that build is older than the source, which is the mistake worth catching early: once a
release is also installed there are two RatNavs on the machine and they look identical.

**On a Mac or Linux**, `RatNav.App` will not build: it is WPF and targets `net8.0-windows`. Core,
Service, the tests and the web app build anywhere. Say so plainly rather than letting them think
something is wrong, and scope their work to the service, the web app, the tests and the docs.

## 3. Get them on a branch

Work happens on `next`, never on `main`, and never directly on `next` either:

```
git fetch origin
git checkout -B next origin/next
git checkout -b <short-name-for-the-change>
```

If `origin/next` does not exist — it can be deleted after a promotion — recreate it from `main` and
say that you did:

```
git checkout -B next origin/main
git push -u origin next
```

`next` is only ever the same as `main` or ahead of it. It is not a long-lived fork, and it never
holds anything `main` will not eventually get.

## 4. Tell them the four things that will actually catch them out

Briefly, in your own words, and only these:

1. **The safety line.** RatNav never touches the game — no memory, no injection, no hooks, no
   synthetic input. It is enforced by `tools/check-the-safety-line.sh`, and a change that crosses
   it is not accepted however good it is. If a task seems to need it, stop and say so.
2. **Nothing personal, and no secrets.** Public repository. `tools/check-for-personal-data.sh`
   reads **tracked** files, so run it after `git add`, and it cannot read a screenshot at all.
3. **Every PR into `next` needs a CHANGELOG entry** under **Unreleased**, saying what a *tester*
   should go and look at. It is a required check.
4. **Run `/review` before opening the PR**, and put what it surfaced in the description —
   including what you chose not to act on, and why.

## 5. Ask what they are doing, then help

Do not start changing things because you have finished orienting. Ask what they came to do, and if
it is not obvious which part of the codebase that is, find out before proposing anything.

Report the state you found: versions, whether the build and tests passed, which branch they are on.
If something is wrong, that is the first thing to fix and the only thing to talk about.
