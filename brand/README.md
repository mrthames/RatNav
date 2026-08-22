# The RatNav mark

A navigation arrow and a rat, in one shape.

An arrow leads with a narrow point and widens behind it. So does a rat, seen from above — the snout
leads and the skull widens to the ears. Putting the ears on the arrow's flanks is the whole idea.
The tail is the only thing added, and it is added last, so it can be taken off again when the mark
gets small.

![The RatNav mark](ratnav-icon-512.png)

## What is here

| File | What it is for |
|---|---|
| `ratnav-mark.svg` | The mark on its own, ink in `currentColor`. Use this wherever the surface already has a background. |
| `ratnav-mark-small.svg` | The same mark with the tail dropped. Use below ~32 px: favicons, tray icons, inline glyphs. |
| `ratnav-icon.svg` | The badge — mark on RatNav's ground, in a rounded square. Use for apps, stores and listings. |
| `ratnav.ico` | Seven sizes, 16 – 256, for the Windows executable and installer. Generated. |
| `ratnav-icon-512.png` | The badge as a raster, for READMEs and anywhere SVG is not accepted. Generated. |
| `ratnav-mark-512.png` | The bare mark as a raster, transparent background. Generated. |
| `render.ps1` | Rebuilds everything marked *generated* above. |

## Rebuilding

```powershell
powershell -File brand/render.ps1
```

It renders each size separately rather than scaling one image down, and **drops the tail below
32 px** — at that size the tail is a smudge that costs the arrow its point. That is ordinary icon
craft rather than a compromise: an icon should be redrawn for the size it is shown at.

The path coordinates live in both `ratnav-mark.svg` and `render.ps1`. That is a deliberate
duplication — WPF's geometry parser reads the same syntax SVG does, so the shapes stay identical,
and reading them out of the SVG at build time would mean shipping an SVG parser to draw a tray
icon. **If you change one, change the other**; they are three path strings and two circles.

## Using it

- **Ink** is `#8ec8ff`, the same accent the app and the overlay use. **Ground** is `#0b0f13`.
- The mark is monochrome by design. It works in white on dark, in the accent on dark, and in near
  black on light. Do not add a gradient — it has to survive being 16 px in a system tray.
- Give it room. The badge's own padding is the minimum; when placing the bare mark, leave clear
  space of at least the width of one ear on every side.
- The corner radius on the badge is 22% of the side, which is within a hair of what iOS, Windows 11
  and Android each round to — one badge does not look wrong on any of them.
- It is meant to be reused: the repo, the executable, the installer, the favicon, and the site all
  draw from these files rather than keeping their own copy.

## License

The mark is part of RatNav and carries the same license as the rest of the repository. It is
RatNav's identity rather than a free asset — please do not use it for anything else.
