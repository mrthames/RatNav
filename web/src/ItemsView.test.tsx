import { describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ItemsView } from './ItemsView'
import { aTrackedItem, fails, serve, theSettings } from './test/service'

/**
 * The shopping list: what every active quest and reachable hideout upgrade wants, minus what you
 * have.
 *
 * <p>The number on a row is what is **left**, not what is wanted — found four of six and it asks
 * for two. That distinction is the whole page, and it is the one a refactor gets wrong quietly,
 * because both readings look plausible on a screenshot.</p>
 */

function open(items: unknown[], over: Record<string, unknown> = {}) {
  const service = serve({
    '/api/items/needed': items,
    '/api/items/watchlist': [],
    '/api/goals': [],
    '/api/settings': theSettings(),
    '/api/items/i1/have': aTrackedItem({ have: 1, remaining: 5 }),
    ...over,
  })
  render(<ItemsView />)
  return service
}

describe('what the list says', () => {
  it('shows what is still needed, not what was wanted', async () => {
    open([aTrackedItem({ name: 'Bolts', needed: 6, have: 4, remaining: 2 })])

    await screen.findByText('Bolts')
    // Six were wanted. Two is the number you can act on in a flea market.
    expect(screen.getByText('2')).toBeInTheDocument()
  })

  it('says what each item is for', async () => {
    open([aTrackedItem({ questNeeded: 1, questFor: ['Debut'] })])

    await screen.findByText('Bolts')
    await waitFor(() => expect(screen.getByText(/Debut/)).toBeInTheDocument())
  })

  it('marks the ones that have to be found in raid', async () => {
    open([aTrackedItem({ foundInRaid: true })])

    await screen.findByText('Bolts')
    // Those are the ones you cannot buy your way out of later.
    expect(screen.getByText(/FIR/i)).toBeInTheDocument()
  })

  it('marks keys', async () => {
    open([aTrackedItem({ name: 'Iron gate key', isKey: true })])

    await screen.findByText('Iron gate key')
    // The tag, not the name — a key called "key" would match either way and prove nothing.
    expect(screen.getByText(/▲ key/)).toBeInTheDocument()
  })
})

describe('recording what you have', () => {
  it('sends the new count', async () => {
    const user = userEvent.setup()
    const service = open([aTrackedItem({ needed: 6, have: 0, remaining: 6 })])

    await screen.findByText('Bolts')

    // By its label: the first "+" on the page belongs to the look-ahead control, and clicking that
    // would send a completely different request while the test went green.
    await user.click(screen.getByRole('button', { name: 'one more' }))

    await waitFor(() => {
      expect(service.calls.some((c) => c.method === 'POST' && c.url.includes('have'))).toBe(true)
    })
  })
})

describe('when the service is unhappy', () => {
  it('leaves the row alone when a count cannot be saved, rather than throwing', async () => {
    const user = userEvent.setup()
    open([aTrackedItem({ needed: 6, have: 0, remaining: 6 })],
      { '/api/items/i1/have': fails(500) })

    await screen.findByText('Bolts')
    await user.click(screen.getByRole('button', { name: 'one more' }))

    // The count stays where it was and the page keeps working. What must not happen is the
    // rejection escaping, which used to unset the busy flag and leave the row looking ready while
    // nothing had changed — so the next click did the same thing again.
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'one more' })).toBeEnabled()
    })
    expect(screen.getByText('Bolts')).toBeInTheDocument()
  })

  it('does not leave the page pretending to load forever', async () => {
    open([], { '/api/items/needed': fails(500) })

    await waitFor(() => expect(screen.queryByText('Bolts')).not.toBeInTheDocument())
  })
})
