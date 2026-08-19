import { useCallback, useEffect, useState } from 'react'
import { api, type TaskSummary } from './api'

type Filter = 'active' | 'available' | 'todo' | 'completed' | 'all'

const FILTERS: [Filter, string][] = [
  ['active', 'Active'],
  ['available', 'Available'],
  ['todo', 'To do'],
  ['completed', 'Done'],
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

        <p className="font-mono text-xs text-muted tabular-nums">{tasks.length} quests</p>
      </div>

      {loading && <Empty>loading…</Empty>}

      {!loading && tasks.length === 0 && (
        <Empty>
          {filter === 'active'
            ? 'No active quests. Switch to Available to see what you can pick up, and mark them active as you accept them in game.'
            : 'Nothing here.'}
        </Empty>
      )}

      {!loading && tasks.length > 0 && (
        <ul className="flex flex-col gap-px border border-line bg-line-soft">
          {tasks.map((task) => (
            <li key={task.id} className="flex flex-wrap items-center gap-3 bg-panel px-3 py-2.5">
              <div className="min-w-56 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={task.state === 'Completed' ? 'text-muted line-through' : ''}>
                    {task.name}
                  </span>
                  {task.kappa && <Tag className="border-warn/50 text-warn">κ</Tag>}
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
