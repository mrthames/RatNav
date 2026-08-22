import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MapPicker } from './MapPicker'
import { HotkeyBar } from './HotkeyBar'
import { HeldBack } from './HeldBack'
import { aMap, line, serve } from './test/service'

/**
 * The small pieces. Each is a few dozen lines, and each is on screen constantly — which is exactly
 * the combination nobody writes a test for and everybody notices breaking.
 */

const MAPS = [
  aMap(),
  aMap({ id: 'factory', name: 'Factory', normalizedName: 'factory', workInProgress: true }),
]

describe('the map picker', () => {
  it('lists every map', () => {
    render(<MapPicker maps={MAPS} selected={null} onSelect={vi.fn()} />)

    expect(screen.getByRole('button', { name: /Customs/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Factory/ })).toBeInTheDocument()
  })

  it('marks the selected one as pressed, not merely coloured', () => {
    render(<MapPicker maps={MAPS} selected={MAPS[0]} onSelect={vi.fn()} />)

    // Colour alone says nothing to a screen reader, and nothing to anybody who cannot tell these
    // two shades apart.
    expect(screen.getByRole('button', { name: /Customs/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /Factory/ })).toHaveAttribute('aria-pressed', 'false')
  })

  it('says which maps are still being worked on', () => {
    render(<MapPicker maps={MAPS} selected={null} onSelect={vi.fn()} />)

    expect(screen.getByText('[WIP]')).toBeInTheDocument()
  })

  it('hands back the map that was clicked', async () => {
    const user = userEvent.setup()
    const chose = vi.fn()
    render(<MapPicker maps={MAPS} selected={null} onSelect={chose} />)

    await user.click(screen.getByRole('button', { name: /Customs/ }))

    expect(chose).toHaveBeenCalledWith(MAPS[0])
  })
})

describe('the hotkey strip', () => {
  it('names each key and what it does', async () => {
    serve({
      '/api/hotkeys/hints': [
        { key: 'F5', does: 'show/hide' },
        { key: 'F6', does: 'interact mode' },
      ],
    })
    render(<HotkeyBar />)

    await screen.findByText('F5')

    // Key and meaning are two nodes in one span, so the whole line is what to look for.
    expect(line('F5 show/hide')).toBeInTheDocument()
    expect(line('F6 interact mode')).toBeInTheDocument()
  })

  it('shows nothing at all rather than an empty strip', async () => {
    serve({ '/api/hotkeys/hints': [] })
    const { container } = render(<HotkeyBar />)

    // A bar reserving space for hints it does not have is worse than no bar.
    await new Promise((r) => setTimeout(r, 20))
    expect(container.textContent).toBe('')
  })
})

describe('coming soon', () => {
  it('names the maps that are one position fix from being finished', async () => {
    serve({
      '/api/maps/held-back': [{
        id: 'factory', name: 'Factory', normalizedName: 'factory',
        hasDrawing: true, confidence: 'Weak',
        reason: 'The extracts sit well inside the map.', canBeSettled: true,
      }],
    })
    render(<HeldBack onSettled={vi.fn()} />)

    await screen.findByText(/Coming soon/i)
    expect(screen.getByText('Factory')).toBeInTheDocument()

    // Why they are not in yet, so the list reads as a decision rather than an omission. The
    // per-map reason is not shown here — it is what the maintainer works from, not the reader.
    expect(screen.getByText(/what is left is working out which way round it goes/)).toBeInTheDocument()
  })

  it('says nothing when there is nothing to promise', async () => {
    serve({ '/api/maps/held-back': [] })
    const { container } = render(<HeldBack onSettled={vi.fn()} />)

    await new Promise((r) => setTimeout(r, 20))
    expect(container.textContent).toBe('')
  })
})
