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

export type InkLevel = 'full' | 'structure' | 'outline'

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path)
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
