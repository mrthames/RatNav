import { screen } from '@testing-library/react'
import type { MapSummary, RaidStop, RaidView } from '../api'
import { vi } from 'vitest'

/**
 * A stand-in for the RatNav service.
 *
 * <p>Every view in this app is a thin thing over an API, so almost every test is really a question
 * about what happens when the service says a particular thing — including when it says no. Rather
 * than each test hand-rolling a fetch mock, they declare what the service answers and then assert
 * on behaviour.</p>
 *
 * <p>Routes are matched on the path, longest pattern first, so a test can stub `/api/tasks`
 * broadly and `/api/tasks?filter=all` specifically without the order of the object mattering.</p>
 */

export type Reply =
  | unknown
  | ((request: { url: string; method: string; body: unknown }) => unknown)

/** A call the app made, in the order it made it. */
export interface Call {
  url: string
  method: string
  body: unknown
}

export interface Service {
  /** Everything asked for, in order. */
  calls: Call[]
  /** Just the calls to a path, which is usually what an assertion is about. */
  callsTo(pattern: string): Call[]
  /** Replaces or adds a route mid-test, for "and now the service says something else". */
  answer(pattern: string, reply: Reply): void
}

/** Reply with an HTTP failure rather than a body, for testing what the app does when told no. */
export function fails(status = 500, message = 'the service said no') {
  return { __failure: true, status, message }
}

function isFailure(value: unknown): value is { __failure: true; status: number; message: string } {
  return typeof value === 'object' && value !== null && '__failure' in value
}

/**
 * Stubs `fetch` for the current test.
 *
 * <p>Anything not listed throws by name — see the note in `setup.ts`. That is deliberate: a view
 * that starts calling something new should break the test that did not expect it, rather than
 * quietly reaching a real service.</p>
 */
export function serve(routes: Record<string, Reply>): Service {
  const table = new Map<string, Reply>(Object.entries(routes))
  const calls: Call[] = []

  const match = (url: string) =>
    [...table.keys()]
      .filter((pattern) => url.includes(pattern))
      .sort((a, b) => b.length - a.length)[0]

  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const method = init?.method ?? 'GET'

    let body: unknown = undefined
    if (typeof init?.body === 'string') {
      try { body = JSON.parse(init.body) } catch { body = init.body }
    }

    calls.push({ url, method, body })

    const pattern = match(url)
    if (pattern === undefined) {
      throw new Error(
        `Unstubbed fetch: ${method} ${url}\n` +
        `Known routes: ${[...table.keys()].join(', ') || '(none)'}`,
      )
    }

    const reply = table.get(pattern)
    const value = typeof reply === 'function'
      ? (reply as (r: Call) => unknown)({ url, method, body })
      : reply

    if (isFailure(value)) {
      return new Response(JSON.stringify({ error: value.message }), {
        status: value.status,
        headers: { 'content-type': 'application/json' },
      })
    }

    return new Response(JSON.stringify(value ?? null), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }))

  return {
    calls,
    callsTo: (pattern: string) => calls.filter((c) => c.url.includes(pattern)),
    answer: (pattern: string, reply: Reply) => { table.set(pattern, reply) },
  }
}

/**
 * Finds the innermost element whose whole text reads as `text`.
 *
 * <p>`getByText` matches a single text node, so a heading built as `{name} {level}` — two nodes
 * with a space between — cannot be found by "Gym 1", and the failure says the text is "broken up
 * by multiple elements" without saying what to do instead. This is what to do instead.</p>
 *
 * <p>Innermost matters: without it, every ancestor containing the phrase also matches and the
 * query fails for finding too many.</p>
 */
export function line(text: string) {
  const flat = (node: Element | null) => node?.textContent?.replace(/\s+/g, ' ').trim()

  return screen.getByText((_, element) =>
    flat(element) === text &&
    !Array.from(element?.children ?? []).some((child) => flat(child) === text))
}

/*
 * Fixtures.
 *
 * <p>Every one of these carries **all** the fields the app reads, not only the ones the test is
 * about. A view that maps over a missing array throws during render, React unmounts the subtree,
 * and what a test sees is an empty page and an assertion failure that says nothing about the
 * cause — which is a long way to travel to learn that a fixture was short a key.</p>
 *
 * <p>So: add to these rather than hand-rolling an object in a test. When the API grows a field,
 * this is the one place that has to hear about it.</p>
 */

