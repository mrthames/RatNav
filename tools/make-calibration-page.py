#!/usr/bin/env python3
"""
Builds a one-click calibration page for one or more maps.

Calibration needs exactly one thing a computer cannot supply: a world position matched to a spot
on the map image. Aspect ratio settles which world axis runs across the image, and published
extract positions usually settle the signs — but where extracts sit comfortably inside the map,
flipping an axis mirrors the layout without pushing anything off the edge, and no data
distinguishes that from the truth. A person pointing at where they stood does.

So the page shows the map and asks for one click. No candidate pins: showing four guesses invites
picking the nearest rather than the right one. The click is scored against every possible mapping,
and the wrong answers miss by half a map rather than a few percent, so a slipped click is harmless.

    python tools/make-calibration-page.py \\
        woods -484.01 -504.17 "the Bridge / South V-Ex extract" \\
        lighthouse 114.47 -977.70 "the Northern Checkpoint extract"

Arguments come in groups of four: map key, world X, world Z, and what to look for.
"""

import json
import os
import re
import sys
import urllib.request

TARKOVDATA = "https://raw.githubusercontent.com/TarkovTracker/tarkovdata/master/"

HEAD = """<title>RatNav Calibration</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Archivo:wght@500;700&family=IBM+Plex+Mono:wght@400;500&family=IBM+Plex+Sans:wght@400;500&display=swap">

<style>
:root {
  --ground:#0b0f13; --panel:#141b21; --panel-hi:#1b242c; --line:#24313b; --line-soft:#1a242c;
  --ink:#c9d6df; --muted:#7b8c9b; --accent:#8ec8ff; --you:#5ce6a0;
  --s0:.8125rem; --s1:.9375rem;
}
*{box-sizing:border-box}
body{margin:0;background:var(--ground);color:var(--ink);font:var(--s1)/1.6 "IBM Plex Sans",system-ui,sans-serif;-webkit-font-smoothing:antialiased}
.wrap{max-width:1180px;margin:0 auto;padding:clamp(1.5rem,4vw,2.5rem) clamp(1rem,3vw,2rem) 4rem;display:flex;flex-direction:column;gap:1.5rem}
.eyebrow{margin:0;font:500 var(--s0)/1 "IBM Plex Mono",monospace;letter-spacing:.14em;text-transform:uppercase;color:var(--muted);display:flex;align-items:center;gap:.75rem}
.eyebrow::after{content:"";flex:1;height:1px;background:var(--line)}
h1{margin:.5rem 0 0;font:700 clamp(2rem,5vw,2.75rem)/1.05 Archivo,system-ui,sans-serif;letter-spacing:-.02em;text-wrap:balance}
.lede{margin:.5rem 0 0;max-width:68ch;color:var(--muted)}
.lede strong{color:var(--ink);font-weight:500}
.tabs{display:flex;gap:2px;flex-wrap:wrap}
.tabs button{font:500 var(--s0)/1 "IBM Plex Sans",sans-serif;color:var(--muted);background:var(--panel-hi);border:1px solid transparent;border-radius:2px;padding:.55rem .9rem;cursor:pointer}
.tabs button:hover{color:var(--ink)}
.tabs button[aria-pressed="true"]{color:var(--ground);background:var(--accent);border-color:var(--accent)}
.tabs button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
section[hidden]{display:none}
.viewer{position:relative;border:1px solid var(--line);background:#0e1317;overflow:hidden;cursor:crosshair;touch-action:none}
.pan{position:absolute;inset:0;transform-origin:0 0;will-change:transform}
.pan svg{width:100%;height:100%;display:block}
.trees{fill:#144043}.cement{fill:#c6c2c2}.land{fill:#1f5054}.rock{fill:#dcd5b6}.water{fill:#4a6b96}
.wood{fill:#593700}.tarmac{fill:#768089}.gravel{fill:#946d3e}.misc{fill:gray}.building{fill:#1a2632}
.floor{fill:#70777f}.locked{fill:#37414c}.stairs{fill:#ffd700}.task{fill:#000}
.map_border{fill:none;stroke:#000;stroke-width:2}.wall{fill:none;stroke:#000;stroke-width:.2}
.fence{fill:none;stroke:#c4e3c3;stroke-width:1}.road_tarmac{fill:none;stroke:#888}
.road_gravel{fill:none;stroke:#946d3e}.railroad{fill:none;stroke:#914833;stroke-dasharray:6;stroke-width:3}
.powerline{fill:none;stroke:#ffce00;stroke-width:2;stroke-dasharray:6,6}
.danger,.danger_small{fill:red;fill-opacity:.4;stroke:red;stroke-dasharray:4,2;stroke-width:2}
.shadow{filter:none}
.marker{position:absolute;transform:translate(-50%,-50%);pointer-events:none;display:grid;place-items:center}
.marker .dot{grid-area:1/1;width:14px;height:14px;border-radius:50%;background:var(--you);box-shadow:0 0 0 3px rgba(11,15,19,.95),0 0 16px 5px color-mix(in srgb,var(--you) 70%,transparent)}
.marker .ring{grid-area:1/1;width:52px;height:52px;border-radius:50%;border:2px solid color-mix(in srgb,var(--you) 60%,transparent)}
.marker .tag{grid-area:1/1;transform:translate(0,-44px);font:500 11px/1 "IBM Plex Mono",monospace;letter-spacing:.08em;text-transform:uppercase;white-space:nowrap;color:var(--ground);background:var(--you);padding:3px 7px;border-radius:2px}
.console{border:1px solid var(--line);border-top:none;background:var(--panel);display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr))}
.cell{padding:1rem 1.15rem;display:flex;flex-direction:column;gap:.5rem;border-right:1px solid var(--line-soft)}
.cell:last-child{border-right:none}
.label{font:500 .6875rem/1 "IBM Plex Mono",monospace;letter-spacing:.12em;text-transform:uppercase;color:var(--muted)}
.readout{font:var(--s0)/1.75 "IBM Plex Mono",monospace;font-variant-numeric:tabular-nums}
.readout .k{color:var(--muted)}
.readout .empty{color:var(--muted);font-style:italic}
.answer{color:var(--you);font-weight:500}
.reset{font:500 var(--s0)/1 "IBM Plex Sans",sans-serif;color:var(--muted);background:var(--panel-hi);border:1px solid var(--line);border-radius:2px;padding:.5rem .7rem;cursor:pointer;align-self:flex-start}
.reset:hover{color:var(--ink);border-color:var(--muted)}
.reset:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.note{margin:0;max-width:74ch;font-size:var(--s0);color:var(--muted)}
.note strong{color:var(--ink);font-weight:500}
@media (prefers-reduced-motion:reduce){*{transition:none!important}}
</style>
"""

