import { useCallback, useEffect, useState } from 'react'
import { api, type HideoutState, type HideoutUpgrade } from './api'

/**
 * The hideout as a build order rather than a wish list.
 *
 * Every un-built level wants items, so the unfiltered answer is hundreds of them, most for
 * upgrades gated behind three others you have not started — a list nobody can shop from. Two
 * controls fix that. **Look ahead** walks the game's own prerequisites: 1 is what you could build
 * tonight, 3 is what to stop vendoring. **Targeting** narrows to the upgrades you actually chose,
 * because widening is not what someone with a plan wants.
 */
export function HideoutView() {
  const [state, setState] = useState<HideoutState | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async (lookAhead?: number) => {
    setState(await api.hideout(lookAhead))
  }, [])

  useEffect(() => { void load() }, [load])

  async function changeLookAhead(levels: number) {
    setBusy(true)
    try {
      await api.setLookAhead(levels)
      await load(levels)
    } finally {
      setBusy(false)
    }
  }

  async function setLevel(stationId: string, level: number) {
    await api.setHideoutLevel(stationId, level)
    await load()
  }

  async function toggleTarget(upgrade: HideoutUpgrade) {
    await api.targetUpgrade(upgrade.stationId, upgrade.level, !upgrade.targeted)
    await load()
  }

  if (!state) return <p className="font-mono text-xs text-muted">loading hideout…</p>

  const targeted = state.upcoming.filter((u) => u.targeted)
  const waves = [...new Set(state.upcoming.map((u) => u.wave))].sort((a, b) => a - b)

  return (
    <div className="flex flex-col gap-6">
      <section className="flex flex-wrap items-center gap-x-6 gap-y-3">
        <div className="flex items-center gap-1">
          <span className="font-mono text-[11px] uppercase tracking-wider text-muted">Look ahead</span>
          <button
            type="button"
            disabled={busy || state.lookAhead <= 1}
            onClick={() => void changeLookAhead(state.lookAhead - 1)}
            aria-label="Look one upgrade less far ahead"
            className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                       hover:text-ink disabled:opacity-30
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            −
          </button>
          <span className="w-6 text-center font-mono text-sm tabular-nums text-ink">{state.lookAhead}</span>
          <button
            type="button"
            disabled={busy || state.lookAhead >= 6}
            onClick={() => void changeLookAhead(state.lookAhead + 1)}
            aria-label="Look one upgrade further ahead"
            className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                       hover:text-ink disabled:opacity-30
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            +
          </button>
        </div>

        {/*
          Said plainly, because the number is otherwise abstract. "9 upgrades, 17 items" is what
          makes the difference between a 1 and a 4 obvious.
        */}
        <p className="font-mono text-xs text-muted">
          {state.upcoming.length} upgrade{state.upcoming.length === 1 ? '' : 's'} ·{' '}
          {new Set(state.upcoming.flatMap((u) => u.items.map((i) => i.itemId))).size} items
          {targeted.length > 0 && ` · ${targeted.length} targeted, and only those count`}
        </p>
      </section>

      {state.upcoming.length === 0 && (
        <p className="font-mono text-xs text-muted">
          Nothing to build — either the hideout is finished, or the levels below need setting.
        </p>
      )}

      {waves.map((wave) => (
        <section key={wave} className="flex flex-col gap-2">
          <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
            {wave === 1 ? 'Buildable now' : `After ${wave - 1} more upgrade${wave === 2 ? '' : 's'}`}
          </h2>

          <div className="flex flex-col gap-px">
            {state.upcoming.filter((u) => u.wave === wave).map((upgrade) => (
              <UpgradeRow
                key={`${upgrade.stationId}-${upgrade.level}`}
                upgrade={upgrade}
                onToggle={() => void toggleTarget(upgrade)}
                onBuilt={() => void setLevel(upgrade.stationId, upgrade.level)}
              />
            ))}
          </div>
        </section>
      ))}

      <section className="flex flex-col gap-2">
        <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">Where you are</h2>
        <p className="text-xs text-muted">
          Set each station to the level it is built to. Everything above works out from these, so
          an upgrade marked by mistake is worth putting back — use the arrows.
        </p>

        <div className="grid gap-px sm:grid-cols-2">
          {state.stations.map((station) => (
            <div key={station.id} className="flex items-center justify-between gap-3 bg-panel px-3 py-2">
              <span className="truncate text-sm">{station.name}</span>

              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={station.builtLevel <= 0}
                  onClick={() => void setLevel(station.id, station.builtLevel - 1)}
                  aria-label={`Lower ${station.name}`}
                  className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                             hover:text-ink disabled:opacity-30
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  −
                </button>

                <span className="w-10 text-center font-mono text-xs tabular-nums text-ink">
                  {station.builtLevel}
                  <span className="text-muted">/{station.maxLevel}</span>
                </span>

                <button
                  type="button"
                  disabled={station.builtLevel >= station.maxLevel}
                  onClick={() => void setLevel(station.id, station.builtLevel + 1)}
                  aria-label={`Raise ${station.name}`}
                  className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                             hover:text-ink disabled:opacity-30
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  +
                </button>
              </div>
            </div>
          ))}
        </div>
      </section>

    </div>
  )
}

function UpgradeRow({
  upgrade, onToggle, onBuilt,
}: {
  upgrade: HideoutUpgrade
  onToggle: () => void
  onBuilt: () => void
}) {
  const short = upgrade.items.filter((i) => i.have < i.count)

  return (
    <div className="bg-panel px-3 py-2">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <button
          type="button"
          onClick={onToggle}
          aria-pressed={upgrade.targeted}
          className="font-mono text-[11px] uppercase tracking-wider text-muted
                     hover:text-ink aria-pressed:text-accent
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          {upgrade.targeted ? '★ targeted' : '☆ target'}
        </button>

        <span className="text-sm text-ink">
          {upgrade.stationName} <span className="tabular-nums">{upgrade.level}</span>
        </span>

        {upgrade.blockers.map((blocker) => (
          <span key={blocker.text} className="font-mono text-[11px] text-warn">
            needs {blocker.text}
          </span>
        ))}

        {/* The moment you would want to record it is the moment you are looking at it. */}
        <button
          type="button"
          onClick={onBuilt}
          className="ml-auto font-mono text-[11px] uppercase tracking-wider text-muted
                     underline-offset-4 hover:text-ink hover:underline
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          Built it
        </button>
      </div>

      {short.length > 0 && (
        <ul className="mt-1 flex flex-wrap gap-x-4 gap-y-1">
          {short.map((item) => (
            <li key={item.itemId} className="font-mono text-xs tabular-nums text-muted">
              <span className={item.foundInRaid ? 'text-need' : 'text-route'}>
                {item.count - item.have}
              </span>{' '}
              {item.shortName ?? item.name}
              {item.foundInRaid && <span className="text-need"> FIR</span>}
            </li>
          ))}
        </ul>
      )}

      {short.length === 0 && upgrade.items.length > 0 && (
        <p className="mt-1 font-mono text-xs text-muted">everything for this is in your stash</p>
      )}
    </div>
  )
}
