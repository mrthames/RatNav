import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  api,
  type MapSummary,
  type PlannableObjective,
  type RaidView,
  type SavedPlan,
  type TurnIn,
} from './api'

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

  // Bumped whenever a plan is built, so the Share section reloads. It keeps its own copy of the
  // saved plans and had no way to hear that a new one existed — so building a plan and then being
  // told there was none to share was the obvious next thing to happen.
  const [planned, setPlanned] = useState(0)

  const calibrated = useMemo(() => maps.filter((m) => m.calibrated), [maps])

  useEffect(() => {
    if (mapId || calibrated.length === 0) return

    // The map of the plan you last used, not whichever map happens to sort first — which is how a
    // Streets session reopened on Factory. The plan itself is already restored on the service
    // side; it was only the picker that was choosing independently.
    const active = raid?.mapId && calibrated.some((m) => m.id === raid.mapId) ? raid.mapId : null

    setMapId(active ?? calibrated[0].id)
  }, [calibrated, mapId, raid?.mapId])

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
      setPlanned((n) => n + 1)
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

      {/*
        Shown whether or not you are in a raid. A plan outlives the raid it was built for, and the
        usual next move after extracting is to strike off what is no longer worth doing and keep
        the rest — which needs the plan still on screen.
      */}
      {(raid?.inRaid || raid?.hasPlan) && <RaidPanel raid={raid} />}
      <TurnIns />
      {mapId && (
        <Sharing
          mapId={mapId}
          mapName={maps.find((m) => m.id === mapId)?.name ?? ''}
          planned={planned}
        />
      )}

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

/**
 * Passing a plan to someone, and taking theirs.
 *
 * A code rather than a file. A file is for keeping; a code is for sending, and "download this,
 * send it, save it, import it" is four steps where one will do. The code carries the plan itself,
 * so there is no server involved and nothing to be up or down.
 */
function Sharing({
  mapId, mapName, planned,
}: {
  mapId: string
  mapName: string
  /** Changes when a plan is built, which is the signal to go and look for it. */
  planned: number
}) {
  const [plans, setPlans] = useState<SavedPlan[]>([])
  const [open, setOpen] = useState<'share' | 'import' | null>(null)
  const [code, setCode] = useState<string | null>(null)
  const [paste, setPaste] = useState('')
  const [note, setNote] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(
    () => api.savedPlans().then(setPlans).catch(() => setPlans([])), [])

  useEffect(() => { void load() }, [load, planned])

  // A Customs code left on screen while Streets is selected is an invitation to send the wrong one.
  useEffect(() => { setCode(null); setNote(null); setOpen(null) }, [mapId])

  const mine = plans.find((p) => p.mapId === mapId)

  async function show() {
    if (!mine) return

    setOpen('share')
    setCode((await api.planCode(mine.id)).code)
  }

  async function take() {
    setBusy(true)
    setNote(null)

    try {
      const result = await api.importCode(paste)
      setPaste('')
      await load()

      const theirs = result.id
      const ours = plans.find((p) => p.mapId === result.plan.mapId && p.id !== theirs)?.id

      if (!ours) {
        setNote(
          `Imported ${result.plan.owner ?? 'their'} ${result.plan.mapName} plan. `
          + `Build one of your own for ${result.plan.mapName} to merge with it.`)

        return
      }

      const merged = await api.mergePlans([ours, theirs])
      const overlap = merged.overlap

      setNote(
        `Merged with ${merged.owners.join(' and ')}. `
        + `${overlap.sharedObjectiveIds.length} shared, `
        + `${overlap.contestedItemIds.length} contested, `
        + `${overlap.redundantKeyItemIds.length} key${overlap.redundantKeyItemIds.length === 1 ? '' : 's'} only one of you needs.`)
    } catch (e) {
      setNote(e instanceof Error ? e.message : 'That code could not be read.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {/*
        Two buttons rather than a panel. Sharing is occasional and reading a plan is what this page
        is for, so the machinery waits behind a click instead of taking a third of the view every
        time the tab is opened.
      */}
      <div className="flex items-center gap-3">
        <button
          type="button"
          disabled={!mine}
          onClick={() => void show()}
          className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted transition-colors
                     hover:text-ink disabled:opacity-40
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          Share plan
        </button>

        <button
          type="button"
          onClick={() => { setOpen('import'); setNote(null) }}
          className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          Import a plan
        </button>

        {!mine && <span className="text-xs text-muted">No {mapName} plan to share yet.</span>}
      </div>

      {open && (
        <Modal
          title={open === 'share' ? `Share your ${mapName} plan` : 'Import a plan'}
          onClose={() => setOpen(null)}
        >
          {open === 'share' ? (
            <>
              <p className="text-sm text-muted">
                Send this to whoever you are running with. They paste it into their own RatNav and
                it merges with theirs — nothing is dropped, and every objective keeps its owner.
              </p>

              <textarea
                readOnly
                value={code ?? 'Building…'}
                rows={4}
                onFocus={(e) => e.currentTarget.select()}
                className="w-full resize-none break-all rounded-sm border border-line bg-ground p-2
                           font-mono text-[11px] text-muted
                           focus-visible:outline-2 focus-visible:outline-accent"
              />

              <button
                type="button"
                disabled={!code}
                onClick={() => code && void navigator.clipboard.writeText(code)}
                className="self-start rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px] uppercase
                           tracking-wider text-ground disabled:opacity-40
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                Copy
              </button>
            </>
          ) : (
            <>
              <p className="text-sm text-muted">
                Paste what a friend sent you. It imports and merges with your plan for the same map
                in one step.
              </p>

              <textarea
                value={paste}
                onChange={(e) => setPaste(e.target.value)}
                rows={4}
                placeholder="RATNAV1-…"
                className="w-full resize-none break-all rounded-sm border border-line bg-ground p-2
                           font-mono text-[11px] text-ink placeholder:text-muted/60
                           focus-visible:outline-2 focus-visible:outline-accent"
              />

              <button
                type="button"
                disabled={busy || paste.trim() === ''}
                onClick={() => void take()}
                className="self-start rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px] uppercase
                           tracking-wider text-ground disabled:opacity-40
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {busy ? 'Importing…' : 'Import and merge'}
              </button>
            </>
          )}

          {note && <p className="text-xs text-muted">{note}</p>}
        </Modal>
      )}
    </>
  )
}

/** A plain dialog: dim the page, close on escape or on the backdrop. */
function Modal({
  title, onClose, children,
}: {
  title: string
  onClose: () => void
  children: React.ReactNode
}) {
  useEffect(() => {
    const escape = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', escape)
    return () => window.removeEventListener('keydown', escape)
  }, [onClose])

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={title}
      onClick={onClose}
      className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex w-full max-w-lg flex-col gap-3 border border-line bg-panel p-4 shadow-xl"
      >
        <div className="flex items-center justify-between">
          <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="font-mono text-sm text-muted hover:text-ink
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            ✕
          </button>
        </div>

        {children}
      </div>
    </div>
  )
}

