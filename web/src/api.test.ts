import { describe, expect, it } from 'vitest'
import { api } from './api'
import { fails, serve } from './test/service'

/**
 * The layer every view talks through.
 *
 * <p>Two things matter here and neither is about happy paths. Names go into URLs, and a map called
 * `Streets of Tarkov` or an item id with a slash in it must not silently become a different
 * request. And when the service refuses, the reason it gives has to survive: RatNav's service
 * explains what is wrong with a folder path far better than the browser can, and a view that
 * replaces that with "something went wrong" has thrown away the only useful part.</p>
 */

describe('putting names into URLs', () => {
  it('encodes a map name with spaces', async () => {
    const service = serve({ '/api/maps/': [] })

    await api.waypoints('Streets of Tarkov')

    // A raw space would be a different request, or no request at all.
    expect(service.calls[0].url).toContain('Streets%20of%20Tarkov')
  })

  it('encodes an id that contains a slash', async () => {
    const service = serve({ '/api/progress/tasks/': { ok: true } })

    await api.setTaskState('odd/id', 'Active')

    // Unencoded, this would address a path that does not exist and quietly 404.
    expect(service.calls[0].url).toContain('odd%2Fid')
    expect(service.calls[0].url).not.toContain('odd/id')
  })

  it('leaves a filter off entirely rather than sending an empty one', async () => {
    const service = serve({ '/api/tasks': [] })

    await api.tasks()

    expect(service.calls[0].url).not.toContain('filter=')
    expect(service.calls[0].url).not.toContain('q=')
  })

  it('sends both a filter and a query when it has them', async () => {
    const service = serve({ '/api/tasks': [] })

    await api.tasks('all', 'debut')

    expect(service.calls[0].url).toContain('filter=all')
    expect(service.calls[0].url).toContain('q=debut')
  })
})

describe('when the service refuses', () => {
  it('carries the reason it gave, not a generic one', async () => {
    serve({ '/api/settings': fails(400, 'That folder has no Logs directory in it.') })

    // The service knows why a path is wrong. Replacing that with "something went wrong" throws
    // away the only part somebody could act on.
    await expect(api.saveSettings({} as never)).rejects.toThrow(/no Logs directory/)
  })

  it('still throws when the failure has no body to explain it', async () => {
    serve({ '/api/status': fails(500, '') })

    await expect(api.status()).rejects.toThrow()
  })
})

describe('sending things', () => {
  it('posts the body as JSON', async () => {
    const service = serve({ '/api/progress/tasks/': { ok: true } })

    await api.setTaskState('q1', 'Active')

    expect(service.calls[0].method).toBe('POST')
    expect(service.calls[0].body).toEqual({ state: 'Active' })
  })

  it('deletes with the right method', async () => {
    const service = serve({ '/api/raid/plan': { ok: true } })

    await api.clearPlan()

    expect(service.calls[0].method).toBe('DELETE')
  })
})
