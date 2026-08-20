#!/usr/bin/env bash
#
# Fails if anything personal has reached the repository.
#
# This is a public repository for a tool other people download. What belongs in it is what somebody
# needs in order to understand RatNav, build it, and install it — and nothing whatsoever about the
# machine it happens to be developed on.
#
# Run by CI on every push and pull request, because a rule nobody checks is a rule that holds until
# the first time somebody is in a hurry.
#
#     tools/check-for-personal-data.sh
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

# Things that are allowed to look like a match: the repository's own URL, and the support link.
# Both are public on purpose.
allowed='mrthames/RatNav|buymeacoffee\.com/thames_|check-for-personal-data|AppPublisher=|Users.(someone|you|user|player)'

find_in_tracked() {
  git grep -n -I -i -E "$1" -- . ':!tools/check-for-personal-data.sh' ':!LICENSE' 2>/dev/null \
    | grep -vE "$allowed" || true
}

# --- A name or a contact address.
# Word boundaries, or "adjusting" matches "justin" and the check cries wolf until nobody reads it.
hits=$(find_in_tracked '\bjustin\b|\bthames\b|@gmail\.com|@outlook\.com|@hotmail\.com')
[ -n "$hits" ] && report "a name or contact address" "$hits"

# --- Paths from somebody's own machine.
hits=$(find_in_tracked 'C:\\Users\\|/home/[a-z]|/Users/[a-z]')
[ -n "$hits" ] && report "a path from a developer's machine" "$hits"

# A whole dotted quad: "10.0.19041" is a Windows version, and matching it made this shout about
# every project file in the repository.
hits=$(find_in_tracked '\b192\.168\.[0-9]{1,3}\.[0-9]{1,3}\b|\b10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\b|\b[A-Za-z0-9-]+\.local\b')
[ -n "$hits" ] && report "an address on a private network" "$hits"

# --- Anything that looks like a credential. Not exhaustive, and not meant to be: the point is to
# --- catch the obvious mistake before it is public, not to replace reading what you commit.
hits=$(find_in_tracked 'ghp_[A-Za-z0-9]{20}|BEGIN (RSA|OPENSSH|PRIVATE) KEY|xox[baprs]-')
[ -n "$hits" ] && report "something shaped like a credential" "$hits"

# --- Files that hold a player's own progress. These belong in %LOCALAPPDATA%, never here.
hits=$(git ls-files | grep -E '(^|/)(settings|tracking|progress|waypoints)\.json$|(^|/)plans/|\.log$' || true)
[ -n "$hits" ] && report "a file holding somebody's own progress" "$hits"

# --- Screenshots from the game, which carry coordinates in their names and a stash in their pixels.
hits=$(git ls-files | grep -E '[0-9]{4}-[0-9]{2}-[0-9]{2}\[[0-9]' || true)
[ -n "$hits" ] && report "a game screenshot" "$hits"

echo
if [ "$fail" -eq 0 ]; then
  echo "Nothing personal in the repository."
else
  echo "Personal data found. It does not belong in a public repository — take it out, and if it has"
  echo "already been pushed, say so rather than quietly removing it from the tip."
fi

exit "$fail"
