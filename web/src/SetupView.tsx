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

      {/*
        Asked for once, at the bottom of the page nobody visits twice. RatNav is free and stays
        free; this is a place to say thank you, not a thing to be nudged about.
      */}
      <div className="flex flex-wrap items-center gap-3 border border-line bg-panel p-4">
        <p className="min-w-56 flex-1 text-sm text-muted">
          RatNav is free, has no ads, no accounts, and nothing that phones home. It is built and
          maintained by one person on his own time.
        </p>

        <a
          href="https://buymeacoffee.com/thames_"
          target="_blank"
          rel="noreferrer"
          className="rounded-sm bg-route px-3 py-2 font-mono text-[11px] uppercase tracking-wider
                     text-ground transition-opacity hover:opacity-90
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          ☕ Buy me a coffee
        </a>
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

      <div className="flex items-end gap-2">
        <div className="flex-1">
          <Field
            label="Escape from Tarkov folder"
            hint={draft.gameDirectory
              ? 'RatNav reads the Logs folder inside this one.'
              : `Detected: ${settings.resolvedGameDirectory ?? 'nothing found — set it here'}`}
            value={draft.gameDirectory ?? ''}
            placeholder={settings.resolvedGameDirectory ?? 'C:\\Battlestate Games\\EFT'}
            onChange={(gameDirectory) => setDraft({ ...draft, gameDirectory })}
          />
        </div>

        {/* Detection covers the ordinary install. This is for the drive you keep games on,
            where typing the path is where it goes subtly wrong. */}
        <button
          type="button"
          onClick={async () => {
            const chosen = await api.browseForFolder(
              draft.gameDirectory || settings.resolvedGameDirectory)

            if (chosen) setDraft({ ...draft, gameDirectory: chosen })
          }}
          className="mb-px rounded-sm bg-panel-hi px-3 py-1.5 text-xs text-muted transition-colors
                     hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          Browse…
        </button>
      </div>

      <Field
        label="Screenshot folder"
        hint={draft.screenshotDirectory
          ? 'Where the game saves screenshots.'
          : `Using: ${settings.resolvedScreenshotDirectory}`}
        value={draft.screenshotDirectory ?? ''}
        placeholder={settings.resolvedScreenshotDirectory}
        onChange={(screenshotDirectory) => setDraft({ ...draft, screenshotDirectory })}
      />

      {/*
        Kept, and worth keeping: RatNav never presses this, but every prompt that asks you to take
        a fix names it, and naming the wrong key is worse than naming none.
      */}
      <div className="flex items-center justify-between gap-3">
        <span className="min-w-0">
          <span className="block text-sm">Your in-game screenshot key</span>
          <span className="block text-xs text-muted">
            Whatever you bound in Tarkov → Settings → Controls → Screenshot. RatNav never presses
            it — this is so every prompt names the key you actually use.
          </span>
        </span>

        <KeyField
          value={draft.screenshotKey}
          onChange={(screenshotKey) => setDraft({ ...draft, screenshotKey })}
        />
      </div>

      {/*
        Character level moved to the top navigation. It gates which quests count as available and
        it changes every few raids — buried here it went stale, and a stale level quietly narrows
        every list downstream of it.
      */}

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
          Click a key and press the one you want. RatNav says if another application already owns
          it.
        </p>

        <div className="grid gap-2 sm:grid-cols-2">
          {HOTKEYS.map(([key, label]) => (
            <div key={key} className="flex items-center justify-between gap-3">
              <span className="text-sm">{label}</span>
              <KeyField
                value={draft.hotkeys[key]}
                onChange={(v) => setDraft({ ...draft, hotkeys: { ...draft.hotkeys, [key]: v } })}
              />
            </div>
          ))}
        </div>
      </div>

      {/*
        Recovery, not a preference. A window dragged onto a monitor that is no longer attached
        cannot be dragged back — there is nothing on screen to grab — and without this the only
        way out is editing settings.json by hand.
      */}
      <div className="flex flex-wrap items-center gap-3 border-t border-line pt-3">
        <button
          type="button"
          onClick={() => void api.resetOverlayPlace()}
          className="rounded-sm bg-panel-hi px-3 py-1.5 font-mono text-[11px] uppercase
                     tracking-wider text-muted transition-colors hover:text-ink
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          Put the overlay back
        </button>

        <span className="text-xs text-muted">
          Moves it to its starting corner and shows it. Use this if it has ended up on a monitor
          you no longer have. Everything else about it — size, ink, scales — is left alone.
        </span>
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
  ['toggleMode', 'Switch overlay style'],
  ['identifyItem', 'Identify item under cursor'],
  ['readExtracts', "Read the game's extract list"],
]

/**
 * A hotkey field you set by pressing the key.
 *
 * <p>Typing `Ctrl+Shift+M` into a text box is an invitation to get it subtly wrong — a misspelt
 * modifier, a key name that is not the one Windows uses — and the failure arrives later, as a key
 * that quietly does nothing. Pressing the key cannot be misspelt.</p>
 *
 * <p>Modifiers alone are ignored while held, so `Ctrl+Shift+M` records on the M rather than
 * committing to `Ctrl` the moment it goes down.</p>
 */
function KeyField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const [listening, setListening] = useState(false)

  function capture(e: React.KeyboardEvent<HTMLButtonElement>) {
    e.preventDefault()

    if (e.key === 'Escape') { setListening(false); return }

    // A modifier on its own is somebody still reaching for the key they mean.
    if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return

    const parts: string[] = []

    if (e.ctrlKey) parts.push('Ctrl')
    if (e.altKey) parts.push('Alt')
    if (e.shiftKey) parts.push('Shift')

    // Windows names a letter key by the letter, not by "KeyM".
    const key = e.key.length === 1 ? e.key.toUpperCase() : e.key

    parts.push(key)
    onChange(parts.join('+'))
    setListening(false)
  }

  return (
    <button
      type="button"
      onClick={() => setListening(true)}
      onBlur={() => setListening(false)}
      onKeyDown={listening ? capture : undefined}
      className={`w-36 border px-2 py-1 text-left font-mono text-xs transition-colors
                  focus-visible:outline-2 focus-visible:outline-accent
                  ${listening
                    ? 'border-accent bg-accent/10 text-accent'
                    : 'border-line bg-ground text-ink hover:border-muted'}`}
    >
      {listening ? 'press a key…' : value || 'not set'}
    </button>
  )
}

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
