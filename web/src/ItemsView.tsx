import { useCallback, useEffect, useMemo, useState } from 'react'
import { QuestBrief } from './QuestBrief'
import {
  amount,
  api,
  type TrackedItem,
  type GoalView,
  type ItemDetail,
} from './api'

/**
 * Which list is on the page.
 *
 * <p>Search is not one of them any more. It was a tab, so looking something up meant leaving
 * whatever you were reading, and coming back meant finding your place again — for an action whose
 * whole purpose is a quick aside. It is a box on the page now: type and the list becomes the
 * results, clear it and your list is where you left it.</p>
 */
type Tab = 'needed' | 'watchlist' | 'custom'

/**
 * What to show of the list.
 *
 * <p>Filtering rather than only sorting. The two sort orders answered "what first", which is a
 * quiet difference on a list of two hundred rows — you had to read the whole thing to see that
 * anything had changed. What is actually wanted is a shorter list: the keys, or the
 * found-in-raid, or what a barter is waiting on.</p>
 */
const FILTERS = [
  { id: 'all', label: 'Everything', of: () => true },

  // Keys count as being for a quest, because they are.
  //
  // They had a filter of their own because they could not match this one: a key is recorded
  // against an objective as something to *bring*, in a list apart from the things you hand in,
  // and this tested only the hand-in count. So "For quests" hid the keys those quests need and a
  // separate Keys button existed to put them back — two answers to one question.
  { id: 'quests', label: 'For quests', of: (r: TrackedItem) => r.questNeeded > 0 || r.isKey },
  { id: 'hideout', label: 'For the hideout', of: (r: TrackedItem) => r.hideoutNeeded > 0 },
] as const

type FilterId = (typeof FILTERS)[number]['id']

