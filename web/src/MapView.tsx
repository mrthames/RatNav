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

/**
 * A labelled checkbox that keeps its place when it has nothing to offer.
 *
 * <p>Disabled rather than absent. Controls that vanish on maps without floors or without extracts
 * made the row reflow every time the map changed, so whatever you were reaching for moved.</p>
 */
function Toggle({ label, checked, onChange, disabled = false }: {
  label: string
  checked: boolean
  onChange: (on: boolean) => void
  disabled?: boolean
}) {
  return (
    <label
      className={`flex items-center gap-2 text-xs ${disabled
        ? 'cursor-not-allowed text-muted/40'
        : 'cursor-pointer text-muted hover:text-ink'}`}
    >
      <input
        type="checkbox"
        checked={checked && !disabled}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="accent-accent"
      />
      {label}
    </label>
  )
}

/**
 * Naming a waypoint you have just placed, in RatNav's own dialog.
 *
 * <p>The browser's <code>prompt</code> did this before, and it is the operating system's box
 * dropped into the middle of a dark application — wrong typeface, wrong colours, wrong buttons,
 * and on a phone it is worse than that.</p>
 *
 * <p>One field. A note can follow from the waypoint's chip below the map, but asking for both at
 * once turns a one-word answer into a form.</p>
 */
function NameWaypoint({ onCancel, onSave }: {
  onCancel: () => void
  onSave: (label: string) => Promise<void>
}) {
  const [label, setLabel] = useState('')
  const [saving, setSaving] = useState(false)

  async function save() {
    if (!label.trim() || saving) return

    setSaving(true)
    try {
      await onSave(label.trim())
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Name this waypoint"
      onClick={onCancel}
      className="fixed inset-0 z-50 grid place-items-center bg-ground/80 p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          // Enter saves and Escape closes, because a one-field dialog you have to aim at is a
          // dialog that costs more than the thing it is asking for.
          if (e.key === 'Escape') onCancel()
          if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void save() }
        }}
        className="flex w-full max-w-sm flex-col gap-3 border border-mark/50 bg-panel p-4"
      >
        <p className="font-mono text-[11px] uppercase tracking-wider text-mark">
          New custom waypoint
        </p>

        <label className="flex flex-col gap-1">
          <span className="text-xs text-muted">What is here?</span>
          <input
            autoFocus
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            placeholder="car batteries"
            className="rounded-sm border border-line bg-ground px-2.5 py-2 text-sm text-ink
                       placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
          />
        </label>

        {/*
          Name and nothing else. A note can be added afterwards from the waypoint's own chip below
          the map — offering both here made a two-field form out of a one-word answer, and the
          second field is the one people would have skipped anyway.
        */}
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => void save()}
            disabled={!label.trim() || saving}
            className="rounded-sm border border-mark bg-mark px-3 py-1.5 text-sm font-medium
                       text-ground transition-opacity hover:opacity-90 disabled:opacity-30
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {saving ? 'Adding...' : 'Add waypoint'}
          </button>

          <button
            type="button"
            onClick={onCancel}
            className="font-mono text-xs text-muted hover:text-ink
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            cancel
          </button>
        </div>
      </div>
    </div>
  )
}

/** How far apart two fingers are. One finger, or none, has no span and cannot pinch. */
function spanOf(points: Map<number, { x: number; y: number }>): number {
  const [a, b] = [...points.values()]
  if (!a || !b) return 0

  return Math.hypot(a.x - b.x, a.y - b.y) || 1
}

