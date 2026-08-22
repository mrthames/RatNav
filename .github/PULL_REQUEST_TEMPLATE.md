<!--
  RatNav takes pull requests from invited collaborators. Anything from outside is closed
  automatically — see CONTRIBUTING.md for why, and for what to do instead.

  Contributions go to `next`, never to `main`. Check the base branch above before you submit.
-->

## What this changes

<!-- A sentence or two. What it does, not how. -->

## What a tester should look at

<!--
  This lands in the next alpha, which real people install. Say what to go and try, and what
  "working" looks like — "open Streets, plan two objectives, the second pin should be numbered 2".
-->

## Review

Run `review-coordinator` before opening this. It reads the diff, runs the auditors that apply
(`safety-auditor`, `privacy-auditor`, `docs-truth-checker`, `code-reviewer`) and hands back one
ranked list. See `.claude/agents/`.

**What it surfaced, and what you did about it:**

<!--
  Paste the findings. Include the ones you decided not to act on and why — that is the half worth
  reading. "Nothing found" is a fine answer if it is true; say which auditors ran.
-->

## Checks

- [ ] `review-coordinator` run, findings above
- [ ] `dotnet test` passes
- [ ] `npm run build` in `web/` passes, if the app changed
- [ ] Tried against a real raid or a real map, if that is what it touches
- [ ] **CHANGELOG.md** has an entry under **Unreleased** saying what a tester should look at
- [ ] Does not read game memory, inject code, hook rendering, or send input to the game

<!--
  That last one is not a formality. It is the line the whole project rests on, a change that
  crosses it is not accepted however useful it is, and tools/check-the-safety-line.sh will fail the
  build. docs/SAFETY.md sets out where it sits.
-->
