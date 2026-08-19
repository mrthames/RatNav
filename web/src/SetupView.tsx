import { useEffect, useState } from 'react'
import { api, type Diagnostics, type HotKeys, type Settings } from './api'

/**
 * Everything RatNav needs to know about your machine, and whether it has it.
 *
 * Two halves, in the order a new install needs them. **What is wrong** comes first, because
 * setup fails in several ways that all look identical from the player's side — an overlay showing
 * nothing. **What to change** comes second: the game folder, the screenshot folder, the key you
 * press in game, and the hotkeys.
 *
 * Nothing here is assumed. Detection is a convenience and it can be wrong — the launcher points
 * anywhere, old copies sit on other drives, OneDrive moves Documents — so every path is shown
 * with where it came from and can be overridden.
 */
export function SetupView() {
  const [state, setState] = useState<Diagnostics | null>(null)
  const [settings, setSettings] = useState<Settings | null>(null)

  const load = () => {
    api.diagnostics().then(setState).catch(() => setState(null))
    api.settings().then(setSettings).catch(() => setSettings(null))
  }

  useEffect(() => {
    load()
    // The one screen where things change underneath you — you launch the game, or take your
    // first screenshot — so the one screen that re-checks on its own.
    const timer = setInterval(load, 5000)
    return () => clearInterval(timer)
  }, [])

  if (!state) return <Empty>checking…</Empty>

  return (
    <div className="flex flex-col gap-5">
      <div className={`border px-4 py-3 ${state.ready
        ? 'border-have/40 bg-have/5 text-have'
        : 'border-warn/40 bg-warn/5 text-warn'}`}>
        <p className="font-mono text-sm">
          {state.ready ? 'RatNav can see your game.' : 'RatNav cannot see your game yet.'}
        </p>
      </div>

      <ul className="flex flex-col gap-px border border-line bg-line-soft">
        {state.checks.map((check) => (
          <li key={check.name} className="flex items-start gap-3 bg-panel px-3 py-2.5">
            <span
              aria-hidden
              className={`mt-1.5 size-2 flex-none rounded-full ${
                check.ok ? 'bg-have' : check.required ? 'bg-need' : 'bg-warn'}`}
            />
            <div className="min-w-0">
              <p className="text-sm">
                {check.name}
                <span className="sr-only">{check.ok ? ' — ok' : ' — needs attention'}</span>
              </p>
              <p className="font-mono text-[11px] break-words text-muted">{check.detail}</p>
              {!check.ok && <p className="mt-1 text-xs text-warn">{check.fix}</p>}
            </div>
          </li>
        ))}
      </ul>

      {settings && <SettingsForm settings={settings} onSaved={(s) => { setSettings(s); load() }} />}

      {state.installs.length > 1 && (
        <div className="border border-line">
          <p className="border-b border-line bg-panel px-3 py-1.5 font-mono text-[11px]
                        uppercase tracking-wider text-muted">
            More than one install
          </p>
          <ul>
            {state.installs.map((install) => (
              <li key={install.directory}
                  className="flex flex-wrap items-baseline gap-x-3 border-b border-line-soft px-3 py-2 last:border-0">
                <span className={`font-mono text-xs ${install.chosen ? 'text-accent' : 'text-muted'}`}>
                  {install.chosen ? '→ watching' : '  ignoring'}
                </span>
                <span className="text-sm">{install.directory}</span>
                <span className="font-mono text-[11px] text-muted">
                  {install.version ?? 'never launched'}
                </span>
              </li>
            ))}
          </ul>
          <p className="px-3 py-2 text-xs text-muted">
            RatNav watches whichever install was played most recently. An old copy on another
            drive would otherwise be read forever, reporting no raids and never saying why. Set
            the folder above to override that.
          </p>
        </div>
      )}

      <div className="flex flex-col gap-2 border border-line bg-panel p-4">
        <p className="font-mono text-[11px] uppercase tracking-wider text-muted">On a second screen</p>
        <p className="text-sm">
          Open{' '}
          <a href={state.openInBrowserUrl}
             className="text-accent underline-offset-2 hover:underline
                        focus-visible:outline-2 focus-visible:outline-accent">
            {state.openInBrowserUrl}
          </a>{' '}
          in any browser and put it wherever you like. It is the same app the overlay's panel
          shows, reading the same live state, so both stay in step.
        </p>
        <p className="text-xs text-muted">
          Served on loopback only — it is not reachable from your network.
        </p>
      </div>
    </div>
  )
}

