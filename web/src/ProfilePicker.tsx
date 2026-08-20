import { useEffect, useState } from 'react'
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
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-label={`Character: ${current?.name ?? profiles.current}`}
        title={`Character: ${current?.name ?? profiles.current}`}
        className="grid size-8 place-items-center rounded-sm border border-line bg-panel-hi
                   font-mono text-sm text-muted transition-colors hover:border-muted
                   hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
      >
        ☰
      </button>

      {open && (
        <div
          role="group"
          aria-label="Character"
          className="absolute right-0 top-full z-30 mt-1 w-max border border-line bg-panel p-1 shadow-xl"
        >
          <p className="px-2 py-1 font-mono text-[10px] uppercase tracking-wider text-muted">
            Character
          </p>

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
