import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  api,
  type MapSummary,
  type PlannableObjective,
  type RaidView,
  type SavedPlan,
  type TurnIn,
  type CustomWaypoint,
} from './api'

/**
 * The pre-raid ritual, in the order it is actually done: pick a map, tick the objectives you are
 * pushing, and get a route with the keys you need to bring.
 */
export function PlanView({ maps, raid }: { maps: MapSummary[]; raid: RaidView | null }) {
  const [mapId, setMapId] = useState<string | null>(null)
  const [objectives, setObjectives] = useState<PlannableObjective[]>([])

  /**
   * Your own marks for this map, offered alongside the quest objectives.
   *
   * <p>They live in the same picked list, so a run can be "these two quest steps, then the stash
   * behind the garage" in whatever order you want — which is the only reason to put a mark in a
   * plan rather than just leave it drawn on the map.</p>
   */
  const [marks, setMarks] = useState<CustomWaypoint[]>([])
  /**
   * The objectives picked, per map, in the order they were picked.
   *
   * <p>An ordered list rather than a set, because the order is the plan. The planner walks the
   * stops as given, so what you tick first is where you go first — and a set would have handed it
   * whatever order the objectives happened to load in.</p>
   *
   * <p>Kept per map rather than one selection at a time. Half-picking a Customs run, glancing at
   * Woods to check something, and coming back to an empty list is a small betrayal — and a common
   * one, because comparing two maps before you queue is exactly what this page is for.</p>
   */
  const [picks, setPicks] = useState<Record<string, string[]>>({})

  const chosen = useMemo(() => (mapId ? picks[mapId] ?? [] : []), [picks, mapId])
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

    try {
      setMarks(await api.waypoints(mapId))
    } catch {
      setMarks([])
    }
  }, [mapId])

  useEffect(() => { load() }, [load])

  /** Edits this map's picks, leaving every other map's alone. */
  const edit = (change: (current: string[]) => string[]) =>
    setPicks((all) => (mapId ? { ...all, [mapId]: change(all[mapId] ?? []) } : all))

  const toggle = (id: string) =>
    edit((current) =>
      current.includes(id) ? current.filter((c) => c !== id) : [...current, id])

  /** Move one stop to another position, keeping everything else in order around it. */
  const move = (from: number, to: number) =>
    edit((current) => {
      if (from === to || to < 0 || to >= current.length) return current

      const next = [...current]
      const [moved] = next.splice(from, 1)
      next.splice(to, 0, moved)

      return next
    })

  // Keys are the thing you cannot fix once the raid starts, so they are gathered from the
  // objectives you picked and shown before you queue rather than after.
  const keys = useMemo(() => {
    const ids = new Set<string>()
    for (const o of objectives) {
      if (chosen.includes(o.objectiveId)) o.neededKeyItemIds.forEach((k) => ids.add(k))
    }
    return [...ids]
  }, [objectives, chosen])

  /**
   * What was picked, in plan order — objectives and your own marks together.
   *
   * <p>Both end up as stops with numbers on the map, so the panel treats them as one list rather
   * than two. What tells them apart is the label, and on the map, the shape.</p>
   */
  const route = useMemo(
    () => chosen.flatMap((id) => {
      const objective = objectives.find((o) => o.objectiveId === id)

      if (objective) {
        return [{
          id,
          title: objective.description || objective.taskName,
          subtitle: objective.taskName,
          mark: false,
        }]
      }

      const own = marks.find((m) => m.id === id)

      return own
        ? [{
            id,
            title: own.label,
            subtitle: own.kind === 'Item' ? 'your mark · pick up' : 'your mark',
            mark: true,
          }]
        : []
    }),
    [chosen, objectives, marks])

  async function build() {
    if (!mapId || chosen.length === 0) return
    setBusy(true)
    setNote(null)
    try {
      // Only what this map still offers. A pick kept from an earlier visit can outlive the quest
      // that produced it — completed since, or dropped by a patch — and sending a dead id would
      // silently shorten the plan rather than say so.
      const built = await api.buildPlan(
        mapId,
        route.filter((r) => !r.mark).map((r) => r.id),
        undefined,

        // Sent in the order they were picked, the same as the objectives, so a plan can interleave
        // "this quest step, then my stash, then that one".
        route.filter((r) => r.mark).map((r) => r.id))
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
            {map.workInProgress && (
              <span className="ml-1.5 font-mono text-[10px] text-warn">[WIP]</span>
            )}
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
          {/*
            Your own marks, offered the same way the quest objectives are. Above them, because a
            short list you wrote yourself should not be below forty derived rows.
          */}
          {marks.length > 0 && (
            <div className="border border-mark/40">
              <p className="border-b border-mark/30 bg-mark/5 px-3 py-1.5 font-mono text-[11px]
                            uppercase tracking-wider text-mark">
                Your marks
              </p>
              <ul>
                {marks.map((mark) => (
                  <li key={mark.id} className="border-b border-line-soft last:border-0">
                    <label className="flex cursor-pointer items-start gap-2.5 px-3 py-2 hover:bg-panel/60">
                      <input
                        type="checkbox"
                        checked={chosen.includes(mark.id)}
                        onChange={() => toggle(mark.id)}
                        className="mt-1 accent-accent"
                      />

                      {chosen.includes(mark.id) && (
                        <span className="mt-0.5 font-mono text-[11px] tabular-nums text-mark">
                          {chosen.indexOf(mark.id) + 1}
                        </span>
                      )}

                      <span className="min-w-0">
                        <span className="block text-sm">{mark.label}</span>
                        <span className="font-mono text-[11px] text-muted">
                          {mark.kind === 'Item' ? 'something to pick up' : 'a place'}
                        </span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {objectives.length === 0 && marks.length === 0 ? (
            <Empty>
              No active quests have objectives on this map. Mark some quests active on the Quests
              view and they will show up here — or mark a spot of your own on the Maps view.
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
                          checked={chosen.includes(o.objectiveId)}
                          onChange={() => toggle(o.objectiveId)}
                          className="mt-1 accent-accent"
                        />

                        {/*
                          The number this stop will carry on the map. Ticking a box and having no
                          idea where it landed in the order is the thing the route panel fixes;
                          this is the same answer where you are looking when you tick.
                        */}
                        {chosen.includes(o.objectiveId) && (
                          <span className="mt-0.5 font-mono text-[11px] tabular-nums text-accent">
                            {chosen.indexOf(o.objectiveId) + 1}
                          </span>
                        )}

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
            {chosen.length} objective{chosen.length === 1 ? '' : 's'}
          </p>

          {route.length > 0 && <Route stops={route} onMove={move} onRemove={toggle} />}

          {keys.length > 0 && <Keys itemIds={keys} />}

          <button
            type="button"
            onClick={build}
            disabled={busy || chosen.length === 0}
            className="rounded-sm border border-accent bg-accent px-3 py-2 text-sm font-medium
                       text-ground transition-opacity hover:opacity-90 disabled:opacity-30
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {busy ? 'Planning…' : 'Plan this raid'}
          </button>

          {note && <p className="font-mono text-xs text-have">{note}</p>}

          <p className="text-xs leading-relaxed text-muted">
            Stops run in the order you ticked them. Drag a row, or use the arrows, to change it.
            Once you are in raid the stops you have not reached re-order around wherever you
            actually are, the first time you take a position fix.
          </p>
        </aside>
      </div>
    </div>
  )
}

/**
 * The keys this plan needs, named, and whether you have each one.
 *
 * <p>"Bring 3 keys" was true and useless — the whole difficulty of keys is that you find out you
 * are missing one from the wrong side of a locked door. Named and marked, it is a check you can
 * actually run before you queue.</p>
 *
 * <p>Held means a have-count of at least one, the same count the Items view keeps. One number for
 * "do I own this", not a second one that could disagree with it.</p>
 */
function Keys({ itemIds }: { itemIds: string[] }) {
  const [keys, setKeys] = useState<{ id: string; name: string; have: number }[]>([])

  const load = useCallback(async () => {
    const found = await Promise.all(itemIds.map(async (id) => {
      try {
        const detail = await api.item(id)
        return { id, name: detail.item.shortName || detail.item.name, have: detail.have }
      } catch {
        // An id the item index does not know — a patch dropped it, or the cache is behind.
        // Better a row saying so than a key silently missing from the list.
        return { id, name: 'unknown key', have: 0 }
      }
    }))

    setKeys(found)
  }, [itemIds])

  useEffect(() => { void load() }, [load])

  async function hold(id: string, held: boolean) {
    await api.setHave(id, { count: held ? 1 : 0 })
    setKeys((current) => current.map((k) => (k.id === id ? { ...k, have: held ? 1 : 0 } : k)))
  }

  const missing = keys.filter((k) => k.have === 0).length

  return (
    <div className="flex flex-col gap-1 border-t border-line pt-2">
      <p className="font-mono text-[11px] uppercase tracking-wider text-muted">
        Keys
        {missing > 0 && <span className="text-need"> · {missing} you do not have</span>}
      </p>

      {keys.map((key) => (
        <label
          key={key.id}
          className="flex cursor-pointer items-center gap-2 text-xs hover:text-ink"
        >
          <input
            type="checkbox"
            checked={key.have > 0}
            onChange={(e) => void hold(key.id, e.target.checked)}
            className="accent-accent"
          />
          <span className={key.have > 0 ? 'text-muted' : 'text-need'}>{key.name}</span>
        </label>
      ))}
    </div>
  )
}

/**
 * The stops in the order they will be run, and the place to change that order.
 *
 * <p>Drag to move, and arrows to move — both, because drag is what people reach for and arrows are
 * what works with a keyboard, a trackpad someone finds fiddly, or a screen reader. The drag is
 * plain HTML5 rather than a library: reordering a list of a dozen rows is not worth a dependency,
 * and the native API already handles the pointer capture that makes hand-rolled dragging go
 * wrong.</p>
 */
function Route({
  stops, onMove, onRemove,
}: {
  stops: { id: string; title: string; subtitle: string; mark: boolean }[]
  onMove: (from: number, to: number) => void
  onRemove: (objectiveId: string) => void
}) {
  const [dragging, setDragging] = useState<number | null>(null)
  const [over, setOver] = useState<number | null>(null)

  return (
    <ol className="flex flex-col gap-px border-t border-line pt-2">
      {stops.map((stop, index) => (
        <li
          key={stop.id}
          draggable
          onDragStart={(e) => {
            setDragging(index)

            // Firefox refuses to start a drag unless something is on the clipboard for it, and
            // the sentence beside this list promises dragging works.
            e.dataTransfer.effectAllowed = 'move'
            e.dataTransfer.setData('text/plain', stop.id)
          }}
          onDragEnd={() => { setDragging(null); setOver(null) }}
          onDragOver={(e) => {
            e.preventDefault()
            e.dataTransfer.dropEffect = 'move'
            setOver(index)
          }}
          onDrop={(e) => {
            e.preventDefault()
            if (dragging !== null) onMove(dragging, index)
            setDragging(null)
            setOver(null)
          }}
          className={`flex cursor-grab items-start gap-2 rounded-sm px-1 py-1 active:cursor-grabbing
                      ${over === index && dragging !== index ? 'bg-accent/15' : ''}
                      ${dragging === index ? 'opacity-40' : ''}`}
        >
          <span className={`font-mono text-[11px] tabular-nums ${stop.mark ? 'text-mark' : 'text-accent'}`}>
            {index + 1}
          </span>

          <span className="min-w-0 flex-1">
            <span className="block truncate text-xs" title={stop.title}>{stop.title}</span>
            <span className={`block truncate font-mono text-[10px] ${stop.mark ? 'text-mark' : 'text-muted'}`}>
              {stop.subtitle}
            </span>
          </span>

          {/* The keyboard route to the same thing the drag does. */}
          <span className="flex flex-col">
            <button
              type="button"
              disabled={index === 0}
              onClick={() => onMove(index, index - 1)}
              aria-label={`Move ${stop.title} earlier`}
              className="font-mono text-[9px] leading-tight text-muted hover:text-ink disabled:opacity-20
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              ▲
            </button>
            <button
              type="button"
              disabled={index === stops.length - 1}
              onClick={() => onMove(index, index + 1)}
              aria-label={`Move ${stop.title} later`}
              className="font-mono text-[9px] leading-tight text-muted hover:text-ink disabled:opacity-20
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              ▼
            </button>
          </span>

          <button
            type="button"
            onClick={() => onRemove(stop.id)}
            aria-label={`Drop ${stop.title} from the plan`}
            className="font-mono text-[11px] text-muted hover:text-need
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            ✕
          </button>
        </li>
      ))}
    </ol>
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
  /**
   * What is left, then what is finished.
   *
   * <p>A ticked stop staying in place means the next thing to do drifts down the list as the raid
   * goes on, and by the end you are reading past six struck-through lines to find it. Numbering
   * counts only what is left, matching the numbers on the map — the map has no marker for a stop
   * you have already done, so neither does this.</p>
   */
  const ordered = useMemo(() => {
    let number = 0

    return [
      ...raid.stops.filter((s) => !s.done).map((stop) => ({ stop, number: ++number })),
      ...raid.stops.filter((s) => s.done).map((stop) => ({ stop, number: null })),
    ]
  }, [raid.stops])

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
        {raid.fixedAt && ` · position ${age(raid.fixedAt)}`}
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
          {ordered.map(({ stop, number }) => (
            <li key={stop.objectiveId} className="flex items-center gap-3 py-0.5">
              {/*
                The same number the overlay draws on the map, and counted the same way — only what
                is left gets one. Reading "go to 3" on the overlay and then hunting for which line
                that is here was work the number could do itself.
              */}
              <span className="w-3 text-right font-mono text-[11px] tabular-nums text-accent">
                {number ?? ''}
              </span>

              {/*
                This ticks off the stop, not the quest. Handing something to a trader is a
                different act and has its own button — a checkbox that quietly retired a quest's
                item needs would be a trap.
              */}
              <input
                type="checkbox"
                checked={stop.done}
                onChange={() => void api.completeObjective(stop.objectiveId, !stop.done)}
                aria-label={`Mark ${stop.taskName} done — the stop, not the quest`}
                className="accent-accent"
              />

              <span className={`text-sm ${stop.done ? 'text-muted line-through' : ''}`}>
                {stop.place ?? stop.taskName}
              </span>
              <span className="truncate font-mono text-[11px] text-muted">{stop.taskName}</span>

              <span className="ml-auto flex items-center gap-3">
                <CompleteQuest taskId={stop.taskId} taskName={stop.taskName} />

                <button
                  type="button"
                  onClick={() => void api.removeStop(stop.objectiveId)}
                  aria-label={`Remove ${stop.taskName}`}
                  className="font-mono text-xs text-muted hover:text-need
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  ✕
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/**
 * Marking a quest complete from where you are looking at it.
 *
 * <p>The turn-in prompt above only appears once every planned objective is ticked, which is the
 * common path and not the only one — a quest can finish on a step you never planned, or off the
 * back of one you did. This is the way out of that without a trip to the Quests view.</p>
 *
 * <p>It asks first. Completing a quest retires its item needs, so a misclick quietly deletes part
 * of a shopping list — and the click that would undo it is on another screen.</p>
 */
function CompleteQuest({ taskId, taskName }: { taskId: string; taskName: string }) {
  const [asking, setAsking] = useState(false)
  const [done, setDone] = useState(false)

  if (done) return <span className="font-mono text-[11px] text-have">complete</span>

  if (asking) {
    return (
      <span className="flex items-center gap-2">
        <button
          type="button"
          onClick={async () => { await api.markTaskState(taskId, 'Completed'); setDone(true) }}
          className="font-mono text-[11px] uppercase tracking-wider text-have underline-offset-4
                     hover:underline focus-visible:outline-2 focus-visible:outline-accent"
        >
          Yes, complete
        </button>
        <button
          type="button"
          onClick={() => setAsking(false)}
          className="font-mono text-[11px] text-muted hover:text-ink
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          no
        </button>
      </span>
    )
  }

  return (
    <button
      type="button"
      onClick={() => setAsking(true)}
      aria-label={`Mark ${taskName} complete`}
      className="font-mono text-[11px] uppercase tracking-wider text-muted underline-offset-4
                 hover:text-ink hover:underline focus-visible:outline-2 focus-visible:outline-accent"
    >
      Quest done
    </button>
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