function SettingsForm({ settings, onSaved }: { settings: Settings; onSaved: (s: Settings) => void }) {
  const [draft, setDraft] = useState(settings)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Re-checks every five seconds would otherwise overwrite what someone is halfway through
  // typing, so the form only takes a new copy when it is not being edited.
  const dirty = JSON.stringify(draft) !== JSON.stringify(settings)
  useEffect(() => { if (!dirty) setDraft(settings) }, [settings, dirty])

  async function save() {
    setSaving(true)
    setError(null)

    try {
      onSaved(await api.saveSettings({
        gameDirectory: draft.gameDirectory ?? '',
        screenshotDirectory: draft.screenshotDirectory ?? '',
        screenshotKey: draft.screenshotKey,
        owner: draft.owner ?? '',
        playerLevel: draft.playerLevel ?? undefined,
        gameEdition: draft.gameEdition,
        hotkeys: draft.hotkeys,
      }))
    } catch (e) {
      // The service explains what is wrong with a path far better than the browser can.
      setError(e instanceof Error ? e.message : 'Could not save.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="flex flex-col gap-4 border border-line bg-panel p-4">
      <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">Settings</h2>

      <Field
        label="Escape from Tarkov folder"
        hint={draft.gameDirectory
          ? 'RatNav reads the Logs folder inside this one.'
          : `Detected: ${settings.resolvedGameDirectory ?? 'nothing found — set it here'}`}
        value={draft.gameDirectory ?? ''}
        placeholder={settings.resolvedGameDirectory ?? 'C:\\Battlestate Games\\EFT'}
        onChange={(gameDirectory) => setDraft({ ...draft, gameDirectory })}
      />

      <Field
        label="Screenshot folder"
        hint={draft.screenshotDirectory
          ? 'Where the game saves screenshots.'
          : `Using: ${settings.resolvedScreenshotDirectory}`}
        value={draft.screenshotDirectory ?? ''}
        placeholder={settings.resolvedScreenshotDirectory}
        onChange={(screenshotDirectory) => setDraft({ ...draft, screenshotDirectory })}
      />

      <Field
        label="Your in-game screenshot key"
        hint="Set this to whatever you bound in Tarkov → Settings → Controls → Screenshot. RatNav
              never presses it — this is so every prompt names the key you actually use."
        value={draft.screenshotKey}
        onChange={(screenshotKey) => setDraft({ ...draft, screenshotKey })}
      />

      <label className="flex flex-col gap-1">
        <span className="text-sm">Your character level</span>
        <input
          type="number"
          min={1}
          max={79}
          value={draft.playerLevel ?? ''}
          placeholder={settings.suggestedPlayerLevel ? `at least ${settings.suggestedPlayerLevel}` : '1'}
          onChange={(e) => setDraft({
            ...draft,
            playerLevel: e.target.value === '' ? null : Number(e.target.value),
          })}
          className="w-24 border border-line bg-ground px-2 py-1.5 font-mono text-xs text-ink
                     placeholder:text-muted/60 focus-visible:outline-2 focus-visible:outline-accent"
        />
        <span className="text-xs text-muted">
          Most quests gate on it, so without this the planner offers quests you cannot accept yet.
          Set by hand — nothing the game writes to disk reports your level, and the endpoint that
          would needs your account password, which RatNav will not ask for.
          {settings.suggestedPlayerLevel != null &&
            ` The quests you have marked complete put you at level ${settings.suggestedPlayerLevel} or above.`}
        </span>
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-sm">Which edition you own</span>
        <select
          value={draft.gameEdition}
          onChange={(e) => setDraft({ ...draft, gameEdition: e.target.value })}
          className="w-56 border border-line bg-ground px-2 py-1.5 font-mono text-xs text-ink
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          <option value="standard">Standard — Stash 1</option>
          <option value="left-behind">Left Behind — Stash 2</option>
          <option value="prepare-for-escape">Prepare for Escape — Stash 3</option>
          <option value="edge-of-darkness">Edge of Darkness — Stash 4</option>
          <option value="unheard">The Unheard Edition — Stash 4</option>
        </select>
        <span className="text-xs text-muted">
          Sets the stash you started with, so it is not listed as an upgrade you still have to
          build. Never lowers a stash you have already upgraded past.
        </span>
      </label>

      <Field
        label="Your name on shared plans"
        hint="Used when you export a plan, so a friend can tell whose objectives are whose."
        value={draft.owner ?? ''}
        placeholder="unset"
        onChange={(owner) => setDraft({ ...draft, owner })}
      />

      <div className="flex flex-col gap-2">
        <p className="font-mono text-[11px] uppercase tracking-wider text-muted">Hotkeys</p>
        <p className="text-xs text-muted">
          Written as text — <code className="font-mono">F5</code>,{' '}
          <code className="font-mono">Alt+F6</code>,{' '}
          <code className="font-mono">Ctrl+Shift+M</code>. Changes apply immediately, and RatNav
          says if another application already owns one.
        </p>

        <div className="grid gap-2 sm:grid-cols-2">
          {HOTKEYS.map(([key, label]) => (
            <label key={key} className="flex items-center justify-between gap-3">
              <span className="text-sm">{label}</span>
              <input
                value={draft.hotkeys[key]}
                onChange={(e) => setDraft({ ...draft, hotkeys: { ...draft.hotkeys, [key]: e.target.value } })}
                className="w-32 border border-line bg-ground px-2 py-1 font-mono text-xs text-ink
                           focus-visible:outline-2 focus-visible:outline-accent"
              />
            </label>
          ))}
        </div>
      </div>

      {error && <p className="text-xs text-need">{error}</p>}

      <div className="flex items-center gap-3">
        <button
          type="button"
          disabled={!dirty || saving}
          onClick={() => void save()}
          className="rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px] uppercase tracking-wider
                     text-ground transition-opacity disabled:opacity-40
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>

        {dirty && (
          <button
            type="button"
            onClick={() => setDraft(settings)}
            className="font-mono text-[11px] uppercase tracking-wider text-muted
                       underline-offset-4 hover:text-ink hover:underline"
          >
            Discard
          </button>
        )}

        <p className="font-mono text-[11px] text-muted">Leave a folder empty to go back to detecting it.</p>
      </div>
    </section>
  )
}

const HOTKEYS: [keyof HotKeys, string][] = [
  ['toggleOverlay', 'Show / hide overlay'],
  ['toggleInteract', 'Interact with overlay'],
  ['expandPanel', 'Open full panel'],
  ['completeObjective', 'Tick objective off'],
  ['toggleMode', 'Switch overlay style'],
  ['identifyItem', 'Identify item under cursor'],
]

function Field({
  label, hint, value, placeholder, onChange,
}: {
  label: string
  hint: string
  value: string
  placeholder?: string
  onChange: (value: string) => void
}) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-sm">{label}</span>
      <input
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        spellCheck={false}
        className="border border-line bg-ground px-2 py-1.5 font-mono text-xs text-ink
                   placeholder:text-muted/60 focus-visible:outline-2 focus-visible:outline-accent"
      />
      <span className="text-xs text-muted">{hint}</span>
    </label>
  )
}

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="border border-line bg-panel px-4 py-8 text-center font-mono text-xs text-muted">
    {children}
  </p>
)
