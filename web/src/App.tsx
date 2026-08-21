import { useCallback, useEffect, useState } from 'react'
import { ago, api, type DataStatus, type FirstRun, type MapSummary, type RaidView } from './api'
import { HeldBack } from './HeldBack'
import { HotkeyBar } from './HotkeyBar'
import { MapPicker } from './MapPicker'
import { ProfileMenu } from './ProfilePicker'
import { MapView } from './MapView'
import { HideoutView } from './HideoutView'
import { ItemsView } from './ItemsView'
import { QuestsView } from './QuestsView'
import { PlanView } from './PlanView'
import { SetupView } from './SetupView'

type View = 'plan' | 'items' | 'hideout' | 'quests' | 'maps' | 'setup'

/**
 * Your character level, in the navigation.
 *
 * <p>It decides which quests count as available — 109 of them gate on it — and it changes every
 * few raids. Buried in Setup it went stale, and a stale level quietly narrows every list
 * downstream of it without saying so.</p>
 *
 * <p>Saved as you press, with no Save button, because a number you nudge is not a form.</p>
 */
function Level() {
  const [level, setLevel] = useState<number | null>(null)

  useEffect(() => {
    api.settings().then((s) => setLevel(s.playerLevel)).catch(() => setLevel(null))
  }, [])

  async function step(by: number) {
    const next = Math.max(1, Math.min(79, (level ?? 1) + by))

    setLevel(next)
    await api.saveSettings({ playerLevel: next }).catch(() => {})
  }

  return (
    /*
      The label above the controls rather than beside them, which makes the block about the width
      of its own caption instead of the caption plus three controls. The header runs out of room
      before anything else on the page does, and this is the piece of it that was spending the
      most width to say the least.
    */
    <div className="flex flex-col items-center gap-0.5">
      <span className="font-mono text-[11px] uppercase tracking-wider text-muted">
        Character level
      </span>

      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={() => void step(-1)}
          aria-label="One level lower"
          className="size-8 rounded-sm bg-panel-hi font-mono text-sm text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent
                     sm:size-6 sm:text-xs"
        >
          −
        </button>

        <span className="w-7 text-center font-mono text-sm tabular-nums text-ink">
          {level ?? '—'}
        </span>

        <button
          type="button"
          onClick={() => void step(1)}
          aria-label="One level higher"
          className="size-8 rounded-sm bg-panel-hi font-mono text-sm text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent
                     sm:size-6 sm:text-xs"
        >
          +
        </button>
      </div>
    </div>
  )
}

/**
 * What a new install still has to do, and in what order.
 *
 * <p>A checklist that tracks itself rather than a wizard. Every step's state is <b>derived</b> —
 * setup is the required checks passing, quests is anything recorded, hideout is any level set, plan
 * is any saved plan — so nothing is stored and nothing can go stale. A remembered wizard position
 * goes wrong the moment somebody does the steps out of order, switches profile or restores from
 * another PC, and then insists on a step already done.</p>
 *
 * <p>It does not trap anybody either. A modal that demands step two is infuriating for the person
 * who wants to look at a map first; this says what is left and gets out of the way.</p>
 *
 * <p>The order matters and that is the reason for guiding it: each step is useless until the one
 * before it is done. Someone who finds the Plan page first sees an empty page and concludes RatNav
 * does not work.</p>
 */
/**
 * The wall in front of every page while RatNav cannot see the game.
 *
 * <p>Setup is a prerequisite, not the first of four tabs. With the game folder unset the item
 * list, the quest log, the hideout and the plan are all empty, and none of them can say why —
 * so somebody who opens Quests first spends several minutes deciding the app is broken. That is
 * not hypothetical: it is the first thing the first user test did.</p>
 *
 * <p>It names what is actually failing rather than saying "go to Setup", because the fix for a
 * missing game folder and the fix for a missing screenshot folder are different jobs.</p>
 */
