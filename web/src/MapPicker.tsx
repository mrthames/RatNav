import { type MapSummary } from './api'

/**
 * Choosing which map you are looking at.
 *
 * <p>One component, used by both the Maps page and the Plan page. They were two lists that
 * happened to look alike, which is the arrangement where one of them quietly stops matching the
 * other — a `[WIP]` marker added in one place and not the other, and now the same map reads two
 * ways depending on which tab you are on.</p>
 */
export function MapPicker({
  maps, selected, onSelect,
}: {
  maps: MapSummary[]
  selected: MapSummary | null
  onSelect: (map: MapSummary) => void
}) {
  return (
    <div className="flex flex-wrap gap-px">
      {maps.map((map) => (
        <button
          key={map.id}
          type="button"
          aria-pressed={selected?.id === map.id}
          onClick={() => onSelect(map)}
          className="rounded-sm bg-panel px-3 py-2 text-sm text-muted transition-colors
                     hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          {map.name}
          {map.workInProgress && (
            <span className="ml-1.5 font-mono text-[10px] text-warn">[WIP]</span>
          )}
        </button>
      ))}
    </div>
  )
}
