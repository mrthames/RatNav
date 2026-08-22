---
description: Run the pre-PR review — the auditors that apply to this diff, plus every blocking check
---

Review the current change the way RatNav expects before a pull request is opened.

## 1. Know what changed

```
git fetch origin
git diff origin/next...HEAD --stat
git log --oneline origin/next..HEAD
```

If the change is aimed at `main` rather than `next`, diff against that instead. Read the diff
before deciding anything — which auditors matter is a judgement about what was touched.

## 2. Run the auditors

Use the `review-coordinator` agent. It reads the diff, runs whichever of `safety-auditor`,
`privacy-auditor`, `docs-truth-checker` and `code-reviewer` apply, and reconciles what comes back
into one ranked list.

Two are not optional:

- **`privacy-auditor` always.** It is the only failure here that cannot be undone, and a diff that
  looks like pure code often carries a comment with somebody's name in it.
- **`safety-auditor` on any new dependency**, however dull it looks. A package can cross the line
  on the project's behalf without a single local line looking wrong.

## 3. Run the checks that will block the merge anyway

Finding out now costs a minute; finding out on the PR costs a round trip.

```
git add -A                              # the personal-data check reads tracked files only
bash tools/check-for-personal-data.sh
bash tools/check-the-safety-line.sh
dotnet test
cd web && npm run build && cd ..        # if the app changed
```

`dotnet test` must report **zero skipped** as well as zero failed. A skipped test is not a passing
test, and the release build refuses to ship past one.

## 4. Check the changelog

`CHANGELOG.md` needs an entry under **Unreleased** saying what a *tester* should go and try — not
what you did. "Fixed the map" is not actionable. "Streets now draws transits; check they are not
mistaken for extracts" is.

If the change genuinely has nothing to tell a tester — a comment typo, a CI tweak — say so, and
tell them to label the PR `no changelog` with a reason.

## 5. Report

One list, most severe first:

1. **Blocking** — file, line, why.
2. **Worth fixing** — or consciously deferring, with the reason.
3. **Worth knowing** — judgement calls to confirm were deliberate.
4. **Checked and clear** — which auditors ran, what they covered, and the result of every command
   above.

Then say plainly whether this is ready to open, and hand them the text to paste into the PR
description.

**Surface; do not fix.** If you find something and quietly correct it, nobody can tell later that
there was a decision. Tell them, and let them decide — including deciding to ship it anyway, with
their reasoning in the PR where a reviewer can see it.
