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
import { MapPicker } from './MapPicker'

/**
 * The pre-raid ritual, in the order it is actually done: pick a map, tick the objectives you are
 * pushing, and get a route with the keys you need to bring.
 */
export function PlanView({ maps, raid }: { maps: MapSummary[]; raid: RaidView | null }) {
  /*
    Planning is closed while you are in a raid with a plan already running.

    The obvious next click on this page is one that replaces the plan you are in the middle of
    walking, which is not a thing anybody means to do mid-raid.

    Both halves are needed and only the pair. A plan with no raid is the ordinary between-raids
    case and stays fully editable — that is where planning happens, and striking off what is no
    longer worth doing is the usual move after extracting. A raid with no plan is somebody who
    queued without one, and offering to build one then is useful rather than dangerous.
  */
  const planning = !(raid?.inRaid && raid?.hasPlan)
  const [mapId, setMapId] = useState<string | null>(null)

  /**
   * Whether the picking half of the page is open.
   *
   * <p>A plan that exists folds it away. The page is two things — building a plan, and the plan
   * you built — and with a plan on it the second is what you came for; leaving a map picker,
   * "0 objectives" and a **Plan this raid** button underneath makes a finished plan read as
   * unfinished work.</p>
   *
   * <p>Folded, not removed. Adding a stop to a plan you already have is an ordinary thing to want
   * — a quest turned in, a second look at what is on the way — and this page is the only place to
   * do it. Clearing the plan or ending the raid opens it again, because then it is all there is.</p>
   */
  const [picking, setPicking] = useState(false)

  const hasPlan = raid?.hasPlan ?? false

  /** Whether the build-a-plan half of the page is on show. */
  const building = planning && (!hasPlan || picking)

  /** Whether the plan menu — sharing and importing — is open. */
  const [planMenu, setPlanMenu] = useState(false)

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

  /**
   * Quests whose planned objectives are all done, waiting to be handed in.
   *
   * <p>This used to be a banner across the top of the page — a whole heading and a row per quest,
   * for what is one control belonging to a quest already listed below it. The row names the quest
   * and the trader; the only thing the banner added was the action, so the action moved to the
   * row.</p>
   */
  const [turnIns, setTurnIns] = useState<TurnIn[]>([])

  const loadTurnIns = useCallback(
    () => api.turnIns().then(setTurnIns).catch(() => setTurnIns([])), [])

  useEffect(() => { void loadTurnIns() }, [loadTurnIns, planned])

  const turnInFor = useMemo(
    () => new Map(turnIns.map((t) => [t.taskId, t])), [turnIns])

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
  /**
   * What the ticked objectives need carried in, named and split.
   *
   * <p>Keys apart from items on purpose: forgetting a key wastes the raid, forgetting a quest item
   * costs a trip back to the stash. The strip colours them differently for that reason.</p>
   */
  const carry = useMemo(() => {
    const keys = new Map<string, string>()
    const items = new Map<string, string>()

    for (const o of objectives) {
      if (!chosen.includes(o.objectiveId)) continue

      for (const need of o.required) {
        (need.isKey ? keys : items).set(need.itemId, need.name)
      }
    }

    const listed = (m: Map<string, string>) =>
      [...m].map(([itemId, name]) => ({ itemId, name }))

    return { keys: listed(keys), items: listed(items) }
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

  /**
   * The one row each ready-to-hand-in quest shows its control on.
   *
   * <p>A quest with stops in two places appears twice, and two controls doing the same thing is
   * one too many. Decided here rather than by ticking a set off as the rows render, because a
   * render that mutates as it goes is a render that behaves differently the second time React
   * runs it.</p>
   */
  const turnInRow = useMemo(() => {
    const first = new Map<string, string>()

    for (const [, group] of byPlace) {
      for (const o of group) {
        if (turnInFor.has(o.taskId) && !first.has(o.taskId)) first.set(o.taskId, o.objectiveId)
      }
    }

    return first
  }, [byPlace, turnInFor])

  return (
    <div className="flex flex-col gap-4">

      {/*
        In the order the work is done: which map, what it will cost you and the button that
        commits it, then the plan itself, then the list you are picking from.

        The plan used to sit at the top, above the thing that builds it, which reads as an answer
        printed before the question.

        And no map preview any more. The Maps page is where a map is looked at; a second copy here
        was a third panel changing size while you worked, on the one page that had already been
        rearranged once to stop things moving under the cursor.
      */}

      {/*
        Which map, and what you can do with the plan as a whole.

        The maps stay on show — that is the choice you make first and change often enough to want
        one click. Sharing and importing go behind the menu on the right: both are occasional, and
        two buttons for them sat next to the maps looking equally important.

        The whole of this — picker, counts and checklist — folds away once a plan exists. See
        `picking` above: the page is two things, and with a plan on it the plan is the one you
        came for.
      */}
      {building && (
      <div className="flex flex-wrap items-start justify-between gap-3">
        <MapPicker
          maps={calibrated}
          selected={calibrated.find((m) => m.id === mapId) ?? null}
          onSelect={(map) => setMapId(map.id)}
        />

        {mapId && (
          <div className="relative">
            <button
              type="button"
              onClick={() => setPlanMenu((open) => !open)}
              aria-expanded={planMenu}
              aria-label="Plan actions"
              className="rounded-sm bg-panel px-3 py-2 font-mono text-sm text-muted
                         transition-colors hover:text-ink
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              ☰
            </button>

            {planMenu && (
              <div className="absolute right-0 top-full z-20 mt-1 w-max border border-line
                              bg-panel p-3 shadow-xl">
                <Sharing
                  mapId={mapId}
                  mapName={maps.find((m) => m.id === mapId)?.name ?? ''}
                  planned={planned}
                />
              </div>
            )}
          </div>
        )}
      </div>
      )}

      {/*
        One strip that never changes height, then the map, then the list.

        The three panels used to resize as you ticked things — the checklist grew, the stops list
        grew, the map moved — so everything you were about to click had gone somewhere else. This
        is the fix: the counts and the warnings live in a row that stays exactly where it is, and
        the only thing below it that changes size is the checklist itself.
      */}
      {building && (
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border border-line bg-panel px-3 py-2">
        <span className="font-mono text-sm tabular-nums">
          {chosen.length} objective{chosen.length === 1 ? '' : 's'}
        </span>

        {/*
          What you have to be carrying when you queue, said loudly enough to be seen rather than
          read.

          This is the one thing on the page you cannot fix afterwards, and it used to sit at the
          same visual weight as the furniture around it — small chips in a row of small chips, with
          the non-key items in the same muted grey as a disabled control despite being just as
          required.

          Keys are filled rather than tinted, because arriving at the right door without one wastes
          the whole raid. Quest items are outlined in the warning colour rather than grey: cheaper
          to forget, still not optional. Both are labelled, so a row of names is never left to be
          guessed at.
        */}
        {(carry.keys.length > 0 || carry.items.length > 0) && (
          <span className="flex flex-wrap items-center gap-1.5 border-l border-line pl-4">
            <span className="font-mono text-[11px] uppercase tracking-wider text-need">
              Bring
            </span>

            {carry.keys.map((need) => (
              <span
                key={need.itemId}
                className="rounded-sm bg-need px-2 py-0.5 text-xs font-semibold text-ground"
              >
                {need.name}
              </span>
            ))}

            {carry.items.map((need) => (
              <span
                key={need.itemId}
                className="rounded-sm border border-warn/70 bg-warn/10 px-2 py-0.5 text-xs text-warn"
              >
                {need.name}
              </span>
            ))}
          </span>
        )}

        <span
          title={'Stops run in the order you ticked them. Drag a row, or use the arrows, to change '
            + 'it. Once you are in raid the stops you have not reached re-order around wherever '
            + 'you actually are, the first time you take a position fix.'}
          className="grid size-4 cursor-help place-items-center rounded-full border border-line
                     font-mono text-[10px] text-muted"
        >
          i
        </span>

        {note && <span className="font-mono text-xs text-have">{note}</span>}

        <button
          type="button"
          onClick={build}
          disabled={!planning || busy || chosen.length === 0}
          title={planning ? undefined : 'End the raid or clear the plan first'}
          className="ml-auto rounded-sm border border-accent bg-accent px-3 py-1.5 text-sm
                     font-medium text-ground transition-opacity hover:opacity-90
                     disabled:opacity-30 focus-visible:outline-2 focus-visible:outline-accent"
        >
          {busy ? 'Planning…' : 'Plan this raid'}
        </button>
      </div>
      )}

      {/*
        Shown whether or not you are in a raid. A plan outlives the raid it was built for, and the
        usual next move after extracting is to strike off what is no longer worth doing and keep
        the rest — which needs the plan still on screen.
      */}
      {(raid?.inRaid || raid?.hasPlan) && <RaidPanel raid={raid} />}
      {route.length > 0 && (
        <details className="border border-line bg-panel">
          <summary className="cursor-pointer px-3 py-2 font-mono text-[11px] uppercase
                              tracking-wider text-muted hover:text-ink">
            Order · {route.length} stop{route.length === 1 ? '' : 's'}
          </summary>
          <div className="px-3 pb-3">
            <Route stops={route} onMove={move} onRemove={toggle} />
          </div>
        </details>
      )}

      {!planning && <RaidInProgress />}

      {planning && hasPlan && !picking && (
        <button
          type="button"
          onClick={() => setPicking(true)}
          className="flex items-center gap-2 border border-line bg-panel px-3 py-2 text-left
                     font-mono text-[11px] uppercase tracking-wider text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          <span aria-hidden>▸</span>
          Add more to this plan
        </button>
      )}

      {building && (
      <div className="flex flex-col gap-3">
          {hasPlan && (
            <button
              type="button"
              onClick={() => setPicking(false)}
              className="flex items-center gap-2 self-start font-mono text-[11px] uppercase
                         tracking-wider text-muted transition-colors hover:text-ink
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              <span aria-hidden>▾</span>
              Done adding
            </button>
          )}

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

                      {/*
                        The name, and the note if there is one.

                        This used to say "something to pick up" or "a place", from a kind chosen
                        before the click. Nobody chooses that any more, and a line under "car
                        batteries" reading "something to pick up" was the label again wearing a
                        claim nobody made. A note is the only thing here worth a second line.
                      */}
                      <span className="min-w-0">
                        <span className="block text-sm">{mark.label}</span>
                        {mark.note && (
                          <span className="font-mono text-[11px] text-muted">{mark.note}</span>
                        )}
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
                          </span>

                          {/*
                            Named, and on the row you tick rather than a panel you open after.
                            "needs a key" said there was a problem; the name says whether you
                            already have it, which is the question you are actually asking.
                          */}
                          {o.required.length > 0 && (
                            <span className="mt-1 flex flex-wrap items-center gap-1">
                              {/*
                                Said, not implied. A bare row of item names beside a quest reads as
                                loot you are going after; these are the opposite — things you have
                                to have on you before you queue.
                              */}
                              <span className="font-mono text-[10px] uppercase tracking-wider text-muted">
                                bring
                              </span>

                              {o.required.map((need) => (
                                <span
                                  key={need.itemId}
                                  className={`rounded-sm px-1.5 py-0.5 text-[11px]
                                              ${need.isKey
                                                ? 'bg-need font-semibold text-ground'
                                                : 'border border-warn/70 bg-warn/10 text-warn'}`}
                                >
                                  {need.isKey ? 'key: ' : ''}{need.name}
                                </span>
                              ))}
                            </span>
                          )}
                        </span>

                        {/*
                          Handing the quest in, on the quest's own row.

                          Only on the first row a quest appears on: one with stops in two places
                          would otherwise offer the same control twice, and pressing either does
                          the same thing. Outside the label's text column so a click on it does
                          not also toggle the checkbox.
                        */}
                        {(() => {
                          const ready = turnInFor.get(o.taskId)
                          if (!ready || turnInRow.get(o.taskId) !== o.objectiveId) return null

                          const partial = ready.objectiveCount < ready.totalObjectiveCount

                          return (
                            <button
                              type="button"
                              onClick={async (e) => {
                                e.preventDefault()
                                await api.markTaskState(o.taskId, 'Completed')
                                void loadTurnIns()
                              }}
                              title={partial
                                ? `You planned ${ready.objectiveCount} of `
                                  + `${ready.totalObjectiveCount} objectives — marking it complete `
                                  + 'retires the items for the ones you did not.'
                                : `Hand to ${ready.traderName ?? 'the trader'}`}
                              className={`ml-auto shrink-0 self-center rounded-sm px-2.5 py-1
                                          font-mono text-[11px] uppercase tracking-wider
                                          transition-colors focus-visible:outline-2
                                          focus-visible:outline-accent
                                          ${partial
                                            ? 'bg-panel-hi text-warn hover:bg-warn hover:text-ground'
                                            : 'bg-panel-hi text-route hover:bg-accent hover:text-ground'}`}
                            >
                              Turned in{partial ? ' ?' : ''}
                            </button>
                          )
                        })()}
                      </label>
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
      </div>
      )}
    </div>
  )
}

/**
 * Why the planning controls are not here, and the two ways to get them back.
 *
 * <p>Said rather than implied. Controls that are simply absent read as a page that has broken, and
 * controls that are present but disabled read as a page that is being unhelpful — both send people
 * looking for a fault instead of for the button.</p>
 *
 * <p>Neither of these is new. They are the same two controls the in-raid strip at the top of the
 * page already carries; this is about making the state legible from where the missing controls
 * were, not about adding a way out.</p>
 */
function RaidInProgress() {
  return (
    <div className="flex flex-col gap-2 border border-accent/40 bg-accent/5 px-3 py-3">
      <p className="font-mono text-[11px] uppercase tracking-wider text-accent">
        Planning is closed while this raid runs
      </p>

      <p className="text-sm text-muted">
        You are in a raid with a plan already active. Building another one now would replace the
        plan you are walking, so the quest list and <strong className="text-ink">Plan this
        raid</strong> are unavailable until this raid is done with.
      </p>

      <div className="flex flex-wrap gap-4">
        <button
          type="button"
          onClick={() => void api.endRaid()}
          className="font-mono text-[11px] uppercase tracking-wider text-muted underline-offset-4
                     hover:text-ink hover:underline focus-visible:outline-2 focus-visible:outline-accent"
        >
          End raid
        </button>

        <button
          type="button"
          onClick={() => void api.clearPlan()}
          className="font-mono text-[11px] uppercase tracking-wider text-muted underline-offset-4
                     hover:text-ink hover:underline focus-visible:outline-2 focus-visible:outline-accent"
        >
          Clear plan
        </button>
      </div>

      <p className="font-mono text-xs text-muted">
        Ending the raid keeps the plan, so you can strike off what is no longer worth doing and
        queue again with the rest. Clearing it starts from nothing.
      </p>
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

      {/*
        No "next: Dorms · 455m · 43° right" here any more.

        It read as useful and was not: a straight line to a pin through whatever walls are between
        you and it, on a bearing relative to a heading that was current at your last position fix.
        Precise about something nobody can walk, and stale the moment you turn. What is left says
        only things that stay true — which map, how much of the plan is done, and how old the
        position is.
      */}

      <span className="font-mono text-xs text-muted tabular-nums">
        {/*
          Counted against this plan's own stops. Completed objectives outlive the plan they were
          finished under, so the raw count could exceed the plan — "1/0 done", which reads as
          nonsense because it is.
        */}
        {raid.stops.filter((s) => raid.completedObjectiveIds.includes(s.objectiveId)).length}
        /{raid.stops.length} done
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

      {/*
        Stops are strikeable between raids, which is what makes a plan worth keeping.

        No checkbox on a row, deliberately. There was one, and ticking it meant alt-tabbing out of
        a raid to do it — so it never got used and the plan stayed lit. **Quest done** on the right
        is the move that actually happens, afterwards, and it retires every objective of that quest
        including the ones that were never planned. The number stays because it is what ties this
        list to the overlay and to the map.
      */}
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
