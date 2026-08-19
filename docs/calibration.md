# Map calibration

How RatNav turns Escape from Tarkov's world coordinates into a position on a map image.

## The answer

Take the map's `bounds` from tarkov.dev's own map metadata and map `x` and `z` straight through:

```
u = (x − bounds[0][0]) / (bounds[1][0] − bounds[0][0])
v = (z − bounds[0][1]) / (bounds[1][1] − bounds[0][1])
```

No rotation. No axis swapping. On every map but one, `coordinateRotation` is not applied to
anything.

Headings are derived from that same mapping — project a short step and measure its angle on the
image — so a facing cone cannot disagree with the pins it sits among.

## How much work it took to arrive at "no rotation"

A great deal, and almost all of it was spent compensating for a bad data source rather than
understanding the problem. The record is worth keeping, because the failure mode repeated three
times: **a rule fitted the evidence available, and the evidence was wrong.**

The original source was [TarkovTracker/tarkovdata](https://github.com/TarkovTracker/tarkovdata),
which is the obvious first hit and turned out to be stale and internally inconsistent:

| map | what tarkovdata got wrong | how it showed up |
|---|---|---|
| Reserve | vertical bounds ~140 m too tall | positions ~75 m out; no mapping fit better than 12.7pp |
| Factory | corners listed in the opposite order from every other map | Factory looked like it needed swapped axes |
| Interchange | a stale map, missing an area the game has had for a while, drawn on a **square** canvas | the aspect-ratio test had nothing to say, by construction |
| The Lab | bounds that put all 7 of its extracts off the image | looked like a rotation problem |

Three successive "rules" were derived from that data and each fit what was in front of it:

1. **`coordinateRotation` never applies to positions.** Fit Customs exactly. Customs declares 180°,
   where applying the correct rotation and applying nothing are the same thing.
2. **Apply `180 − coordinateRotation`.** Fit Customs and Factory. Both happened to agree with their
   declared rotations by coincidence.
3. **Orientation is per-map and not derivable.** True of the bad data, and the reason for the
   solver below — but not true of the maps.

The correction came from switching to **tarkov.dev's own map metadata**
(`src/data/maps.json` in the tarkov-dev repo, images from `assets.tarkov.dev`), which is what
their site draws with. Against those numbers all ten maps resolve to a plain `(x, z)` mapping,
agreed independently by the aspect-ratio check on every single one, to within 1.4%.

**The lesson worth carrying:** when a rule needs a new exception for every case, suspect the
inputs before inventing a fourth rule.

## The solver

Calibration is still solved per map rather than assumed, because a new map may arrive with data
nobody has checked. Two independent signals:

- **Aspect ratio** decides whether the axes are swapped. The world span on the image's width and
  the span on its height must share a metres-per-pixel scale. Decisive on most maps; useless on a
  square image.
- **Extract positions** decide the signs. Published coordinates have to land on the map. Decisive
  where extracts hug the edges; useless where they sit comfortably inside, because flipping an axis
  mirrors the layout without pushing anything off.

Where both are weak the map is reported `Weak` and says why, rather than asserting a pin. One
position marked in game settles any map outright, and `CalibrationSolver.VerifiedMappings` exists
to hold such answers — currently empty, because nothing needs overriding.

`tools/make-calibration-page.py` builds a one-click page for marking a position on any map.

## What the map metadata also carries

Switching source brought more than correct bounds:

- **Floors with height bands.** Interchange's second floor is world height 25–34, its third 34+.
  A screenshot filename carries the player's elevation, so the right level can be selected without
  anyone touching a control mid-raid.
- **303 named places** across the maps — "Big Red", "Dorms", "New Gas", "Resort", "Sawmill",
  "Goshan". Exactly the vocabulary players use, so a route can name its stops.
- **Attribution.** Each map credits its author; these are community mapmakers' work.

Room-level labels (Dorms 114 and so on) are still absent — the named landmarks are the more useful
half, and there are no `<text>` elements in any map SVG to mine.

## Headings

Two screenshots four seconds apart, standing still and turning 90° right. Position moved 0.25 m,
so this is one spot and two facings — the only arrangement that separates a correct sign from an
inverted one.

```
before   182.67°
after    274.77°
change   +92.10°
```

A compass reading that rises when you turn right is correct.
