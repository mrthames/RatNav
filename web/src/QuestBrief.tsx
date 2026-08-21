import { useEffect, useState } from 'react'
import { api, type QuestBriefing } from './api'

/**
 * A quest, read at the moment you are standing at one of its waypoints.
 *
 * <p>A pin says where to walk. What it does not say is which of six identical buildings, which
 * door inside it, or which step of the quest this even is — and answering those meant leaving the
 * map for a browser tab.</p>
 *
 * <p>So: what it wants, every step with the one you are on marked, and the wiki's pictures of the
 * place. The pictures are the half that turns "walk to this pin" into "find this door", so they
 * take the space.</p>
 */
export function QuestBrief({
  taskId, objectiveId, onClose,
}: {
  taskId: string
  objectiveId?: string | null
  onClose: () => void
}) {
  const [brief, setBrief] = useState<QuestBriefing | null>(null)
  const [failed, setFailed] = useState(false)
  const [at, setAt] = useState(0)

  // Which step is marked. Starts at the one the waypoint you clicked serves, and moves when you
  // click another — kept here rather than refetched, because the answer is already on screen.
  const [atObjective, setAtObjective] = useState<string | null>(null)

  useEffect(() => {
    setBrief(null)
    setFailed(false)
    setAt(0)
    setAtObjective(null)

    api.questBrief(taskId, objectiveId).then(setBrief).catch(() => setFailed(true))
  }, [taskId, objectiveId])

  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      if (e.key === 'ArrowRight') setAt((n) => n + 1)
      if (e.key === 'ArrowLeft') setAt((n) => n - 1)
    }

    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [onClose])

  const count = brief?.images.length ?? 0
  const index = count === 0 ? 0 : ((at % count) + count) % count
  const image = brief?.images[index]

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={brief?.name ?? 'Quest'}
      onClick={onClose}
      className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex max-h-full w-full max-w-5xl flex-col gap-3 overflow-auto border border-line bg-panel p-4"
      >
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <h2 className="font-display text-xl font-bold tracking-tight">
            {brief?.name ?? 'Loading…'}
          </h2>

          <span className="font-mono text-[11px] text-muted">
            {[brief?.traderName, brief?.minPlayerLevel ? `level ${brief.minPlayerLevel}+` : null]
              .filter(Boolean).join(' · ')}
          </span>

          <span className="ml-auto flex items-center gap-4">
            {brief?.wikiUrl && (
              <a
                href={brief.wikiUrl}
                target="_blank"
                rel="noreferrer"
                className="font-mono text-[11px] text-accent hover:underline
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                wiki ↗
              </a>
            )}

            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              className="font-mono text-sm text-muted hover:text-ink
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              ✕
            </button>
          </span>
        </div>

        {failed && <p className="text-xs text-warn">Could not load this quest.</p>}

        {/* Every step, with the one this waypoint serves marked. A pin out of context is a pin. */}
        {/*
          The steps are clickable, so reading a neighbouring step does not mean closing this,
          going back to the map and hunting for its waypoint.

          It moves the mark and nothing else, on purpose. The wiki pictures belong to the quest
          rather than to a step — there is one set for all four — so a click that implied it was
          fetching different ones would be lying about what it did.
        */}
        {brief && (
          <ol className="flex flex-col gap-px border border-line">
            {brief.objectives.map((objective) => {
              // Before anything is clicked, the marked step is whichever the brief called
              // current — the one the waypoint served.
              const here = atObjective === null ? objective.current : objective.id === atObjective

              return (
                <li key={objective.id}>
                  <button
                    type="button"
                    onClick={() => setAtObjective(objective.id)}
                    aria-current={here}
                    className={`flex w-full items-start gap-2.5 px-3 py-1.5 text-left text-sm
                                transition-colors hover:bg-panel-hi
                                focus-visible:outline-2 focus-visible:outline-accent
                                ${here ? 'bg-accent/10' : 'bg-ground'}`}
                  >
                    <span
                      aria-hidden
                      className={`mt-1.5 size-1.5 flex-none rounded-full
                                  ${objective.done ? 'bg-have' : here ? 'bg-accent' : 'bg-muted/40'}`}
                    />

                    <span className={objective.done ? 'text-muted line-through' : ''}>
                      {objective.description}
                      {objective.optional && (
                        <span className="ml-2 font-mono text-[10px] text-muted">optional</span>
                      )}
                      {here && (
                        <span className="ml-2 font-mono text-[10px] text-accent">you are here</span>
                      )}
                    </span>
                  </button>
                </li>
              )
            })}
          </ol>
        )}

        {/*
          What to carry in. This is the one thing in the modal you can still act on before you
          queue, and the quest text does not always name the key.
        */}
        {brief && brief.required.length > 0 && (
          <div className="flex flex-col gap-1.5">
            <h3 className="font-mono text-[11px] uppercase tracking-wider text-muted">Bring</h3>

            <ul className="flex flex-wrap gap-1.5">
              {brief.required.map((need) => (
                <li
                  key={need.itemId}
                  className={`flex items-center gap-1.5 border px-2 py-1 text-xs
                              ${need.isKey
                                ? 'border-warn/50 bg-warn/10 text-warn'
                                : 'border-line bg-ground text-ink'}`}
                >
                  {!need.isKey && need.count > 1 && (
                    <b className="font-mono tabular-nums">{need.count}×</b>
                  )}

                  {need.iconUrl && (
                    <img src={need.iconUrl} alt="" className="size-5 flex-none object-contain" />
                  )}
                  {need.name}
                  {need.isKey && <span className="font-mono text-[10px] opacity-70">key</span>}
                </li>
              ))}
            </ul>
          </div>
        )}

        {brief && count === 0 && (
          <p className="text-xs text-muted">The wiki article for this quest has no screenshots.</p>
        )}

        {image && (
          <>
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
                className="max-h-[55vh] min-h-0 min-w-0 flex-1 self-center object-contain"
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

            {/* Credit where the licence requires it. */}
            <p className="text-xs text-muted">
              <span className="font-mono">{image.title}</span> · {index + 1} of {count} · from the{' '}
              <a
                href={brief?.wikiUrl ?? '#'}
                target="_blank"
                rel="noreferrer"
                className="text-accent hover:underline"
              >
                Escape from Tarkov Wiki
              </a>
              , licensed CC BY-SA.
            </p>
          </>
        )}
      </div>
    </div>
  )
}