/**
 * Quests whose every planned objective is done, waiting on a trader.
 *
 * RatNav will not mark these complete on its own. Finishing the objectives and handing the quest
 * in are different events, the game does not reliably log the second, and a completed quest
 * retires its item needs — so guessing wrong quietly deletes a shopping list. It asks instead.
 */
function TurnIns() {
  const [ready, setReady] = useState<TurnIn[]>([])

  const load = () => api.turnIns().then(setReady).catch(() => setReady([]))
  useEffect(() => { void load() }, [])

  async function confirm(task: TurnIn) {
    await api.markTaskState(task.taskId, 'Completed')
    await load()
  }

  if (ready.length === 0) return null

  return (
    <div className="flex flex-col gap-2 border border-route/40 bg-route/5 px-3 py-2">
      <span className="font-mono text-[11px] uppercase tracking-wider text-route">
        Done in raid — turned in?
      </span>

      {ready.map((task) => (
        <div key={task.taskId} className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span className="text-sm">{task.taskName}</span>

          {task.traderName && (
            <span className="font-mono text-xs text-muted">hand to {task.traderName}</span>
          )}

          {/*
            Said plainly when the plan only covered part of the quest. Marking it complete would
            retire item needs for objectives you never selected.
          */}
          {task.objectiveCount < task.totalObjectiveCount && (
            <span className="font-mono text-xs text-warn">
              you planned {task.objectiveCount} of {task.totalObjectiveCount} objectives
            </span>
          )}

          <button
            type="button"
            onClick={() => void confirm(task)}
            className="ml-auto rounded-sm bg-panel-hi px-2.5 py-1 font-mono text-[11px] uppercase
                       tracking-wider text-muted transition-colors hover:bg-accent hover:text-ground
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            Mark turned in
          </button>
        </div>
      ))}
    </div>
  )
}

/** What the overlay shows, mirrored here so the pre-raid screen doubles as a second monitor view. */
function RaidPanel({ raid }: { raid: RaidView }) {
  const bearing = raid.nextStopRelativeBearing
  const direction = bearing == null ? null : `${Math.abs(Math.round(bearing))}° ${bearing > 0 ? 'right' : 'left'}`

  return (
    <div className="flex flex-col gap-2 border border-accent/40 bg-accent/5 px-3 py-2">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
      <span className="font-mono text-[11px] uppercase tracking-wider text-accent">
        {raid.inRaid ? 'In raid' : 'Plan ready'}
      </span>
      <span className="text-sm">{raid.mapName}</span>

      {/* A plan for somewhere else still exists; it just does not apply to the raid you are in. */}
      {raid.planMapName && (
        <span className="font-mono text-xs text-warn">plan is for {raid.planMapName}</span>
      )}

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

      {/*
        RatNav ends the raid on its own when the game goes back to the menu. This is here for
        when it does not — the game writes no "raid over" line, so detection reads a proxy, and a
        proxy can be missed. Ticked objectives are kept either way.
      */}
      <div className="ml-auto flex gap-4">
        {raid.inRaid && (
          <button
            type="button"
            onClick={() => void api.endRaid()}
            className="font-mono text-[11px] uppercase tracking-wider text-muted underline-offset-4 hover:text-ink hover:underline"
          >
            End raid
          </button>
        )}

        {raid.hasPlan && (
          <button
            type="button"
            onClick={() => void api.clearPlan()}
            className="font-mono text-[11px] uppercase tracking-wider text-muted underline-offset-4 hover:text-ink hover:underline"
          >
            Clear plan
          </button>
        )}
      </div>
      </div>

      {/* Stops are strikeable between raids, which is what makes a plan worth keeping. */}
      {raid.stops.length > 0 && (
        <ul className="flex flex-col gap-px">
          {raid.stops.map((stop) => (
            <li key={stop.objectiveId} className="flex items-center gap-3 py-0.5">
              <span className={`text-sm ${stop.done ? 'text-muted line-through' : ''}`}>
                {stop.place ?? stop.taskName}
              </span>
              <span className="truncate font-mono text-[11px] text-muted">{stop.taskName}</span>

              <button
                type="button"
                onClick={() => void api.removeStop(stop.objectiveId)}
                aria-label={`Remove ${stop.taskName}`}
                className="ml-auto font-mono text-xs text-muted hover:text-need
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}
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
