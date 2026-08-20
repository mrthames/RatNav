import { useCallback, useEffect, useState } from 'react'
import { ago, api, type DataStatus, type MapSummary, type RaidView } from './api'
import { HeldBack } from './HeldBack'
import { HotkeyBar } from './HotkeyBar'
import { MapPicker } from './MapPicker'
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
    <div className="flex items-center gap-1">
      <span className="font-mono text-[11px] uppercase tracking-wider text-muted">
        Character level
      </span>

      <button
        type="button"
        onClick={() => void step(-1)}
        aria-label="One level lower"
        className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                   hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
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
        className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                   hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
      >
        +
      </button>
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
    <div className="mx-auto flex min-h-full max-w-5xl flex-col gap-6 px-5 py-6">
      <header className="flex flex-wrap items-end justify-between gap-4 border-b border-line pb-4">
        <div>
          <p className="font-mono text-[11px] uppercase tracking-[0.14em] text-muted">RatNav</p>
          <div className="flex items-baseline gap-4">
            {(['plan', 'items', 'hideout', 'quests', 'maps', 'setup'] as View[]).map((id) => (
              <button
                key={id}
                type="button"
                onClick={() => setView(id)}
                aria-pressed={view === id}
                className="font-display text-3xl font-bold tracking-tight capitalize text-muted
                           transition-colors hover:text-ink aria-pressed:text-ink
                           focus-visible:outline-2 focus-visible:outline-accent"
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

          <div className="text-right font-mono text-xs leading-relaxed text-muted">
            <div className={status?.servingStale ? 'text-warn' : ''}>
              updated {ago(status?.fetchedAt ?? null)}
              {status?.servingStale && ' · serving cached data'}
            </div>
          </div>
          <button
            type="button"
            onClick={refresh}
            disabled={refreshing}
            className="rounded-sm border border-line bg-panel-hi px-3 py-2 text-xs text-muted
                       transition-colors hover:border-muted hover:text-ink disabled:opacity-50
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </button>
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
