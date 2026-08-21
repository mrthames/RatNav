/** Types and calls for the local RatNav service. */

/** Whether RatNav answers on the network, and what stands between a phone and it. */
export interface LanInfo {
  enabled: boolean
  port: number

  /** What the running service is actually doing, which can differ from what is saved. */
  running: boolean

  /** This machine's addresses on the local network — what you type on the phone. */
  addresses: string[]

  firewallAllowed: boolean
  firewallCommand: string
}

/** What a new install still has to do. Every step is derived from real state, never remembered. */
export interface FirstRun {
  done: boolean

  /**
   * Whether RatNav can see the game at all.
   *
   * <p>Separate from the four steps because it is a prerequisite rather than the first of them.
   * The others are things you have not done yet; this one leaves every page empty for a reason
   * no page explains.</p>
   */
  setupComplete: boolean

  /** The required checks that are failing, so the way back can say what is wrong. */
  missing: { name: string; detail: string; fix: string }[]

  steps: { id: string; title: string; why: string; done: boolean; view: string }[]
}

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

  /** A transit to another map rather than a way out of the raid. */
  transit?: boolean
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

  /** What the handbook files it under — "Food", "Mechanical keys". Null where it files it nowhere. */
  category: string | null
  questNeeded: number

  /** The active quests wanting it, by name. */
  questFor: string[]
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

/** In the order the keys are used, which is the order F5 to F11 run in. */
export interface HotKeys {
  toggleOverlay: string
  toggleInteract: string
  toggleMode: string
  /** Hold the map still, or have it keep you centred. */
  toggleFollow: string
  /** Put the map back on you, once, without starting to follow. */
  centerMap: string
  /** Read the extract list the game is showing, while it is showing it. */
  readExtracts: string
  identifyItem: string
}

/** What version this is, and whether GitHub has a newer stable one. */
export interface UpdateStatus {
  current: string
  latest: string | null
  available: boolean
  url: string | null
  checkedAt: string | null

  /** Set when the check could not be made. Never worth interrupting anybody about. */
  problem: string | null
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

  /** How deep into the hideout build order to count. One setting, three dials. */
  hideoutLookAhead: number

  /** Whether the daily update check runs. A manual check ignores it. */
  checkForUpdates: boolean
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

/** Which character RatNav is tracking. */
export interface Profiles {
  current: string
  all: { id: string; name: string }[]
}

/** One line of the key-bind reminder strip. */
export interface HotkeyHint {
  key: string
  does: string
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
  /**
   * Kept because older saved waypoints carry one, and no longer chosen when adding.
   *
   * <p>It drove which of two shapes the pin took. A waypoint is a waypoint: what separates one of
   * yours from a quest's is where it came from, which is what its colour says.</p>
   */
  kind: 'Place' | 'Item'

  /**
   * What you could not otherwise remember about the place.
   *
   * <p>Separate from the label because they are different lengths of thing. A label is drawn on a
   * map over a game and has to be short; a note is read standing still and can be a sentence.</p>
   */
  note: string | null
  createdAt: string
}

export interface GoalView {
  id: string
  name: string
  /** How many times over. Two of a goal wants twice its items. */
  times: number
  items: {
    itemId: string
    name: string
    iconUrl: string | null
    count: number
    /** How many you have found for *this* one. Not a stash total. */
    found: number
    /** Whether it has to be found in raid. Yours to set — RatNav cannot know. */
    foundInRaid: boolean
  }[]
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
  trail: { x: number; y: number }[]
}

export interface Diagnostics {
  ready: boolean
  openInBrowserUrl: string
  checks: { name: string; ok: boolean; detail: string; fix: string; required: boolean }[]
  installs: { directory: string; version: string | null; lastPlayed: string | null; chosen: boolean }[]
}

export type InkLevel = 'graphical' | 'full' | 'structure' | 'outline'

/**
 * Turns a failed response into the reason the service gave for it.
 *
 * <p>RatNav explains its refusals carefully — "That code is incomplete or damaged, ask for it
 * again", "That plan has no stops in it" — and returns each as <code>{ error }</code> beside the
 * status. Every one was thrown away here and replaced with a URL and a number, so the first tester
 * to hit one saw <em>"/api/plans/import-code returned 400"</em> and nobody could tell which refusal
 * had fired, including us.</p>
 *
 * <p>Falls back to the status when there is no body to read: a 500 from a crash has nothing to say,
 * and inventing something is worse than a number.</p>
 */
async function failure(path: string, response: Response): Promise<Error> {
  try {
    const body = await response.json() as { error?: unknown }

    if (typeof body?.error === 'string' && body.error.trim()) return new Error(body.error)
  } catch {
    // No body, or not JSON. The status is all there is.
  }

  return new Error(`${path} returned ${response.status}`)
}

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) throw await failure(path, response)
  return response.json() as Promise<T>
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw await failure(path, response)
  return response.json() as Promise<T>
}

