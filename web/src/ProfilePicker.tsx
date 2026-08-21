import { useEffect, useRef, useState } from 'react'
import { api, type Profiles } from './api'

/**
 * Which character RatNav is tracking.
 *
 * <p>Behind a menu rather than on show. It is reachable from every page, because switching
 * character changes what every page says — but it is chosen occasionally, and three buttons for
 * it across the top looked as important as the navigation itself.</p>
 */
export function ProfileMenu({ onSwitched }: { onSwitched: () => void }) {
  const [profiles, setProfiles] = useState<Profiles | null>(null)
  const [busy, setBusy] = useState(false)
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)

  /**
   * Clicking anywhere else closes it, as a menu should.
   *
   * <p>Without this the only way to dismiss it is the control that opened it, which means finding
   * your way back to a small caret to undo a click you have already decided against.</p>
   *
   * <p>On pointerdown rather than click, so the press that lands on a page control closes the menu
   * on the way past rather than after it. Escape too, because a menu that traps a keyboard is
   * worse than one that traps a mouse.</p>
   */
  useEffect(() => {
    if (!open) return

    function away(e: PointerEvent) {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }

    function key(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }

    document.addEventListener('pointerdown', away)
    document.addEventListener('keydown', key)

    return () => {
      document.removeEventListener('pointerdown', away)
      document.removeEventListener('keydown', key)
    }
  }, [open])

  useEffect(() => { api.profiles().then(setProfiles).catch(() => setProfiles(null)) }, [])

  if (!profiles) return null

  async function use(id: string) {
    if (busy || id === profiles?.current) return

    setBusy(true)
    try {
      await api.useProfile(id)
      setProfiles(await api.profiles())
      onSwitched()
    } catch {
      // Leaving the buttons as they were says the switch did not happen, which is the truth.
    } finally {
      setBusy(false)
    }
  }

  const current = profiles.all.find((p) => p.id === profiles.current)

  return (
    <div ref={box} className="relative">
      {/*
        The wordmark and the character are one control.

        They were two things: "RatNav" printed above the navigation, and a hamburger at the far end
        of the header that opened a list of characters. Neither said which character was in use, so
        the answer to "whose progress am I looking at" — which changes what every page on the site
        shows — was behind a click on an icon that gives no clue it is about that.

        Said together, the header answers it without being asked, and the caret is where anybody
        would look to change it.
      */}
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-label={`Character: ${current?.name ?? profiles.current}. Change character.`}
        className="flex items-baseline gap-1.5 font-mono text-[11px] uppercase tracking-[0.14em]
                   text-muted transition-colors hover:text-ink
                   focus-visible:outline-2 focus-visible:outline-accent"
      >
        <span>RatNav</span>
        <span aria-hidden className="text-line">—</span>
        <span className="text-ink">{current?.name ?? profiles.current}</span>
        <span aria-hidden className="text-[9px]">{open ? '▴' : '▾'}</span>
      </button>

      {open && (
        <div
          role="group"
          aria-label="Character"
          className="absolute left-0 top-full z-30 mt-1 w-max border border-line bg-panel p-1 shadow-xl"
        >
          {profiles.all.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => { setOpen(false); void use(p.id) }}
              aria-pressed={p.id === profiles.current}
              disabled={busy}
              className="block w-full rounded-sm px-2 py-1.5 text-left font-mono text-[11px]
                         uppercase tracking-wider text-muted transition-colors hover:text-ink
                         aria-pressed:bg-accent aria-pressed:text-ground disabled:opacity-50
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {p.name}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
