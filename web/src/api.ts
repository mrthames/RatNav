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

export interface ExtractPin {
  name: string
  /** "pmc", "scav", or "shared" — shared works whichever you queued as. */
  faction: string
  x: number
  y: number
  elevation: number
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
  /** The nearest hideout upgrade wanting this — "Medstation 3". */
  hideoutUpgrade: string | null
  /** How far out that upgrade is. 1 means you could build it today. */
  hideoutWave: number | null
  needed: number
  have: number
  remaining: number
  foundInRaid: boolean
  isKey: boolean
  watched: boolean
  watchNote: string | null
  watchTarget: number | null
}

export interface HideoutStationSummary {
  id: string
  name: string
  builtLevel: number
  maxLevel: number
}

export interface HideoutUpgradeItem {
  itemId: string
  name: string
  shortName: string | null
  count: number
  have: number
  foundInRaid: boolean
}

export interface HideoutUpgrade {
  stationId: string
  stationName: string
  level: number
  /** 1 is buildable now; 2 is buildable once everything at 1 is done. */
  wave: number
  targeted: boolean
  description: string | null
  constructionTimeSeconds: number
  blockers: { kind: string; text: string }[]
  items: HideoutUpgradeItem[]
}

export interface HideoutState {
  lookAhead: number
  stations: HideoutStationSummary[]
  upcoming: HideoutUpgrade[]
}

export interface TurnIn {
  taskId: string
  taskName: string
  traderName: string | null
  objectiveCount: number
  totalObjectiveCount: number
  wikiUrl: string | null
}

export interface HotKeys {
  toggleOverlay: string
  toggleInteract: string
  expandPanel: string
  completeObjective: string
  toggleMode: string
  identifyItem: string
}

export interface Settings {
  gameDirectory: string | null
  screenshotDirectory: string | null
  screenshotKey: string
  screenshotDisposal: string
  owner: string | null
  hotkeys: HotKeys
  playerLevel: number | null
  gameEdition: string
  /** Lowest level consistent with the quests you have completed — a floor, not your real level. */
  suggestedPlayerLevel: number | null
  /** The install in use, whether set by hand or found. */
  resolvedGameDirectory: string | null
  resolvedScreenshotDirectory: string
  /** True when the folder in use was detected rather than chosen. */
  gameDirectoryDetected: boolean
}

export interface Trader {
  name: string
  /** Loyalty level, 1–4. Set by hand: nothing on disk reports it. */
  level: number
  total: number
  completed: number
  active: number
  availableNow: number
  next: { id: string; name: string; minPlayerLevel: number | null; wikiUrl: string | null }[]
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
  /** Why it cannot be started yet — "needs level 20", "needs Debut". Empty when it can. */
  blockers: string[]
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
  /** True when a plan is loaded, whether or not you are in the raid it was built for. */
  hasPlan: boolean
  /** Set when the active plan is for a different map than the one on screen. */
  planMapName: string | null
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

async function del<T>(path: string): Promise<T> {
  const response = await fetch(path, { method: 'DELETE' })
  if (!response.ok) throw new Error(`${path} returned ${response.status}`)
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>)
}

export const api = {
  status: () => get<DataStatus>('/api/status'),
  refresh: async () => {
    const response = await fetch('/api/refresh', { method: 'POST' })
    if (!response.ok) throw new Error(`refresh returned ${response.status}`)
    return response.json() as Promise<DataStatus>
  },
  maps: () => get<MapSummary[]>('/api/maps'),

  /**
   * What to pick up. `lookAhead` decides how deep into the hideout build order to count, and
   * `sort` of "next" leads with the upgrades you are closest to finishing rather than with
   * quantity.
   */
  neededItems: (options?: { lookAhead?: number; sort?: 'next' }) => {
    const query = new URLSearchParams()
    if (options?.lookAhead) query.set('lookAhead', String(options.lookAhead))
    if (options?.sort) query.set('sort', options.sort)

    const suffix = query.toString()
    return get<TrackedItem[]>(`/api/items/needed${suffix ? `?${suffix}` : ''}`)
  },
  watchlist: () => get<TrackedItem[]>('/api/items/watchlist'),
  searchItems: (query: string) =>
    get<TrackedItem[]>(`/api/items/search?q=${encodeURIComponent(query)}&limit=40`),

  setHave: (itemId: string, body: { count?: number; delta?: number }) =>
    post<TrackedItem>(`/api/items/${encodeURIComponent(itemId)}/have`, body),

  /**
   * Changes a watchlist entry. Omitted fields are left alone, so setting a target does not blank
   * a note. `have` is the watchlist's own count — separate from the stash total, so what is set
   * aside for the hideout is not counted as available for this.
   */
  setWatch: (
    itemId: string,
    watch: boolean,
    fields?: { note?: string; target?: number | null; have?: number },
  ) =>
    post<TrackedItem>(`/api/items/${encodeURIComponent(itemId)}/watch`, { watch, ...fields }),

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

  traders: () => get<Trader[]>('/api/traders'),

  setTraderLevel: (name: string, level: number) =>
    post<unknown>(`/api/traders/${encodeURIComponent(name)}/level`, { level }),

  settings: () => get<Settings>('/api/settings'),

  /** Absent fields are left alone; an empty string clears a path back to being detected. */
  saveSettings: (update: Partial<Omit<Settings, 'resolvedGameDirectory' | 'resolvedScreenshotDirectory' | 'gameDirectoryDetected'>>) =>
    post<Settings>('/api/settings', update),

  hideout: (lookAhead?: number) =>
    get<HideoutState>(`/api/hideout${lookAhead ? `?lookAhead=${lookAhead}` : ''}`),

  setLookAhead: (levels: number) =>
    post<{ lookAhead: number }>('/api/hideout/look-ahead', { levels }),

  setHideoutLevel: (stationId: string, level: number) =>
    post<{ id: string; level: number }>(`/api/progress/hideout/${encodeURIComponent(stationId)}`, { level }),

  targetUpgrade: (stationId: string, level: number, targeted: boolean) =>
    post<unknown>(
      `/api/hideout/${encodeURIComponent(stationId)}/levels/${level}/target`, { targeted }),

  /** Quests whose every planned objective is done, waiting on a trader. */
  turnIns: () => get<TurnIn[]>('/api/raid/turn-ins'),

  markTaskState: (taskId: string, state: string) =>
    post<unknown>(`/api/progress/tasks/${encodeURIComponent(taskId)}`, { state }),

  removeStop: (objectiveId: string) =>
    del<RaidView>(`/api/raid/stops/${encodeURIComponent(objectiveId)}`),

  clearPlan: () => del<RaidView>('/api/raid/plan'),

  /** Where you can leave from. Faction comes back raw so the view decides what to show. */
  extracts: (mapId: string) =>
    get<ExtractPin[]>(`/api/maps/${encodeURIComponent(mapId)}/extracts`),
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
