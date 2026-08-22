#!/usr/bin/env bash
#
# Fails if personal data, or anything sensitive, has reached the repository.
#
# This is a public repository for a tool other people download. What belongs in it is what somebody
# needs in order to understand RatNav, build it, and install it. Nothing else.
#
# Two categories, and they fail for different reasons:
#
#   **Secrets** are an incident. A key that reaches a public repository is compromised the moment
#   it lands — scrapers watch the event firehose — and rotating it is the only real fix. Deleting
#   it from the tip does nothing at all.
#
#   **Personal data** is somebody's, not the project's. Git history is permanent and a public
#   repository is cloned by strangers, so the asymmetry runs one way: leaving something out costs
#   a little clarity, and putting it in cannot be undone.
#
# **On names.** People who contribute here under their own GitHub account are named by git itself,
# and that is theirs to decide. Anybody who has not signed up for that — a tester, a bug reporter,
# somebody mentioned in passing — is not named, because they never agreed to appear in a public
# history. A regex cannot tell those two apart, so it does not try; that is what the
# privacy-auditor persona is for. What a regex *can* check is the machine somebody works on, which
# is nobody's business either way.
#
# Run by CI on every push and pull request, because a rule nobody checks is a rule that holds until
# the first time somebody is in a hurry.
#
#     tools/check-for-personal-data.sh [commit-range]
#
set -uo pipefail

cd "$(dirname "$0")/.."

fail=0

report() {
  fail=1
  echo
  echo "FOUND: $1"
  echo "$2"
}

# Things allowed to look like a match, each for a stated reason. Anything added here is a decision,
# so say why:
#
#   - the repository's own URL and support link, public on purpose
#   - this script, which necessarily contains every pattern it looks for
#   - the installer's publisher field, which is meant to name the author
#   - placeholder profile directories, so documentation can show the shape of a path
#   - the no-reply addresses git and GitHub generate, which reach nobody
#   - example.com and example.org, reserved by RFC 2606 for exactly this
#   - GitHub Actions expressions, where a token is a reference and not a value
allowed='mrthames/RatNav|buymeacoffee\.com/thames_|check-for-personal-data|AppPublisher=|Users.(someone|you|user|username|player|<)|noreply@|no-reply@|@example\.(com|org)|\$\{\{ *secrets\.|secrets\.GITHUB_TOKEN'

# git grep reads what is tracked. A new file that has not been added yet is invisible to every
# check below, so running this before `git add` reports success on a file nobody looked at — which
# is exactly how a Windows profile path reached CI once. Say so rather than passing quietly.
untracked=$(git ls-files --others --exclude-standard 2>/dev/null || true)
if [ -n "$untracked" ]; then
  echo "NOTE: these files are untracked and were NOT checked. 'git add' them and run again:"
  echo "$untracked" | sed 's/^/  /'
  echo
fi

find_in_tracked() {
  git grep -n -I -i -E "$1" -- . ':!tools/check-for-personal-data.sh' ':!LICENSE' 2>/dev/null \
    | grep -viE "$allowed" || true
}

# --- Credentials ------------------------------------------------------------------------------
#
# Prefixed tokens are matched by their prefix, because that is what makes them unmistakable — and
# unmistakable is what stops an argument about whether a given string is a real key.

hits=$(find_in_tracked 'gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{20,}')
[ -n "$hits" ] && report "a GitHub token" "$hits"

hits=$(find_in_tracked 'AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|aws_secret_access_key')
[ -n "$hits" ] && report "an AWS credential" "$hits"

hits=$(find_in_tracked 'AIza[0-9A-Za-z_-]{35}|ya29\.[0-9A-Za-z_-]{20,}')
[ -n "$hits" ] && report "a Google API key or OAuth token" "$hits"

hits=$(find_in_tracked 'xox[baprs]-[0-9A-Za-z-]{10,}|hooks\.slack\.com/services/')
[ -n "$hits" ] && report "a Slack token or webhook" "$hits"

hits=$(find_in_tracked '(sk|rk)_live_[0-9A-Za-z]{20,}|sk-[A-Za-z0-9]{32,}')
[ -n "$hits" ] && report "an API secret key" "$hits"

hits=$(find_in_tracked 'BEGIN ([A-Z]+ )?PRIVATE KEY|PuTTY-User-Key-File')
[ -n "$hits" ] && report "a private key" "$hits"

hits=$(find_in_tracked 'eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.')
[ -n "$hits" ] && report "a JSON web token" "$hits"

# A secret with a value beside it. Placeholders are how examples get written, so they are excluded
# rather than reported — a check that fires on documentation is a check people learn to ignore.
hits=$(find_in_tracked '(password|passwd|pwd|api[_-]?key|access[_-]?token|client[_-]?secret|connectionstring)["'"'"']? *[:=] *["'"'"']?[^"'"'"' ,;)}]{8,}' \
  | grep -viE 'your|example|placeholder|changeme|xxxx|\*\*\*|<|\{|\$\(|process\.env|getenvironmentvariable|environment\.get|config\[|options\.' || true)