function SetupFirst({ missing, onGo }: { missing: FirstRun['missing']; onGo: () => void }) {
  return (
    <section className="flex flex-col gap-3 border border-warn/40 bg-warn/5 px-4 py-4">
      <div>
        <p className="font-mono text-[11px] uppercase tracking-wider text-warn">Setup first</p>
        <p className="mt-1 text-sm text-ink">
          RatNav cannot see the game yet, so this page has nothing to show. Finish setup and
          everything here fills in.
        </p>
      </div>

      {missing.length > 0 && (
        <ul className="flex flex-col gap-1.5">
          {missing.map((check) => (
            <li key={check.name} className="text-sm">
              <span className="text-ink">{check.name}</span>
              <span className="block text-xs text-muted">{check.detail || check.fix}</span>
            </li>
          ))}
        </ul>
      )}

      <div>
        <button
          type="button"
          onClick={onGo}
          className="rounded-sm border border-accent bg-accent px-3 py-1.5 text-sm font-medium
                     text-ground transition-opacity hover:opacity-90
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          Go to Setup
        </button>
      </div>
    </section>
  )
}

function GettingStarted({ view, onGo }: { view: View; onGo: (view: View) => void }) {
  const [state, setState] = useState<FirstRun | null>(null)

  // Dismissal is remembered, because being told again on every launch is the wizard's worst habit
  // smuggled back in. Per browser rather than per install: it is a preference about being nagged.
  const [hidden, setHidden] = useState(
    () => localStorage.getItem('ratnav.gettingStarted') === 'dismissed')

  // Re-asked whenever the page changes, which is the cheapest possible proxy for "you may have
  // just done one of these". No polling: the answer only changes when you act.
  useEffect(() => {
    void api.firstRun().then(setState).catch(() => setState(null))
  }, [view])

  if (hidden || !state || state.done) return null

  const next = state.steps.find((s) => !s.done)

  return (
    <section className="flex flex-col gap-2 border border-accent/40 bg-accent/5 px-3 py-3">
      <div className="flex flex-wrap items-baseline gap-x-3">
        <p className="font-mono text-[11px] uppercase tracking-wider text-accent">Getting started</p>
        <p className="text-xs text-muted">
          {state.steps.filter((s) => s.done).length} of {state.steps.length} done
        </p>

        <button
          type="button"
          onClick={() => { localStorage.setItem('ratnav.gettingStarted', 'dismissed'); setHidden(true) }}
          className="ml-auto font-mono text-[11px] text-muted underline-offset-4 hover:text-ink
                     hover:underline focus-visible:outline-2 focus-visible:outline-accent"
        >
          hide this
        </button>
      </div>

      <ol className="flex flex-col gap-1.5">
        {state.steps.map((step, at) => (
          <li key={step.id} className="flex items-start gap-2.5">
            <span
              aria-hidden
              className={`mt-0.5 grid size-4 shrink-0 place-items-center rounded-full border
                          font-mono text-[10px]
                          ${step.done
                            ? 'border-have bg-have text-ground'
                            : 'border-line text-muted'}`}
            >
              {step.done ? '✓' : at + 1}
            </span>

            <span className="min-w-0">
              <button
                type="button"
                onClick={() => onGo(step.view as View)}
                className={`text-left text-sm underline-offset-4 hover:underline
                            focus-visible:outline-2 focus-visible:outline-accent
                            ${step.done ? 'text-muted line-through' : 'text-ink'}`}
              >
                {step.title}
              </button>

              {/* The reason, not just the instruction. "RatNav cannot read your quest log" earns
                  the click that "Go to Quests" does not. Only where it is still needed. */}
              {!step.done && <span className="block text-xs text-muted">{step.why}</span>}
            </span>
          </li>
        ))}
      </ol>

      {next && (
        <div>
          <button
            type="button"
            onClick={() => onGo(next.view as View)}
            className="rounded-sm border border-accent bg-accent px-3 py-1.5 text-sm font-medium
                       text-ground transition-opacity hover:opacity-90
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {next.title}
          </button>
        </div>
      )}
    </section>
  )
}

