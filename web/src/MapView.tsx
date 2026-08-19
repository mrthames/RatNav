import { useEffect, useMemo, useRef, useState } from 'react'
import { api, type InkLevel, type MapSummary, type ObjectivePin } from './api'

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

        {!map.calibrationVerified && (
          <span className="rounded-sm border border-warn/40 px-2 py-1 font-mono text-[11px] tracking-wide text-warn">
            calibration unverified · {map.coordinateRotation}°
          </span>
        )}
      </div>

      <div className="relative overflow-hidden border border-line bg-[#0e1317]">
        {markup === null && <Placeholder>loading map…</Placeholder>}
        {markup === '' && <Placeholder>this map has no image yet</Placeholder>}
        {markup && (
          <div
            ref={host}
            className="[&>svg]:block [&>svg]:h-auto [&>svg]:w-full"
            dangerouslySetInnerHTML={{ __html: markup }}
          />
        )}

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

      <p className="font-mono text-xs text-muted">
        {positioned.length > 0
          ? `${positioned.length} objective${positioned.length === 1 ? '' : 's'} on this map`
          : 'no objectives — quest data unavailable'}
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
