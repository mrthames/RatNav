/** Types and calls for the local RatNav service. */

export interface DataStatus {
  loaded: boolean
  fetchedAt: string | null
  gameVersion: string | null
  taskCount: number
  itemCount: number
  mapCount: number
  calibratedMapCount: number
  servingStale: boolean
  lastError: string | null
}

export interface MapSummary {
  id: string
  name: string
  normalizedName: string | null
  calibrated: boolean
  imageUrl: string | null
  coordinateRotation: number
  extractCount: number
  /** False where the coordinate rule has not been checked against a real in-game position. */
  calibrationVerified: boolean
}

export interface ObjectivePin {
  taskId: string
  taskName: string
  traderName: string | null
  objectiveId: string
  description: string
  type: string | null
  optional: boolean
  /** Fraction of the image, so any render scale works. */
  x: number
  y: number
  elevation: number
  neededKeyItemIds: string[]
}

export interface TrackedItem {
  id: string
  name: string
  shortName: string | null
  iconUrl: string | null
  wikiUrl: string | null
  avg24hPrice: number | null
  questNeeded: number
  hideoutNeeded: number
  needed: number
  have: number
  remaining: number
  foundInRaid: boolean
  isKey: boolean
  watched: boolean
  watchNote: string | null
  watchTarget: number | null
}

export interface ProgressSummary {
  notStarted: number
  active: number
  completed: number
  failed: number
  availableNow: number
}

export interface TaskSummary {
  id: string
  name: string
  traderName: string | null
  minPlayerLevel: number | null
  kappa: boolean
  wikiUrl: string | null
  objectiveCount: number
  mapIds: string[]
  state: string
  available: boolean
  positionedObjectiveCount: number
}

export interface PlannableObjective {
  objectiveId: string
  taskId: string
  taskName: string
  traderName: string | null
  description: string
  optional: boolean
  x: number
  y: number
  place: string | null
  neededKeyItemIds: string[]
  itemIds: string[]
}

export interface RaidStop {
  objectiveId: string
  taskName: string
  description: string
  x: number
  y: number
  owner: string | null
  place: string | null
  done: boolean
}

export interface RaidView {
  inRaid: boolean
  mapId: string | null
  mapName: string | null
  x: number | null
  y: number | null
  headingDegrees: number | null
  fixedAt: string | null
  floor: string | null
  stops: RaidStop[]
  completedObjectiveIds: string[]
  nextStopName: string | null
  nextStopMetres: number | null
  nextStopRelativeBearing: number | null
  trail: { x: number; y: number }[]
}

export interface Diagnostics {
  ready: boolean
  openInBrowserUrl: string
  checks: { name: string; ok: boolean; detail: string; fix: string; required: boolean }[]
  installs: { directory: string; version: string | null; lastPlayed: string | null; chosen: boolean }[]
}

export type InkLevel = 'full' | 'structure' | 'outline'

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) throw new Error(`${path} returned ${response.status}`)
  return response.json() as Promise<T>
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`${path} returned ${response.status}`)
  return response.json() as Promise<T>
}

export const api = {
  status: () => get<DataStatus>('/api/status'),
  refresh: async () => {
    const response = await fetch('/api/refresh', { method: 'POST' })
    if (!response.ok) throw new Error(`refresh returned ${response.status}`)
    return response.json() as Promise<DataStatus>
  },
  maps: () => get<MapSummary[]>('/api/maps'),

  neededItems: () => get<TrackedItem[]>('/api/items/needed'),
  watchlist: () => get<TrackedItem[]>('/api/items/watchlist'),
  searchItems: (query: string) =>
    get<TrackedItem[]>(`/api/items/search?q=${encodeURIComponent(query)}&limit=40`),

  setHave: (itemId: string, body: { count?: number; delta?: number }) =>
    post<TrackedItem>(`/api/items/${encodeURIComponent(itemId)}/have`, body),

  setWatch: (itemId: string, watch: boolean, note?: string, target?: number) =>
    post<TrackedItem>(`/api/items/${encodeURIComponent(itemId)}/watch`, { watch, note, target }),

  progress: () => get<ProgressSummary>('/api/progress'),

  tasks: (filter?: string, q?: string) => {
    const params = new URLSearchParams()
    if (filter) params.set('filter', filter)
    if (q) params.set('q', q)
    return get<TaskSummary[]>(`/api/tasks?${params}`)
  },

  plannable: (mapId: string) =>
    get<PlannableObjective[]>(`/api/maps/${encodeURIComponent(mapId)}/plannable`),

  buildPlan: (mapId: string, objectiveIds: string[], shoppingListItemIds?: string[]) =>
    post<{ id: string; plan: { stops: unknown[] } }>('/api/plans', {
      mapId, objectiveIds, shoppingListItemIds,
    }),

  activatePlan: (id: string) =>
    post<RaidView>(`/api/plans/${encodeURIComponent(id)}/activate`, {}),

  raid: () => get<RaidView>('/api/raid'),
  diagnostics: () => get<Diagnostics>('/api/diagnostics'),

  completeObjective: (objectiveId: string, done: boolean) =>
    post<RaidView>(`/api/raid/objectives/${encodeURIComponent(objectiveId)}`, { done }),

  /**
   * Ends the raid by hand. The log watcher does this on its own when the game returns to the
   * menu, so this is the fallback for when it misses — a raid you cannot dismiss is worse than
   * one that was never noticed.
   */
  endRaid: () => post<RaidView>('/api/raid/end', {}),

  /**
   * Live raid state. Pushed rather than polled, so a position fix reaches every surface at once
   * and the overlay never shows something the browser has already moved past.
   */
  watchRaid(onChange: (view: RaidView) => void): () => void {
    let socket: WebSocket | null = null
    let retry: ReturnType<typeof setTimeout> | null = null
    let closed = false

    const connect = () => {
      if (closed) return
      const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/raid`
      socket = new WebSocket(url)

      socket.onmessage = (event) => {
        try { onChange(JSON.parse(event.data) as RaidView) } catch { /* ignore a bad frame */ }
      }

      // The service restarts during development and after an update; reconnecting quietly beats
      // making someone reload the page.
      socket.onclose = () => { if (!closed) retry = setTimeout(connect, 1500) }
      socket.onerror = () => socket?.close()
    }

    connect()

    return () => {
      closed = true
      if (retry) clearTimeout(retry)
      socket?.close()
    }
  },

  setTaskState: (taskId: string, state: string) =>
    post<{ id: string; state: string }>(`/api/progress/tasks/${encodeURIComponent(taskId)}`, { state }),
  objectives: (mapId: string) => get<ObjectivePin[]>(`/api/maps/${encodeURIComponent(mapId)}/objectives`),

  /** The map image, restyled server-side so the overlay and this app cannot disagree. */
  imageUrl: (mapId: string, ink: InkLevel, opacity = 1) =>
    `/api/maps/${encodeURIComponent(mapId)}/image?ink=${ink}&opacity=${opacity}`,
}

/** "3 minutes ago" — the app should always be able to say how old its data is. */
export function ago(iso: string | null): string {
  if (!iso) return 'never'
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 90) return 'just now'
  const minutes = seconds / 60
  if (minutes < 90) return `${Math.round(minutes)} min ago`
  const hours = minutes / 60
  if (hours < 36) return `${Math.round(hours)} h ago`
  return `${Math.round(hours / 24)} d ago`
}
