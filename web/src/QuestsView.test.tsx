import { describe, expect, it } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QuestsView } from './QuestsView'
import { aQuest, fails, serve } from './test/service'

/**
 * Setting up a fresh install means marking fifty or more quests active, read off the game on the
 * other monitor. These are about the keyboard path that makes that bearable — and about the parts
 * of it that are easy to break without noticing, because they are about focus and clearing rather
 * than about anything visible in a diff.
 */

const TRADERS = [{ name: 'Prapor', level: 2, imageUrl: null }]

function quests(...over: Partial<Record<string, unknown>>[]) {
  return over.map((o, i) => aQuest({ id: `q${i + 1}`, ...o }))
}

function show(tasks: unknown[]) {
  const service = serve({
    '/api/traders': TRADERS,
    '/api/tasks': tasks,
    '/api/progress/tasks/': { ok: true },
  })
  render(<QuestsView />)
  return service
}

const search = () => screen.getByRole('combobox')

describe('marking a quest active from the keyboard', () => {
  it('marks the highlighted quest active and empties the box', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Debut')
    await user.keyboard('{Enter}')

    await waitFor(() => {
      const posted = service.callsTo('/api/progress/tasks/').find((c) => c.method === 'POST')
      expect(posted?.body).toEqual({ state: 'Active' })
    })

    // The clearing is the feature. Without it, entering fifty quests means fifty extra
    // select-alls, and the shortcut stops being worth using.
    expect(search()).toHaveValue('')
  })

  it('says what it did, because the row scrolls away as it changes', async () => {
    const user = userEvent.setup()
    show(quests({ name: 'Debut' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Debut{Enter}')

    const announcement = await screen.findByText(/Debut is now active/i)
    // Politely, so it waits for a pause rather than interrupting somebody typing quickly.
    expect(announcement).toHaveAttribute('aria-live', 'polite')
  })

  it('says so when the quest is already active, rather than going quiet', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut', state: 'Active' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Debut{Enter}')

    await screen.findByText(/already active/i)

    // Silence would read as "it did not work" and get typed again. Nothing is sent, though.
    expect(service.callsTo('/api/progress/tasks/').filter((c) => c.method === 'POST')).toHaveLength(0)
    expect(search()).toHaveValue('')
  })

  it('says so when nothing matches, and keeps what was typed', async () => {
    const user = userEvent.setup()
    show([])

    await user.click(search())
    await user.keyboard('nothing-like-this{Enter}')

    await screen.findByText(/Nothing matches/i)
    expect(search()).toHaveValue('nothing-like-this')
  })
})

describe('moving the highlight', () => {
  it('starts on the first row and moves with the arrow keys', async () => {
    const user = userEvent.setup()
    show(quests({ name: 'Debut' }, { name: 'Checking' }, { name: 'Shootout Picnic' }))

    await screen.findByText('Debut')
    const rows = () => screen.getAllByRole('listitem')

    expect(rows()[0]).toHaveAttribute('aria-selected', 'true')

    await user.click(search())
    await user.keyboard('{ArrowDown}')
    expect(rows()[1]).toHaveAttribute('aria-selected', 'true')

    await user.keyboard('{ArrowUp}')
    expect(rows()[0]).toHaveAttribute('aria-selected', 'true')
  })

  it('stops at both ends rather than wrapping', async () => {
    const user = userEvent.setup()
    show(quests({ name: 'Debut' }, { name: 'Checking' }))

    await screen.findByText('Debut')
    const rows = () => screen.getAllByRole('listitem')

    await user.click(search())
    await user.keyboard('{ArrowUp}{ArrowUp}')
    expect(rows()[0]).toHaveAttribute('aria-selected', 'true')

    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(rows()[1]).toHaveAttribute('aria-selected', 'true')
  })

  it('marks the row the arrows landed on, not the first one', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut' }, { name: 'Checking' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('{ArrowDown}{Enter}')

    await waitFor(() => {
      const posted = service.callsTo('/api/progress/tasks/').find((c) => c.method === 'POST')
      expect(posted?.url).toContain('q2')
    })
  })

  it('tells a screen reader which row Enter would act on', async () => {
    const user = userEvent.setup()
    show(quests({ name: 'Debut' }, { name: 'Checking' }))

    await screen.findByText('Debut')
    const rows = () => screen.getAllByRole('listitem')

    // A highlight only sighted users can see is not a highlight.
    expect(search()).toHaveAttribute('aria-activedescendant', rows()[0].id)

    await user.click(search())
    await user.keyboard('{ArrowDown}')
    expect(search()).toHaveAttribute('aria-activedescendant', rows()[1].id)
  })
})

describe('the search box', () => {
  it('clears on Escape without marking anything', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Deb{Escape}')

    expect(search()).toHaveValue('')
    expect(service.callsTo('/api/progress/tasks/').filter((c) => c.method === 'POST')).toHaveLength(0)
  })

  it('keeps focus, so the next name can be typed straight away', async () => {
    const user = userEvent.setup()
    show(quests({ name: 'Debut' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Debut{Enter}')

    await waitFor(() => expect(search()).toHaveValue(''))
    expect(search()).toHaveFocus()
  })

  it('asks the service to filter, rather than filtering what it already has', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('Deb')

    // Search has to reach every quest in the game, not only the page that happens to be loaded.
    await waitFor(() => {
      expect(service.callsTo('/api/tasks').some((c) => c.url.includes('q=Deb'))).toBe(true)
    })
  })
})

describe('when the service is unhappy', () => {
  it('shows no quests rather than breaking', async () => {
    serve({ '/api/traders': TRADERS, '/api/tasks': fails(500) })
    render(<QuestsView />)

    await waitFor(() => expect(screen.queryByRole('listitem')).not.toBeInTheDocument())
    // The page is still usable; the search box has not gone anywhere.
    expect(search()).toBeInTheDocument()
  })

  it('does not lose the highlight when the list changes underneath it', async () => {
    const user = userEvent.setup()
    const service = show(quests({ name: 'Debut' }, { name: 'Checking' }))

    await screen.findByText('Debut')
    await user.click(search())
    await user.keyboard('{ArrowDown}')

    // Narrowing the search re-fetches, and row 2 of the old results is nothing in the new ones.
    service.answer('/api/tasks', quests({ name: 'Debut' }))
    await user.keyboard('Deb')

    await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(1))
    expect(screen.getAllByRole('listitem')[0]).toHaveAttribute('aria-selected', 'true')
  })
})

describe('the rows themselves', () => {
  it('still has the four state buttons for the mouse', async () => {
    show(quests({ name: 'Debut' }))

    const row = await screen.findByRole('listitem')
    for (const label of ['Not started', 'Active', 'Complete', 'Failed']) {
      expect(within(row).getByRole('button', { name: label })).toBeInTheDocument()
    }
  })

  it('marks the current state as pressed', async () => {
    show(quests({ name: 'Debut', state: 'Active' }))

    const row = await screen.findByRole('listitem')
    expect(within(row).getByRole('button', { name: 'Active' })).toHaveAttribute('aria-pressed', 'true')
    expect(within(row).getByRole('button', { name: 'Complete' })).toHaveAttribute('aria-pressed', 'false')
  })
})
