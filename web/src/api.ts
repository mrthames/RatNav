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
  /** Quest positions on this map are still settling — shown as [WIP] beside the name. */
  workInProgress: boolean
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
  /** How many the goals you are collecting for want. */
  goalNeeded: number
  /** Which goals want it, by the names you gave them. */
  goalFor: string[]
  watched: boolean
  watchNote: string | null
  watchTarget: number | null
}

export interface HideoutStationSummary {
  id: string
  name: string
  /** The station's own icon, so the hideout reads like the hideout screen. */
  imageUrl: string | null
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
  toggleMode: string
  identifyItem: string
  /** Read the extract list the game is showing, while it is showing it. */
  readExtracts: string
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
  /** The trader's portrait, so a list of them looks like the ones in the game. */
  imageUrl: string | null
  /** Loyalty level, 1–4. Set by hand: nothing on disk reports it. */
  level: number
  /** What each loyalty level costs, and whether your character level can have reached it. */
  levels: { level: number; requiredPlayerLevel: number; reachable: boolean }[]
  total: number
  completed: number
  active: number
  availableNow: number
  next: { id: string; name: string; minPlayerLevel: number | null; wikiUrl: string | null }[]
}

export interface SavedPlan {
  id: string
  mapId: string
  mapName: string
  owner: string | null
  createdAt: string
  stops: number
}

/** What merging two plans reveals: the things that change how you run the raid. */
export interface MergeOverlap {
  sharedObjectiveIds: string[]
  contestedItemIds: string[]
  redundantKeyItemIds: string[]
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
  /** What to carry in for this step, keys first and named rather than counted. */
  required: { itemId: string; name: string; isKey: boolean }[]
}

export interface ItemDetail {
  item: { id: string; name: string; shortName: string | null; wikiUrl: string | null }
  /** Every quest that wants it, whether or not you have started them. */
  quests: {
    taskId: string
    taskName: string
    objectiveId: string
    count: number
    foundInRaid: boolean
    traderName: string | null
  }[]
  hideout: { stationName: string; level: number; count: number }[]
  /** Quests this item opens the way for, when it is a key. */
  asKey: { taskId: string; taskName: string }[]
  have: number
}

export interface QuestBriefing {
  id: string
  name: string
  traderName: string | null
  minPlayerLevel: number | null
  wikiUrl: string | null
  state: string
  objectives: {
    id: string
    description: string
    optional: boolean
    /** Whether this step has a position on a map RatNav can draw. */
    onThisMap: boolean
    /** True for the step the waypoint you clicked serves. */
    current: boolean
    done: boolean
  }[]
  /** What to carry in. Keys first — turning up without one wastes the raid. */
  required: {
    itemId: string
    name: string
    iconUrl: string | null
    isKey: boolean
  }[]
  images: WikiImage[]
}

export interface WikiImage {
  title: string
  url: string
  width: number
  height: number
}

export interface HeldBackMap {
  id: string
  name: string
  normalizedName: string | null
  /** False when no community drawing exists at all, which no screenshot can fix. */
  hasDrawing: boolean
  confidence: string
  reason: string
  canBeSettled: boolean
}

export interface PlaceLabel {
  text: string
  x: number
  y: number
}

export interface CustomWaypoint {
  id: string
  mapId: string
  label: string
  x: number
  y: number
  floor: string | null
  /** "Place" for somewhere worth remembering, "Item" for something to pick up. */
  kind: 'Place' | 'Item'
  createdAt: string
}

export interface GoalView {
  id: string
  name: string
  /** How many times over. Two of a goal wants twice its items. */
  times: number
  items: { itemId: string; name: string; count: number; have: number }[]
}

export interface RaidStop {
  objectiveId: string
  taskId: string
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
  /** True when there is a map to draw, raid or not. */
  hasMap: boolean
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

export type InkLevel = 'graphical' | 'full' | 'structure' | 'outline'

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

