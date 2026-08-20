import { useState } from 'react'
import { api, type StashScan as Scan } from './api'

/**
 * Reading your stash out of a screenshot, and checking it before anything is written.
 *
 * <p>The whole thing rests on one constraint, and it is yours rather than the code's: shoot a
 * <b>scav junk box</b>, which is a fixed grid, or <b>one block of your stash</b> with a row of
 * bandages marking where it ends. Either is a known rectangle. A scrolling page is not, and no
 * amount of cleverness makes it one.</p>
 *
 * <p>Nothing is written until you say so. Recognition from an icon is never perfect — that is the
 * nature of it, not a bug to be fixed later — so this shows you what it read and lets you correct
 * it. Counts somebody spent weeks accumulating are not worth being confident about.</p>
 */
export function StashScan({ onApplied }: { onApplied: () => void }) {
  const [scan, setScan] = useState<Scan | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /** What each cell was settled as: the item chosen, and how many. */
  const [chosen, setChosen] = useState<Record<string, { itemId: string; count: number }>>({})
  const [applied, setApplied] = useState<number | null>(null)

  async function read(file: File) {
    setBusy(true)
    setError(null)
    setApplied(null)
    setChosen({})

    try {
      const result = await api.scanStash(file)

      setScan(result)

      // Start from the best guess for every cell it was sure enough about. The review is for
      // correcting, and starting from nothing would make it data entry instead.
      const start: Record<string, { itemId: string; count: number }> = {}

      for (const cell of result.cells) {
        const best = cell.matches[0]

        if (best && best.confidence >= 0.6) {
          start[`${cell.column},${cell.row}`] = { itemId: best.itemId, count: 1 }
        }
      }

      setChosen(start)
    } catch {
      setError('That could not be read. A PNG or JPEG of the container works best.')
    } finally {
      setBusy(false)
    }
  }

  /** One line per item, with the cells holding it added up. */
  const totals = Object.values(chosen).reduce<Record<string, number>>((all, pick) => {
    all[pick.itemId] = (all[pick.itemId] ?? 0) + pick.count
    return all
  }, {})

  const named = (itemId: string) =>
    scan?.cells.flatMap((c) => c.matches).find((m) => m.itemId === itemId)?.name ?? itemId

  async function apply() {
    const counts = Object.entries(totals).map(([itemId, count]) => ({ itemId, count }))

    const result = await api.applyStash(counts)

    setApplied(result.applied)
    onApplied()
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2 border border-line bg-panel p-4">
        <p className="font-mono text-[11px] uppercase tracking-wider text-muted">
          Read a container from a screenshot
        </p>

        <p className="text-sm text-muted">
          Open your <b>scav junk box</b> and take a screenshot of it. A box is a fixed grid, which
          is what makes this work at all.
        </p>

        <p className="text-sm text-muted">
          <b>No scav box yet?</b> Group the things you want counted into one block of your stash and
          shoot that instead. If the block is taller than the screen, fill one whole row with a
          single cheap item — bandages — and shoot it in pieces with that row visible each time.
          RatNav reads that row as a divider, never counts it, and knows exactly where two
          screenshots meet.
        </p>

        <label className="self-start">
          <span className="sr-only">Choose a screenshot</span>
          <input
            type="file"
            accept="image/*"
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void read(f) }}
            className="text-xs text-muted file:mr-3 file:rounded-sm file:border-0 file:bg-accent
                       file:px-3 file:py-1.5 file:font-mono file:text-[11px] file:uppercase
                       file:tracking-wider file:text-ground"
          />
        </label>

        {busy && <p className="font-mono text-xs text-muted">reading…</p>}
        {error && <p className="text-xs text-need">{error}</p>}
      </div>

      {scan && !scan.found && <p className="text-sm text-warn">{scan.problem}</p>}

      {scan?.found && (
        <>
          <p className="font-mono text-xs text-muted">
            {scan.columns}×{scan.rows} container · {scan.cells.length} cells with something in them
            {scan.separatorRows.length > 0 && (
              <span className="text-accent">
                {' '}· {scan.separatorRows.length} divider
                {scan.separatorRows.length === 1 ? '' : ' rows'} ignored
              </span>
            )}
          </p>

          <ul className="flex flex-col gap-px border border-line">
            {scan.cells.map((cell) => {
              const key = `${cell.column},${cell.row}`
              const pick = chosen[key]

              return (
                <li key={key} className="flex flex-wrap items-center gap-3 bg-ground px-3 py-2">
                  <span className="w-12 font-mono text-[11px] text-muted tabular-nums">
                    {cell.column + 1},{cell.row + 1}
                  </span>

                  {cell.matches.length === 0 ? (
                    <span className="text-xs text-warn">
                      Nothing on your list looks like this — leave it, or it is something you are
                      not tracking.
                    </span>
                  ) : (
                    <select
                      value={pick?.itemId ?? ''}
                      onChange={(e) => setChosen({
                        ...chosen,
                        ...(e.target.value
                          ? { [key]: { itemId: e.target.value, count: pick?.count ?? 1 } }
                          : {}),
                        ...(e.target.value ? {} : { [key]: undefined as never }),
                      })}
                      className="min-w-56 border border-line bg-panel px-2 py-1 text-xs text-ink
                                 focus-visible:outline-2 focus-visible:outline-accent"
                    >
                      <option value="">— skip this cell —</option>
                      {cell.matches.map((match) => (
                        <option key={match.itemId} value={match.itemId}>
                          {match.name} ({Math.round(match.confidence * 100)}% sure)
                        </option>
                      ))}
                    </select>
                  )}

                  {pick && (
                    <label className="flex items-center gap-1 text-xs text-muted">
                      stack of
                      <input
                        type="number"
                        min={1}
                        value={pick.count}
                        onChange={(e) => setChosen({
                          ...chosen,
                          [key]: { ...pick, count: Math.max(1, Number(e.target.value) || 1) },
                        })}
                        className="w-16 border border-line bg-panel px-1.5 py-0.5 font-mono text-xs
                                   text-ink focus-visible:outline-2 focus-visible:outline-accent"
                      />
                    </label>
                  )}
                </li>
              )
            })}
          </ul>

          {Object.keys(totals).length > 0 && (
            <div className="flex flex-col gap-2 border border-accent/40 bg-accent/5 p-3">
              <p className="font-mono text-[11px] uppercase tracking-wider text-accent">
                What will be written
              </p>

              <ul className="flex flex-col gap-px">
                {Object.entries(totals).map(([itemId, count]) => (
                  <li key={itemId} className="flex items-baseline gap-3 text-sm">
                    <span className="w-10 text-right font-mono tabular-nums">{count}</span>
                    <span>{named(itemId)}</span>
                  </li>
                ))}
              </ul>

              <p className="text-xs text-muted">
                These <b>replace</b> your current counts for these items, rather than adding to
                them — a scan is a reading of what is there, and adding would double everything the
                second time you scanned the same box.
              </p>

              <button
                type="button"
                onClick={() => void apply()}
                className="self-start rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px]
                           uppercase tracking-wider text-ground
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                Write these counts
              </button>

              {applied !== null && (
                <p className="font-mono text-xs text-have">{applied} counts written.</p>
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