export default function App() {
  const [view, setView] = useState<View>('plan')

  /**
   * A first run opens on Setup, not on Plan.
   *
   * <p>The Plan page is the right home once RatNav knows where the game is. Before that it is an
   * empty page with no route to the thing that would fill it — which is exactly what the first
   * tester met: nothing ever asked for the Escape from Tarkov folder or the screenshot folder, and
   * "open Setup" lived in the README, which is not where a first run should have to happen.</p>
   *
   * <p>Asked of the diagnostics, which already marks which checks are <b>required</b>, so this
   * needs no new idea of what "set up" means. Once, on first load: a check that fails later — the
   * game folder renamed, say — must not drag somebody off the page they are reading.</p>
   */
  const [routed, setRouted] = useState(false)

  useEffect(() => {
    if (routed) return

    let live = true

    void api.diagnostics()
      .then((d) => {
        if (!live) return
        setRouted(true)

        if (d.checks.some((c) => c.required && !c.ok)) setView('setup')
      })
      .catch(() => setRouted(true))

    return () => { live = false }
  }, [routed])

  /**
   * Whether setup is finished, re-asked whenever the page changes.
   *
   * <p>Shared by the gate below and the getting-started checklist, and re-asked on navigation
   * for the same reason the checklist is: the answer only changes when you act, so there is
   * nothing to poll for. Finishing setup and clicking away is what clears the wall.</p>
   */
  const [firstRun, setFirstRun] = useState<FirstRun | null>(null)

  useEffect(() => {
    void api.firstRun().then(setFirstRun).catch(() => setFirstRun(null))
  }, [view])

  // Unknown is not blocked. If the service cannot be asked, a wall in front of every page would
  // be a worse answer than letting somebody look at whatever is there.
  const gated = view !== 'setup' && firstRun !== null && !firstRun.setupComplete

  const [status, setStatus] = useState<DataStatus | null>(null)
  const [maps, setMaps] = useState<MapSummary[]>([])
  const [selected, setSelected] = useState<MapSummary | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [raid, setRaid] = useState<RaidView | null>(null)

  // One subscription for the whole app: every view reads the same live state.
  useEffect(() => api.watchRaid(setRaid), [])

  useEffect(() => {
    api.status().then(setStatus).catch(() => setStatus(null))
    api.maps().then((all) => {
      setMaps(all)
      setSelected((current) => current ?? all.find((m) => m.calibrated) ?? all[0] ?? null)
    }).catch(() => setMaps([]))
  }, [])

  /** After a map is settled it becomes one RatNav will offer, so the list has to hear about it. */
  const reloadMaps = useCallback(async () => {
    try {
      setMaps(await api.maps())
    } catch {
      // Keeping the list we had beats emptying it.
    }
  }, [])

  async function refresh() {
    setRefreshing(true)
    try {
      setStatus(await api.refresh())
      setMaps(await api.maps())
    } catch {
      // A failed refresh is not fatal: the service keeps serving what it had, and the
      // freshness line below goes on telling the truth about how old that is.
    } finally {
      setRefreshing(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-full max-w-5xl flex-col gap-6 px-3 py-6 sm:px-5">
      <header className="flex flex-wrap items-end justify-between gap-x-4 gap-y-3 border-b border-line pb-4">
        <div>
          <ProfileMenu onSwitched={() => window.location.reload()} />
          {/*
            Six words that wrap rather than six words on one line.

            At desktop width this looks exactly as it did. On a phone the six run past the edge,
            and a navigation you have to scroll sideways to reach half of is not one. They are also
            smaller there, because a 3xl word is most of a phone's width on its own — and the tap
            targets stay honest through the vertical padding rather than through the font size.
          */}
          <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
            {(['plan', 'items', 'hideout', 'quests', 'maps', 'setup'] as View[]).map((id) => (
              <button
                key={id}
                type="button"
                onClick={() => setView(id)}
                aria-pressed={view === id}
                className="py-1 font-display text-2xl font-bold tracking-tight capitalize text-muted
                           transition-colors hover:text-ink aria-pressed:text-ink
                           focus-visible:outline-2 focus-visible:outline-accent sm:text-3xl"
              >
                {id}
              </button>
            ))}
          </div>
        </div>

        <div className="flex items-center gap-4">
          {/*
            Character level, where you can reach it. It gates which quests count as available, it
            changes constantly, and it was three clicks deep in Setup — which meant it went stale
            and quietly narrowed everything downstream of it.
          */}
          <Level />

          {/*
            Wide enough for the longest thing it can say, so it cannot move anything.

            This line is live — it counts up on its own, and every boundary it crosses changes its
            length. Left to size itself, pressing Refresh turned "updated never" into "updated just
            now" and the extra characters pushed this whole group onto a line of its own below the
            navigation. Text that changes on a timer has no business resizing the furniture around
            it.
          */}
          {/*
            Named, because a timestamp with no subject reads as the whole app being stale.

            It is when the quest, item and map catalogue was last fetched from tarkov.dev, and that
            only changes when the game does — so hours or days is the normal and correct state, and
            an unexplained old number sends people looking for a fault that is not there. Nothing
            else waits on it: raids, position fixes, plans and progress are all live.
          */}
          <div
            title={'When RatNav last fetched quests, items and maps from tarkov.dev. That data only '
              + 'changes when the game does, so this being hours or days old is normal. Everything '
              + 'else — your raid, your position, your plan — is live.'}
            className="w-[8.5rem] cursor-help text-right font-mono text-xs leading-relaxed text-muted"
          >
            <div className={status?.servingStale ? 'text-warn' : ''}>
              game data
              <span className="block">{ago(status?.fetchedAt ?? null)}</span>
              {status?.servingStale && <span className="block">serving cached copy</span>}
            </div>
          </div>

          {/* An icon rather than the word. It is the same control either way and costs less room. */}
          <button
            type="button"
            onClick={refresh}
            disabled={refreshing}
            aria-label={refreshing ? 'Refreshing' : 'Refresh game data from tarkov.dev'}
            title={'Fetch quests, items and maps from tarkov.dev again. Only worth pressing after a '
              + 'game patch — nothing else in RatNav waits on it.'}
            className="grid size-8 place-items-center rounded-sm border border-line bg-panel-hi
                       text-sm text-muted transition-colors hover:border-muted hover:text-ink
                       disabled:opacity-50 focus-visible:outline-2 focus-visible:outline-accent"
          >
            <span aria-hidden className={refreshing ? 'animate-spin' : ''}>↻</span>
          </button>
        </div>
      </header>

      <GettingStarted view={view} onGo={setView} />

      {status?.lastError && (
        <p className="border border-warn/30 bg-warn/5 px-3 py-2 font-mono text-xs text-warn">
          {status.lastError}
        </p>
      )}

      {/*
        Setup is the one page that is always reachable — it is the way out of this state, and a
        wall in front of the fix would be a locked door with the key behind it.
      */}
      {view === 'setup' && <SetupView />}

      {view !== 'setup' && firstRun && !firstRun.setupComplete && (
        <SetupFirst missing={firstRun.missing} onGo={() => setView('setup')} />
      )}

      {gated || (
        <>
          {view === 'plan' && <PlanView maps={maps} raid={raid} />}
          {view === 'items' && <ItemsView />}
          {view === 'hideout' && <HideoutView />}
          {view === 'quests' && <QuestsView />}

          {view === 'maps' && (
            <MapPicker maps={maps} selected={selected} onSelect={setSelected} />
          )}

          {view === 'maps' && (selected
            ? <MapView key={selected.id} map={selected} />
            : <p className="font-mono text-xs text-muted">no maps loaded</p>)}

          {view === 'maps' && <HeldBack onSettled={reloadMaps} />}
        </>
      )}

      <HotkeyBar />
    </div>
  )
}