export function ItemsView() {
  const [tab, setTab] = useState<Tab>('needed')
  const [query, setQuery] = useState('')

  // Nearest upgrade first, because "what finishes the thing I am closest to finishing" is the
  // question this page is usually open for. Sorting by quantity answers a different one — what to
  // grab if you happen to see it — and it was the default for no better reason than being written
  // first.
  const [sort, setSort] = useState<'default' | 'next'>('next')

  // How far into the hideout build order to count. The hideout page and the overlay have the same
  // dial, and this one used to be a local number starting at 2 — so three places showed three
  // depths and only one of them was saved. It reads the setting and writes it back now.
  const [lookAhead, setLookAhead] = useState(1)

  useEffect(() => {
    void api.settings()
      .then((s) => setLookAhead(s.hideoutLookAhead))
      .catch(() => { /* the dial keeps its default; the list still loads. */ })
  }, [])

  async function changeLookAhead(levels: number) {
    setLookAhead(levels)

    // Saved, because the overlay reads it from settings rather than from this page.
    try { await api.setLookAhead(levels) } catch { /* the list still reflects the change here. */ }
  }
  const [rows, setRows] = useState<TrackedItem[]>([])

  /** Typing in the box takes over the list, whichever tab is underneath it. */
  const searching = query.trim().length > 0
  const [filter, setFilter] = useState<FilterId>('all')

  /** The quest opened from an item's reasons, if any. */
  const [readingQuest, setReadingQuest] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      if (tab === 'custom') {
        // Their own components, with their own data. Nothing to load here.
        setRows([])
      } else if (searching) {
        // The box wins over the tab. Clearing it puts the tab's own list back.
        setRows(await api.searchItems(query))
      } else {
        setRows(tab === 'needed'
          ? await api.neededItems({ lookAhead, sort: sort === 'next' ? 'next' : undefined })
          : await api.watchlist())
      }
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [tab, query, searching, sort, lookAhead])

  // Search runs as you type, so it waits for a pause rather than firing per keystroke. A tab
  // change with the box empty has nothing to wait for.
  useEffect(() => {
    if (!searching) { load(); return }
    const timer = setTimeout(load, 200)
    return () => clearTimeout(timer)
  }, [load, searching])

  // Updates return the changed row, so one item changing doesn't reload the whole list and
  // throw away your scroll position.
  const replace = (updated: TrackedItem) =>
    setRows((current) =>
      // A row that has stopped being watched has no business on the watchlist. Left in place it
      // looked like the button had done nothing.
      tab === 'watchlist' && !updated.watched
        ? current.filter((r) => r.id !== updated.id)
        : current.map((r) => (r.id === updated.id ? updated : r)))

  // Filtering happens here rather than on the server: the list is already loaded, and a filter
  // that answers instantly is one people actually use.
  const shown = useMemo(
    () => rows.filter(FILTERS.find((f) => f.id === filter)?.of ?? (() => true)),
    [rows, filter])

  const totals = useMemo(() => ({
    items: shown.length,
    remaining: shown.reduce((sum, r) => sum + r.remaining, 0),
    fir: shown.filter((r) => r.foundInRaid).length,
  }), [shown])

  return (
    <div className="flex flex-col gap-4">
      {readingQuest && (
        <QuestBrief taskId={readingQuest} onClose={() => setReadingQuest(null)} />
      )}

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex gap-px">
          {(['needed', 'watchlist', 'custom'] as Tab[]).map((id) => (
            <button
              key={id}
              type="button"
              aria-pressed={tab === id}
              onClick={() => setTab(id)}
              className="rounded-sm bg-panel-hi px-3 py-1.5 text-sm text-muted capitalize transition-colors
                         hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              {id}
            </button>
          ))}
        </div>

        {tab === 'needed' && (
          <div className="flex items-center gap-1">
            <span className="font-mono text-[11px] uppercase tracking-wider text-muted">Sort</span>
            {([['next', 'Nearest upgrade'], ['default', 'Most needed']] as const).map(([id, text]) => (
              <button
                key={id}
                type="button"
                aria-pressed={sort === id}
                onClick={() => setSort(id)}
                className="rounded-sm bg-panel-hi px-2.5 py-1.5 text-xs text-muted transition-colors
                           hover:text-ink aria-pressed:bg-accent aria-pressed:text-ground
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {text}
              </button>
            ))}
          </div>
        )}

        {tab === 'needed' && (
          <div className="flex items-center gap-1">
            <span className="font-mono text-[11px] uppercase tracking-wider text-muted">Look ahead</span>
            <button
              type="button"
              disabled={lookAhead <= 0}
              onClick={() => void changeLookAhead(lookAhead - 1)}
              aria-label="Look one upgrade less far ahead"
              className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                         hover:text-ink disabled:opacity-30
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              −
            </button>
            <span className="w-6 text-center font-mono text-xs tabular-nums text-ink">{lookAhead}</span>
            <button
              type="button"
              disabled={lookAhead >= 5}
              onClick={() => void changeLookAhead(lookAhead + 1)}
              aria-label="Look one upgrade further ahead"
              className="size-6 rounded-sm bg-panel-hi font-mono text-xs text-muted transition-colors
                         hover:text-ink disabled:opacity-30
                         focus-visible:outline-2 focus-visible:outline-accent"
            >
              +
            </button>
          </div>
        )}

        {/*
          Always here, not behind a tab. Starring a result puts it on the watchlist and the numbers
          follow, which is the point of looking something up in the first place.
        */}
        {tab !== 'custom' && (
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search all items…"
            className="min-w-56 flex-1 rounded-sm border border-line bg-panel px-3 py-1.5 text-sm
                       text-ink placeholder:text-muted focus-visible:outline-2 focus-visible:outline-accent"
          />
        )}

        {tab !== 'custom' && (
          <p className="ml-auto font-mono text-xs text-muted tabular-nums">
            {totals.items} items · {totals.remaining} still needed · {totals.fir} found-in-raid
          </p>
        )}
      </div>

      {/* Filters, on the lists where there is enough to filter. */}
      {(searching || tab === 'needed') && rows.length > 0 && (
        <div className="flex flex-wrap gap-px">
          {FILTERS.map((f) => {
            const count = rows.filter(f.of).length

            return (
              <button
                key={f.id}
                type="button"
                disabled={count === 0}
                aria-pressed={filter === f.id}
                onClick={() => setFilter(f.id)}
                className="rounded-sm bg-panel-hi px-2.5 py-1 font-mono text-[11px] text-muted
                           tabular-nums transition-colors hover:text-ink disabled:opacity-30
                           aria-pressed:bg-accent aria-pressed:text-ground
                           focus-visible:outline-2 focus-visible:outline-accent"
              >
                {f.label} {count}
              </button>
            )
          })}
        </div>
      )}

      {/*
        What the dial currently means. The same list means different things at depth 1 and depth 4
        and nothing on screen said which — so a number you set last week quietly changed what you
        were reading today.
      */}
      {tab === 'needed' && !searching && (
        <p className="text-xs text-muted">
          {/*
            Two sentences, one per setting, rather than one sentence with holes punched in it. The
            spliced version read "what active quests and upgrades you could build today want", which
            makes "want" arrive far too late to attach to anything.
          */}
          {lookAhead <= 0
            ? <>Showing only what you can finish now: <b>active quests</b>, and{' '}
              <b>hideout upgrades you could build today</b>.</>
            : <>Looking <b>{lookAhead} deep</b>: active quests and the{' '}
              <b>{lookAhead} {lookAhead === 1 ? 'quest' : 'quests'}</b> they unlock, plus the next{' '}
              <b>{lookAhead} hideout {lookAhead === 1 ? 'upgrade' : 'upgrades'}</b>.</>}
        </p>
      )}

      {tab === 'watchlist' && !searching && (
        <p className="text-xs text-muted">
          These numbers are yours. <b>Need</b> is the amount you are after, and <b>Have</b> is what
          you have set aside for it — kept apart from your stash count, so items promised to a
          quest or a hideout upgrade are not counted as available here too.
        </p>
      )}

      {tab === 'custom' && <Custom />}

      {tab !== 'custom' && loading && <Empty>loading…</Empty>}

      {tab !== 'custom' && !loading && shown.length === 0 && rows.length > 0 && (
        <Empty>Nothing here matches that filter.</Empty>
      )}

      {tab !== 'custom' && !loading && rows.length === 0 && (
        <Empty>
          {searching
            ? 'No items match that.'
            : tab === 'needed'
              ? 'Nothing needed. Mark some quests active on the Quests view and they will appear here.'
              : 'Nothing on the watchlist. Search for an item above and star it to keep an eye on it.'}
        </Empty>
      )}

      {tab !== 'custom' && !loading && shown.length > 0 && (
        <div className="overflow-x-auto border border-line">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-line bg-panel text-left">
                <Th className="w-full">Item</Th>
                <Th align="right">Need</Th>
                <Th align="center">Have</Th>
                <Th align="right">Left</Th>
                <Th align="center">{tab === 'watchlist' && !searching ? 'Remove' : 'Watch'}</Th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => (
                <Row
                  key={row.id}
                  row={row}
                  onChange={replace}
                  watchlist={tab === 'watchlist' && !searching}
                  onReadQuest={setReadingQuest}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/**
 * Things you are tracking yourself: a name, and the items under it.
 *
 * <p>This replaced a searchable catalogue of all 789 barters and 214 crafts. Finding the one you
 * meant needed you to already know which of Therapist's four Dorm 303 trades it was, and what you
 * actually think is "the document case".</p>
 *
 * <p>Nothing checks a goal against the game's own trades, on purpose. It can be a barter, a craft,
 * a kit you build for yourself, or a promise to a friend — RatNav has no business having an
 * opinion about which.</p>
 */
function Custom() {
  const [goals, setGoals] = useState<GoalView[]>([])
  const [editing, setEditing] = useState<GoalView | 'new' | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setGoals(await api.goals())
    } catch {
      setGoals([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={() => setEditing('new')}
          className="rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px] uppercase tracking-wider
                     text-ground focus-visible:outline-2 focus-visible:outline-accent"
        >
          Add tracking
        </button>

        <p className="text-xs text-muted">
          Name something you are tracking and put items under it — a barter, a hideout upgrade, a
          kit, anything you are setting things aside for. Each item counts down as you find it,
          <b>counted apart</b> from quests and the hideout, so finishing a quest does not make it
          look done.
        </p>
      </div>

      {loading && <Empty>loading…</Empty>}

      {!loading && goals.length === 0 && (
        <Empty>
          Nothing yet. This is for a barter, a hideout upgrade, a kit, or anything else you are
          putting items aside for.
        </Empty>
      )}

      {goals.map((goal) => {
        return (
          <div key={goal.id} className="flex flex-col gap-2 border border-line bg-panel px-3 py-2">
            <div className="flex flex-wrap items-baseline gap-x-3">
              <span className="text-sm">{goal.name}</span>

              {goal.times > 1 && (
                <span className="font-mono text-[11px] text-muted">×{goal.times}</span>
              )}

              {/*
                No "N still short" beside the name. It counted how many *kinds* of item were not
                yet complete, which for a collection wanting four of one thing and one each of two
                others read "3" — a number that looks like it is about items and is not. Each row
                below carries how many of that item are left, which is the question being asked.
              */}

              <span className="ml-auto flex gap-3">
                <button
                  type="button"
                  onClick={() => setEditing(goal)}
                  className="font-mono text-[11px] uppercase tracking-wider text-muted
                             hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
                >
                  Edit
                </button>

                <button
                  type="button"
                  onClick={async () => { await api.removeGoal(goal.id); void load() }}
                  aria-label={`Forget ${goal.name}`}
                  className="font-mono text-[11px] text-muted hover:text-need
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  ✕
                </button>
              </span>
            </div>

            {/*
              A stepper per item, counting this collection's own progress.

              The number that matters is what is *left*, so that is what the row leads with. The
              count belongs to this collection rather than to a stash total: two collections
              wanting the same item are two separate answers, and plugs set aside for one are not
              also available for the other.
            */}
            <ul className="flex flex-col gap-px">
              {goal.items.map((item) => {
                const wanted = item.count * goal.times
                const left = Math.max(0, wanted - item.found)

                return (
                  <li
                    key={item.itemId}
                    className="flex items-center gap-2 bg-ground px-2 py-1"
                  >
                    {item.iconUrl && (
                      <img src={item.iconUrl} alt="" className="size-6 flex-none object-contain" />
                    )}

                    <span className={`font-mono text-[11px] tabular-nums
                                      ${left === 0 ? 'text-have' : 'text-route'}`}>
                      {left === 0 ? '✓' : left}
                    </span>

                    <span className={`min-w-0 truncate text-sm
                                      ${left === 0 ? 'text-muted line-through' : ''}`}>
                      {item.name}
                      {item.foundInRaid && (
                        <span className="ml-1.5 font-mono text-[10px] text-need">FIR</span>
                      )}
                    </span>

                    <span className="ml-auto flex items-center gap-1">
                      <Step
                        label={`One fewer ${item.name}`}
                        disabled={item.found === 0}
                        onClick={async () => {
                          await api.adjustGoalItem(goal.id, item.itemId, -1)
                          void load()
                        }}
                      >
                        −
                      </Step>

                      <span className="w-14 text-center font-mono text-[11px] tabular-nums text-muted">
                        {item.found}/{wanted}
                      </span>

                      <Step
                        label={`One more ${item.name}`}
                        disabled={item.found >= wanted}
                        onClick={async () => {
                          await api.adjustGoalItem(goal.id, item.itemId, 1)
                          void load()
                        }}
                      >
                        +
                      </Step>
                    </span>
                  </li>
                )
              })}
            </ul>
          </div>
        )
      })}

      {editing && (
        <GoalForm
          goal={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void load() }}
        />
      )}
    </div>
  )
}

/** Naming a goal and listing what it takes. */
function GoalForm({
  goal, onClose, onSaved,
}: {
  goal: GoalView | null
  onClose: () => void
  onSaved: () => void
}) {
  const [name, setName] = useState(goal?.name ?? '')
  const [items, setItems] = useState<
    { itemId: string; name: string; count: number; foundInRaid: boolean }[]
  >(goal?.items.map((i) => ({
    itemId: i.itemId, name: i.name, count: i.count, foundInRaid: i.foundInRaid,
  })) ?? [])

  const [query, setQuery] = useState('')
  const [found, setFound] = useState<TrackedItem[]>([])


  // Searching the item list is still how you name an item — RatNav needs its id to count what you
  // have. What is gone is having to search a catalogue of trades to find the one you meant.
  useEffect(() => {
    if (query.trim().length < 2) { setFound([]); return }

    const timer = setTimeout(
      () => { api.searchItems(query).then((r) => setFound(r.slice(0, 6))).catch(() => setFound([])) },
      200)

    return () => clearTimeout(timer)
  }, [query])

  async function save() {
    await api.saveGoal({
      id: goal?.id,
      name,
      times: 1,
      items: items.map((i) => ({
        itemId: i.itemId, count: i.count, foundInRaid: i.foundInRaid,
      })),
    })

    onSaved()
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={goal ? `Edit ${goal.name}` : 'Add tracking'}
      onClick={onClose}
      className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="flex w-full max-w-lg flex-col gap-3 border border-line bg-panel p-4"
      >
        <div className="flex items-center justify-between">
          <h2 className="font-mono text-[11px] uppercase tracking-wider text-muted">
            {goal ? 'Edit' : 'Add tracking'}
          </h2>

          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="font-mono text-sm text-muted hover:text-ink
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            ✕
          </button>
        </div>

        <label className="flex flex-col gap-1">
          <span className="text-sm">What are you tracking?</span>
          <input
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Document case"
            className="border border-line bg-ground px-2 py-1.5 text-sm text-ink
                       placeholder:text-muted/60 focus-visible:outline-2 focus-visible:outline-accent"
          />
        </label>


        {items.length > 0 && (
          <ul className="flex flex-col gap-px border border-line">
            {items.map((item, index) => (
              <li key={item.itemId} className="flex items-center gap-2 bg-ground px-2 py-1.5">
                <input
                  type="number"
                  min={1}
                  value={item.count}
                  onChange={(e) => setItems(items.map((it, i) =>
                    i === index ? { ...it, count: Math.max(1, Number(e.target.value) || 1) } : it))}
                  className="w-16 border border-line bg-panel px-1.5 py-0.5 font-mono text-xs text-ink
                             focus-visible:outline-2 focus-visible:outline-accent"
                />

                <span className="min-w-0 flex-1 truncate text-xs">{item.name}</span>

                {/*
                  Yours to say, because RatNav cannot know: a barter may demand found-in-raid
                  where a kit you are building for yourself does not, and the same item is either
                  depending on what you are collecting it for. It colours the number on the
                  overlay the red the quest and hideout lists already use for the same meaning.
                */}
                <label className="flex flex-none items-center gap-1 font-mono text-[10px] text-muted">
                  <input
                    type="checkbox"
                    checked={item.foundInRaid}
                    onChange={(e) => setItems(items.map((it, i) =>
                      i === index ? { ...it, foundInRaid: e.target.checked } : it))}
                    className="accent-need"
                  />
                  <span className={item.foundInRaid ? 'text-need' : ''}>FIR</span>
                </label>

                <button
                  type="button"
                  onClick={() => setItems(items.filter((_, i) => i !== index))}
                  aria-label={`Remove ${item.name}`}
                  className="font-mono text-[11px] text-muted hover:text-need
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}

        <label className="flex flex-col gap-1">
          <span className="text-sm">What does it take?</span>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search for an item…"
            className="border border-line bg-ground px-2 py-1.5 text-sm text-ink
                       placeholder:text-muted/60 focus-visible:outline-2 focus-visible:outline-accent"
          />
        </label>

        {found.length > 0 && (
          <ul className="flex flex-col gap-px border border-line">
            {found.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  onClick={() => {
                    if (!items.some((i) => i.itemId === item.id)) {
                      setItems([...items, {
                        itemId: item.id, name: item.name, count: 1, foundInRaid: false,
                      }])
                    }

                    setQuery('')
                  }}
                  className="w-full bg-ground px-2 py-1.5 text-left text-xs text-muted
                             transition-colors hover:bg-panel-hi hover:text-ink
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  {item.name}
                </button>
              </li>
            ))}
          </ul>
        )}

        <button
          type="button"
          disabled={name.trim() === '' || items.length === 0}
          onClick={() => void save()}
          className="self-start rounded-sm bg-accent px-3 py-1.5 font-mono text-[11px] uppercase
                     tracking-wider text-ground disabled:opacity-40
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          {goal ? 'Save' : 'Add goal'}
        </button>
      </div>
    </div>
  )
}

/**
 * Why an item is on the list, when the row's one line cannot say it.
 *
 * <p>A row reads "3 for quests" because naming three quests would not fit. That is the right
 * summary and the wrong answer to "which ones" — and which ones decides whether you carry the
 * thing out or leave it.</p>
 *
 * <p>Quest names open the quest, so the chain from "why am I carrying this" to "here is the door"
 * is two clicks rather than a browser tab.</p>
 */
function Why({ itemId, onReadQuest }: { itemId: string; onReadQuest: (taskId: string) => void }) {
  const [detail, setDetail] = useState<ItemDetail | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    api.item(itemId).then(setDetail).catch(() => setFailed(true))
  }, [itemId])

  if (failed) return <p className="px-3 py-2 text-xs text-warn">Could not load this item.</p>
  if (!detail) return <p className="px-3 py-2 font-mono text-[11px] text-muted">loading…</p>

  const nothing =
    detail.quests.length === 0 && detail.hideout.length === 0 && detail.asKey.length === 0

  return (
    <div className="flex flex-col gap-1 px-3 py-2">
      {detail.quests.map((quest) => (
        <button
          key={quest.objectiveId}
          type="button"
          onClick={() => onReadQuest(quest.taskId)}
          className="flex flex-wrap items-baseline gap-x-2 text-left font-mono text-[11px]
                     text-muted transition-colors hover:text-ink
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          <span className="text-route">QUEST</span>
          <span className="text-ink">{amount(quest.count)}×</span>
          <span>{quest.taskName}</span>
          {quest.traderName && <span>· {quest.traderName}</span>}
          {quest.foundInRaid && <span className="text-need">· found in raid</span>}
        </button>
      ))}

      {detail.hideout.map((station) => (
        <p
          key={`${station.stationName}-${station.level}`}
          className="flex flex-wrap items-baseline gap-x-2 font-mono text-[11px] text-muted"
        >
          <span className="text-route">HIDEOUT</span>
          <span className="text-ink">{amount(station.count)}×</span>
          <span>{station.stationName} level {station.level}</span>
        </p>
      ))}

      {detail.asKey.map((quest) => (
        <button
          key={quest.taskId}
          type="button"
          onClick={() => onReadQuest(quest.taskId)}
          className="flex flex-wrap items-baseline gap-x-2 text-left font-mono text-[11px]
                     text-muted transition-colors hover:text-ink
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          <span className="text-accent">KEY</span>
          <span>opens the way for {quest.taskName}</span>
        </button>
      ))}

      {nothing && (
        <p className="font-mono text-[11px] text-muted">
          No quest or hideout upgrade wants this. It is here because you put it there.
        </p>
      )}

      {detail.item.wikiUrl && (
        <a
          href={detail.item.wikiUrl}
          target="_blank"
          rel="noreferrer"
          className="font-mono text-[11px] text-accent hover:underline
                     focus-visible:outline-2 focus-visible:outline-accent"
        >
          wiki ↗
        </a>
      )}
    </div>
  )
}

function Row({
  row, onChange, watchlist, onReadQuest,
}: {
  row: TrackedItem
  onChange: (item: TrackedItem) => void
  /** On the watchlist the numbers are yours: an editable target, and a count kept apart from the stash. */
  watchlist: boolean
  onReadQuest: (taskId: string) => void
}) {
  const [busy, setBusy] = useState(false)

  /** Whether the row is showing what wants it. */
  const [why, setWhy] = useState(false)

  /**
   * The watchlist keeps its own count.
   *
   * Twenty bundles of wires with fifteen earmarked for the hideout is not twenty available for a
   * barter, and one shared number says it is — which is how you spend something already promised.
   */
  const setWatchHave = async (value: string) => {
    const have = Math.max(0, Number.parseInt(value, 10) || 0)
    if (have === row.have) return

    setBusy(true)
    try { onChange(await api.setWatch(row.id, true, { have })) } finally { setBusy(false) }
  }

  async function setTarget(value: string) {
    const target = value.trim() === '' ? null : Math.max(0, Number(value) || 0)

    setBusy(true)
    try {
      // Setting a target is also how you put something on the watchlist — wanting four of a thing
      // and watching it are the same statement.
      onChange(await api.setWatch(row.id, true, { target }))
    } finally {
      setBusy(false)
    }
  }

  const adjust = async (delta: number) => {
    setBusy(true)
    try { onChange(await api.setHave(row.id, { delta })) } finally { setBusy(false) }
  }

  const setExact = async (value: string) => {
    const count = Number.parseInt(value, 10)
    if (Number.isNaN(count) || count === row.have) return
    setBusy(true)
    try { onChange(await api.setHave(row.id, { count })) } finally { setBusy(false) }
  }

  const toggleWatch = async () => {
    setBusy(true)
    try { onChange(await api.setWatch(row.id, !row.watched)) } finally { setBusy(false) }
  }

  return (
    <>
    <tr className="border-b border-line-soft last:border-0 hover:bg-panel/60">
      <Td>
        <div className="flex items-center gap-2.5">
          {/*
            The row's one line says "3 for quests" because naming three would not fit. Which three
            is what decides whether you carry the thing out.
          */}
          <button
            type="button"
            aria-expanded={why}
            aria-label={`Why ${row.name} is on the list`}
            onClick={() => setWhy(!why)}
            className="size-5 flex-none rounded-full border border-line font-mono text-[10px]
                       text-muted transition-colors hover:border-muted hover:text-ink
                       aria-expanded:border-accent aria-expanded:text-accent
                       focus-visible:outline-2 focus-visible:outline-accent"
          >
            i
          </button>

          {row.iconUrl
            ? <img src={row.iconUrl} alt="" loading="lazy" className="size-7 flex-none rounded-xs bg-panel-hi object-contain" />
            : <div className="size-7 flex-none rounded-xs bg-panel-hi" />}
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              {/*
                Deep-linked rather than scraped, so the guidance stays current with zero
                maintenance — the wiki page is where spawn locations actually live, and it is
                kept up to date by people playing the game.
              */}
              {row.wikiUrl ? (
                <a
                  href={row.wikiUrl}
                  target="_blank"
                  rel="noreferrer"
                  title={`${row.name} on the wiki — spawns, uses, and barters`}
                  className="truncate text-accent underline-offset-2 hover:underline
                             focus-visible:outline-2 focus-visible:outline-accent"
                >
                  {row.name}
                </a>
              ) : (
                <span className="truncate">{row.name}</span>
              )}
              {/* Shape as well as colour: found-in-raid changes what you do with an item, and
                  it has to survive colour-blindness. */}
              {row.foundInRaid && <Tag className="border-need/50 text-need">◆ FIR</Tag>}
              {row.isKey && <Tag className="border-route/50 text-route">▲ key</Tag>}
              {row.hideoutWave === 1 && <Tag className="border-accent/50 text-accent">● now</Tag>}
            </div>
            <div className="font-mono text-[11px] text-muted">
              {[
                // Named, like the hideout line beside it. "30 for quests" left out the one word
                // that decides whether to keep the thing. Past three the names stop fitting and
                // stop helping, so it counts instead — and the row expansion lists them all.
                row.questNeeded > 0 && (row.questFor.length > 0 && row.questFor.length <= 3
                  ? `${row.questNeeded} for ${row.questFor.join(', ')}`
                  : row.questFor.length > 3
                    ? `${row.questNeeded} for ${row.questFor.length} quests`
                    : `${row.questNeeded} for quests`),
                // Named rather than counted. "4 for Medstation 3" tells you whether to keep the
                // thing; "4 for hideout" does not.
                row.hideoutNeeded > 0 && (row.hideoutUpgrade
                  ? `${row.hideoutNeeded} for ${row.hideoutUpgrade}`
                  : `${row.hideoutNeeded} for hideout`),

                // Named the same way, and separately — the whole reason goals are counted apart
                // is so this line can say which of the two the number is for.
                row.goalNeeded > 0
                  && `${row.goalNeeded} for ${row.goalFor.join(', ') || 'a goal'}`,
                row.watchNote,
              ].filter(Boolean).join(' · ')}
            </div>
          </div>
        </div>
      </Td>

      <Td align="right">
        {/*
          Quest and hideout needs are worked out; a watchlist target is not, so it is editable.
          Showing a derived number in a box you can type in would be a lie about what it is.
        */}
        {!watchlist && (row.questNeeded > 0 || row.hideoutNeeded > 0 || row.goalNeeded > 0) ? (
          <span className="font-mono text-xs tabular-nums">{row.needed}</span>
        ) : (
          <input
            key={`t-${row.watchTarget}`}
            defaultValue={row.watchTarget ?? ''}
            placeholder="—"
            onBlur={(e) => setTarget(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            aria-label={`how many ${row.name} you want`}
            className="w-12 rounded-sm border border-line bg-panel px-1 py-0.5 text-center font-mono
                       text-xs tabular-nums placeholder:text-muted
                       focus-visible:outline-2 focus-visible:outline-accent"
          />
        )}
      </Td>

      <Td align="center">
        <div className="flex items-center justify-center gap-1">
          <Step onClick={() => adjust(-1)} disabled={busy || row.have === 0 || watchlist} label="one fewer">−</Step>
          <input
            key={row.have}
            defaultValue={row.have}
            onBlur={(e) => (watchlist ? setWatchHave(e.target.value) : setExact(e.target.value))}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            aria-label={`how many ${row.name} you have`}
            className="w-12 rounded-sm border border-line bg-panel px-1 py-0.5 text-center font-mono
                       text-xs tabular-nums focus-visible:outline-2 focus-visible:outline-accent"
          />
          <Step onClick={() => adjust(1)} disabled={busy || watchlist} label="one more">+</Step>
        </div>
      </Td>

      <Td align="right" mono className={row.remaining === 0 ? 'text-have' : ''}>
        {row.remaining === 0 ? '✓' : row.remaining}
      </Td>

      <Td align="center">
        {/*
          A star to add, a cross to remove. On the watchlist "un-star" is the same action as
          "take this off the list", and saying the second is clearer than implying it.
        */}
        <button
          type="button"
          onClick={toggleWatch}
          disabled={busy}
          aria-pressed={row.watched}
          aria-label={watchlist
            ? `remove ${row.name} from the watchlist`
            : row.watched ? `stop watching ${row.name}` : `watch ${row.name}`}
          className={`rounded-sm px-1.5 transition-colors focus-visible:outline-2
                      focus-visible:outline-accent ${watchlist
                        ? 'text-muted hover:text-need'
                        : 'text-muted hover:text-warn aria-pressed:text-warn'}`}
        >
          {watchlist ? '✕' : row.watched ? '★' : '☆'}
        </button>
      </Td>
    </tr>

    {why && (
      <tr className="border-b border-line-soft bg-ground/60">
        <td colSpan={5}>
          <Why itemId={row.id} onReadQuest={onReadQuest} />
        </td>
      </tr>
    )}
    </>
  )
}

function Step({ onClick, disabled, label, children }: {
  onClick: () => void; disabled: boolean; label: string; children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className="size-6 rounded-sm bg-panel-hi text-muted transition-colors hover:text-ink
                 disabled:opacity-30 focus-visible:outline-2 focus-visible:outline-accent"
    >
      {children}
    </button>
  )
}

const Th = ({ children, align = 'left', className = '' }: {
  children: React.ReactNode; align?: 'left' | 'right' | 'center'; className?: string
}) => (
  <th className={`px-3 py-2 font-mono text-[11px] font-normal uppercase tracking-wider text-muted text-${align} ${className}`}>
    {children}
  </th>
)

const Td = ({ children, align = 'left', mono = false, className = '' }: {
  children: React.ReactNode; align?: 'left' | 'right' | 'center'; mono?: boolean; className?: string
}) => (
  <td className={`px-3 py-2 text-${align} ${mono ? 'font-mono tabular-nums' : ''} ${className}`}>
    {children}
  </td>
)

const Tag = ({ children, className }: { children: React.ReactNode; className: string }) => (
  <span className={`flex-none rounded-xs border px-1 font-mono text-[10px] tracking-wide ${className}`}>
    {children}
  </span>
)

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="border border-line bg-panel px-4 py-8 text-center font-mono text-xs text-muted">
    {children}
  </p>
)
