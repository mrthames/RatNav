import { useCallback, useEffect, useState } from 'react'
import { api, type HeldBackMap } from './api'

/**
 * The maps that are coming.
 *
 * <p>Every map here has a drawing. What none of them has is a settled orientation: each one's
 * extracts all sit inside its border, so mirroring the layout moves nothing off the edge and no
 * amount of arithmetic tells the mirror from the truth. Settling one takes a person who stood
 * somewhere and can point at it on the picture — which is work for whoever builds RatNav, not for
 * somebody who installed it, so it happens here and the map ships finished.</p>
 *
 * <p>Maps with no drawing at all are not listed and are not called "coming soon" anywhere, because
 * nothing in this app can make one arrive. That is a rule rather than an oversight.</p>
 */
export function HeldBack({ onSettled: _onSettled }: { onSettled: () => void }) {
  const [maps, setMaps] = useState<HeldBackMap[]>([])

  const load = useCallback(
    () => api.heldBackMaps().then(setMaps).catch(() => setMaps([])), [])

  useEffect(() => { void load() }, [load])

  if (maps.length === 0) return null

  return (
    <section className="flex flex-col gap-3 border border-line bg-panel p-4">
      <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
        Coming soon
      </h2>

      <p className="text-xs text-muted">
        These have a drawing, and what is left is working out which way round it goes. They will
        appear here once they are finished.
      </p>

      <ul className="flex flex-wrap gap-px">
        {maps.map((map) => (
          <li key={map.id} className="bg-ground px-3 py-2 text-sm">{map.name}</li>
        ))}
      </ul>
    </section>
  )
}
