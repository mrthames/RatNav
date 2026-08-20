import { useEffect, useState } from 'react'
import { api, type Profiles } from './api'

/**
 * Which character RatNav is tracking.
 *
 * <p>In the navigation rather than in Setup, beside character level. The game gives you a PvE
 * character, a PvP one and a seasonal PvP one; they share nothing, and which you are playing
 * changes often enough that burying the switch would be the same mistake character level was
 * making before it moved up here.</p>
 */
export function ProfilePicker({ onSwitched }: { onSwitched: () => void }) {
  const [profiles, setProfiles] = useState<Profiles | null>(null)
  const [busy, setBusy] = useState(false)

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

  return (
    <div className="flex items-center gap-1" role="group" aria-label="Character">
      {profiles.all.map((p) => (
        <button
          key={p.id}
          type="button"
          onClick={() => void use(p.id)}
          aria-pressed={p.id === profiles.current}
          disabled={busy}
          className="rounded-sm px-2 py-1 font-mono text-[11px] uppercase tracking-wider
                     text-muted transition-colors hover:text-ink
                     aria-pressed:bg-accent aria-pressed:text-ground disabled:opacity-50
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          {p.name}
        </button>
      ))}
    </div>
  )
}