export function MapView({
  map,
  objectives,
  minimal = false,
}: {
  map: MapSummary

  /**
   * The waypoints to draw, when the caller has its own set.
   *
   * <p>The Plan page passes the stops you have ticked, so the route appears as you build it.
   * Left out, the map fetches every active quest's objectives, which is what the Maps page
   * wants.</p>
   */
  objectives?: ObjectivePin[]

  /**
   * Drops everything except the map, the pins and the ability to move around them.
   *
   * <p>For the Plan page, where the map is there to watch a plan take shape rather than to be
   * studied. Draw levels, floors, exits, marks and place search all belong to the Maps page.</p>
   */
  minimal?: boolean
}) {
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
  const [placing, setPlacing] = useState(false)
  const [showMarks, setShowMarks] = useState(true)

  /** Where a waypoint is being placed, while its name is being asked for. */
  const [naming, setNaming] = useState<{ x: number; y: number } | null>(null)

  const [places, setPlaces] = useState<PlaceLabel[]>([])

  /** Names drawn on the map, the way the overlay draws them. */
  const [names, setNames] = useState(true)

  /**
   * How many quests to pin.
   *
   * <p>Streets with every quest on it is a map you cannot read. "Active" is the useful default —
   * what you are actually doing — with "all" there for planning what to pick up next and "off" for
   * when you want to look at the map itself.</p>
   */
  const [showing, setShowing] = useState<'active' | 'all' | 'off'>('active')
  const [allPins, setAllPins] = useState<ObjectivePin[] | null>(null)

  /** The waypoint whose quest is open, if any. */
  const [reading, setReading] = useState<ObjectivePin | null>(null)

  useEffect(() => {
    if (!minimal) api.places(map.id).then(setPlaces).catch(() => setPlaces([]))
  }, [map.id, minimal])

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

    // Named straight away rather than placed and named later, because an unnamed dot on a map is a
    // puzzle and "name it afterwards" is a step people skip. Asked for in RatNav's own dialog: the
    // browser's prompt box is the operating system's, dropped into the middle of a dark
    // application in the wrong typeface with the wrong buttons.
    setNaming({ x, y })
  }

  async function nameIt(label: string) {
    if (!naming) return

    await api.addWaypoint(map.id, label, naming.x, naming.y, floor)

    setNaming(null)
    setPlacing(false)
    await loadMarks()
  }

  async function forget(id: string) {
    await api.removeWaypoint(id)
    await loadMarks()
  }

  async function annotate(mark: CustomWaypoint) {
    // Seeded with whatever is there, so editing is editing rather than retyping. Cancel leaves it
    // alone; clearing the box and accepting removes the note, which is the only way to take one
    // off and has to be reachable.
    const note = window.prompt(`Note for "${mark.label}"`, mark.note ?? '')
    if (note === null) return

    await api.setWaypointNote(mark.id, note)
    await loadMarks()
  }

  // Zoom and pan, to match the overlay. A map you can only see whole is not much use on Streets.
  //
  // One piece of state rather than two, because zooming about the pointer moves both at once and
  // has to move them from the same starting values.
  const [view, setView] = useState({ zoom: 1, pan: { x: 0, y: 0 } })
  const { zoom, pan } = view
  const dragging = useRef<{ x: number; y: number } | null>(null)

  /**
   * Fingers currently on the map, and the pinch in progress.
   *
   * <p>Refs rather than state: these change on every pointer event and none of them belong in a
   * render. The zoom they produce does.</p>
   */
  const touches = useRef(new Map<number, { x: number; y: number }>())
  const pinch = useRef<{ span: number; zoom: number } | null>(null)
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
    // Only fetched when the caller has not supplied a set of its own.
    if (objectives === undefined) {
      api.objectives(map.id).then(setPins).catch(() => setPins([]))
    }

    // Extracts belong to the Maps page's controls, so a minimal map does not ask for them.
    if (!minimal) api.extracts(map.id).then(setExtracts).catch(() => setExtracts([]))
  }, [map.id, objectives === undefined, minimal])

  useEffect(() => { setAllPins(null) }, [map.id])

  useEffect(() => {
    // Every quest rather than the active ones, fetched the first time it is asked for. It is the
    // bigger answer and most people never want it.
    if (showing !== 'all' || allPins !== null) return

    api.allObjectives(map.id).then(setAllPins).catch(() => setAllPins([]))
  }, [map.id, showing, allPins])

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

  const shown = objectives
    ?? (showing === 'off' ? [] : showing === 'all' ? (allPins ?? pins) : pins)

  const positioned = useMemo(() => shown.filter((p) => p.x >= 0 && p.x <= 1), [shown])

  const exits = useMemo(
    () =>
      faction === 'off'
        ? []
        : extracts.filter((e) => e.faction === 'shared' || e.faction === faction),
    [extracts, faction],
  )

  return (
    <div className="flex flex-col gap-3">
      {/*
        One row, and always the same row.

        Every control is present on every map. Half of them used to render only when they had
        something to offer — the floor picker on a map with floors, exits on a map with extracts —
        so changing map reflowed the row and whatever you were reaching for moved. A control with
        nothing to offer here reads as unavailable instead, because absent means everything after
        it slides sideways.
      */}
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        {/* Maps-page furniture. The Plan page wants the map, the pins, and nothing else. */}
        {!minimal && (<>
        <Segment
          label="Draw"
          options={[
            ['graphical', 'Graphical'],
            ['full', 'Full'],
            ['structure', 'Structure'],
            ['outline', 'Outline'],
          ]}
          value={ink}
          onChange={(v) => setInk(v as InkLevel)}
        />

        <Segment
          label="Floor"
          options={floors.length > 1
            ? floors.map((f) => [f, FLOOR_LABELS[f] ?? f])
            : [['', 'One floor']]}
          value={floors.length > 1 ? floor ?? '' : ''}
          onChange={setFloor}
          disabled={floors.length <= 1}
        />

        <Toggle
          label="Ghost other floors"
          checked={ghost}
          onChange={setGhost}
          disabled={floors.length <= 1}
        />

        <Segment
          label="Quests"
          options={[['active', 'Active'], ['all', 'All'], ['off', 'Off']]}
          value={showing}
          onChange={(v) => setShowing(v as 'active' | 'all' | 'off')}
        />

        <Segment
          label="Exits"
          options={[['pmc', 'PMC'], ['scav', 'Scav'], ['off', 'Off']]}
          value={faction}
          onChange={(v) => setFaction(v as Faction)}
          disabled={extracts.length === 0}
        />

        <Toggle
          label="Place names"
          checked={names}
          onChange={setNames}
          disabled={places.length === 0}
        />
        </>)}

        {/*
          Custom waypoints: whether they are drawn, and a way to add one.

          No place-or-item choice before the click any more. Deciding which of two shapes a spot is
          before you have said what it is was a decision nobody wanted to make, and the shapes were
          not worth the question.
        */}
        <Toggle label="Custom waypoints" checked={showMarks} onChange={setShowMarks} />

        <button
          type="button"
          onClick={() => setPlacing((on) => !on)}
          aria-pressed={placing}
          title="Add a custom waypoint, then click the spot on the map"
          className={`flex items-center gap-1.5 rounded-sm px-2.5 py-1.5 text-xs transition-colors
                      focus-visible:outline-2 focus-visible:outline-accent
                      ${placing ? 'bg-mark text-ground' : 'bg-panel-hi text-muted hover:text-ink'}`}
        >
          <svg viewBox="-9 -9 18 18" aria-hidden className="size-3.5">
            <path
              d="M 0,-7 L 7,0 L 0,7 L -7,0 Z"
              fill="none"
              stroke={placing ? 'var(--color-ground)' : 'var(--color-mark)'}
              strokeWidth="2.4"
            />
          </svg>
          {placing ? 'Click the map...' : '+ Waypoint'}
        </button>

        {!minimal && (
          /* Straight onto the overlay, no plan required -- for a look before you queue. */
          <button
            type="button"
            onClick={() => void api.showMap(map.id)}
            className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                       hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
          >
            Show on overlay
          </button>
        )}

        {/*
          Last, and the only one that comes and goes -- because it is the one control that is
          meaningless until you have done something, and it has nothing after it to push.
        */}
        {(zoom !== 1 || pan.x !== 0 || pan.y !== 0) && (
          <button
            type="button"
            onClick={() => setView({ zoom: 1, pan: { x: 0, y: 0 } })}
            className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                       hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
          >
            Reset view - {zoom.toFixed(1)}x
          </button>
        )}
      </div>

      <div
        ref={frame}
        className="relative touch-none overflow-hidden border border-line bg-[#0e1317]"
        // Zoom about the pointer, not the corner.
        //
        // The content is drawn as translate(pan) then scale(z) from the top-left, so a point c in
        // the content sits at pan + c*z on screen. Changing z alone therefore pulls everything
        // toward the top-left corner, which is what this used to do. Holding the point under the
        // cursor still means moving pan by the same amount the scale moved it:
        //
        //     c = (m - pan) / z          the content point currently under the cursor
        //     pan' = m - c * z'          where it has to be for that point to stay at m
        onWheel={(e) => {
          e.preventDefault()

          const box = frame.current?.getBoundingClientRect()
          if (!box) return

          const mouseX = e.clientX - box.left
          const mouseY = e.clientY - box.top

          setView((v) => {
            const next = Math.min(8, Math.max(1, v.zoom * (e.deltaY < 0 ? 1.15 : 1 / 1.15)))
            const ratio = next / v.zoom

            return {
              zoom: next,
              pan: {
                x: mouseX - (mouseX - v.pan.x) * ratio,
                y: mouseY - (mouseY - v.pan.y) * ratio,
              },
            }
          })
        }}
        // Right-drag to pan, same as the overlay. The context menu would otherwise open on
        // release and swallow the gesture.
        //
        // A finger has no right button, so on touch one finger pans and two pinch — which is what
        // every map on a phone does and therefore what fingers already try. The mouse is left
        // exactly as it was: left-drag on a desktop is a selection, and turning it into a pan
        // would break the thing people do reach for.
        onContextMenu={(e) => e.preventDefault()}
        onPointerDown={(e) => {
          const touch = e.pointerType !== 'mouse'
          if (!touch && e.button !== 2) return

          e.currentTarget.setPointerCapture(e.pointerId)
          if (touch) touches.current.set(e.pointerId, { x: e.clientX, y: e.clientY })

          // Two fingers down is a pinch, not two pans. The span between them at the start is what
          // every later span is measured against.
          if (touch && touches.current.size === 2) {
            pinch.current = { span: spanOf(touches.current), zoom }
            dragging.current = null
            return
          }

          dragging.current = { x: e.clientX, y: e.clientY }
        }}
        onPointerMove={(e) => {
          const touch = e.pointerType !== 'mouse'
          if (touch && touches.current.has(e.pointerId)) {
            touches.current.set(e.pointerId, { x: e.clientX, y: e.clientY })
          }

          // Pinch, about the point between the fingers — the same rule the wheel follows about the
          // cursor. Zooming about the corner while two fingers hold a building is the thing that
          // makes a map feel broken.
          if (pinch.current && touches.current.size === 2) {
            const box = frame.current?.getBoundingClientRect()
            if (!box) return

            const start = pinch.current
            const points = [...touches.current.values()]
            const midX = (points[0].x + points[1].x) / 2 - box.left
            const midY = (points[0].y + points[1].y) / 2 - box.top

            setView((v) => {
              const next = Math.min(8, Math.max(1, start.zoom * (spanOf(touches.current) / start.span)))
              const ratio = next / v.zoom

              return {
                zoom: next,
                pan: {
                  x: midX - (midX - v.pan.x) * ratio,
                  y: midY - (midY - v.pan.y) * ratio,
                },
              }
            })

            return
          }

          const from = dragging.current
          if (!from) return

          setView((v) => ({
            ...v,
            pan: { x: v.pan.x + e.clientX - from.x, y: v.pan.y + e.clientY - from.y },
          }))
          dragging.current = { x: e.clientX, y: e.clientY }
        }}
        onPointerUp={(e) => {
          touches.current.delete(e.pointerId)
          if (touches.current.size < 2) pinch.current = null

          dragging.current = null
          e.currentTarget.releasePointerCapture(e.pointerId)
        }}
        onPointerCancel={(e) => {
          // A gesture the browser took over — an edge swipe, a call arriving. Without this the map
          // keeps thinking a finger is down and the next tap jumps it.
          touches.current.delete(e.pointerId)
          pinch.current = null
          dragging.current = null
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
          const colour = exit.transit
            ? 'var(--color-transit)'
            : exit.faction === 'scav'
              ? 'var(--color-scav)'
              : 'var(--color-pmc)'

          return (
            <div
              key={`${exit.name}-${exit.x}-${exit.y}`}
              title={
                exit.transit
                  ? `${exit.name} · transit to another map`
                  : `${exit.name} · ${exit.faction} extract`
              }
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
                  d={
                    exit.transit
                      ? 'M -6,-6 L 0,0 L -6,6 M 0,-6 L 6,0 L 0,6'
                      : 'M -6,-7 L -6,7 L 6,7 L 6,-7 Z M -2,0 L 4,0 M 1,-3 L 4,0 L 1,3'
                  }
                  fill={exit.transit ? 'none' : 'var(--color-ground)'}
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
        {(showMarks ? marks : []).map((mark) => (
          <div
            key={mark.id}
            title={[`${mark.label} · your waypoint`, mark.note].filter(Boolean).join(' — ')}
            className="pointer-events-none absolute -translate-x-1/2 -translate-y-1/2"
            style={{ left: `${mark.x * 100}%`, top: `${mark.y * 100}%` }}
          >
            <svg
              viewBox="-9 -15 18 18"
              style={{ width: `${16 / zoom}px`, height: `${16 / zoom}px`, display: 'block' }}
            >
              {/*
                The same pin a quest stop draws, in its own colour. What separates a waypoint of
                yours from a quest's is where it came from, which is what a colour is for — the
                shape only ever said which of two kinds it was, and that is no longer a choice
                anybody makes.
              */}
              <path
                d="M 0,0 C -3,-5 -7,-8 -7,-12 A 7,7 0 1 1 7,-12 C 7,-8 3,-5 0,0 Z"
                fill="var(--color-mark)"
                stroke="var(--color-ground)"
                strokeWidth="1.5"
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

      {naming && (
        <NameWaypoint
          onCancel={() => { setNaming(null); setPlacing(false) }}
          onSave={nameIt}
        />
      )}

      {reading && (
        <QuestBrief
          taskId={reading.taskId}
          objectiveId={reading.objectiveId}
          onClose={() => setReading(null)}
        />
      )}

      {/*
        Every mark on this map, and the place they are managed from.

        A list rather than a menu on each pin: the map answers "not this one", but "what have I
        accumulated" is a question about all of them at once, and a pin you have to find first is a
        bad place to ask it.
      */}
      {marks.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {marks.map((mark) => (
            <li
              key={mark.id}
              className="flex items-center gap-2 rounded-sm border border-mark/40 bg-mark/5 px-2 py-1"
            >
              <span className="text-xs">{mark.label}</span>

              {mark.note && (
                <span className="max-w-[16rem] truncate text-[11px] text-muted" title={mark.note}>
                  {mark.note}
                </span>
              )}

              {/*
                The sentence you cannot remember at the time — "third shelf, behind the crates".
                It shows against the stop in the overlay's quest log once the mark joins a plan,
                which is the moment it is worth having.
              */}
              <button
                type="button"
                onClick={() => void annotate(mark)}
                aria-label={mark.note ? `Edit the note on ${mark.label}` : `Add a note to ${mark.label}`}
                className="font-mono text-[11px] text-muted hover:text-ink
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {mark.note ? 'note ✎' : '+ note'}
              </button>

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
  label, options, value, onChange, disabled = false,
}: {
  label: string
  options: [string, string][]
  value: string
  onChange: (value: string) => void

  /**
   * Present but not offering anything on this map.
   *
   * <p>Dimmed and unclickable rather than removed. A control that disappears on maps without
   * floors or without extracts makes the whole row reflow when the map changes, so whatever you
   * were reaching for moves out from under you.</p>
   */
  disabled?: boolean
}) {
  return (
    <div className={`flex items-center gap-2 ${disabled ? 'opacity-40' : ''}`}>
      <span className="font-mono text-[11px] uppercase tracking-wider text-muted">{label}</span>
      <div className="flex gap-px">
        {options.map(([id, text]) => (
          <button
            key={id}
            type="button"
            disabled={disabled}
            aria-pressed={value === id}
            onClick={() => onChange(id)}
            className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                       hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                       disabled:cursor-not-allowed disabled:hover:text-muted
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {text}
          </button>
        ))}
      </div>
    </div>
  )
}
