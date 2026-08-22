import { describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlanView } from './PlanView'
import { aMap, aStop, noRaid, serve } from './test/service'

/**
 * The page a raid is built on, and the one with the most to lose.
 *
 * <p>The tick list is component state. The plan it produced lives on the service. Those two can
 * disagree, and when they do the failure is silent and expensive: adding one stop to a plan of
 * seven can replace it with a plan of one, and nothing about the screen says that is what
 * happened. Most of what follows is about that.</p>
 */

const MAPS = [
  aMap({ id: 'streets', name: 'Streets of Tarkov', normalizedName: 'streets' }),
  aMap(),
]

function objective(over: Partial<Record<string, unknown>> = {}) {
  return {
    objectiveId: 'o1',
    taskId: 't1',
    taskName: 'Debut',
    traderName: 'Prapor',
    description: 'Find the thing',
    optional: false,
    x: 0.5,
    y: 0.5,
    place: 'Dorms',
    neededKeyItemIds: [],
    itemIds: [],
    required: [],
    ...over,
  }
}

function open(raid = noRaid, over: Record<string, unknown> = {}) {
  const service = serve({
    '/api/maps/streets/plannable': [
      objective(),
      objective({ objectiveId: 'o2', taskId: 't2', taskName: 'Checking', description: 'Do the other thing' }),
    ],
    '/api/maps/customs/plannable': [],
    '/api/maps/streets/waypoints': [],
    '/api/maps/customs/waypoints': [],
    '/api/raid/turn-ins': [],
    '/api/raid/plan': { ok: true },
    '/api/plans': [],
    ...over,
  })
  render(<PlanView maps={MAPS} raid={raid} />)
  return service
}

describe('building a plan', () => {
  it('offers the objectives for the map, grouped by where they are', async () => {
    open()

    await screen.findByText(/Find the thing/)
    expect(screen.getByText(/DORMS/i)).toBeInTheDocument()
  })

  it('will not build an empty plan', async () => {
    open()

    await screen.findByText(/Find the thing/)
    expect(screen.getByRole('button', { name: /Plan this raid/i })).toBeDisabled()
  })

  it('numbers the stops in the order they were ticked', async () => {
    const user = userEvent.setup()
    open()

    await screen.findByText(/Find the thing/)
    const boxes = screen.getAllByRole('checkbox')

    // What you tick first is where you go first. Nothing re-orders it afterwards.
    await user.click(boxes[1])
    await user.click(boxes[0])

    await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument())
    expect(screen.getByText('2')).toBeInTheDocument()
  })

  it('sends the ticked objectives, in that order', async () => {
    const user = userEvent.setup()
    const service = open()

    await screen.findByText(/Find the thing/)
    const boxes = screen.getAllByRole('checkbox')
    await user.click(boxes[1])
    await user.click(boxes[0])
    await user.click(screen.getByRole('button', { name: /Plan this raid/i }))

    await waitFor(() => {
      const built = service.calls.find((c) => c.method === 'POST' && c.url.includes('plan'))
      expect(built?.body).toMatchObject({ objectiveIds: ['o2', 'o1'] })
    })
  })
})

describe('a plan that already exists', () => {
  const planned = {
    ...noRaid,
    hasPlan: true,
    mapId: 'streets',
    mapName: 'Streets of Tarkov',
    stops: [
      aStop(),
      aStop({ objectiveId: 'o2', taskId: 't2', taskName: 'Checking', description: 'Do the other thing' }),
    ],
  }

  it('folds the page down to the plan', async () => {
    open(planned)

    await screen.findByText(/End raid/i)
    // The picking half is put away: the plan is what you came for.
    expect(screen.queryByRole('button', { name: /Plan this raid/i })).not.toBeInTheDocument()
  })

  it('offers a deliberate way back to the list', async () => {
    const user = userEvent.setup()
    open(planned)

    const add = await screen.findByRole('button', { name: /Add a stop to this plan/i })
    await user.click(add)

    await waitFor(() => expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(0))
  })

  it('seeds the ticks from the running plan, so adding one stop cannot replace it', async () => {
    const user = userEvent.setup()
    open(planned)

    await user.click(await screen.findByRole('button', { name: /Add a stop to this plan/i }))

    // The ticks live in this component and the plan lives on the service. If a reload emptied the
    // ticks while the plan carried on existing, "add one stop" would quietly become "replace the
    // plan with one stop" — silently, and with no way back to what was dropped.
    await waitFor(() => {
      const ticked = screen.getAllByRole('checkbox').filter((b) => (b as HTMLInputElement).checked)
      expect(ticked).toHaveLength(2)
    })
  })
})

describe('the objectives strip', () => {
  it('counts what has been picked', async () => {
    const user = userEvent.setup()
    open()

    await screen.findByText(/Find the thing/)
    await user.click(screen.getAllByRole('checkbox')[0])

    await screen.findByText(/1 objective/i)
  })

  it('names a key that has to be carried, because that cannot be fixed once queued', async () => {
    const user = userEvent.setup()
    open(noRaid, {
      '/api/maps/streets/plannable': [
        objective({ required: [{ itemId: 'k1', name: 'Iron gate key', isKey: true, count: 1 }] }),
      ],
    })

    await screen.findByText(/Find the thing/)
    await user.click(screen.getAllByRole('checkbox')[0])

    await screen.findByText('Iron gate key')
  })
})
