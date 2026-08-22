import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'

/**
 * What every test in this project starts from.
 *
 * <p>Two rules run the whole file. **Nothing reaches the network**: an unstubbed `fetch` throws
 * with the URL it wanted, so a component that quietly grew a new call fails loudly in the test
 * that did not know about it, rather than silently hanging or hitting a service that happens to be
 * running on the developer's machine. And **nothing leaks between tests**: the DOM, storage, and
 * every stub are reset after each one, because a test that passes only when it runs second is
 * worse than no test.</p>
 */

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  window.localStorage.clear()
})

beforeEach(() => {
  // A call nobody stubbed is a bug in the test, and it should say which call it was.
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    throw new Error(
      `Unstubbed fetch: ${String(input)}\n` +
      `Add it to the route table for this test — see src/test/service.ts.`,
    )
  }))

  // jsdom implements neither, and components use both for layout and for the highlighted row.
  window.HTMLElement.prototype.scrollIntoView = vi.fn()
  window.matchMedia ??= vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia
})
