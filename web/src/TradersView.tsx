import { useCallback, useEffect, useState } from 'react'
import { api, type Trader } from './api'

/**
 * Traders, and where you stand with each of them.
 *
 * Quests are organised by trader in the game — you go to Prapor, not to a quest list — so this is
 * the view that matches how the game is actually played. It leads with what each trader will give
 * you *right now*, because that is the reason to open it.
 *
 * Loyalty is set by hand. Nothing Escape from Tarkov writes to disk reports it, and the endpoint
 * that would needs account credentials RatNav will not ask for. It matters because hideout
 * upgrades gate on it, and RatNav shows those gates rather than guessing at them.
 */
export function TradersView() {
  const [traders, setTraders] = useState<Trader[] | null>(null)

  const load = useCallback(() => api.traders().then(setTraders).catch(() => setTraders([])), [])
  useEffect(() => { void load() }, [load])

  async function setLevel(name: string, level: number) {
    await api.setTraderLevel(name, level)
    await load()
  }

  if (!traders) return <p className="font-mono text-xs text-muted">loading traders…</p>
  if (traders.length === 0) return <p className="font-mono text-xs text-muted">no trader data</p>

  return (
    <div className="flex flex-col gap-px">
      {traders.map((trader) => (
        <section key={trader.name} className="flex flex-col gap-2 bg-panel px-3 py-3">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <h2 className="text-sm text-ink">{trader.name}</h2>

            <div className="flex items-center gap-1">
              <span className="font-mono text-[11px] uppercase tracking-wider text-muted">LL</span>
              {[1, 2, 3, 4].map((level) => (
                <button
                  key={level}
                  type="button"
                  aria-pressed={trader.level === level}
                  onClick={() => void setLevel(trader.name, level)}
                  className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                             hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  {level}
                </button>
              ))}
            </div>

            <p className="font-mono text-xs tabular-nums text-muted">
              {trader.completed}/{trader.total} done
              {trader.active > 0 && ` · ${trader.active} active`}
            </p>

            {trader.availableNow > 0 && (
              <p className="font-mono text-xs text-accent">
                {trader.availableNow} to pick up
              </p>
            )}
          </div>

          {trader.next.length > 0 && (
            <ul className="flex flex-wrap gap-x-4 gap-y-1">
              {trader.next.map((task) => (
                <li key={task.id} className="font-mono text-xs text-muted">
                  {task.wikiUrl ? (
                    <a
                      href={task.wikiUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="text-ink underline-offset-2 hover:text-accent hover:underline
                                 focus-visible:outline-2 focus-visible:outline-accent"
                    >
                      {task.name}
                    </a>
                  ) : (
                    <span className="text-ink">{task.name}</span>
                  )}
                  {task.minPlayerLevel != null && ` · lvl ${task.minPlayerLevel}`}
                </li>
              ))}
            </ul>
          )}
        </section>
      ))}
    </div>
  )
}
