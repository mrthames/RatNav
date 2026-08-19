import { useEffect, useState } from 'react'
import { api, type Diagnostics } from './api'

/**
 * Whether RatNav can see the game.
 *
 * Setup fails in four ways that all look identical from the player's side — an overlay showing
 * nothing — so each is reported separately with what to do about it.
 */
export function SetupView() {
  const [state, setState] = useState<Diagnostics | null>(null)

  const load = () => api.diagnostics().then(setState).catch(() => setState(null))

  useEffect(() => {
    load()
    // Setup is the one screen where things change underneath you — you launch the game, or take
    // your first screenshot — so this is the one screen that re-checks on its own.
    const timer = setInterval(load, 5000)
    return () => clearInterval(timer)
  }, [])

  if (!state) return <Empty>checking…</Empty>

  return (
    <div className="flex flex-col gap-5">
      <div className={`border px-4 py-3 ${state.ready
        ? 'border-have/40 bg-have/5 text-have'
        : 'border-warn/40 bg-warn/5 text-warn'}`}>
        <p className="font-mono text-sm">
          {state.ready ? 'RatNav can see your game.' : 'RatNav cannot see your game yet.'}
        </p>
      </div>

      <ul className="flex flex-col gap-px border border-line bg-line-soft">
        {state.checks.map((check) => (
          <li key={check.name} className="flex items-start gap-3 bg-panel px-3 py-2.5">
            <span
              aria-hidden
              className={`mt-1.5 size-2 flex-none rounded-full ${
                check.ok ? 'bg-have' : check.required ? 'bg-need' : 'bg-warn'}`}
            />
            <div className="min-w-0">
              <p className="text-sm">
                {check.name}
                <span className="sr-only">{check.ok ? ' — ok' : ' — needs attention'}</span>
              </p>
              <p className="font-mono text-[11px] break-words text-muted">{check.detail}</p>
              {!check.ok && <p className="mt-1 text-xs text-warn">{check.fix}</p>}
            </div>
          </li>
        ))}
      </ul>

      {state.installs.length > 1 && (
        <div className="border border-line">
          <p className="border-b border-line bg-panel px-3 py-1.5 font-mono text-[11px]
                        uppercase tracking-wider text-muted">
            More than one install
          </p>
          <ul>
            {state.installs.map((install) => (
              <li key={install.directory}
                  className="flex flex-wrap items-baseline gap-x-3 border-b border-line-soft px-3 py-2 last:border-0">
                <span className={`font-mono text-xs ${install.chosen ? 'text-accent' : 'text-muted'}`}>
                  {install.chosen ? '→ watching' : '  ignoring'}
                </span>
                <span className="text-sm">{install.directory}</span>
                <span className="font-mono text-[11px] text-muted">
                  {install.version ?? 'never launched'}
                </span>
              </li>
            ))}
          </ul>
          <p className="px-3 py-2 text-xs text-muted">
            RatNav watches whichever install was played most recently. An old copy on another
            drive would otherwise be read forever, reporting no raids and never saying why.
          </p>
        </div>
      )}

      <div className="flex flex-col gap-2 border border-line bg-panel p-4">
        <p className="font-mono text-[11px] uppercase tracking-wider text-muted">On a second screen</p>
        <p className="text-sm">
          Open{' '}
          <a href={state.openInBrowserUrl}
             className="text-accent underline-offset-2 hover:underline
                        focus-visible:outline-2 focus-visible:outline-accent">
            {state.openInBrowserUrl}
          </a>{' '}
          in any browser and put it wherever you like. It is the same app the overlay's panel
          shows, reading the same live state, so both stay in step.
        </p>
        <p className="text-xs text-muted">
          Served on loopback only — it is not reachable from your network.
        </p>
      </div>
    </div>
  )
}

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="border border-line bg-panel px-4 py-8 text-center font-mono text-xs text-muted">
    {children}
  </p>
)
