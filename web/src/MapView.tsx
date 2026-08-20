import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { QuestBrief } from './QuestBrief'
import {
  api,
  type CustomWaypoint,
  type ExtractPin,
  type InkLevel,
  type MapSummary,
  type ObjectivePin,
  type PlaceLabel,
} from './api'

/**
 * Which extracts to draw. Nothing the game writes to disk says which side you queued as, so this
 * is a choice rather than a guess — and shared extracts show under either, because they work
 * whichever you are.
 */
type Faction = 'pmc' | 'scav' | 'off'

/** Human labels for the group ids the map SVGs use for their levels. */
const FLOOR_LABELS: Record<string, string> = {
  Ground_Level: 'Ground',
  Ground_Floor: 'Ground',
  Underground_Level: 'Underground',
  Technical_Level: 'Technical',
  Basement: 'Basement',
  Bunkers: 'Bunkers',
  First_Floor: '1st',
  Second_Floor: '2nd',
  Third_Floor: '3rd',
  Fourth_Floor: '4th',
  Fifth_Floor: '5th',
  First_Level: '1st',
  Second_Level: '2nd',
}

/** Bottom-to-top, so the floor buttons read like a building rather than like a hash order. */
const FLOOR_ORDER = [
  'Basement', 'Bunkers', 'Underground_Level', 'Technical_Level',
  'Ground_Level', 'Ground_Floor',
  'First_Floor', 'First_Level', 'Second_Floor', 'Second_Level',
  'Third_Floor', 'Fourth_Floor', 'Fifth_Floor',
]

