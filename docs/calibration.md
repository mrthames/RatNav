# Map calibration

How RatNav turns Escape from Tarkov's world coordinates into a position on a map image, how that
was worked out, and what is still unproven.

This exists because the answer is not obvious, two plausible readings of the data are still alive,
and getting it wrong produces software that looks like it works. Every route, pin, and bearing in
RatNav rides on this.

## The rule

Given a map's `bounds` and `coordinateRotation` from
[TarkovTracker/tarkovdata](https://github.com/TarkovTracker/tarkovdata/blob/master/maps.json):

```
rotate (x, z) by (180° − coordinateRotation)
then normalize through bounds:
    u = (x' − bounds[0][0]) / (bounds[1][0] − bounds[0][0])
    v = (z' − bounds[0][1]) / (bounds[1][1] − bounds[0][1])
```

Headings go the other way:

```
image heading = world heading − coordinateRotation
```

Bounds are corner pairs, not min/max. A map whose image runs opposite to the world axis has its
first bound larger than its second, and the subtraction handles that on its own — sorting them
into min/max would mirror the map.

## How it was established

Escape from Tarkov writes the player's world position into the filename of every screenshot. So
the measurement is: take a screenshot at a spot you can identify, then point at that spot on the
map image. Position on the image and position in the world, for the same instant.

### Customs — `coordinateRotation: 180`

Screenshot at world `(-14.44, -139.32)`. Player marked the spot at **66.7%, 30.9%**.

| candidate | result | |
|---|---|---|
| raw `x, z` through bounds | 66.6%, 30.8% | **matches** |
| rotate 180° about origin | 63.9%, 82.0% | |
| rotate 180° about bounds centre | 33.4%, 69.2% | |
| axis swap | 78.3%, 53.8% | |

A one-pixel agreement on a 1062-pixel map.

**This map alone is misleading.** At 180°, "apply the correct rotation" and "apply no rotation"
produce identical pins, so the first conclusion drawn here — that `coordinateRotation` never
applies to positions — fit the evidence perfectly and was still wrong.

### Factory — `coordinateRotation: 90`

Two screenshots, both marked by the player:

| | world | marked |
|---|---|---|
| turn spot | `44.16, 39.67` | 21.2%, 23.3% |
| extract | `58.66, 66.15` | 4.1%, 12.7% |

Neither point alone settles it: the player was standing where `x` and `z` are nearly equal, which
is the one region of the map where swapping the axes barely moves the pin. Two candidates survived
within click error — `(-x, z)` at 5.5pp and `(-z, x)` at 5.3pp.

The **direction between the two marks** decided it, because a click can slip a few percent but not
thirty degrees:

| | predicted bearing |
|---|---|
| `(-x, z)` | 115° |
| `(-z, x)` | **148°** |
| measured | **146°** |

`(-z, x)` is a clean +90° rotation, which with Customs' 0° gives the rule above.

Residuals on Factory are a consistent few percent in the same direction on both points, which
reads as click bias rather than calibration error. Worth revisiting if a map ever looks
systematically shifted, but not worth fitting to two samples.

### Headings

Two screenshots four seconds apart, player standing still and turning 90° right. Position moved
0.25 m, so this is genuinely one spot and two facings — the only arrangement that can separate a
correct sign from an inverted one.

```
before   182.67°
after    274.77°
change   +92.10°
```

A compass reading that rises when you turn right is correct. The image-space direction was
measured separately, from how a walk between two known points appears on the image: 180° from its
world bearing on Customs, 90° the other way on Factory. Hence *minus* the rotation.

## What is still unproven

Two rules fit every measurement so far, and they agree everywhere except one value:

| declared rotation | `180 − r` | `\|180 − r\|` | |
|---|---|---|---|
| 180 | 0 | 0 | agree |
| 90 | +90 | +90 | agree |
| **270** | **−90** | **+90** | **disagree** |

**The Lab is the only 270° map in the game.** It is therefore both the only place the remaining
ambiguity can appear and the only place it can be resolved. Every other map declares 180°, which
Customs covers, and Factory covers 90°.

RatNav ships the `180 − r` form, so it predicts **−90°**, i.e. `(z, −x)`, for The Lab.
`MapImage.CalibrationVerified` returns false for 270° maps so the UI can say a pin might be wrong
rather than quietly asserting it.

### How to settle it

The Lab requires a keycard, so this is blocked on access rather than effort. When you can get in:

1. Bind a screenshot key in **Settings → Controls → Screenshot**.
2. Take a screenshot somewhere recognisable, then walk a good distance and take another. Distance
   matters more than precision — the bearing between two marks is what decides, and it wants a
   long baseline. Avoid standing where `x ≈ z`; that is what made Factory ambiguous.
3. Mark both spots on the map image.
4. If the pins land where you marked, `180 − r` is confirmed for all ten maps. If they land
   mirrored across the diagonal, the rule is `|180 − r|` and only The Lab changes.

## Floors

Seven of ten maps are multi-level. Streets has seven levels.

**`maps.json`'s declared floor list does not match what the SVG files contain**, so floors are read
from the drawing's top-level group ids and `maps.json` is used only for the default floor. Two
examples of the mismatch:

| map | maps.json says | the SVG actually has |
|---|---|---|
| Factory | Basement, Ground_Floor, First_Floor, Second_Floor | Basement, Ground_Floor, Second_Floor, Third_Floor |
| Customs | Ground_Level | Ground_Level, Underground_Level, First_Floor, Second_Floor, Third_Floor |

Customs is the one that matters: trusting the manifest would hide every building interior on the
map, including the Dorms upper floors, which are drawn and present in the file.

Screenshot filenames carry height as well as position, so floor selection can eventually be
automatic — two Factory screenshots one storey apart differed by 4.08 m. Pinning the height band
for each floor needs several samples per map, so for now the floor is a manual choice.

**Room numbers are not in the data.** There are zero `<text>` elements in any map SVG. The geometry
of Dorms' rooms exists; the labels "114", "214" and so on do not, and would need a separate source
or hand annotation.
