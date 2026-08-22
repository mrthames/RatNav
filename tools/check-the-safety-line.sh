#!/usr/bin/env bash
#
# The line RatNav rests on, checked by a machine rather than by remembering.
#
# RatNav reads two things the game already writes to disk. It does not read game memory, inject
# code, hook rendering or the keyboard, or send input to the game. That promise is on the front
# page, in docs/SAFETY.md, and is the reason anybody is willing to run this at all — so it is worth
# more than a line in CONTRIBUTING that everyone means to honor.
#
# Two checks:
#
#   1. Names that have no innocent use here. ReadProcessMemory is not a thing you reach for by
#      accident.
#   2. Any native call that is not on the allowlist. This is the stronger of the two: it does not
#      try to guess which new P/Invoke is dangerous, it just insists that adding one is a decision
#      somebody made on purpose. Four exist today, all against RatNav's own window.
#
# Adding to the allowlist is allowed. Doing it silently is not — it lands in the same diff, where
# a reviewer sees it.
#
# Run from the repository root. CI runs it on every push and pull request.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

failed=0

report() {
  echo "FOUND: $1"
  echo "$2"
  echo
  failed=1
}

# --- 1. APIs that would cross the line ------------------------------------------------------
#
# Reading or writing another process's memory, starting a thread inside it, installing a
# system-wide hook, or synthesizing input. -w so a comment saying "we do not use SendInput" is not
# what trips it; these are matched as whole words in real code.

forbidden='ReadProcessMemory|WriteProcessMemory|VirtualAllocEx|VirtualProtectEx|CreateRemoteThread|NtCreateThreadEx|OpenProcess|SetWindowsHookEx|SetWinEventHook|SendInput|keybd_event|mouse_event|BlockInput|IDXGISwapChain|ID3D11Device|D3D11CreateDevice|GetAsyncKeyState|GetKeyboardState'

hits=$(grep -rnEw "$forbidden" --include='*.cs' --include='*.csproj' src tests 2>/dev/null || true)
[ -n "$hits" ] && report "a call that would cross the safety line" "$hits"

# --- 2. Native calls that nobody has agreed to ----------------------------------------------
#
# Every P/Invoke in the repository, against the four that exist. Two set and read the overlay
# window's own extended styles; two register RatNav's hotkeys with Windows, which is the documented
# alternative to watching the keyboard.

allowed='GetWindowLongPtr SetWindowLongPtr RegisterHotKey UnregisterHotKey'

declared=$(grep -rhoE 'extern [^(]+[(]' --include='*.cs' src tests 2>/dev/null \
  | grep -oE '[A-Za-z_][A-Za-z0-9_]*[(]' \
  | tr -d '(' \
  | sort -u)

unexpected=''
for name in $declared; do
  case " $allowed " in
    *" $name "*) ;;
    *) unexpected="$unexpected$name"$'\n' ;;
  esac
done

if [ -n "$unexpected" ]; then
  report "a native call that is not on the allowlist" "$unexpected
RatNav talks to Windows in four places, all about its own window. A new one may well be
fine — screen reading and window placement both need Windows — but it is a decision, not a
detail. Add it to 'allowed' in this script in the same commit, and say in the PR what it
does and why it does not touch the game."
fi

if [ "$failed" -ne 0 ]; then
  echo "The safety line is what this project rests on. See docs/SAFETY.md."
  exit 1
fi

echo "The safety line holds."