export function MapView({ map }: { map: MapSummary }) {
  const [markup, setMarkup] = useState<string | null>(null)
  const [pins, setPins] = useState<ObjectivePin[]>([])
  const [ink, setInk] = useState<InkLevel>('graphical')
  const [floors, setFloors] = useState<string[]>([])
  const [floor, setFloor] = useState<string | null>(null)
  const [ghost, setGhost] = useState(true)
  const [extracts, setExtracts] = useState<ExtractPin[]>([])
  const [faction, setFaction] = useState<Faction>('pmc')

  /**
   * Marks of your own, and whether a click on the map places one.
   *
   * <p>Placing is a mode rather than the default, because the same click also has to be able to do
   * nothing — a map you cannot look at without marking it is a map you stop looking at.</p>
   */
  const [marks, setMarks] = useState<CustomWaypoint[]>([])
  const [placing, setPlacing] = useState<'Place' | 'Item' | null>(null)

  /**
   * Finding somewhere by name.
   *
   * <p>Streets knows 46 named places and nothing searched them, so the only way to find Pinewood
   * was to already know where it was — which is the thing you came here not knowing.</p>
   */
  const [places, setPlaces] = useState<PlaceLabel[]>([])
  const [search, setSearch] = useState('')

  /** Names drawn on the map, the way the overlay draws them. */
  const [names, setNames] = useState(true)

  /** The waypoint whose quest is open, if any. */
  const [reading, setReading] = useState<ObjectivePin | null>(null)

  useEffect(() => {
    setSearch('')
    api.places(map.id).then(setPlaces).catch(() => setPlaces([]))
  }, [map.id])

  const found = useMemo(() => {
    const needle = search.trim().toLowerCase()
    if (!needle) return []

    return places.filter((p) => p.text.toLowerCase().includes(needle)).slice(0, 8)
  }, [places, search])

  /** Puts a point in the middle of the frame at whatever zoom is set. */
  function centreOn(x: number, y: number) {
    const box = frame.current?.getBoundingClientRect()
    if (!box) return

    // Zoomed all the way out the whole map is already on screen, so centring would only push half
    // of it off the edge. A closer look is what "take me there" means.
    const next = Math.max(zoom, 2.5)

    setZoom(next)
    setPan({ x: box.width / 2 - x * box.width * next, y: box.height / 2 - y * box.height * next })
  }

  const loadMarks = useCallback(
    () => api.waypoints(map.id).then(setMarks).catch(() => setMarks([])), [map.id])

  useEffect(() => { void loadMarks() }, [loadMarks])

  /** Where on the map a click landed, as a fraction of its width and height. */
  async function place(e: React.MouseEvent<HTMLDivElement>) {
    if (!placing || !host.current) return

    const box = host.current.getBoundingClientRect()
    const x = (e.clientX - box.left) / box.width
    const y = (e.clientY - box.top) / box.height

    if (x < 0 || x > 1 || y < 0 || y > 1) return

    // Asked for straight away rather than placed and named later. An unnamed dot is a puzzle, and
    // "name it afterwards" is a step people skip.
    const label = window.prompt(
      placing === 'Item' ? 'What is here to pick up?' : 'What is here?')

    if (label === null) return

    await api.addWaypoint(map.id, label, x, y, floor, placing)
    await loadMarks()
    setPlacing(null)
  }

  async function forget(id: string) {
    await api.removeWaypoint(id)
    await loadMarks()
  }

  // Zoom and pan, to match the overlay. A map you can only see whole is not much use on Streets.
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragging = useRef<{ x: number; y: number } | null>(null)
  const host = useRef<HTMLDivElement>(null)
  const frame = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let cancelled = false
    setMarkup(null)

    fetch(api.imageUrl(map.id, ink))
      .then((r) => (r.ok ? r.text() : Promise.reject(new Error(`image ${r.status}`))))
      .then((text) => {
        if (cancelled) return
        setMarkup(text)

        // Floors are read from the drawing, never from maps.json — the manifest disagrees with
        // what the files contain, and on Customs it omits every building interior.
        const doc = new DOMParser().parseFromString(text, 'image/svg+xml')
        const ids = Array.from(doc.querySelectorAll('svg > g'))
          .map((g) => g.id)
          .filter((id) => id && FLOOR_ORDER.includes(id))
        const ordered = FLOOR_ORDER.filter((id) => ids.includes(id))

        setFloors(ordered)
        setFloor(ordered.find((f) => f.startsWith('Ground')) ?? ordered[0] ?? null)
      })
      .catch(() => !cancelled && setMarkup(''))

    return () => { cancelled = true }
  }, [map.id, ink])

  useEffect(() => {
    api.objectives(map.id).then(setPins).catch(() => setPins([]))
    api.extracts(map.id).then(setExtracts).catch(() => setExtracts([]))
  }, [map.id])

  // Applying the active floor by class rather than re-rendering the SVG: these files run to
  // 300 KB and re-parsing one on every floor click would be visible.
  useEffect(() => {
    const svg = host.current?.querySelector('svg')
    if (!svg) return
    svg.classList.add('map-svg')
    svg.setAttribute('data-ghost', ghost ? 'on' : 'off')
    for (const group of Array.from(svg.children)) {
      group.classList.toggle('floor-active', group.id === floor)
    }
  }, [markup, floor, ghost])

  const positioned = useMemo(() => pins.filter((p) => p.x >= 0 && p.x <= 1), [pins])

  const exits = useMemo(
    () =>
      faction === 'off'
        ? []
        : extracts.filter((e) => e.faction === 'shared' || e.faction === faction),
    [extracts, faction],
  )

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Segment
          label="Ink"
          options={[
            ['graphical', 'Graphical'],
            ['full', 'Full'],
            ['structure', 'Structure'],
            ['outline', 'Outline'],
          ]}
          value={ink}
          onChange={(v) => setInk(v as InkLevel)}
        />

        {floors.length > 1 && (
          <>
            <Segment
              label="Floor"
              options={floors.map((f) => [f, FLOOR_LABELS[f] ?? f])}
              value={floor ?? ''}
              onChange={setFloor}
            />
            <label className="flex cursor-pointer items-center gap-2 text-xs text-muted hover:text-ink">
              <input
                type="checkbox"
                checked={ghost}
                onChange={(e) => setGhost(e.target.checked)}
                className="accent-accent"
              />
              Ghost others
            </label>
          </>
        )}

        {(zoom !== 1 || pan.x !== 0 || pan.y !== 0) && (
          <button
            type="button"
            onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }) }}
            className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                       hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
          >
            Reset view · {zoom.toFixed(1)}×
          </button>
        )}

        {extracts.length > 0 && (
          <Segment
            label="Exits"
            options={[['pmc', 'PMC'], ['scav', 'Scav'], ['off', 'Off']]}
            value={faction}
            onChange={(v) => setFaction(v as Faction)}
          />
        )}

        {places.length > 0 && (
          <label className="flex cursor-pointer items-center gap-2 text-xs text-muted hover:text-ink">
            <input
              type="checkbox"
              checked={names}
              onChange={(e) => setNames(e.target.checked)}
              className="accent-accent"
            />
            Place names
          </label>
        )}

        {places.length > 0 && (
          <div className="relative">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={`Find a place… (${places.length})`}
              className="w-44 rounded-sm border border-line bg-panel px-2.5 py-1.5 text-xs text-ink
                         placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
            />

            {found.length > 0 && (
              <ul className="absolute left-0 top-full z-10 mt-1 w-56 border border-line bg-panel shadow-xl">
                {found.map((place) => (
                  <li key={`${place.text}-${place.x}`}>
                    <button
                      type="button"
                      onClick={() => { centreOn(place.x, place.y); setSearch('') }}
                      className="w-full px-2.5 py-1.5 text-left text-xs text-muted transition-colors
                                 hover:bg-panel-hi hover:text-ink
                                 focus-visible:outline-2 focus-visible:outline-accent"
                    >
                      {place.text}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}

        {/* Two kinds, chosen before the click rather than corrected after it. */}
        <Segment
          label="Mark"
          options={[['off', 'Off'], ['Place', 'A place'], ['Item', 'An item']]}
          value={placing ?? 'off'}
          onChange={(v) => setPlacing(v === 'off' ? null : (v as 'Place' | 'Item'))}
        />

        {placing && <span className="text-xs text-mark">Click the map…</span>}

        {/* Straight onto the overlay, no plan required — for a look before you queue. */}
        <button
          type="button"
          onClick={() => void api.showMap(map.id)}
          className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          Show on overlay
        </button>


      </div>

      <div
        ref={frame}
        className="relative overflow-hidden border border-line bg-[#0e1317]"
        onWheel={(e) => {
          e.preventDefault()
          setZoom((z) => Math.min(8, Math.max(1, z * (e.deltaY < 0 ? 1.15 : 1 / 1.15))))
        }}
        // Right-drag to pan, same as the overlay. The context menu would otherwise open on
        // release and swallow the gesture.
        onContextMenu={(e) => e.preventDefault()}
        onPointerDown={(e) => {
          if (e.button !== 2) return
          dragging.current = { x: e.clientX, y: e.clientY }
          e.currentTarget.setPointerCapture(e.pointerId)
        }}
        onPointerMove={(e) => {
          const from = dragging.current
          if (!from) return

          setPan((p) => ({ x: p.x + e.clientX - from.x, y: p.y + e.clientY - from.y }))
          dragging.current = { x: e.clientX, y: e.clientY }
        }}
        onPointerUp={(e) => {
          dragging.current = null
          e.currentTarget.releasePointerCapture(e.pointerId)
        }}
        onClick={place}
        style={{ cursor: placing ? 'crosshair' : dragging.current ? 'grabbing' : 'default' }}
      >
        <div
          className="origin-top-left"
          style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})` }}
        >
        {markup === null && <Placeholder>loading map…</Placeholder>}
        {markup === '' && <Placeholder>this map has no image yet</Placeholder>}
        {markup && (
          <div
            ref={host}
            className="[&>svg]:block [&>svg]:h-auto [&>svg]:w-full"
            dangerouslySetInnerHTML={{ __html: markup }}
          />
        )}

        {/*
          The names players use for places, drawn where they are. Under the pins, because a pin is
          something to go to and a name is the ground it stands on.

          Held at a constant screen size against the zoom, so a name stays readable at 6× rather
          than growing into a banner across the map.
        */}
        {names && places.map((place) => (
          <span
            key={`${place.text}-${place.x}`}
            className="pointer-events-none absolute font-mono whitespace-nowrap text-white/85"
            style={{
              left: `${place.x * 100}%`,
              top: `${place.y * 100}%`,
              fontSize: `${11 / zoom}px`,
              transform: 'translate(-50%, -50%)',
              textShadow: '0 0 3px #0b0f13, 0 0 3px #0b0f13',
            }}
          >
            {place.text}
          </span>
        ))}

        {/*
          Extracts are diamonds and objectives are circles. The shapes carry the difference on
          their own, so the two are still tellable apart without relying on colour.
        */}
        {exits.map((exit) => {
          const colour = exit.faction === 'scav' ? 'var(--color-scav)' : 'var(--color-pmc)'

          return (
            <div
              key={`${exit.name}-${exit.x}-${exit.y}`}
              title={`${exit.name} · ${exit.faction} extract`}
              className="pointer-events-none absolute -translate-x-1/2 -translate-y-1/2"
              style={{ left: `${exit.x * 100}%`, top: `${exit.y * 100}%` }}
            >
              {/* The same door-with-an-arrow the overlay draws, so an exit looks like an exit
                  on whichever screen you are reading. */}
              <svg
                viewBox="-9 -10 18 20"
                style={{ width: `${16 / zoom}px`, height: `${18 / zoom}px`, display: 'block' }}
              >
                <path
                  d="M -6,-7 L -6,7 L 6,7 L 6,-7 Z M -2,0 L 4,0 M 1,-3 L 4,0 L 1,3"
                  fill="var(--color-ground)"
                  stroke={colour}
                  strokeWidth="1.8"
                />
              </svg>

              <span
                className="absolute left-1/2 -translate-x-1/2 whitespace-nowrap font-mono"
                style={{
                  color: colour,
                  fontSize: `${10 / zoom}px`,
                  top: `${11 / zoom}px`,
                  textShadow: '0 0 3px #0b0f13, 0 0 3px #0b0f13',
                }}
              >
                {exit.name}
              </span>
            </div>
          )
        })}

        {positioned.map((pin) => (
          <button
            type="button"
            key={pin.objectiveId}
            title={`${pin.taskName} — ${pin.description}`}
            onClick={(e) => { e.stopPropagation(); setReading(pin) }}
            className="absolute -translate-x-1/2 cursor-pointer
                       focus-visible:outline-2 focus-visible:outline-accent"
            style={{
              left: `${pin.x * 100}%`,
              top: `${pin.y * 100}%`,
              transform: `translate(-50%, -100%)`,
            }}
          >
            {/* The overlay's pin, pointing at the objective rather than sitting on top of it —
                which is what lets a bigger marker stay precise. */}
            <svg
              viewBox="-9 -22 18 24"
              style={{ width: `${18 / zoom}px`, height: `${24 / zoom}px`, display: 'block' }}
            >
              <path
                d="M 0,0 C -3,-5 -7,-8 -7,-12 A 7,7 0 1 1 7,-12 C 7,-8 3,-5 0,0 Z"
                fill="var(--color-need)"
                stroke="var(--color-ground)"
                strokeWidth="1.5"
              />
            </svg>
          </button>
        ))}

        {/*
          Your own marks, in their own colour and their own shape — the same pairing the overlay
          uses, so a mark looks like a mark on whichever screen you are reading.
        */}
        {marks.map((mark) => (
          <div
            key={mark.id}
            title={mark.kind === 'Item' ? `${mark.label} · pick up` : `${mark.label} · your mark`}
            className="pointer-events-none absolute -translate-x-1/2 -translate-y-1/2"
            style={{ left: `${mark.x * 100}%`, top: `${mark.y * 100}%` }}
          >
            <svg
              viewBox="-9 -9 18 18"
              style={{ width: `${16 / zoom}px`, height: `${16 / zoom}px`, display: 'block' }}
            >
              {/* A diamond is a place; a box is something to pick up when you get there. */}
              <path
                d={mark.kind === 'Item'
                  ? 'M -6,-6 L 6,-6 L 6,6 L -6,6 Z'
                  : 'M 0,-7 L 7,0 L 0,7 L -7,0 Z'}
                fill="var(--color-ground)"
                stroke="var(--color-mark)"
                strokeWidth="1.8"
              />
            </svg>

            <span
              className="absolute left-1/2 -translate-x-1/2 whitespace-nowrap font-mono text-mark"
              style={{
                fontSize: `${10 / zoom}px`,
                top: `${10 / zoom}px`,
                textShadow: '0 0 3px #0b0f13, 0 0 3px #0b0f13',
              }}
            >
              {mark.label}
            </span>
          </div>
        ))}
        </div>
      </div>

      {reading && (
        <QuestBrief
          taskId={reading.taskId}
          objectiveId={reading.objectiveId}
          onClose={() => setReading(null)}
        />
      )}

      {marks.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {marks.map((mark) => (
            <li
              key={mark.id}
              className="flex items-center gap-2 rounded-sm border border-mark/40 bg-mark/5 px-2 py-1"
            >
              <span className="text-xs">{mark.label}</span>

              <button
                type="button"
                onClick={() => void forget(mark.id)}
                aria-label={`Forget ${mark.label}`}
                className="font-mono text-[11px] text-muted hover:text-need
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}

      <p className="font-mono text-xs text-muted">
        {positioned.length > 0
          ? `${positioned.length} objective${positioned.length === 1 ? '' : 's'} on this map`
          : 'no objectives — quest data unavailable'}
        {exits.length > 0 && ` · ${exits.length} exit${exits.length === 1 ? '' : 's'}`}
      </p>
    </div>
  )
}

function Placeholder({ children }: { children: React.ReactNode }) {
  return (
    <div className="grid aspect-video place-items-center font-mono text-xs text-muted">{children}</div>
  )
}

function Segment({
  label, options, value, onChange,
}: {
  label: string
  options: [string, string][]
  value: string
  onChange: (value: string) => void
}) {
  return (
    <div className="flex items-center gap-2">
      <span className="font-mono text-[11px] uppercase tracking-wider text-muted">{label}</span>
      <div className="flex gap-px">
        {options.map(([id, text]) => (
          <button
            key={id}
            type="button"
            aria-pressed={value === id}
            onClick={() => onChange(id)}
            className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                       hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {text}
          </button>
        ))}
      </div>
    </div>
  )
}
