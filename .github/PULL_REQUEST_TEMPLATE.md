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

## Checks

- [ ] `dotnet test` passes locally
- [ ] `npm run build` in `web/` passes, if the app changed
- [ ] Tried it against a real raid or a real map, if that is what it touches
- [ ] **CHANGELOG.md** has an entry under **Unreleased** saying what changed
- [ ] Does not read game memory, inject code, hook rendering, or send input to the game

<!--
  That last one is not a formality. It is the line the whole project rests on, and a change that
  crosses it is not accepted however useful it is. docs/SAFETY.md sets out where it sits.
-->
