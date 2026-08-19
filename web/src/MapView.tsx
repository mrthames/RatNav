import { useEffect, useMemo, useRef, useState } from 'react'
import { api, type ExtractPin, type InkLevel, type MapSummary, type ObjectivePin } from './api'

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
  const [ink, setInk] = useState<InkLevel>('full')
  const [floors, setFloors] = useState<string[]>([])
  const [floor, setFloor] = useState<string | null>(null)
  const [ghost, setGhost] = useState(true)
  const [extracts, setExtracts] = useState<ExtractPin[]>([])
  const [faction, setFaction] = useState<Faction>('pmc')

  // Zoom and pan, to match the overlay. A map you can only see whole is not much use on Streets.
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragging = useRef<{ x: number; y: number } | null>(null)
  const host = useRef<HTMLDivElement>(null)

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
          options={[['full', 'Full'], ['structure', 'Structure'], ['outline', 'Outline']]}
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

        {/* Straight onto the overlay, no plan required — for a look before you queue. */}
        <button
          type="button"
          onClick={() => void api.showMap(map.id)}
          className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          Show on overlay
        </button>

        {!map.calibrationVerified && (
          <span className="rounded-sm border border-warn/40 px-2 py-1 font-mono text-[11px] tracking-wide text-warn">
            calibration unverified · {map.coordinateRotation}°
          </span>
        )}
      </div>

      <div
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
        style={{ cursor: dragging.current ? 'grabbing' : 'default' }}
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
          Extracts are diamonds and objectives are circles. The shapes carry the difference on
          their own, so the two are still tellable apart without relying on colour.
        */}
        {exits.map((exit) => (
          <div
            key={`${exit.name}-${exit.x}-${exit.y}`}
            title={`${exit.name} · ${exit.faction} extract`}
            className={`absolute size-2.5 -translate-x-1/2 -translate-y-1/2 rotate-45 border
                        bg-ground shadow-[0_0_0_1px_var(--color-ground)]
                        ${exit.faction === 'scav' ? 'border-route' : 'border-accent'}`}
            style={{ left: `${exit.x * 100}%`, top: `${exit.y * 100}%` }}
          />
        ))}

        {positioned.map((pin) => (
          <div
            key={pin.objectiveId}
            title={`${pin.taskName} — ${pin.description}`}
            className="absolute size-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-need
                       shadow-[0_0_0_2px_var(--color-ground)]"
            style={{ left: `${pin.x * 100}%`, top: `${pin.y * 100}%` }}
          />
        ))}
        </div>
      </div>

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
