import { describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SetupView } from './SetupView'
import { fails, serve } from './test/service'

/**
 * Setup is the page every install starts on and the page people return to when something is wrong,
 * so it has two jobs that pull against each other: say plainly what is broken, and never lose what
 * somebody typed.
 *
 * <p>The saving tests are the ones worth having. A disabled Save button and an unsaved change look
 * identical from across a desk, and losing a folder path somebody just pasted is the kind of thing
 * that makes people give up on a tool rather than report a bug.</p>
 */

/** A Windows path, built without a literal backslash so the shell that writes this file keeps it. */
const FOLDER = String.fromCharCode(92) + 'Games' + String.fromCharCode(92) + 'EFT'

function check(name: string, over: Record<string, unknown> = {}) {
  return { name, ok: true, detail: '', fix: '', required: true, ...over }
}

const DIAGNOSTICS = {
  ready: true,
  openInBrowserUrl: 'http://localhost:8722/',
  checks: [
    check('Game found', { detail: 'F:\\Escape From Tarkov' }),
    check('Reading the game logs', { detail: 'version 1.1.0' }),
    check('Game running', { ok: false, detail: 'Not running.', fix: 'Run the game.', required: false }),
  ],
  installs: [],
}

const SETTINGS = {
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
  resolvedGameDirectory: 'F:\\Escape From Tarkov',
  resolvedScreenshotDirectory: 'D:\\Screenshots',
  gameDirectoryDetected: true,
}

function open(over: Record<string, unknown> = {}) {
  const service = serve({
    '/api/diagnostics': DIAGNOSTICS,
    '/api/settings': SETTINGS,
    '/api/profiles': { current: 'pvp-seasonal', all: [{ id: 'pvp-seasonal', name: 'PvP Seasonal' }, { id: 'pvp', name: 'PvP' }] },
    '/api/lan': { enabled: false, port: 8722, addresses: [], firewall: 'unknown' },
    '/api/first-run': { done: true, setupComplete: true, missing: [], steps: [] },
    ...over,
  })
  render(<SetupView />)
  return service
}

describe('the checks', () => {
  it('names each one and what it found', async () => {
    open()

    await screen.findByText('Game found')
    // The path shows in the check and again as the folder field's placeholder.
    expect(screen.getAllByText(/Escape From Tarkov/).length).toBeGreaterThan(0)
  })

  it('says what to do about the ones that are not green', async () => {
    open()

    await screen.findByText('Game running')
    // A red light that does not say what to do is a red light people stop reading.
    expect(screen.getByText(/Run the game/)).toBeInTheDocument()
  })

  it('survives the service being unreachable', async () => {
    open({ '/api/diagnostics': fails(500) })

    // No checks, but no crash and no blank page either.
    await waitFor(() => expect(screen.queryByText('Game found')).not.toBeInTheDocument())
  })
})

describe('saving settings', () => {
  it('starts with Save disabled and nothing to save', async () => {
    open()

    await screen.findByText(/everything saved/i)
    expect(screen.getByRole('button', { name: /^Save$/ })).toBeDisabled()
  })

  it('says there are unsaved changes as soon as something is typed', async () => {
    const user = userEvent.setup()
    open()

    const folder = await screen.findByLabelText('Escape from Tarkov folder')
    await user.type(folder, 'D:' + FOLDER)

    // Said out loud, because a disabled Save and an unsaved change look the same from a distance.
    await screen.findByText(/unsaved changes/i)
    expect(screen.getByRole('button', { name: /^Save$/ })).toBeEnabled()
  })

  it('sends what was typed', async () => {
    const user = userEvent.setup()
    const service = open()

    const folder = await screen.findByLabelText('Escape from Tarkov folder')
    await user.type(folder, 'D:' + FOLDER)
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await waitFor(() => {
      const saved = service.callsTo('/api/settings').find((c) => c.method === 'POST')
      expect(saved?.body).toMatchObject({ gameDirectory: 'D:' + FOLDER })
    })
  })

  it('does not overwrite what somebody is typing when the page reloads underneath them', async () => {
    const user = userEvent.setup()
    const service = open()

    const folder = await screen.findByLabelText('Escape from Tarkov folder')
    await user.type(folder, 'D:' + FOLDER)

    // Setup re-checks itself every few seconds. If a reload replaced the draft, a pasted path would
    // be gone before it could be saved — and it would look like the app had eaten it.
    service.answer('/api/settings', { ...SETTINGS })
    await new Promise((r) => setTimeout(r, 60))

    expect(screen.getByLabelText('Escape from Tarkov folder')).toHaveValue('D:' + FOLDER)
  })
})

describe('when saving fails', () => {
  it('keeps the change rather than pretending it worked', async () => {
    const user = userEvent.setup()
    open({ '/api/settings': (r: { method: string }) => (r.method === 'POST' ? fails(500) : SETTINGS) })

    const folder = await screen.findByLabelText('Escape from Tarkov folder')
    await user.type(folder, 'D:' + FOLDER)
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    // Whatever it says about the failure, what must not happen is the typed path disappearing.
    await waitFor(() => {
      expect(screen.getByLabelText('Escape from Tarkov folder')).toHaveValue('D:' + FOLDER)
    })
  })
})

describe('what you are accepting', () => {
  it('says it plainly, the first time', async () => {
    open()

    await screen.findByText(/What you are accepting/i)

    // The two things somebody has to actually decide about: no approval, and their account.
    expect(screen.getByText(/have not been asked to/i)).toBeInTheDocument()
    expect(screen.getByText(/so is your account/i)).toBeInTheDocument()
  })

  it('folds away once read, and stays reachable', async () => {
    const user = userEvent.setup()
    open()

    await user.click(await screen.findByRole('button', { name: 'Understood' }))

    // A warning that cannot be dismissed stops being read within a week and takes the rest of the
    // page down with it. One click away for ever after is the trade.
    await waitFor(() => expect(screen.queryByText(/so is your account/i)).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: /What you are accepting/i })).toBeInTheDocument()
  })

  it('shows again on a machine that has not seen it', async () => {
    open()
    await screen.findByText(/so is your account/i)
  })
})