[ -n "$hits" ] && report "a secret with a value next to it" "$hits"

# --- Personal data ----------------------------------------------------------------------------

# Lock files are generated, and what is in them belongs to whoever published the package: npm
# records deprecation notices verbatim, and some of those carry the author's own address. Not ours
# to police, and not removable without editing a file we do not write. Credentials in a lock file
# are still caught — every check above reads them.
hits=$(find_in_tracked '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'   | grep -vE '(package-lock|pnpm-lock|yarn\.lock|packages\.lock)\.?j?s?o?n?:')
[ -n "$hits" ] && report "an email address" "$hits"

# Separators are required, so that a version number, a timestamp or an item id is not read as a
# phone number.
hits=$(find_in_tracked '\+[0-9]{1,3}[ -][0-9]{3}[ -][0-9]{3,4}[ -][0-9]{4}|\(\+?[0-9]{3}\) ?[0-9]{3}-[0-9]{4}|\b[0-9]{3}-[0-9]{3}-[0-9]{4}\b')
[ -n "$hits" ] && report "a phone number" "$hits"

hits=$(find_in_tracked '\b[0-9]{3}-[0-9]{2}-[0-9]{4}\b')
[ -n "$hits" ] && report "something shaped like a social security number" "$hits"

hits=$(find_in_tracked '\b(4[0-9]{3}|5[1-5][0-9]{2}|3[47][0-9]{2}|6011)[ -][0-9]{4}[ -][0-9]{4}[ -][0-9]{4}\b')
[ -n "$hits" ] && report "something shaped like a payment card number" "$hits"

# --- The machine somebody works on ------------------------------------------------------------

hits=$(find_in_tracked '[A-Za-z]:\\Users\\|[A-Za-z]:/Users/|/home/[a-z]|/Users/[a-z]|%USERPROFILE%\\[A-Za-z]')
[ -n "$hits" ] && report "a path from a developer's machine" "$hits"

# A whole dotted quad: "10.0.19041" is a Windows version, and matching it made this shout about
# every project file in the repository.
# A whole dotted quad: "10.0.19041" is a Windows version, and matching it made this shout about
# every project file in the repository.
#
# LanBoundaryTests is excluded, and only from this check. It asserts that an address on the local
# network is refused the run of the machine, which it cannot do without naming some — and the ones
# it names are invented. Every other check still reads that file.
hits=$(find_in_tracked '\b192\.168\.[0-9]{1,3}\.[0-9]{1,3}\b|\b10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\b|\b172\.(1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}\b|\b[A-Za-z0-9-]+\.local\b' \
  | grep -v 'LanBoundaryTests\.cs:')
[ -n "$hits" ] && report "an address on a private network" "$hits"

hits=$(find_in_tracked 'ssh +[a-z_][a-z0-9_-]*@|scp +[^ ]+ +[a-z_][a-z0-9_-]*@')
[ -n "$hits" ] && report "a login on somebody's machine" "$hits"

# --- Somebody's own data, which lives in their data directory and never here -------------------

hits=$(git ls-files | grep -E '(^|/)(settings|tracking|progress|waypoints|profile)\.json$|(^|/)plans/|\.log$|(^|/)\.env($|\.)' || true)
[ -n "$hits" ] && report "a file holding somebody's own data" "$hits"

# Screenshots from the game, which carry coordinates in their names and a stash in their pixels.
hits=$(git ls-files | grep -E '[0-9]{4}-[0-9]{2}-[0-9]{2}\[[0-9]' || true)
[ -n "$hits" ] && report "a game screenshot" "$hits"

# --- And the commit messages ------------------------------------------------------------------
#
# Every check above reads the working tree, and a message is neither a tracked file nor something
# anybody looks at twice. It is also the expensive one to get wrong: a file is fixed with an edit,
# a message only by rewriting every commit after it.
#
# Only what this push adds, so a rewrite is never demanded for history somebody has already cloned.
# CI passes the range; run by hand it reads the last twenty.
range="${1:-HEAD~20..HEAD}"

if git rev-parse --quiet --verify "${range%%..*}" >/dev/null 2>&1; then
  hits=$(git log --format='%H %s%n%b' "$range" 2>/dev/null \
    | grep -inE '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|gh[pousr]_[A-Za-z0-9]{16,}|AKIA[0-9A-Z]{16}|[A-Za-z]:\\Users\\|/home/[a-z]|/Users/[a-z]' \
    | grep -viE "$allowed|Co-Authored-By|Claude-Session" || true)

  [ -n "$hits" ] && report "an address, a credential or a machine path in a commit message" "$hits"
fi

echo

if [ "$fail" -ne 0 ]; then
  cat <<'MESSAGE'
This does not belong in a public repository.

If it is a credential, treat it as compromised and rotate it. A key that reached a public
repository was scraped before you noticed, and deleting it changes nothing.

If it has already been pushed, say so rather than quietly taking it off the tip. Removing it from
HEAD leaves it in the history and creates the belief that it is gone.
MESSAGE
  exit 1
fi

echo "Nothing personal in the repository."
