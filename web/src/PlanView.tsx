import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, type MapSummary, type PlannableObjective, type RaidView } from './api'

/**
 * The pre-raid ritual, in the order it is actually done: pick a map, tick the objectives you are
 * pushing, and get a route with the keys you need to bring.
 */
export function PlanView({ maps, raid }: { maps: MapSummary[]; raid: RaidView | null }) {
  const [mapId, setMapId] = useState<string | null>(null)
  const [objectives, setObjectives] = useState<PlannableObjective[]>([])
  const [chosen, setChosen] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState<string | null>(null)

  const calibrated = useMemo(() => maps.filter((m) => m.calibrated), [maps])

  useEffect(() => {
    if (!mapId && calibrated.length > 0) setMapId(calibrated[0].id)
  }, [calibrated, mapId])

  const load = useCallback(async () => {
    if (!mapId) return
    try {
      setObjectives(await api.plannable(mapId))
    } catch {
      setObjectives([])
    }
    setChosen(new Set())
  }, [mapId])

  useEffect(() => { load() }, [load])

  const toggle = (id: string) =>
    setChosen((current) => {
      const next = new Set(current)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  // Keys are the thing you cannot fix once the raid starts, so they are gathered from the
  // objectives you picked and shown before you queue rather than after.
  const keys = useMemo(() => {
    const ids = new Set<string>()
    for (const o of objectives) {
      if (chosen.has(o.objectiveId)) o.neededKeyItemIds.forEach((k) => ids.add(k))
    }
    return [...ids]
  }, [objectives, chosen])

  async function build() {
    if (!mapId || chosen.size === 0) return
    setBusy(true)
    setNote(null)
    try {
      const built = await api.buildPlan(mapId, [...chosen])
      await api.activatePlan(built.id)
      setNote(`Plan active — ${built.plan.stops.length} stops.`)
    } catch {
      setNote('Could not build that plan.')
    } finally {
      setBusy(false)
    }
  }

  const byPlace = useMemo(() => {
    const groups = new Map<string, PlannableObjective[]>()
    for (const o of objectives) {
      const place = o.place ?? 'Elsewhere'
      if (!groups.has(place)) groups.set(place, [])
      groups.get(place)!.push(o)
    }
    return [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  }, [objectives])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        {calibrated.map((map) => (
          <button
            key={map.id}
            type="button"
            aria-pressed={mapId === map.id}
            onClick={() => setMapId(map.id)}
            className="rounded-sm bg-panel px-3 py-1.5 text-sm text-muted transition-colors
                       hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {map.name}
          </button>
        ))}
      </div>

      {raid?.inRaid && <RaidPanel raid={raid} />}

      <div className="grid gap-4 lg:grid-cols-[1fr_260px]">
        <div className="flex flex-col gap-3">
          {objectives.length === 0 ? (
            <Empty>
              No active quests have objectives on this map. Mark some quests active on the Quests
              view and they will show up here.
            </Empty>
          ) : (
            byPlace.map(([place, group]) => (
              <div key={place} className="border border-line">
                <p className="border-b border-line bg-panel px-3 py-1.5 font-mono text-[11px]
                              uppercase tracking-wider text-muted">
                  {place}
                </p>
                <ul>
                  {group.map((o) => (
                    <li key={o.objectiveId} className="border-b border-line-soft last:border-0">
                      <label className="flex cursor-pointer items-start gap-2.5 px-3 py-2 hover:bg-panel/60">
                        <input
                          type="checkbox"
                          checked={chosen.has(o.objectiveId)}
                          onChange={() => toggle(o.objectiveId)}
                          className="mt-1 accent-accent"
                        />
                        <span className="min-w-0">
                          <span className="block text-sm">{o.description || o.taskName}</span>
                          <span className="font-mono text-[11px] text-muted">
                            {[o.taskName, o.traderName, o.optional ? 'optional' : null]
                              .filter(Boolean).join(' · ')}
                            {o.neededKeyItemIds.length > 0 && ' · needs a key'}
                          </span>
                        </span>
                      </label>
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
        </div>

        <aside className="flex h-fit flex-col gap-3 border border-line bg-panel p-3">
          <p className="font-mono text-[11px] uppercase tracking-wider text-muted">This raid</p>

          <p className="font-mono text-sm tabular-nums">
            {chosen.size} objective{chosen.size === 1 ? '' : 's'}
          </p>

          {keys.length > 0 && (
            <p className="font-mono text-xs text-route">
              bring {keys.length} key{keys.length === 1 ? '' : 's'}
            </p>
          )}

          <button
            type="button"
            onClick={build}
            disabled={busy || chosen.size === 0}
            className="rounded-sm border border-accent bg-accent px-3 py-2 text-sm font-medium
                       text-ground transition-opacity hover:opacity-90 disabled:opacity-30
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {busy ? 'Planning…' : 'Plan this raid'}
          </button>

          {note && <p className="font-mono text-xs text-have">{note}</p>}

          <p className="text-xs leading-relaxed text-muted">
            The route is ordered for you, and re-orders itself around wherever you actually spawn
            the first time you take a position fix in game.
          </p>
        </aside>
      </div>
    </div>
  )
}

/** What the overlay shows, mirrored here so the pre-raid screen doubles as a second monitor view. */
function RaidPanel({ raid }: { raid: RaidView }) {
  const bearing = raid.nextStopRelativeBearing
  const direction = bearing == null ? null : `${Math.abs(Math.round(bearing))}° ${bearing > 0 ? 'right' : 'left'}`

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-2 border border-accent/40 bg-accent/5 px-3 py-2">
      <span className="font-mono text-[11px] uppercase tracking-wider text-accent">In raid</span>
      <span className="text-sm">{raid.mapName}</span>

      {raid.nextStopName && (
        <span className="font-mono text-sm tabular-nums">
          next: {raid.nextStopName}
          {raid.nextStopMetres != null && ` · ${Math.round(raid.nextStopMetres)}m`}
          {direction && ` · ${direction}`}
        </span>
      )}

      <span className="font-mono text-xs text-muted tabular-nums">
        {raid.completedObjectiveIds.length}/{raid.stops.length} done
        {raid.fixedAt && ` · fix ${age(raid.fixedAt)}`}
      </span>
    </div>
  )
}

/** "45s ago" — how much to trust the marker, without pretending it updates on its own. */
function age(iso: string): string {
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 60) return `${Math.round(seconds)}s ago`
  return `${Math.round(seconds / 60)}m ago`
}

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="border border-line bg-panel px-4 py-8 text-center font-mono text-xs text-muted">
    {children}
  </p>
)
