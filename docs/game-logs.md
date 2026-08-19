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

## Quest events — the notifications log, not `application`

This is the part worth knowing, and it is not where you would first look.

Quest state changes arrive as **chat notifications**, and the file has been renamed:
`notifications.log` on 0.16, `push-notifications_000.log` on 1.1.0. Matching on the word
`notifications` covers both.

**They are pretty-printed across many lines, not one object per line.** Verbatim from a live
client:

```
2025-01-15 07:57:15.224 -08:00|Info|push-notifications|Got notification | ChatMessageReceived
{
  "type": "new_message",
  "dialogId": "54cb50c76803fa8b248b4571",
  "message": {
    "type": 10,
    "text": "",
    "templateId": "657315df034d76585f032e01 description",
    "hasRewards": false
  }
}
```

A line-at-a-time parser reads this file forever and finds nothing, which looks exactly like "the
game doesn't log quests". RatNav accumulates text and extracts brace-balanced objects, keeping any
partial one for the next poll — catching an object mid-print is normal, not exceptional.

```
message.type        10 = accepted. 11 and 12 are believed to be failed and finished
message.templateId  "<taskId> <suffix>" — the task id is everything before the first space
```

The task id joins directly to tarkov.dev task ids. Only type 10 has been observed here; 11 and 12
come from TarkovMonitor's reading of the game's enum, where the quest types are contiguous.

The same stream carries flea market events, distinguished by `templateId`
(`5bdabfb886f7743e152e867e 0` for a sale, `5bdabfe486f7743e1665df6e 0` for an expiry), plus player
chat and group invitations. All of it must be skipped.

**A caution on what "no events" means.** A session of offline raids produces a notifications log
with no quest entries at all, because nothing changed. That is indistinguishable from a parser
that cannot read the format, which is why the parser has a test built from a real captured
notification rather than a hand-written one.

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