async function del<T>(path: string): Promise<T> {
  const response = await fetch(path, { method: 'DELETE' })
  if (!response.ok) throw await failure(path, response)
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

  /**
   * Whether there is a newer RatNav.
   *
   * <p>`force` is the manual check: it asks GitHub whatever the age of the cached answer, and it
   * asks even when the daily check is turned off — somebody pressing the button is somebody
   * asking. Without it the answer is at most a day old and costs nothing.</p>
   */
  update: (force = false) => get<UpdateStatus>(`/api/update${force ? '?force=true' : ''}`),

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
    items: { itemId: string; count: number; foundInRaid?: boolean }[]
  }) => post<unknown>('/api/goals', goal),

  removeGoal: (id: string) => del<unknown>(`/api/goals/${encodeURIComponent(id)}`),

  /** Found one, or put one back. */
  adjustGoalItem: (goalId: string, itemId: string, by: number) =>
    post<unknown>(
      `/api/goals/${encodeURIComponent(goalId)}/items/${encodeURIComponent(itemId)}`,
      { by }),

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

  addWaypoint: (mapId: string, label: string, x: number, y: number, floor?: string | null) =>
    post<CustomWaypoint>(
      `/api/maps/${encodeURIComponent(mapId)}/waypoints`, { label, x, y, floor }),

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

  /** Stops RatNav itself, not just this page. The overlay and the service go with it. */
  quit: () => post<{ quitting: boolean }>('/api/quit', {}),

  /** Sets or clears a mark's note. Blank clears it. */
  setWaypointNote: (markId: string, note: string) =>
    post<{ id: string; note: string }>(`/api/waypoints/${encodeURIComponent(markId)}/note`, { note }),

  /** The four things a new install has to do, and which are done. */
  firstRun: () => get<FirstRun>('/api/first-run'),

  /** Whether RatNav answers on the network, and what stands between you and it. */
  lan: () => get<LanInfo>('/api/lan'),

  saveLan: (change: { enabled?: boolean; port?: number }) =>
    post<{ needsRestart: boolean }>('/api/lan', change),

  /** Raises a UAC prompt on the machine RatNav runs on. Refused from anywhere else. */
  allowThroughFirewall: () =>
    post<{ added: boolean; problem: string | null }>('/api/lan/firewall', {}),

  setTaskState: (taskId: string, state: string) =>
    post<{ id: string; state: string }>(`/api/progress/tasks/${encodeURIComponent(taskId)}`, { state }),
  /** The objectives of your active quests on a map. */
  objectives: (mapId: string) =>
    get<ObjectivePin[]>(`/api/maps/${encodeURIComponent(mapId)}/objectives?active=true`),

  /** Every quest's objectives on a map, including ones you have not accepted. */
  allObjectives: (mapId: string) =>
    get<ObjectivePin[]>(`/api/maps/${encodeURIComponent(mapId)}/objectives`),

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

  /** The key-bind reminder strip, the same list the overlay shows along its own footer. */
  hotkeyHints: () => get<HotkeyHint[]>('/api/hotkeys/hints'),

  /** Which character is being tracked, and which others there are. */
  profiles: () => get<Profiles>('/api/profiles'),

  useProfile: (id: string) =>
    post<{ current: string; name: string }>(`/api/profiles/${encodeURIComponent(id)}`, {}),

  /** Back to a fresh character. Names the profile rather than assuming the current one. */
  wipeProfile: (id: string) =>
    post<{ wiped: string; name: string }>(
      `/api/profiles/${encodeURIComponent(id)}/wipe`, {}),
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

/**
 * A quantity, grouped.
 *
 * <p>Hideout upgrades and quests ask for roubles alongside bolts, and "400000" beside "12" is
 * two numbers written in different languages. Small counts are unchanged — grouping starts where
 * it starts helping.</p>
 */
export function amount(value: number): string {
  return value.toLocaleString('en-US')
}