  buildPlan: (
    mapId: string,
    objectiveIds: string[],
    shoppingListItemIds?: string[],
    /** Marks of your own to run alongside the quest objectives, in the order given. */
    waypointIds?: string[],
  ) =>
    post<{ id: string; plan: { stops: unknown[] } }>('/api/plans', {
      mapId, objectiveIds, shoppingListItemIds, waypointIds,
    }),

  activatePlan: (id: string) =>
    post<RaidView>(`/api/plans/${encodeURIComponent(id)}/activate`, {}),

  savedPlans: () => get<SavedPlan[]>('/api/plans'),

  /** The plan as a line of text to paste wherever you are already talking. */
  planCode: (id: string) =>
    get<{ code: string }>(`/api/plans/${encodeURIComponent(id)}/code`),

  importCode: (code: string) =>
    post<{
      id: string
      plan: { mapId: string; mapName: string; owner: string | null; stops: unknown[] }
    }>('/api/plans/import-code', { code }),

  /** Combines saved plans for the same map. Nothing is dropped; the overlap is what it adds. */
  mergePlans: (planIds: string[]) =>
    post<{ owners: string[]; overlap: MergeOverlap; raid: RaidView }>(
      '/api/plans/merge', { planIds }),

  raid: () => get<RaidView>('/api/raid'),

  /** Draws a map on the overlay with no plan and without waiting for the game. */
  showMap: (mapId: string) =>
    post<RaidView>(`/api/raid/map/${encodeURIComponent(mapId)}`, {}),

  traders: () => get<Trader[]>('/api/traders'),

  setTraderLevel: (name: string, level: number) =>
    post<unknown>(`/api/traders/${encodeURIComponent(name)}/level`, { level }),

  settings: () => get<Settings>('/api/settings'),

  /** Absent fields are left alone; an empty string clears a path back to being detected. */
  saveSettings: (update: Partial<Omit<Settings, 'resolvedGameDirectory' | 'resolvedScreenshotDirectory' | 'gameDirectoryDetected'>>) =>
    post<Settings>('/api/settings', update),

  /**
   * Puts the overlay back where it starts.
   *
   * The recovery for a window dragged onto a monitor that is no longer there: there is nothing on
   * screen to grab, so no amount of dragging brings it back.
   */
  resetOverlayPlace: () => post<unknown>('/api/settings/overlay/reset', {}),

  hideout: (lookAhead?: number) =>
    get<HideoutState>(`/api/hideout${lookAhead ? `?lookAhead=${lookAhead}` : ''}`),

  setLookAhead: (levels: number) =>
    post<{ lookAhead: number }>('/api/hideout/look-ahead', { levels }),

  setHideoutLevel: (stationId: string, level: number) =>
    post<{ id: string; level: number }>(`/api/progress/hideout/${encodeURIComponent(stationId)}`, { level }),

  targetUpgrade: (stationId: string, level: number, targeted: boolean) =>
    post<unknown>(
      `/api/hideout/${encodeURIComponent(stationId)}/levels/${level}/target`, { targeted }),


  /**
   * The goals you are collecting for, named by you.
   *
   * This replaced a searchable catalogue of every barter and craft in the game: picking one out of
   * 789 needed you to already know which of Therapist's four Dorm 303 trades you meant.
   */
  goals: () => get<GoalView[]>('/api/goals'),

  saveGoal: (goal: {
    id?: string
    name: string
    times?: number
    items: { itemId: string; count: number }[]
  }) => post<unknown>('/api/goals', goal),

  removeGoal: (id: string) => del<unknown>(`/api/goals/${encodeURIComponent(id)}`),

  /** Quests whose every planned objective is done, waiting on a trader. */
  turnIns: () => get<TurnIn[]>('/api/raid/turn-ins'),

  markTaskState: (taskId: string, state: string) =>
    post<unknown>(`/api/progress/tasks/${encodeURIComponent(taskId)}`, { state }),

  removeStop: (objectiveId: string) =>
    del<RaidView>(`/api/raid/stops/${encodeURIComponent(objectiveId)}`),

