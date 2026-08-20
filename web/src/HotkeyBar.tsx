import { useEffect, useState } from 'react'
import { api, type HotkeyHint } from './api'

/**
 * The key-bind reminder, stuck to the bottom of the window.
 *
 * <p>The same list the overlay shows along its own footer, from the same endpoint, so the two
 * cannot end up disagreeing about which key does what.</p>
 *
 * <p>It names the keys you actually bound — including your in-game screenshot key, which RatNav
 * never presses and which is the one people forget.</p>
 */
export function HotkeyBar() {
  const [hints, setHints] = useState<HotkeyHint[]>([])

  useEffect(() => { api.hotkeyHints().then(setHints).catch(() => setHints([])) }, [])

  if (hints.length === 0) return null

  return (
    <footer
      className="sticky bottom-0 z-20 -mx-5 mt-auto flex flex-wrap items-center gap-x-4 gap-y-1
                 border-t border-line bg-ground/95 px-5 py-1.5 backdrop-blur"
    >
      {hints.map((hint) => (
        <span key={hint.does} className="font-mono text-[11px] text-muted">
          <span className="text-ink">{hint.key}</span> {hint.does}
        </span>
      ))}
    </footer>
  )
}
