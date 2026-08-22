import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type TaskSummary, type Trader, type WikiImage } from './api'

/**
 * Three groups, because those are the three that are true.
 *
 * Grouping by "reachable" was tried and dropped. RatNav can see prerequisites, character level and
 * the loyalty you have recorded — but not reputation or how much you have spent, so it can never
 * be certain a quest is available, and a tab claiming to know sorts quests you *can* take into a
 * list called Locked.
 *
 * For the same reason the rows say nothing about gates any more. They carried an "available" tag
 * and a line of "needs Prapor LL2" reasons, and both were RatNav guessing at a screen the player
 * is looking at. You can see the trader; RatNav cannot. So: All is a searchable list of every
 * quest, you make one active, and it moves to Active and then to Complete.
 */
type Filter = 'active' | 'complete' | 'all'

const FILTERS: [Filter, string][] = [
  ['active', 'Active'],
  ['complete', 'Complete'],
  ['all', 'All'],
]

/** The states a quest can be moved between, and how each reads. */
const STATES: [string, string][] = [
  ['NotStarted', 'Not started'],
  ['Active', 'Active'],
  ['Completed', 'Complete'],
  ['Failed', 'Failed'],
]

export function QuestsView() {
  const [filter, setFilter] = useState<Filter>('active')

  /** The quest whose wiki pictures are open, if any. */
  const [showing, setShowing] = useState<TaskSummary | null>(null)
  const [traders, setTraders] = useState<Trader[]>([])
  const [trader, setTrader] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [tasks, setTasks] = useState<TaskSummary[]>([])
  const [loading, setLoading] = useState(true)

  /**
   * Which row the keyboard is on.
   *
   * <p>Setting up a fresh install means marking fifty or more quests active, read off the game on
   * the other monitor. Done with the mouse that is fifty round trips of type, let go of the
   * keyboard, aim at a small button, come back — before you have seen a single thing RatNav is
   * good for. So the whole job is done from the search box: type part of a name, press Enter.</p>
   *
   * <p>An index rather than an id, because the list is re-fetched as you type and an id would
   * point at a row that is no longer shown.</p>
   */
  const [highlighted, setHighlighted] = useState(0)

  /**
   * Said out loud after a keystroke does something.
   *
   * <p>The row often scrolls out of view the moment it changes state, and a change you cannot see
   * is one you do not trust — so you reach for the mouse to check, which is the thing this exists
   * to avoid. Read by screen readers through aria-live, and shown next to the box for everybody
   * else.</p>
   */
  const [said, setSaid] = useState('')

  const searchBox = useRef<HTMLInputElement>(null)
  const rows = useRef<(HTMLLIElement | null)[]>([])

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

  const failed = shown.filter((t) => t.state === 'Failed').length

  async function setState(task: TaskSummary, state: string) {
    await api.setTaskState(task.id, state)
    // Marking a quest active is what makes its items appear on the Items view, so the list has
    // to reflect the new filter rather than leave a completed quest sitting under "Active".
    load()
  }

  // The highlight follows the list. Re-fetching on every keystroke means row 4 of the old results
  // is nothing in particular in the new ones.
  useEffect(() => { setHighlighted(0) }, [tasks, trader, filter])

  // Keep the highlighted row on screen. Without this, arrowing down walks off the bottom and the
  // row you are about to act on is the one you cannot see.
  useEffect(() => {
    rows.current[highlighted]?.scrollIntoView({ block: 'nearest' })
  }, [highlighted])

  /**
   * Marks the highlighted quest active, then clears the box.
   *
   * <p>The clearing is the point. Typing a name, pressing Enter and then having to select and
   * delete what you typed is three actions where there should be one — and with fifty quests to
   * enter, that difference is the whole feature. Type, Enter, type, Enter.</p>
   */
  async function activateHighlighted() {
    const task = shown[highlighted]

    if (!task) {
      setSaid(query ? `Nothing matches "${query}".` : 'Nothing to mark.')
      return
    }

    if (task.state === 'Active') {
      // Not an error and not silence. Saying so is what stops somebody typing it again.
      setSaid(`${task.name} was already active.`)
      setQuery('')
      return
    }

    await setState(task, 'Active')
    setSaid(`${task.name} is now active.`)
    setQuery('')
  }

  /**
   * The search box is the whole interface, so it carries the keys.
   *
   * <p>Arrows move the highlight without the caret leaving the text, which is what lets you narrow
   * a search and pick from what is left without your hands moving.</p>
   */
  function onSearchKey(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setHighlighted((at) => Math.min(at + 1, shown.length - 1))
      return
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault()
      setHighlighted((at) => Math.max(at - 1, 0))
      return
    }

    if (event.key === 'Enter') {
      event.preventDefault()
      void activateHighlighted()
      return
    }

    // Escape empties the box rather than blurring it. Somebody who mistypes a name wants to type
    // the next one, not to find their way back to the field.
    if (event.key === 'Escape') {
      event.preventDefault()
      setQuery('')
      setSaid('')
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {showing && <Photos task={showing} onClose={() => setShowing(null)} />}

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

        {/*
          A combobox over the list below, so a screen reader announces which row Enter would act
          on rather than leaving the highlight as something only sighted users can see.
        */}
        <input
          ref={searchBox}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={onSearchKey}
          role="combobox"
          aria-expanded={shown.length > 0}
          aria-controls="quest-list"
          aria-activedescendant={shown[highlighted] ? `quest-${shown[highlighted].id}` : undefined}
          aria-label="Search quests. Enter marks the highlighted quest active."
          placeholder={filter === 'all' ? 'Search every quest…' : 'Quest or trader…'}
          className="min-w-48 flex-1 rounded-sm border border-line bg-panel px-3 py-1.5 text-sm
                     text-ink placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
        />

        <p className="font-mono text-xs text-muted tabular-nums">
          {shown.length} quests
          {failed > 0 && <span className="text-need"> · {failed} failed</span>}
        </p>
      </div>

      {/*
        What the keys do, and what the last one did.

        The hint is here rather than in the guide because somebody setting up for the first time is
        looking at this page, not reading documentation — and this is the page where the shortcut
        saves an hour.

        aria-live is polite: it waits for a pause rather than interrupting, which is right for
        somebody typing quickly through a list of fifty.
      */}
      <div className="flex flex-wrap items-center justify-between gap-3 font-mono text-[11px]">
        <p className="text-muted">
          <kbd className="text-ink">Enter</kbd> marks the highlighted quest active
          {' · '}
          <kbd className="text-ink">↑</kbd> <kbd className="text-ink">↓</kbd> move
          {' · '}
          <kbd className="text-ink">Esc</kbd> clears
        </p>

        <p aria-live="polite" className="text-accent">{said}</p>
      </div>

      {/*
        Traders, with their loyalty. Set by hand: nothing the game writes to disk reports it, and
        the endpoint that would needs your account password. Clicking a name filters to their
        quests, which is how the game organizes them.
      */}
      <div className="flex flex-wrap gap-3">
        {traders.map((t) => {
          const next = t.levels?.find((l) => l.level === t.level + 1)
          const canRaise = t.level < 4 && (next?.reachable ?? true)

          return (
            <div
              key={t.name}
              className={`flex w-[104px] flex-col items-center gap-1 border bg-panel p-2
                          ${trader === t.name ? 'border-accent' : 'border-line'}`}
            >
              {/* The portrait, so this reads like the trader screen rather than a table of names. */}
              <button
                type="button"
                onClick={() => setTrader(trader === t.name ? null : t.name)}
                title={`Show only ${t.name}'s quests`}
                className="flex flex-col items-center gap-1
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {t.imageUrl ? (
                  <img
                    src={t.imageUrl}
                    alt=""
                    className={`size-14 object-cover transition-opacity
                                ${trader && trader !== t.name ? 'opacity-40' : ''}`}
                  />
                ) : (
                  <span className="grid size-14 place-items-center bg-panel-hi font-display
                                   text-xl font-bold text-muted">
                    {t.name.slice(0, 2)}
                  </span>
                )}

                <span className="text-xs text-ink">{t.name}</span>
              </button>

              {/*
                The level it is at, with one step either way. Four buttons with the current one lit
                meant reading four things to learn one, and the number that mattered was the same
                size as the three that did not.
              */}
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={t.level <= 1}
                  onClick={() => void setTraderLevel(t.name, t.level - 1)}
                  aria-label={`${t.name} one loyalty level lower`}
                  className="size-5 rounded-sm bg-panel-hi font-mono text-[11px] text-muted
                             transition-colors hover:text-ink disabled:opacity-25
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  −
                </button>

                <span className="font-mono text-sm tabular-nums text-accent">LL{t.level}</span>

                <button
                  type="button"
                  disabled={!canRaise}
                  title={canRaise
                    ? undefined
                    : `Needs character level ${next?.requiredPlayerLevel}`}
                  onClick={() => void setTraderLevel(t.name, t.level + 1)}
                  aria-label={`${t.name} one loyalty level higher`}
                  className="size-5 rounded-sm bg-panel-hi font-mono text-[11px] text-muted
                             transition-colors hover:text-ink disabled:opacity-25
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  +
                </button>
              </div>
            </div>
          )
        })}
      </div>

      {loading && <Empty>loading…</Empty>}

      {!loading && shown.length === 0 && (
        <Empty>
          {filter === 'active'
            ? 'No active quests. Search All for one you have picked up in game and mark it active.'
            : 'Nothing here.'}
        </Empty>
      )}

      {!loading && shown.length > 0 && (
        <ul id="quest-list" className="flex flex-col gap-px border border-line bg-line-soft">
          {shown.map((task, at) => (
            <li
              key={task.id}
              id={`quest-${task.id}`}
              ref={(row) => { rows.current[at] = row }}
              aria-selected={at === highlighted}
              className={`flex flex-wrap items-center gap-3 px-3 py-2.5 ${
                at === highlighted
                  ? 'bg-panel-hi ring-1 ring-inset ring-accent'
                  : 'bg-panel'
              }`}
            >
              <div className="min-w-56 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={
                    task.state === 'Completed' || task.state === 'Failed'
                      ? 'text-muted line-through'
                      : ''
                  }>
                    {task.name}
                  </span>

                  {/*
                    Failed is a finished state, and the Complete tab holds both — but they are not
                    the same thing, and a failed quest that reads as done is one you never go back
                    and look at.
                  */}
                  {task.state === 'Failed' && (
                    <Tag className="border-need/50 text-need">failed</Tag>
                  )}

                  {task.kappa && <Tag className="border-warn/50 text-warn">κ</Tag>}
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
                <button
                  type="button"
                  onClick={() => setShowing(task)}
                  title="Screenshots from the wiki: which building, which door"
                  className="font-mono text-xs text-muted hover:text-accent
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  photos
                </button>
              )}

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

/**
 * The wiki's screenshots for a quest, one at a time.
 *
 * <p>A pin says where to walk; a picture of the door says which door. The wiki has these for most
 * quests and RatNav had no way to reach them, so the answer to "which of these six identical
 * buildings" was a browser tab and a lost place in the list.</p>
 *
 * <p>Loaded from the wiki and credited to it. They are other people's work under CC BY-SA and are
 * never redistributed — the page shows them from where they live, and links back.</p>
 */
function Photos({ task, onClose }: { task: TaskSummary; onClose: () => void }) {
  const [images, setImages] = useState<WikiImage[] | null>(null)
  const [at, setAt] = useState(0)

  useEffect(() => {
    setImages(null)
    setAt(0)

    api.taskImages(task.id).then((r) => setImages(r.images)).catch(() => setImages([]))
  }, [task.id])

  // Arrow keys, because a carousel you have to aim at is one you stop using.
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowRight') setAt((n) => n + 1)
      if (e.key === 'ArrowLeft') setAt((n) => n - 1)
    }

    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [onClose])

  const count = images?.length ?? 0
  const index = count === 0 ? 0 : ((at % count) + count) % count
  const image = images?.[index]

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Wiki pictures for ${task.name}`}
      onClick={onClose}
      className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-full w-full max-w-4xl flex-col gap-3 border border-line bg-panel p-4"
      >
        <div className="flex items-center justify-between gap-4">
          <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
            {task.name}
            {count > 0 && <span className="text-ink"> · {index + 1} of {count}</span>}
          </h2>

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

        {images === null && <Empty>asking the wiki…</Empty>}

        {images !== null && count === 0 && (
          <Empty>The wiki article for this quest has no screenshots.</Empty>
        )}

        {image && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setAt(at - 1)}
              aria-label="Previous picture"
              disabled={count < 2}
              className="shrink-0 px-2 py-6 font-mono text-lg text-muted hover:text-ink
                         disabled:opacity-20 focus-visible:outline-2 focus-visible:outline-accent"
            >
              ‹
            </button>

            <img
              src={api.wikiPictureUrl(image.url)}
              alt={image.title}
              className="max-h-[65vh] min-h-0 min-w-0 flex-1 self-center object-contain"
            />

            <button
              type="button"
              onClick={() => setAt(at + 1)}
              aria-label="Next picture"
              disabled={count < 2}
              className="shrink-0 px-2 py-6 font-mono text-lg text-muted hover:text-ink
                         disabled:opacity-20 focus-visible:outline-2 focus-visible:outline-accent"
            >
              ›
            </button>
          </div>
        )}

        {/* Credit where it is due, and where the license requires it. */}
        <p className="text-xs text-muted">
          {image && <span className="font-mono">{image.title} · </span>}
          From the{' '}
          <a
            href={task.wikiUrl ?? '#'}
            target="_blank"
            rel="noreferrer"
            className="text-accent hover:underline"
          >
            Escape from Tarkov Wiki
          </a>
          , licensed CC BY-SA. RatNav shows them from the wiki and never redistributes them.
        </p>
      </div>
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