SECTION = """
<section data-map="{key}" data-x1="{x1}" data-y1="{y1}" data-x2="{x2}" data-y2="{y2}"
         data-wx="{wx}" data-wz="{wz}" {hidden}>
  <p class="lede" style="margin-bottom:1rem">
    <strong>Find {place} and click it.</strong>
    Scroll to zoom, drag to pan. The game recorded you at <code>{x}, {z}</code>.
  </p>

  <div class="viewer" style="aspect-ratio:{w} / {h}">
    <div class="pan">
      {svg}
      <div class="marker" hidden>
        <div class="ring"></div><div class="dot"></div><div class="tag">{short}</div>
      </div>
    </div>
  </div>

  <div class="console">
    <div class="cell">
      <span class="label">Where the game put you</span>
      <div class="readout"><span class="k">world</span> {x}, {z}</div>
    </div>
    <div class="cell">
      <span class="label">Where you clicked</span>
      <div class="readout" data-role="clicked"><span class="empty">click the map&hellip;</span></div>
    </div>
    <div class="cell">
      <span class="label">Best fit</span>
      <div class="readout" data-role="answer"><span class="empty">&mdash;</span></div>
    </div>
    <div class="cell">
      <span class="label">View</span>
      <button type="button" class="reset">Reset zoom</button>
    </div>
  </div>
</section>
"""

