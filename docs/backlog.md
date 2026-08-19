# Backlog

Requests are written down here **before** they are worked on.

This exists because a long working session gets summarised as it runs, and anything only held in
conversation can be lost when that happens. A request that made it to this file survives; one that
did not, might not. So: capture first, then execute, then tick.

Status: `[ ]` not started · `[~]` in progress · `[x]` done · `[?]` needs a decision from Justin

---

## Round 5 — 2026-08-19

### Overlay

- [x] **Separate saved state for F5 and F9.** Arranging the centred map overwrote the corner
  panel's position and size. Placement, zoom, pan, and follow are now per presentation; ink,
  opacity, halo, line weight and the items panel stay shared.
- [x] **Floor ghosting.** Draw the floors below the active one faintly underneath it. On Streets,
  ground and second floor need to be readable together — walking off a street into a building, the
  room means nothing without the street it came off.
- [x] **Extracts: green for PMC, yellow for Scav.** Bigger, using an extract icon, with the extract
  name drawn on the overlay. Currently too small to read.
- [x] **Place names on the map**, toggleable — "Old Gas", "New Gas", "Dorms".
- [x] **Waypoints bigger**, using a waypoint icon. Hover tooltip in the F5 view saying which
  objective it is; F9 needs the marker only, no tooltip.
- [x] **Cursor vanishes over the map in F6.** Should stay visible while positioning.
- [x] **F5 should hide the popped-out items window too** — it toggles every overlay component, not
  just the map.

### Items

- [x] **Drop money from item tracking** — roubles, dollars, euros. Overlay and buddy app both.
  These are not things you find in raid; they come from selling and quests.
- [x] **Set how many you need**, not only how many you have. The Needed view behaves correctly;
  the watchlist needs the same controls.
- [x] **Remove the flea price column.**
- [x] **Wiki link per item**, plus whatever indicates likely spawn locations.
- [~] **Items tab: sections like the overlay** — quests/hideout available now, collapsible, plus a
  look-ahead control so it is not 561 rows of the whole wipe.

### Hideout

- [x] **Game edition sets the starting stash level.** A Setup dropdown; Edge of Darkness and
  Unheard start at Stash 4, Prepare for Escape at 3, Left Behind at 2. Never lowers a stash you
  have already upgraded past.
- [x] **Check why Stash cannot be built** under "Buildable now" — confirmed correct, not a bug.
  Stash is 4/4 on your profile, so there is no next level to offer. Nothing was gating it; it is
  finished.

### Buddy app

- [ ] **Map controls to match the overlay** — zoom, right-drag pan, layer control.
- [ ] **Plan page: call out required keys.** Show which key each objective needs, in red when not
  marked as held, and let it be marked found/held.
- [x] **Character level**, configurable in Setup, and now filtering which quests count as
  available. **Cannot be read automatically** — nothing the game writes to disk reports it, and
  the only endpoint that does needs your account password. Setup suggests a floor from the quests
  you have marked complete instead.
- [x] **Traders and their loyalty levels.** Own tab: loyalty 1–4 per trader, quests done and
  active, and what each will give you right now. **Loyalty cannot be derived** — it depends on
  rep, level and spend, none of which are on disk — so it is set by hand.

---

## Deferred, with reasons

- **Inventory OCR of the stash.** A different discipline — grid detection and icon matching — that
  needs tuning against real screenshots, and would ship behind a review-before-apply screen even
  then. Manual counts are reliable; this is an accelerator, not a foundation.
- **Live squad position sharing.** Needs hosting and NAT traversal. Plan merging delivers the
  coordination benefit with no infrastructure.
- **The Lab calibration.** Keycard-gated; needs a screenshot from inside.
- **ratnav.dev.** GitHub Pages site, `ratnav.app` redirect via CloudFront.
- **the tester's design pass.**
