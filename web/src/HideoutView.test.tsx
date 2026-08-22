import { describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HideoutView } from './HideoutView'
import { anItem, anUpgrade, aStation, fails, line, serve } from './test/service'

/**
 * The hideout is a build order, and **Look ahead** is what makes it readable: at 0 it is what you
 * could build tonight, and every step past that adds what one more upgrade unlocks. Turn it up too
 * far and it becomes the several-hundred-item list nobody can shop from.
 *
 * <p>It is also one number shared by three places — this page, the items page and the overlay — so
 * the control has to write it back rather than keep a copy.</p>
 */

const STATE = {
  lookAhead: 1,
  stations: [aStation()],
  upcoming: [
    anUpgrade({ items: [anItem({ count: 15 })] }),
    anUpgrade({ stationId: 'library', stationName: 'Library', wave: 2 }),
  ],
}

function open(over: Record<string, unknown> = {}) {
  const service = serve({ '/api/hideout': STATE, ...over })
  render(<HideoutView />)
  return service
}

describe('the build order', () => {
  it('lists what could be built and what it still takes', async () => {
    open()

    await screen.findByText(/Buildable now/)

    // The heading is the station and the level it would reach, built from two nodes.
    expect(line('Gym 1')).toBeInTheDocument()
    expect(line('15 Bolts')).toBeInTheDocument()
  })

  it('shows the stations and their levels', async () => {
    open()

    // Named once as an upgrade and once in the row of levels you set by hand.
    await waitFor(() => expect(screen.getAllByText('Gym').length).toBeGreaterThan(0))
  })
})

describe('look ahead', () => {
  it('sends the new depth so every other view agrees', async () => {
    const user = userEvent.setup()
    const service = open()

    await screen.findByText(/Buildable now/)
    const buttons = screen.getAllByRole('button')
    const up = buttons.find((b) => b.textContent === '+')
    await user.click(up!)

    // One setting with three dials on it. A copy kept here would drift from the items page.
    await waitFor(() => {
      expect(service.callsTo('/api/hideout/look-ahead').length).toBeGreaterThan(0)
    })
  })

  it('will not go below zero', async () => {
    const user = userEvent.setup()
    const service = open({ '/api/hideout': { ...STATE, lookAhead: 0 } })

    await screen.findByText(/Buildable now/)
    const down = screen.getAllByRole('button').find((b) => b.textContent === '−')

    // Zero is "what I could build tonight". There is nothing shallower to ask for.
    expect(down).toBeDisabled()
    if (down && !down.hasAttribute('disabled')) {
      await user.click(down)
      expect(service.callsTo('/api/hideout/look-ahead')).toHaveLength(0)
    }
  })
})

describe('when the service is unhappy', () => {
  it('says why there is nothing, rather than loading forever', async () => {
    open({ '/api/hideout': fails(500) })

    // Waiting forever is the one answer that tells you nothing: not whether it is slow, not
    // whether it is broken, not whether to reload.
    await screen.findByText(/could not be read/i)
    expect(screen.queryByText(/loading hideout/i)).not.toBeInTheDocument()
  })

})