SCRIPT = """
<script>
// Every way the world axes can lie on an image. One click picks between them.
const MAPPINGS = [
  ["( x, z)", (x, z) => [x, z]],   ["(-x, z)", (x, z) => [-x, z]],
  ["( x,-z)", (x, z) => [x, -z]],  ["(-x,-z)", (x, z) => [-x, -z]],
  ["( z, x)", (x, z) => [z, x]],   ["(-z, x)", (x, z) => [-z, x]],
  ["( z,-x)", (x, z) => [z, -x]],  ["(-z,-x)", (x, z) => [-z, -x]],
];

for (const section of document.querySelectorAll("section[data-map]")) {
  const viewer = section.querySelector(".viewer");
  const pan = section.querySelector(".pan");
  const svg = pan.querySelector("svg");
  const marker = section.querySelector(".marker");
  const clicked = section.querySelector('[data-role="clicked"]');
  const answer = section.querySelector('[data-role="answer"]');

  const n = (name) => Number(section.dataset[name]);
  const X1 = n("x1"), Y1 = n("y1"), X2 = n("x2"), Y2 = n("y2"), WX = n("wx"), WZ = n("wz");

  let scale = 1, tx = 0, ty = 0, dragging = false, moved = false, lastX = 0, lastY = 0;
  const apply = () => pan.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;

  const clamp = () => {
    const r = viewer.getBoundingClientRect();
    tx = Math.min(0, Math.max(r.width - r.width * scale, tx));
    ty = Math.min(0, Math.max(r.height - r.height * scale, ty));
  };

  viewer.addEventListener("wheel", (e) => {
    e.preventDefault();
    const r = viewer.getBoundingClientRect();
    const px = e.clientX - r.left, py = e.clientY - r.top;
    const next = Math.min(16, Math.max(1, scale * (e.deltaY < 0 ? 1.18 : 1 / 1.18)));
    tx = px - (px - tx) * (next / scale);
    ty = py - (py - ty) * (next / scale);
    scale = next; clamp(); apply();
  }, { passive: false });

  viewer.addEventListener("pointerdown", (e) => {
    dragging = true; moved = false; lastX = e.clientX; lastY = e.clientY;
    // Some pointer types refuse capture, and throwing here would swallow the click.
    try { viewer.setPointerCapture(e.pointerId); } catch {}
  });

  viewer.addEventListener("pointermove", (e) => {
    if (!dragging) return;
    const dx = e.clientX - lastX, dy = e.clientY - lastY;
    if (Math.abs(dx) + Math.abs(dy) > 3) moved = true;
    tx += dx; ty += dy; lastX = e.clientX; lastY = e.clientY;
    clamp(); apply();
  });

  viewer.addEventListener("pointerup", (e) => {
    dragging = false;
    try { viewer.releasePointerCapture(e.pointerId); } catch {}
    if (moved) return;

    const r = svg.getBoundingClientRect();
    const u = (e.clientX - r.left) / r.width;
    const v = (e.clientY - r.top) / r.height;
    if (u < 0 || u > 1 || v < 0 || v > 1) return;

    marker.style.left = (u * 100) + "%";
    marker.style.top = (v * 100) + "%";
    marker.hidden = false;
    clicked.innerHTML = `<span class="k">image</span> ${(u * 100).toFixed(1)}%, ${(v * 100).toFixed(1)}%`;

    const scored = MAPPINGS
      .map(([label, f]) => {
        const [a, b] = f(WX, WZ);
        const mu = (a - X1) / (X2 - X1), mv = (b - Y1) / (Y2 - Y1);
        return { label, err: Math.hypot(mu - u, mv - v), mu, mv };
      })
      .sort((p, q) => p.err - q.err);

    const best = scored[0], next = scored[1];
    answer.innerHTML =
      `<span class="answer">${section.dataset.map} ${best.label}</span> off by ${(best.err * 100).toFixed(1)}pp<br>` +
      `<span class="k">lands</span> ${(best.mu * 100).toFixed(1)}%, ${(best.mv * 100).toFixed(1)}%<br>` +
      `<span class="k">next</span> ${next.label} at ${(next.err * 100).toFixed(1)}pp`;
  });

  section.querySelector(".reset").addEventListener("click", () => { scale = 1; tx = 0; ty = 0; apply(); });
  apply();
}

const tabs = document.getElementById("tabs");
tabs.addEventListener("click", (event) => {
  const button = event.target.closest("button");
  if (!button) return;
  for (const b of tabs.querySelectorAll("button")) b.setAttribute("aria-pressed", String(b === button));
  for (const s of document.querySelectorAll("section[data-map]")) s.hidden = s.dataset.map !== button.dataset.map;
});
</script>
"""


