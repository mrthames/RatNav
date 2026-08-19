import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, type TrackedItem } from './api'

type Tab = 'needed' | 'watchlist' | 'search'

export function ItemsView() {
  const [tab, setTab] = useState<Tab>('needed')
  const [query, setQuery] = useState('')

  // "next" leads with the hideout upgrades you are closest to finishing. The default leads with
  // quantity, which answers a different question: what to grab if you happen to see it.
  const [sort, setSort] = useState<'default' | 'next'>('default')
  const [rows, setRows] = useState<TrackedItem[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      if (tab === 'search') {
        setRows(query.trim() ? await api.searchItems(query) : [])
      } else {
        setRows(tab === 'needed'
          ? await api.neededItems({ sort: sort === 'next' ? 'next' : undefined })
          : await api.watchlist())
      }
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [tab, query, sort])

  // Search runs as you type, so it waits for a pause rather than firing per keystroke.
  useEffect(() => {
    if (tab !== 'search') { load(); return }
    const timer = setTimeout(load, 200)
    return () => clearTimeout(timer)
  }, [load, tab])

  // Updates return the changed row, so one item changing doesn't reload the whole list and
  // throw away your scroll position.
  const replace = (updated: TrackedItem) =>
    setRows((current) => current.map((r) => (r.id === updated.id ? updated : r)))

  const totals = useMemo(() => ({
    items: rows.length,
    remaining: rows.reduce((sum, r) => sum + r.remaining, 0),
    fir: rows.filter((r) => r.foundInRaid).length,
  }), [rows])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex gap-px">
          {(['needed', 'watchlist', 'search'] as Tab[]).map((id) => (
            <button
              key={id}
              type="button"
              aria-pressed={tab === id}
              onClick={() => setTab(id)}
              className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted capitalize transition-colors
                         hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {id}
            </button>
          ))}
        </div>

        {tab === 'needed' && (
          <div className="flex gap-px">
            {([['default', 'By amount'], ['next', "What's next"]] as const).map(([id, text]) => (
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

        <p className="ml-auto font-mono text-xs text-muted tabular-nums">
          {totals.items} items · {totals.remaining} still needed · {totals.fir} found-in-raid
        </p>
      </div>

      {loading && <Empty>loading…</Empty>}

      {!loading && rows.length === 0 && (
        <Empty>
          {tab === 'needed'
            ? 'Nothing needed. Mark some quests active on the Quests view and they will appear here.'
            : tab === 'watchlist'
              ? 'Nothing on the watchlist. Search for an item and star it to keep an eye on it.'
              : query.trim() ? 'No items match that.' : 'Type to search 5,000-odd items.'}
        </Empty>
      )}

      {!loading && rows.length > 0 && (
        <div className="overflow-x-auto border border-line">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-line bg-panel text-left">
                <Th className="w-full">Item</Th>
                <Th align="right">Need</Th>
                <Th align="center">Have</Th>
                <Th align="right">Left</Th>
                <Th align="right">Flea</Th>
                <Th align="center">Watch</Th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <Row key={row.id} row={row} onChange={replace} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function Row({ row, onChange }: { row: TrackedItem; onChange: (item: TrackedItem) => void }) {
  const [busy, setBusy] = useState(false)

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
              <span className="truncate">{row.name}</span>
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
                row.watchNote,
              ].filter(Boolean).join(' · ') || row.shortName}
            </div>
          </div>
        </div>
      </Td>

      <Td align="right" mono>{row.needed || '—'}</Td>

      <Td align="center">
        <div className="flex items-center justify-center gap-1">
          <Step onClick={() => adjust(-1)} disabled={busy || row.have === 0} label="one fewer">−</Step>
          <input
            key={row.have}
            defaultValue={row.have}
            onBlur={(e) => setExact(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            aria-label={`how many ${row.name} you have`}
            className="w-12 rounded-sm border border-line bg-panel px-1 py-0.5 text-center font-mono
                       text-xs tabular-nums focus-visible:outline-2 focus-visible:outline-accent"
          />
          <Step onClick={() => adjust(1)} disabled={busy} label="one more">+</Step>
        </div>
      </Td>

      <Td align="right" mono className={row.remaining === 0 ? 'text-have' : ''}>
        {row.remaining === 0 ? '✓' : row.remaining}
      </Td>

      <Td align="right" mono className="text-muted">
        {row.avg24hPrice ? row.avg24hPrice.toLocaleString() : '—'}
      </Td>

      <Td align="center">
        <button
          type="button"
          onClick={toggleWatch}
          disabled={busy}
          aria-pressed={row.watched}
          aria-label={row.watched ? `stop watching ${row.name}` : `watch ${row.name}`}
          className="rounded-sm px-1.5 text-muted transition-colors hover:text-warn
                     aria-pressed:text-warn focus-visible:outline-2 focus-visible:outline-accent"
        >
          {row.watched ? '★' : '☆'}
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
