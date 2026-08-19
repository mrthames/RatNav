import { useCallback, useEffect, useState } from 'react'
import { api, type TaskSummary, type Trader } from './api'

/**
 * Four groups, each answering a different question.
 *
 * "Available" was never redundant with "active" — one is what you accepted, the other is what you
 * could accept — it was just badly named, so it is "Ready" now. "To do" genuinely was redundant
 * and is gone. "Locked" is new, and holds what is waiting on a level or an earlier quest.
 */
type Filter = 'active' | 'ready' | 'done' | 'locked' | 'all'

const FILTERS: [Filter, string][] = [
  ['active', 'Active'],
  ['ready', 'Ready'],
  ['done', 'Done'],
  ['locked', 'Locked'],
  ['all', 'All'],
]

/** The states a quest can be moved between, and how each reads. */
const STATES: [string, string][] = [
  ['NotStarted', 'Not started'],
  ['Active', 'Active'],
  ['Completed', 'Done'],
  ['Failed', 'Failed'],
]

export function QuestsView() {
  const [filter, setFilter] = useState<Filter>('active')
  const [traders, setTraders] = useState<Trader[]>([])
  const [trader, setTrader] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [tasks, setTasks] = useState<TaskSummary[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setTasks(await api.tasks(filter, query))
    } catch {
      setTasks([])
    } finally {
      setLoading(false)
    }
  }, [filter, query])

  useEffect(() => {
    const timer = setTimeout(load, query ? 200 : 0)
    return () => clearTimeout(timer)
  }, [load, query])

  const loadTraders = useCallback(
    () => api.traders().then(setTraders).catch(() => setTraders([])), [])

  useEffect(() => { void loadTraders() }, [loadTraders])

  async function setTraderLevel(name: string, level: number) {
    await api.setTraderLevel(name, level)
    await loadTraders()
  }

  // Quests belong to traders in the game — you go to Prapor, not to a quest list — so the trader
  // is a filter here rather than a separate tab showing the same quests a second time.
  const shown = trader ? tasks.filter((t) => t.traderName === trader) : tasks

  async function setState(task: TaskSummary, state: string) {
    await api.setTaskState(task.id, state)
    // Marking a quest active is what makes its items appear on the Items view, so the list has
    // to reflect the new filter rather than leave a completed quest sitting under "Active".
    load()
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex gap-px">
          {FILTERS.map(([id, label]) => (
            <button
              key={id}
              type="button"
              aria-pressed={filter === id}
              onClick={() => setFilter(id)}
              className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted transition-colors
                         hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {label}
            </button>
          ))}
        </div>

        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Quest or trader…"
          className="min-w-48 flex-1 rounded-sm border border-line bg-panel px-3 py-1.5 text-sm
                     text-ink placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
        />

        <p className="font-mono text-xs text-muted tabular-nums">{shown.length} quests</p>
      </div>

      {/*
        Traders, with their loyalty. Set by hand: nothing the game writes to disk reports it, and
        the endpoint that would needs your account password. Clicking a name filters to their
        quests, which is how the game organises them.
      */}
      <div className="flex flex-wrap gap-px border border-line bg-line-soft">
        {traders.map((t) => (
          <div
            key={t.name}
            className={`flex items-center gap-2 bg-panel px-2.5 py-1.5 ${
              trader === t.name ? 'ring-1 ring-accent ring-inset' : ''}`}
          >
            <button
              type="button"
              onClick={() => setTrader(trader === t.name ? null : t.name)}
              className="text-sm text-ink hover:text-accent
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {t.name}
            </button>

            <span className="font-mono text-[11px] text-muted">LL</span>
            {[1, 2, 3, 4].map((level) => (
              <button
                key={level}
                type="button"
                aria-pressed={t.level === level}
                aria-label={`${t.name} loyalty ${level}`}
                onClick={() => void setTraderLevel(t.name, level)}
                className="size-5 rounded-sm bg-panel-hi font-mono text-[11px] text-muted
                           transition-colors hover:text-ink aria-pressed:bg-accent
                           aria-pressed:text-ground focus-visible:outline-2 focus-visible:outline-accent"
              >
                {level}
              </button>
            ))}

            {t.availableNow > 0 && (
              <span className="font-mono text-[11px] text-accent">{t.availableNow} ready</span>
            )}
          </div>
        ))}
      </div>

      {loading && <Empty>loading…</Empty>}

      {!loading && shown.length === 0 && (
        <Empty>
          {filter === 'active'
            ? 'No active quests. Switch to Ready to see what you can pick up, and mark them active as you accept them in game.'
            : filter === 'locked'
              ? 'Nothing locked — everything is either done or within reach.'
              : 'Nothing here.'}
        </Empty>
      )}

      {!loading && shown.length > 0 && (
        <ul className="flex flex-col gap-px border border-line bg-line-soft">
          {shown.map((task) => (
            <li key={task.id} className="flex flex-wrap items-center gap-3 bg-panel px-3 py-2.5">
              <div className="min-w-56 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={task.state === 'Completed' ? 'text-muted line-through' : ''}>
                    {task.name}
                  </span>
                  {task.kappa && <Tag className="border-warn/50 text-warn">κ</Tag>}
                  {/* A padlock tells you nothing; "needs level 20" tells you what to do. */}
                  {task.blockers.map((why) => (
                    <span key={why} className="font-mono text-[11px] text-warn">{why}</span>
                  ))}
                  {task.available && task.state === 'NotStarted' && (
                    <Tag className="border-have/50 text-have">available</Tag>
                  )}
                  {task.positionedObjectiveCount > 0 && (
                    <Tag className="border-line text-muted">
                      {task.positionedObjectiveCount} on map
                    </Tag>
                  )}
                </div>
                <div className="font-mono text-[11px] text-muted">
                  {[task.traderName, task.minPlayerLevel ? `level ${task.minPlayerLevel}+` : null]
                    .filter(Boolean)
                    .join(' · ')}
                </div>
              </div>

              <div className="flex gap-px">
                {STATES.map(([id, label]) => (
                  <button
                    key={id}
                    type="button"
                    aria-pressed={task.state === id}
                    onClick={() => setState(task, id)}
                    className="rounded-sm bg-panel-hi px-2 py-1 text-xs text-muted transition-colors
                               hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                               focus-visible:outline-2 focus-visible:outline-accent"
                  >
                    {label}
                  </button>
                ))}
              </div>

              {task.wikiUrl && (
                <a
                  href={task.wikiUrl}
                  target="_blank"
                  rel="noreferrer"
                  title="Open the wiki page for this quest"
                  className="font-mono text-xs text-muted hover:text-accent
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  wiki ↗
                </a>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

const Tag = ({ children, className }: { children: React.ReactNode; className: string }) => (
  <span className={`flex-none rounded-xs border px-1 font-mono text-[10px] tracking-wide ${className}`}>
    {children}
  </span>
)

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="border border-line bg-panel px-4 py-8 text-center font-mono text-xs text-muted">
    {children}
  </p>
)
