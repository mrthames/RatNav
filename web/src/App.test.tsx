import { describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import { fails, serve } from './test/service'

/**
 * Onboarding, which is the part of RatNav most people see once and never again — and therefore the
 * part nobody notices breaking.
 *
 * <p>Two things have to hold. Somebody whose setup is not finished must be told so on whichever
 * page they landed on, because every page is empty for a reason none of them explains. And
 * somebody whose service is simply unreachable must not be shown that same wall, because "finish
 * setup" is the wrong answer to "the service is down".</p>
 */

const READY = {
  done: true,
  setupComplete: true,
  missing: [],
  steps: [],
}

const NOT_READY = {
  done: false,
  setupComplete: false,
  missing: [
    { name: 'Game found', detail: 'No Escape from Tarkov folder set.', fix: 'Set it on Setup.' },
    { name: 'Screenshot folder', detail: '', fix: 'Point RatNav at it.' },
  ],
  steps: [],
}

const STATUS = {
  loaded: true,
  fetchedAt: '2026-08-22T00:00:00Z',
  taskCount: 517,
  itemCount: 5312,
  mapCount: 14,
  barterCount: 789,
  calibratedMapCount: 10,
  servingStale: false,
  lastError: null,
  brokenSources: {},
}

function open(firstRun: unknown, over: Record<string, unknown> = {}) {
  const service = serve({
    '/api/first-run': firstRun,
    '/api/status': STATUS,
    '/api/maps': [],
    '/api/raid': { inRaid: false, hasPlan: false, stops: [], completedObjectiveIds: [], trail: [] },
    // Listed separately: '/api/raid' would otherwise match it and hand back an object where the
    // page expects a list.
    '/api/raid/turn-ins': [],
    '/api/traders': [],
    '/api/tasks': [],
    '/api/plans': [],
    ...over,
  })
  render(<App />)
  return service
}

describe('when setup is not finished', () => {
  it('says so, and names what is missing', async () => {
    open(NOT_READY)

    await screen.findByText(/RatNav cannot see the game yet/)

    // Not a padlock. What is wrong, named, in words somebody can act on.
    expect(screen.getByText('Game found')).toBeInTheDocument()
    expect(screen.getByText('Screenshot folder')).toBeInTheDocument()
    expect(screen.getByText(/No Escape from Tarkov folder set/)).toBeInTheDocument()
  })

  it('offers a way to the page that fixes it', async () => {
    const user = userEvent.setup()
    open(NOT_READY)

    await user.click(await screen.findByRole('button', { name: 'Go to Setup' }))

    // Setup itself is never gated — it is the way out, so the wall must not follow you onto it.
    await waitFor(() => {
      expect(screen.queryByText(/RatNav cannot see the game yet/)).not.toBeInTheDocument()
    })
  })
})

describe('when setup is finished', () => {
  it('shows no wall at all', async () => {
    open(READY)

    await waitFor(() => {
      expect(screen.queryByText(/RatNav cannot see the game yet/)).not.toBeInTheDocument()
    })
  })
})

describe('when the service cannot be reached', () => {
  it('does not claim setup is incomplete', async () => {
    open(fails(500))

    // Unknown is not blocked. A wall saying "finish setup" is a wrong answer, and a confident one,
    // which is worse than letting somebody look at whatever is there.
    await waitFor(() => {
      expect(screen.queryByText(/RatNav cannot see the game yet/)).not.toBeInTheDocument()
    })
  })

  it('still renders the navigation', async () => {
    open(fails(500))

    for (const view of ['plan', 'items', 'quests', 'maps', 'setup']) {
      const tab = await screen.findByRole('button', { name: new RegExp('^' + view + '$') })
      expect(tab).toBeInTheDocument()
    }
  })
})

describe('the navigation', () => {
  it('moves between views', async () => {
    const user = userEvent.setup()
    open(READY)

    await user.click(await screen.findByRole('button', { name: /^quests$/ }))
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument())
  })

  it('re-checks setup when the view changes, since finishing it is what clears the wall', async () => {
    const user = userEvent.setup()
    const service = open(NOT_READY)

    await screen.findByText(/RatNav cannot see the game yet/)
    const before = service.callsTo('/api/first-run').length

    await user.click(screen.getByRole('button', { name: /^setup$/ }))

    await waitFor(() => {
      expect(service.callsTo('/api/first-run').length).toBeGreaterThan(before)
    })
  })
})
