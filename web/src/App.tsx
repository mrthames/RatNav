import { useEffect, useState } from 'react'
import { ago, api, type DataStatus, type MapSummary, type RaidView } from './api'
import { MapView } from './MapView'
import { HideoutView } from './HideoutView'
import { ItemsView } from './ItemsView'
import { TradersView } from './TradersView'
import { QuestsView } from './QuestsView'
import { PlanView } from './PlanView'
import { SetupView } from './SetupView'

type View = 'plan' | 'items' | 'hideout' | 'quests' | 'traders' | 'maps' | 'setup'

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
            {(['plan', 'items', 'hideout', 'quests', 'traders', 'maps', 'setup'] as View[]).map((id) => (
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
          <div className="text-right font-mono text-xs leading-relaxed text-muted">
            <div>
              {status?.taskCount ?? 0} quests · {status?.itemCount ?? 0} items ·{' '}
              {status?.calibratedMapCount ?? 0} maps
            </div>
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
      {view === 'traders' && <TradersView />}
      {view === 'setup' && <SetupView />}

      {view === 'maps' && <div className="flex flex-wrap gap-px">
        {maps.map((map) => (
          <button
            key={map.id}
            type="button"
            aria-pressed={selected?.id === map.id}
            onClick={() => setSelected(map)}
            className="rounded-sm bg-panel px-3 py-2 text-sm text-muted transition-colors
                       hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            {map.name}
          </button>
        ))}
      </div>}

      {view === 'maps' && (selected
        ? <MapView key={selected.id} map={selected} />
        : <p className="font-mono text-xs text-muted">no maps loaded</p>)}
    </div>
  )
}
