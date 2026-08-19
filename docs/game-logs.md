# Reading Escape from Tarkov's logs

What the game writes to disk, where, and which lines are worth reading. Everything here is
observed from a live 1.1.0 client or taken from
[the-hideout/TarkovMonitor](https://github.com/the-hideout/TarkovMonitor), which has been doing
this for years and saved RatNav a great deal of reverse engineering.

RatNav only ever **reads** these files.

## Where they are

```
<install>\Logs\log_<yyyy.MM.dd>_<HH-mm-ss>_<version>\
    <timestamp> application_000.log
    <timestamp> notifications_000.log
    ...
```

Three things bite here, all found the hard way:

**Filenames changed.** Older clients wrote `<timestamp> application.log`; 1.1.0 writes
`application_000.log`, with `_001` and so on as a log rolls. Match on the word `application`
rather than either exact name.

**The game holds them open, and Windows reports them as zero bytes.** An ordinary read returns an
empty string — not an error, which looks exactly like "nothing has happened yet". Open with
`FileShare.ReadWrite` to get the real content.

**There can be more than one install.** A development machine had a stale v0.16 from over a year
earlier alongside the live 1.1.0, on different drives. Picking the first `EscapeFromTarkov.exe`
found reads year-old logs forever and reports no raids, with nothing to explain why. Rank installs
by the modification time of their newest log session directory.

## Raid lifecycle — `application` log

| line contains | meaning |
|---|---|
| `application\|LocationLoaded` | the map has finished loading |
| `[Transit] ... Locations:<nameId>` | **which map** — `bigmap` is Customs, `factory4_day` is Factory |
| `application\|GameStarting` / `GameStarted` | raid is beginning |
| `application\|MatchingCompleted` | queue finished |
| `TRACE-NetworkGameCreate profileStatus` | contains `RaidMode: Online` vs offline |
| `Got notification \| UserMatchOver` | raid ended |
| `application\|Init: pstrGameVersion:` | game version, more reliable than the folder name |

`nameId` joins straight to tarkov.dev's map records, so a raid start resolves to a map with no
guessing. Verified live: `Locations:bigmap` while playing Customs.

## Quest events — `notifications` log, not `application`

This is the part worth knowing, and it is not where you would first look.

Quest state changes arrive as **chat notifications** in `notifications_000.log`, one JSON object
per line. The shape that matters:

```
message.type        an enum; the range TaskStarted..TaskFinished marks a quest event
message.templateId  "<taskId> <suffix>" — the task id is everything before the first space
```

So the task id joins directly to tarkov.dev task ids, and the status comes from the message type:
started, failed, or finished. The same file carries flea market events, distinguished by
`templateId` (`5bdabfb886f7743e152e867e 0` for a sale, `5bdabfe486f7743e1665df6e 0` for an
expiry), which is a plausible later feature.

Player chat messages appear in the same stream and must be skipped.

## Known gaps

The game does not always write in-raid quest state changes, so log parsing alone will miss some
progress. RatNav layers log events *under* a manual override, so anything missed can be corrected
by hand and never gets clobbered by a later replay.

The BSG launcher's **Clear Cache** deletes log files, which wipes the history available for a
first-time import of past progress.

## What this is not

RatNav does not read game memory, inject code, hook rendering, modify game files, or send input to
the game. It reads log files and screenshot filenames the game writes to your own disk, and
nothing else.
