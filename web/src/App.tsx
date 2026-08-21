import { useCallback, useEffect, useState } from 'react'
import { ago, api, type DataStatus, type MapSummary, type RaidView } from './api'
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

export default function App() {
  const [view, setView] = useState<View>('plan')
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
          <p className="font-mono text-[11px] uppercase tracking-[0.14em] text-muted">RatNav</p>
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
          <div className="w-[8.5rem] text-right font-mono text-xs leading-relaxed text-muted">
            <div className={status?.servingStale ? 'text-warn' : ''}>
              updated {ago(status?.fetchedAt ?? null)}
              {status?.servingStale && ' · serving cached data'}
            </div>
          </div>

          {/* An icon rather than the word. It is the same control either way and costs less room. */}
          <button
            type="button"
            onClick={refresh}
            disabled={refreshing}
            aria-label={refreshing ? 'Refreshing' : 'Refresh game data'}
            title="Refresh game data"
            className="grid size-8 place-items-center rounded-sm border border-line bg-panel-hi
                       text-sm text-muted transition-colors hover:border-muted hover:text-ink
                       disabled:opacity-50 focus-visible:outline-2 focus-visible:outline-accent"
          >
            <span aria-hidden className={refreshing ? 'animate-spin' : ''}>↻</span>
          </button>

          {/*
            Which character, behind a menu. It is switched occasionally — a row of three buttons
            for it sat at the top of every page looking as important as the navigation itself.
          */}
          <ProfileMenu onSwitched={() => window.location.reload()} />
        </div>
      </header>

      {status?.lastError && (
        <p className="border border-warn/30 bg-warn/5 px-3 py-2 font-mono text-xs text-warn">
          {status.lastError}
        </p>
      )}

      {view === 'plan' && <PlanView maps={maps} raid={raid} />}
      {view === 'items' && <ItemsView />}
      {view === 'hideout' && <HideoutView />}
      {view === 'quests' && <QuestsView />}
      {view === 'setup' && <SetupView />}

      {view === 'maps' && (
        <MapPicker maps={maps} selected={selected} onSelect={setSelected} />
      )}

      {view === 'maps' && (selected
        ? <MapView key={selected.id} map={selected} />
        : <p className="font-mono text-xs text-muted">no maps loaded</p>)}

      {view === 'maps' && <HeldBack onSettled={reloadMaps} />}

      <HotkeyBar />
    </div>
  )
}