/** A map, with every field the pickers and the plan page read. */
export function aMap(over: Partial<MapSummary> = {}): MapSummary {
  return {
    id: 'customs',
    name: 'Customs',
    normalizedName: 'customs',
    workInProgress: false,
    calibrated: true,
    imageUrl: null,
    coordinateRotation: 0,
    extractCount: 8,
    calibrationVerified: true,
    ...over,
  }
}

/** A quest, as the Quests page reads it. */
export function aQuest(over: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'q1',
    name: 'Debut',
    traderName: 'Prapor',
    state: 'NotStarted',
    kappa: false,
    minPlayerLevel: 0,
    positionedObjectiveCount: 0,
    wikiUrl: null,
    ...over,
  }
}

/** One thing an upgrade still wants. */
export function anItem(over: Partial<Record<string, unknown>> = {}) {
  return {
    itemId: 'bolts',
    name: 'Bolts',
    shortName: null,
    count: 1,
    have: 0,
    foundInRaid: false,
    ...over,
  }
}

/** A hideout upgrade. `blockers` is the field everybody forgets, and forgetting it throws. */
export function anUpgrade(over: Partial<Record<string, unknown>> = {}) {
  return {
    stationId: 'gym',
    stationName: 'Gym',
    level: 1,
    wave: 1,
    targeted: false,
    description: null,
    constructionTimeSeconds: 0,
    blockers: [],
    items: [anItem()],
    ...over,
  }
}

/** A station as it appears in the row of levels. */
export function aStation(over: Partial<Record<string, unknown>> = {}) {
  return { id: 'gym', name: 'Gym', imageUrl: null, builtLevel: 0, maxLevel: 3, ...over }
}

/** What a settings page needs to render at all. */
export function theSettings(over: Partial<Record<string, unknown>> = {}) {
  const drive = (letter: string, rest: string) =>
    `${letter}:${String.fromCharCode(92)}${rest}`

  return {
    gameDirectory: null,
    screenshotDirectory: null,
    screenshotKey: 'Middle Mouse',
    screenshotDisposal: 'archive',
    owner: null,
    hotkeys: {
      toggleOverlay: 'F5', toggleInteract: 'F6', toggleMode: 'F7', toggleFollow: 'F8',
      centerMap: 'F9', readExtracts: 'F10', identifyItem: 'F11',
    },
    playerLevel: 22,
    gameEdition: 'standard',
    hideoutLookAhead: 1,
    checkForUpdates: true,
    suggestedPlayerLevel: 20,
    resolvedGameDirectory: drive('F', 'Escape From Tarkov'),
    resolvedScreenshotDirectory: drive('D', 'Screenshots'),
    gameDirectoryDetected: true,
    ...over,
  }
}

/** An item as the Items page reads it. Every field, because the page reads most of them. */
export function aTrackedItem(over: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'i1',
    name: 'Bolts',
    shortName: null,
    iconUrl: null,
    wikiUrl: null,
    avg24hPrice: null,
    category: null,
    questNeeded: 0,
    questFor: [],
    hideoutNeeded: 0,
    hideoutUpgrade: null,
    hideoutWave: null,
    needed: 1,
    have: 0,
    remaining: 1,
    foundInRaid: false,
    isKey: false,
    goalNeeded: 0,
    goalFor: [],
    watched: false,
    watchNote: null,
    watchTarget: null,
    ...over,
  }
}

/** The two characters a test needs to see the picker work. */
export const theProfiles = {
  current: 'pvp-seasonal',
  all: [{ id: 'pvp-seasonal', name: 'PvP Seasonal' }, { id: 'pvp', name: 'PvP' }],
}

/**
 * A raid with nothing happening, which is what most pages start from.
 *
 * <p>Typed, so that spreading it with a real map id in a test is not rejected for widening `null`
 * into `string`.</p>
 */
export const noRaid: RaidView = {
  inRaid: false,
  hasPlan: false,
  hasMap: false,
  planMapName: null,
  mapId: null,
  mapName: null,
  x: null,
  y: null,
  headingDegrees: null,
  fixedAt: null,
  floor: null,
  stops: [],
  completedObjectiveIds: [],
  trail: [],
}

/** One stop on a plan, with every field RaidStop actually has. */
export function aStop(over: Partial<RaidStop> = {}): RaidStop {
  return {
    objectiveId: 'o1',
    taskId: 't1',
    taskName: 'Debut',
    description: 'Find the thing',
    x: 0.5,
    y: 0.5,
    owner: null,
    place: 'Dorms',
    done: false,
    ...over,
  }
}
