import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, type TrackedItem, type Trade } from './api'

type Tab = 'needed' | 'watchlist' | 'trades' | 'search'

/**
 * What to show of the list.
 *
 * <p>Filtering rather than only sorting. The two sort orders answered "what first", which is a
 * quiet difference on a list of two hundred rows — you had to read the whole thing to see that
 * anything had changed. What is actually wanted is a shorter list: the keys, or the
 * found-in-raid, or what a barter is waiting on.</p>
 */
const FILTERS = [
  { id: 'all', label: 'Everything', of: () => true },
  { id: 'fir', label: 'Found in raid', of: (r: TrackedItem) => r.foundInRaid },
  { id: 'quests', label: 'For quests', of: (r: TrackedItem) => r.questNeeded > 0 },
  { id: 'hideout', label: 'For the hideout', of: (r: TrackedItem) => r.hideoutNeeded > 0 },
  { id: 'trades', label: 'For a trade', of: (r: TrackedItem) => r.tradeNeeded > 0 },
  { id: 'keys', label: 'Keys', of: (r: TrackedItem) => r.isKey },
] as const

type FilterId = (typeof FILTERS)[number]['id']

export function ItemsView() {
  const [tab, setTab] = useState<Tab>('needed')
  const [query, setQuery] = useState('')

  // "next" leads with the hideout upgrades you are closest to finishing. The default leads with
  // quantity, which answers a different question: what to grab if you happen to see it.
  const [sort, setSort] = useState<'default' | 'next'>('default')

  // How far into the hideout build order to count. The overlay has the same dial; without it this
  // view is several hundred rows of things gated behind upgrades not yet started.
  const [lookAhead, setLookAhead] = useState(2)
  const [rows, setRows] = useState<TrackedItem[]>([])
  const [filter, setFilter] = useState<FilterId>('all')
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      if (tab === 'trades') {
        // Its own component, with its own data. Nothing to load here.
        setRows([])
      } else if (tab === 'search') {
        setRows(query.trim() ? await api.searchItems(query) : [])
      } else {
        setRows(tab === 'needed'
          ? await api.neededItems({ lookAhead, sort: sort === 'next' ? 'next' : undefined })
          : await api.watchlist())
      }
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [tab, query, sort, lookAhead])

  // Search runs as you type, so it waits for a pause rather than firing per keystroke.
  useEffect(() => {
    if (tab !== 'search') { load(); return }
    const timer = setTimeout(load, 200)
    return () => clearTimeout(timer)
  }, [load, tab])

  // Updates return the changed row, so one item changing doesn't reload the whole list and
  // throw away your scroll position.
  const replace = (updated: TrackedItem) =>
    setRows((current) =>
      // A row that has stopped being watched has no business on the watchlist. Left in place it
      // looked like the button had done nothing.
      tab === 'watchlist' && !updated.watched
        ? current.filter((r) => r.id !== updated.id)
        : current.map((r) => (r.id === updated.id ? updated : r)))

  // Filtering happens here rather than on the server: the list is already loaded, and a filter
  // that answers instantly is one people actually use.
  const shown = useMemo(
    () => rows.filter(FILTERS.find((f) => f.id === filter)?.of ?? (() => true)),
    [rows, filter])

  const totals = useMemo(() => ({
    items: shown.length,
    remaining: shown.reduce((sum, r) => sum + r.remaining, 0),
    fir: shown.filter((r) => r.foundInRaid).length,
  }), [shown])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex gap-px">
          {(['needed', 'watchlist', 'trades', 'search'] as Tab[]).map((id) => (
            <button
              key={id}
              type="button"
              aria-pressed={tab === id}
              onClick={() => setTab(id)}
              className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted capitalize transition-colors
                         hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {id === 'trades' ? 'barters & crafts' : id}
            </button>
          ))}
        </div>

        {tab === 'needed' && (
          <div className="flex items-center gap-1">
            <span className="font-mono text-[11px] uppercase tracking-wider text-muted">Sort</span>
            {([['default', 'Most needed'], ['next', 'Nearest upgrade']] as const).map(([id, text]) => (
              <button
                key={id}
                type="button"
                aria-pressed={sort === id}
                onClick={() => setSort(id)}
                className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                           hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {text}
              </button>
            ))}
          </div>
        )}

        {tab === 'needed' && (
          <div className="flex items-center gap-1">
            <span className="font-mono text-[11px] uppercase tracking-wider text-muted">Look ahead</span>
            <button
              type="button"
              disabled={lookAhead <= 1}
              onClick={() => setLookAhead(lookAhead - 1)}
              aria-label="Look one upgrade less far ahead"
              className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                         hover:text-ink disabled:opacity-30
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              −
            </button>
            <span className="w-6 text-center font-mono text-xs tabular-nums text-ink">{lookAhead}</span>
            <button
              type="button"
              disabled={lookAhead >= 6}
              onClick={() => setLookAhead(lookAhead + 1)}
              aria-label="Look one upgrade further ahead"
              className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                         hover:text-ink disabled:opacity-30
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              +
            </button>
          </div>
        )}

        {tab === 'search' && (
          <input
            autoFocus
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Item name or short name…"
            className="min-w-56 flex-1 rounded-sm border border-line bg-panel px-3 py-1.5 text-sm
                       text-ink placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
          />
        )}

        {tab !== 'trades' && (
          <p className="ml-auto font-mono text-xs text-muted tabular-nums">
            {totals.items} items · {totals.remaining} still needed · {totals.fir} found-in-raid
          </p>
        )}
      </div>

      {/* Filters, on the lists where there is enough to filter. */}
      {(tab === 'needed' || tab === 'search') && rows.length > 0 && (
        <div className="flex flex-wrap gap-px">
          {FILTERS.map((f) => {
            const count = rows.filter(f.of).length

            return (
              <button
                key={f.id}
                type="button"
                disabled={count === 0}
                aria-pressed={filter === f.id}
                onClick={() => setFilter(f.id)}
                className="rounded-sm bg-panel-hi px-2.5 py-1 font-mono text-[11px] text-muted
                           tabular-nums transition-colors hover:text-ink disabled:opacity-30
                           aria-pressed:bg-accent aria-pressed:text-ground
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {f.label} {count}
              </button>
            )
          })}
        </div>
      )}

      {tab === 'watchlist' && (
        <p className="text-xs text-muted">
          These numbers are yours. <b>Need</b> is the amount you are after, and <b>Have</b> is what
          you have set aside for it — kept apart from your stash count, so items promised to a
          quest or a hideout upgrade are not counted as available here too.
        </p>
      )}

      {tab === 'trades' && <Trades />}

      {tab !== 'trades' && loading && <Empty>loading…</Empty>}

      {tab !== 'trades' && !loading && shown.length === 0 && rows.length > 0 && (
        <Empty>Nothing here matches that filter.</Empty>
      )}

      {tab !== 'trades' && !loading && rows.length === 0 && (
        <Empty>
          {tab === 'needed'
            ? 'Nothing needed. Mark some quests active on the Quests view and they will appear here.'
            : tab === 'watchlist'
              ? 'Nothing on the watchlist. Search for an item and star it to keep an eye on it.'
              : query.trim() ? 'No items match that.' : 'Type to search 5,000-odd items.'}
        </Empty>
      )}

      {tab !== 'trades' && !loading && shown.length > 0 && (
        <div className="overflow-x-auto border border-line">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-line bg-panel text-left">
                <Th className="w-full">Item</Th>
                <Th align="right">Need</Th>
                <Th align="center">Have</Th>
                <Th align="right">Left</Th>
                <Th align="center">{tab === 'watchlist' ? 'Remove' : 'Watch'}</Th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => (
                <Row key={row.id} row={row} onChange={replace} watchlist={tab === 'watchlist'} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/**
 * The barters and crafts on offer, and the ones you have decided to work towards.
 *
 * <p>Picking one puts its inputs on your list, counted apart from what quests and the hideout
 * want. That separation is the point: an item wanted three times for a quest and seven for a
 * barter is two reasons, and a single "10" hides that finishing the quest leaves seven to
 * find.</p>
 */
function Trades() {
  const [rows, setRows] = useState<Trade[]>([])
  const [query, setQuery] = useState('')

  /** Trades you cannot do yet — the trader is not high enough, or the station is not built. */
  const [all, setAll] = useState(false)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await api.trades({ q: query.trim() || undefined, all }))
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [query, all])

  useEffect(() => {
    const timer = setTimeout(load, query ? 200 : 0)
    return () => clearTimeout(timer)
  }, [load, query])

  async function pick(trade: Trade, tracked: boolean) {
    await api.trackTrade(trade.id, trade.kind, tracked, trade.times)
    setRows((current) => current.map((r) => (r.id === trade.id ? { ...r, tracked } : r)))
  }

  const picked = rows.filter((r) => r.tracked).length

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="What it makes, who offers it, or what it costs…"
          className="min-w-56 flex-1 rounded-sm border border-line bg-panel px-3 py-1.5 text-sm
                     text-ink placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
        />

        <label className="flex items-center gap-2 text-xs text-muted">
          <input
            type="checkbox"
            checked={all}
            onChange={(e) => setAll(e.target.checked)}
            className="accent-accent"
          />
          Include ones I cannot do yet
        </label>

        <p className="ml-auto font-mono text-xs text-muted tabular-nums">
          {picked} being worked towards
        </p>
      </div>

      <p className="text-xs text-muted">
        Only what your traders and hideout can actually offer, unless you ask for the rest. Pick one
        and what it costs joins your list — <b>counted apart</b> from quests and the hideout, so
        finishing a quest does not make a barter look done.
      </p>

      {loading && <Empty>loading…</Empty>}

      {!loading && rows.length === 0 && (
        <Empty>
          {query.trim()
            ? 'Nothing on offer matches that.'
            : 'No barters or crafts available. Set your trader levels on the Quests view and your '
              + 'station levels on the Hideout view, and what you can do will appear here.'}
        </Empty>
      )}

      {!loading && rows.length > 0 && (
        <ul className="flex flex-col gap-px border border-line">
          {rows.map((trade) => (
            <li
              key={trade.id}
              className={`flex flex-wrap items-start gap-x-4 gap-y-1 px-3 py-2
                          ${trade.tracked ? 'bg-accent/5' : ''}`}
            >
              <label className="flex cursor-pointer items-center gap-2.5">
                <input
                  type="checkbox"
                  checked={trade.tracked}
                  onChange={(e) => void pick(trade, e.target.checked)}
                  className="accent-accent"
                />
                <span className="text-sm">{trade.makes}</span>
              </label>

              <span className="font-mono text-[11px] text-muted">
                {trade.kind === 'barter'
                  ? `${trade.source} LL${trade.level}`
                  : `${trade.source} ${trade.level}`}
                {!trade.available && ' · not yet'}
              </span>

              {/* What it costs, and how much of that you already have. */}
              <span className="ml-auto flex flex-wrap justify-end gap-x-3 font-mono text-[11px]">
                {trade.costs.map((cost) => (
                  <span
                    key={cost.itemId}
                    className={cost.have >= cost.count ? 'text-have' : 'text-muted'}
                  >
                    {cost.count}× {cost.name}
                    {cost.have > 0 && ` (${Math.min(cost.have, cost.count)})`}
                  </span>
                ))}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function Row({
  row, onChange, watchlist,
}: {
  row: TrackedItem
  onChange: (item: TrackedItem) => void
  /** On the watchlist the numbers are yours: an editable target, and a count kept apart from the stash. */
  watchlist: boolean
}) {
  const [busy, setBusy] = useState(false)

  /**
   * The watchlist keeps its own count.
   *
   * Twenty bundles of wires with fifteen earmarked for the hideout is not twenty available for a
   * barter, and one shared number says it is — which is how you spend something already promised.
   */
  const setWatchHave = async (value: string) => {
    const have = Math.max(0, Number.parseInt(value, 10) || 0)
    if (have === row.have) return

    setBusy(true)
    try { onChange(await api.setWatch(row.id, true, { have })) } finally { setBusy(false) }
  }

  async function setTarget(value: string) {
    const target = value.trim() === '' ? null : Math.max(0, Number(value) || 0)

    setBusy(true)
    try {
      // Setting a target is also how you put something on the watchlist — wanting four of a thing
      // and watching it are the same statement.
      onChange(await api.setWatch(row.id, true, { target }))
    } finally {
      setBusy(false)
    }
  }

  const adjust = async (delta: number) => {
    setBusy(true)
    try { onChange(await api.setHave(row.id, { delta })) } finally { setBusy(false) }
  }

  const setExact = async (value: string) => {
    const count = Number.parseInt(value, 10)
    if (Number.isNaN(count) || count === row.have) return
    setBusy(true)
    try { onChange(await api.setHave(row.id, { count })) } finally { setBusy(false) }
  }

  const toggleWatch = async () => {
    setBusy(true)
    try { onChange(await api.setWatch(row.id, !row.watched)) } finally { setBusy(false) }
  }

  return (
    <tr className="border-b border-line-soft last:border-0 hover:bg-panel/60">
      <Td>
        <div className="flex items-center gap-2.5">
          {row.iconUrl
            ? <img src={row.iconUrl} alt="" loading="lazy" className="size-7 flex-none rounded-xs bg-panel-hi object-contain" />
            : <div className="size-7 flex-none rounded-xs bg-panel-hi" />}
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              {/*
                Deep-linked rather than scraped, so the guidance stays current with zero
                maintenance — the wiki page is where spawn locations actually live, and it is
                kept up to date by people playing the game.
              */}
              {row.wikiUrl ? (
                <a
                  href={row.wikiUrl}
                  target="_blank"
                  rel="noreferrer"
                  title={`${row.name} on the wiki — spawns, uses, and barters`}
                  className="truncate text-accent underline-offset-2 hover:underline
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  {row.name}
                </a>
              ) : (
                <span className="truncate">{row.name}</span>
              )}
              {/* Shape as well as colour: found-in-raid changes what you do with an item, and
                  it has to survive colour-blindness. */}
              {row.foundInRaid && <Tag className="border-need/50 text-need">◆ FIR</Tag>}
              {row.isKey && <Tag className="border-route/50 text-route">▲ key</Tag>}
              {row.hideoutWave === 1 && <Tag className="border-accent/50 text-accent">● now</Tag>}
            </div>
            <div className="font-mono text-[11px] text-muted">
              {[
                row.questNeeded > 0 && `${row.questNeeded} for quests`,
                // Named rather than counted. "4 for Medstation 3" tells you whether to keep the
                // thing; "4 for hideout" does not.
                row.hideoutNeeded > 0 && (row.hideoutUpgrade
                  ? `${row.hideoutNeeded} for ${row.hideoutUpgrade}`
                  : `${row.hideoutNeeded} for hideout`),

                // Named the same way, and separately — the whole reason trades are counted apart
                // is so this line can say which of the two the number is for.
                row.tradeNeeded > 0
                  && `${row.tradeNeeded} for ${row.tradeFor.join(', ') || 'a trade'}`,
                row.watchNote,
              ].filter(Boolean).join(' · ') || row.shortName}
            </div>
          </div>
        </div>
      </Td>

      <Td align="right">
        {/*
          Quest and hideout needs are worked out; a watchlist target is not, so it is editable.
          Showing a derived number in a box you can type in would be a lie about what it is.
        */}
        {!watchlist && (row.questNeeded > 0 || row.hideoutNeeded > 0 || row.tradeNeeded > 0) ? (
          <span className="font-mono text-xs tabular-nums">{row.needed}</span>
        ) : (
          <input
            key={`t-${row.watchTarget}`}
            defaultValue={row.watchTarget ?? ''}
            placeholder="—"
            onBlur={(e) => setTarget(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            aria-label={`how many ${row.name} you want`}
            className="w-12 rounded-sm border border-line bg-panel px-1 py-0.5 text-center font-mono
                       text-xs tabular-nums placeholder:text-muted
                       focus-visible:outline-2 focus-visible:outline-accent"
          />
        )}
      </Td>

      <Td align="center">
        <div className="flex items-center justify-center gap-1">
          <Step onClick={() => adjust(-1)} disabled={busy || row.have === 0 || watchlist} label="one fewer">−</Step>
          <input
            key={row.have}
            defaultValue={row.have}
            onBlur={(e) => (watchlist ? setWatchHave(e.target.value) : setExact(e.target.value))}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            aria-label={`how many ${row.name} you have`}
            className="w-12 rounded-sm border border-line bg-panel px-1 py-0.5 text-center font-mono
                       text-xs tabular-nums focus-visible:outline-2 focus-visible:outline-accent"
          />
          <Step onClick={() => adjust(1)} disabled={busy || watchlist} label="one more">+</Step>
        </div>
      </Td>

      <Td align="right" mono className={row.remaining === 0 ? 'text-have' : ''}>
        {row.remaining === 0 ? '✓' : row.remaining}
      </Td>

      <Td align="center">
        {/*
          A star to add, a cross to remove. On the watchlist "un-star" is the same action as
          "take this off the list", and saying the second is clearer than implying it.
        */}
        <button
          type="button"
          onClick={toggleWatch}
          disabled={busy}
          aria-pressed={row.watched}
          aria-label={watchlist
            ? `remove ${row.name} from the watchlist`
            : row.watched ? `stop watching ${row.name}` : `watch ${row.name}`}
          className={`rounded-sm px-1.5 transition-colors focus-visible:outline-2
                      focus-visible:outline-accent ${watchlist
                        ? 'text-muted hover:text-need'
                        : 'text-muted hover:text-warn aria-pressed:text-warn'}`}
        >
          {watchlist ? '✕' : row.watched ? '★' : '☆'}
        </button>
      </Td>
    </tr>
  )
}

function Step({ onClick, disabled, label, children }: {
  onClick: () => void; disabled: boolean; label: string; children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className="size-6 rounded-sm bg-panel-hi text-muted transition-colors hover:text-ink
                 disabled:opacity-30 focus-visible:outline-2 focus-visible:outline-accent"
    >
      {children}
    </button>
  )
}

const Th = ({ children, align = 'left', className = '' }: {
  children: React.ReactNode; align?: 'left' | 'right' | 'center'; className?: string
}) => (
  <th className={`px-3 py-2 font-mono text-[11px] font-normal uppercase tracking-wider text-muted text-${align} ${className}`}>
    {children}
  </th>
)

const Td = ({ children, align = 'left', mono = false, className = '' }: {
  children: React.ReactNode; align?: 'left' | 'right' | 'center'; mono?: boolean; className?: string
}) => (
  <td className={`px-3 py-2 text-${align} ${mono ? 'font-mono tabular-nums' : ''} ${className}`}>
    {children}
  </td>
)

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
