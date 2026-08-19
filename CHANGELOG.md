# Changelog

Notable changes to RatNav. Versions follow [semantic versioning](https://semver.org).

## Unreleased

First working version.

### Plan a raid

- Pick a map and see the objectives of your active quests grouped by the place players call it —
  Depot, Dorms, Old Construction — with the quest and trader for each.
- Tick what you are pushing and get a route ordered for you, plus the keys to bring. Keys are
  shown before you queue, because they are the one thing you cannot fix once the raid starts.

### Navigate it

- A hotkey-toggled overlay over the game, showing the route, your position, and the distance and
  bearing to your next stop — "Dorms · 140 m · 30° right".
- Position comes from the coordinates Escape from Tarkov writes into screenshot filenames. Tap
  your in-game screenshot key and the marker snaps, the route re-orders from where you actually
  are, and the screenshot is archived so the folder never fills up.
- Nothing animates between fixes, and the overlay says how old your last one is rather than
  implying the marker is current.

### Track items

- What every active quest and un-built hideout module needs, minus what you have.
- Have-counts entered by hand, because the game puts stash contents in no file on disk and
  guessing would be worse than asking.
- A watchlist for anything else worth remembering, with notes and targets.

### Share and merge plans

- Export a plan as a `.ratnav` file and merge it with a friend's. Nothing is dropped: every
  objective survives carrying its owner, and one you both picked becomes a single shared stop.
- Merging flags what changes how you run the raid — objectives to do together, items you are both
  hunting, and keys only one of you needs to carry.

### Honest about what it does not know

- Maps whose calibration could not be established say so instead of showing a pin that might be
  75 metres out.
- Quest progress read from the game's logs sits *under* anything you correct by hand, so a later
  replay can never undo a correction.
- When tarkov.dev is unavailable, the last good data keeps being served and the app says it is
  stale rather than emptying.