  clearPlan: () => del<RaidView>('/api/raid/plan'),

  /**
   * The pictures on a quest's wiki article — which building, which door.
   *
   * Fetched from the wiki and credited to it, never redistributed: they are other people's work
   * under CC BY-SA.
   */
  taskImages: (taskId: string) =>
    get<{ taskName: string; wikiUrl: string | null; images: WikiImage[] }>(
      `/api/tasks/${encodeURIComponent(taskId)}/images`),

  /**
   * A quest, as you would read it standing at one of its waypoints: what it wants, which step this
   * pin serves, and the wiki's pictures of the place.
   */
  questBrief: (taskId: string, objectiveId?: string | null) =>
    get<QuestBriefing>(
      `/api/tasks/${encodeURIComponent(taskId)}/brief`
      + (objectiveId ? `?objectiveId=${encodeURIComponent(objectiveId)}` : '')),

  /** One item, with everything anyone asks about it: why it is needed, and how many you have. */
  item: (id: string) => get<ItemDetail>(`/api/items/${encodeURIComponent(id)}`),

  /**
   * Asks the desktop app to open a folder picker, and returns what was chosen.
   *
   * Null when it was cancelled, or when nothing can put a window on screen.
   */
  browseForFolder: async (start?: string | null) => {
    const answer = await post<{ path: string } | null>('/api/settings/browse', { start })
    return answer?.path ?? null
  },

  /** Maps that are coming: a drawing exists and only the orientation is still being worked out. */
  heldBackMaps: () => get<HeldBackMap[]>('/api/maps/held-back'),

  /** The last position read from a screenshot, before it is placed on any map. */
  latestPosition: () =>
    get<{ x: number; y: number; z: number; takenAt: string; mapId: string | null } | null>(
      '/api/position/latest'),

  /** Settles a map's layout from where you were and where that is on the drawing. */
  calibrate: (mapId: string, world: { x: number; y: number; z: number }, imageX: number, imageY: number) =>
    post<{ settled: boolean; mapping: string; miss: number; runnerUpMiss: number; reason: string }>(
      `/api/maps/${encodeURIComponent(mapId)}/calibrate`, { ...world, imageX, imageY }),

  forgetCalibration: (mapId: string) =>
    del<unknown>(`/api/maps/${encodeURIComponent(mapId)}/calibrate`),

  /** The names players use for places — "Old Gas", "Dorms" — with where each one is. */
  places: (mapId: string) =>
    get<PlaceLabel[]>(`/api/maps/${encodeURIComponent(mapId)}/places`),

  /**
   * Spots you marked by hand.
   *
   * Kept apart from plans: a plan is for one raid and gets cleared, and "car batteries behind the
   * garage" is true every raid.
   */
  waypoints: (mapId: string) =>
    get<CustomWaypoint[]>(`/api/maps/${encodeURIComponent(mapId)}/waypoints`),

  addWaypoint: (
    mapId: string, label: string, x: number, y: number,
    floor?: string | null, kind: 'Place' | 'Item' = 'Place',
  ) =>
    post<CustomWaypoint>(
      `/api/maps/${encodeURIComponent(mapId)}/waypoints`, { label, x, y, floor, kind }),

  renameWaypoint: (id: string, label: string) =>
    post<unknown>(`/api/waypoints/${encodeURIComponent(id)}/label`, { label, x: 0, y: 0 }),

  removeWaypoint: (id: string) => del<unknown>(`/api/waypoints/${encodeURIComponent(id)}`),

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

  /**
   * A wiki picture, through RatNav rather than straight from the wiki's CDN.
   *
   * Loading them from here does not work: the CDN answers a request carrying a foreign Referer
   * with a 404 and a placeholder, which is what the carousel was drawing under its correct
   * titles. Going through the service also means each picture is fetched once and kept.
   */
  wikiPictureUrl: (url: string) => `/api/wiki/picture?url=${encodeURIComponent(url)}`,
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
