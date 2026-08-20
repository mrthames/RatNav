import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type HeldBackMap, type InkLevel } from './api'

/**
 * The maps RatNav will not offer yet, and the thirty seconds that fixes most of them.
 *
 * <p>Two different problems wear the same label. Some of the game's locations have no community
 * drawing at all, and nothing here can conjure one. The rest have a drawing whose orientation
 * cannot be settled from published data — every extract sits inside the border, so mirroring the
 * layout moves nothing off the edge and no amount of arithmetic distinguishes it from the truth.
 * A person who stood somewhere and can point at it distinguishes it immediately.</p>
 *
 * <p>So: take a screenshot in game somewhere you can recognise, come back, and click that spot.
 * The margin is enormous — a wrong layout is a mirror image and misses by around half the map,
 * while a hurried click misses by a few percent — so there is no realistic way to click badly
 * enough to pick the wrong answer.</p>
 */
export function HeldBack({ onSettled }: { onSettled: () => void }) {
  const [maps, setMaps] = useState<HeldBackMap[]>([])
  const [settling, setSettling] = useState<HeldBackMap | null>(null)

  const load = useCallback(
    () => api.heldBackMaps().then(setMaps).catch(() => setMaps([])), [])

  useEffect(() => { void load() }, [load])

  if (maps.length === 0) return null

  const settleable = maps.filter((m) => m.canBeSettled)
  const impossible = maps.filter((m) => !m.canBeSettled)

  return (
    <section className="flex flex-col gap-3 border border-line bg-panel p-4">
      <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
        Maps not offered yet
      </h2>

      {settleable.length > 0 && (
        <>
          <p className="text-xs text-muted">
            These have a drawing, but which way round it goes cannot be worked out from published
            data — every extract sits inside the border, so a mirrored layout looks exactly as
            valid as the real one. <b>One position settles it.</b> Take a screenshot in game
            somewhere you can recognise on the map, then come back and click that spot.
          </p>

          <ul className="flex flex-col gap-px">
            {settleable.map((map) => (
              <li key={map.id} className="flex flex-wrap items-center gap-3 bg-ground px-3 py-2">
                <span className="text-sm">{map.name}</span>

                <button
                  type="button"
                  onClick={() => setSettling(map)}
                  className="ml-auto rounded-sm bg-panel-hi px-2.5 py-1 font-mono text-[11px]
                             uppercase tracking-wider text-muted transition-colors
                             hover:bg-accent hover:text-ground
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  Settle it
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      {impossible.length > 0 && (
        <p className="text-xs text-muted">
          <b>No drawing exists</b> for {impossible.map((m) => m.name).join(', ')}. Nothing here can
          fix that — it needs someone in the community to draw one.
        </p>
      )}

      {settling && (
        <Settle
          map={settling}
          onClose={() => setSettling(null)}
          onSettled={() => { void load(); onSettled() }}
        />
      )}
    </section>
  )
}

/** One map, its drawing, and the click that settles it. */
function Settle({
  map, onClose, onSettled,
}: {
  map: HeldBackMap
  onClose: () => void
  onSettled: () => void
}) {
  const [markup, setMarkup] = useState<string | null>(null)
  const [position, setPosition] = useState<{ x: number; y: number; z: number; takenAt: string } | null>(null)
  const [result, setResult] = useState<{ settled: boolean; reason: string } | null>(null)
  const [manual, setManual] = useState({ x: '', z: '' })
  const host = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const ink: InkLevel = 'full'

    fetch(api.imageUrl(map.id, ink))
      .then((r) => (r.ok ? r.text() : Promise.reject(new Error(String(r.status)))))
      .then(setMarkup)
      .catch(() => setMarkup(''))

    api.latestPosition().then(setPosition).catch(() => setPosition(null))
  }, [map.id])

  // Typed coordinates win when they are filled in: someone reading them off an old screenshot
  // filename should not have to retake it just because RatNav has a newer fix in memory.
  const world = manual.x !== '' && manual.z !== ''
    ? { x: Number(manual.x), y: 0, z: Number(manual.z) }
    : position

  async function settle(e: React.MouseEvent<HTMLDivElement>) {
    if (!world || !host.current) return

    const box = host.current.getBoundingClientRect()
    const imageX = (e.clientX - box.left) / box.width
    const imageY = (e.clientY - box.top) / box.height

    const answer = await api.calibrate(map.id, world, imageX, imageY)

    setResult(answer)
    if (answer.settled) onSettled()
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Settle ${map.name}`}
      className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-4"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-full w-full max-w-4xl flex-col gap-3 overflow-auto border border-line bg-panel p-4"
      >
        <div className="flex items-center justify-between gap-4">
          <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
            Settle {map.name}
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

        {/* What it is working from. Without a position there is nothing to click against. */}
        <div className="flex flex-wrap items-center gap-3 text-xs">
          {position ? (
            <span className="text-muted">
              Using your last position:{' '}
              <span className="font-mono text-ink">
                {position.x.toFixed(1)}, {position.z.toFixed(1)}
              </span>
            </span>
          ) : (
            <span className="text-warn">
              No position read yet. Take a screenshot in game, or type the coordinates from a
              screenshot filename.
            </span>
          )}

          <label className="flex items-center gap-1 text-muted">
            x
            <input
              value={manual.x}
              onChange={(e) => setManual({ ...manual, x: e.target.value })}
              className="w-20 border border-line bg-ground px-1.5 py-0.5 font-mono text-xs text-ink
                         focus-visible:outline-2 focus-visible:outline-accent"
            />
          </label>

          <label className="flex items-center gap-1 text-muted">
            z
            <input
              value={manual.z}
              onChange={(e) => setManual({ ...manual, z: e.target.value })}
              className="w-20 border border-line bg-ground px-1.5 py-0.5 font-mono text-xs text-ink
                         focus-visible:outline-2 focus-visible:outline-accent"
            />
          </label>
        </div>

        <p className="text-xs text-muted">
          Click the spot on the map where you were standing. Close enough is close enough — a wrong
          layout misses by about half the map, so a few percent either way changes nothing.
        </p>

        {markup === null && <p className="font-mono text-xs text-muted">loading map…</p>}
        {markup === '' && <p className="font-mono text-xs text-warn">this map has no drawing</p>}

        {markup && (
          <div
            ref={host}
            onClick={(e) => void settle(e)}
            className={`[&>svg]:block [&>svg]:h-auto [&>svg]:w-full
                        ${world ? 'cursor-crosshair' : 'pointer-events-none opacity-40'}`}
            dangerouslySetInnerHTML={{ __html: markup }}
          />
        )}

        {result && (
          <p className={`text-xs ${result.settled ? 'text-have' : 'text-warn'}`}>
            {result.reason}
          </p>
        )}
      </div>
    </div>
  )
}