def fetch(url: str, path: str) -> str:
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path)
    return open(path, encoding="utf-8", errors="replace").read()


def build_section(maps, key, wx, wz, place, out_dir, first):
    svg_meta = maps[key]["svg"]
    bounds = svg_meta["bounds"]

    markup = fetch(TARKOVDATA + "maps/" + svg_meta["file"], os.path.join(out_dir, svg_meta["file"]))

    # The map's own stylesheet goes, so the page's palette applies. Geometry is untouched.
    markup = re.sub(r"<style\b[^>]*>.*?</style>", "", markup, flags=re.S | re.I)

    box = re.search(r'viewBox="\S+\s+\S+\s+([\d.\-eE]+)\s+([\d.\-eE]+)"', markup)
    width, height = (float(box.group(1)), float(box.group(2))) if box else (1000.0, 1000.0)

    markup = re.sub(
        r"<svg\b[^>]*>",
        f'<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" '
        f'viewBox="0 0 {width} {height}" preserveAspectRatio="xMidYMid meet">',
        markup, count=1, flags=re.I)

    return SECTION.format(
        key=key, svg=markup, w=width, h=height, place=place,
        short=place.replace("the ", "").replace(" extract", ""),
        x1=bounds[0][0], y1=bounds[0][1], x2=bounds[1][0], y2=bounds[1][1],
        wx=wx, wz=wz, x=f"{wx:.2f}", z=f"{wz:.2f}",
        hidden="" if first else "hidden")


def main() -> int:
    args = sys.argv[1:]
    if not args or len(args) % 4 != 0:
        print(__doc__)
        return 1

    out_dir = os.environ.get("RATNAV_OUT", ".")
    maps = json.loads(fetch(TARKOVDATA + "maps.json", os.path.join(out_dir, "maps.json")))

    specs = [args[i:i + 4] for i in range(0, len(args), 4)]
    for key, *_ in specs:
        if key not in maps:
            print(f"No map called {key!r}. Known: {', '.join(sorted(maps))}")
            return 1

    tabs = "".join(
        f'<button type="button" data-map="{key}"{" aria-pressed=\"true\"" if i == 0 else ""}>'
        f'{(maps[key].get("locale") or {}).get("en", key)}</button>'
        for i, (key, *_) in enumerate(specs))

    sections = "".join(
        build_section(maps, key, float(x), float(z), place, out_dir, i == 0)
        for i, (key, x, z, place) in enumerate(specs))

    page = HEAD + f"""
<div class="wrap">
  <header>
    <p class="eyebrow">RatNav &middot; calibration</p>
    <h1>One click per map</h1>
    <p class="lede">
      Aspect ratio settles which way round each map's axes go, and extract positions usually settle
      the signs &mdash; but where extracts sit well inside the map, a mirrored layout keeps them all
      on the image and looks identical. <strong>Only a person pointing at where they stood can tell
      the difference.</strong>
    </p>
  </header>

  <div class="tabs" id="tabs">{tabs}</div>
  {sections}

  <p class="note">
    <strong>Send me the "best fit" line</strong> and the map is settled for good &mdash; the answer
    ships in the repo so nobody has to do it again. A slipped click is harmless: the wrong answers
    miss by half a map, not a few percent.
  </p>
</div>
""" + SCRIPT

    out = os.path.join(out_dir, "calibrate.html")
    with open(out, "w", encoding="utf-8") as handle:
        handle.write(page)

    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
