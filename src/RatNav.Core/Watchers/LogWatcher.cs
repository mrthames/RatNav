using System.Text.Json;
using System.Text.RegularExpressions;
using RatNav.Core.Game;
using RatNav.Core.Progress;

namespace RatNav.Core.Watchers;

/// <summary>A raid starting, with the map the game said it loaded.</summary>
public sealed record RaidStarted
{
    /// <summary>The game's own name for the map — "bigmap", "factory4_day". Joins to a map's nameId.</summary>
    public required string LocationId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

/// <summary>A quest changing state, as the game reported it.</summary>
public sealed record QuestEvent
{
    public required string TaskId { get; init; }
    public required QuestState State { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Reads Escape from Tarkov's log files as it writes them.
///
/// <para>Two files matter, and they are not the two you would guess. Raid lifecycle — which map
/// loaded, when it started and ended — is in <c>application.log</c>. <b>Quest state changes are
/// not there at all</b>: they arrive in <c>notifications.log</c> as JSON chat notifications, where
/// the message template id begins with the task id. That detail comes from
/// <see href="https://github.com/the-hideout/TarkovMonitor">TarkovMonitor</see>, which has been
/// doing this for years.</para>
///
/// <para>Polling rather than a file watcher: the game holds these files open and writes to them
/// continuously, so change notifications fire constantly and tell you nothing about what changed.
/// Reading forward from a remembered offset is both simpler and cheaper.</para>
///
/// <para>See docs/game-logs.md for the format and its traps.</para>
/// </summary>
public sealed partial class LogWatcher : IDisposable
{
    private readonly string? _installDirectory;
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Text read but not yet parsed, because an object was still being written.</summary>
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private Timer? _timer;
    private string? _session;

    public event EventHandler<RaidStarted>? RaidStarted;
    public event EventHandler<QuestEvent>? QuestChanged;

    /// <summary>The game version from the current log session, which is how a patch is detected.</summary>
    public string? GameVersion { get; private set; }

    /// <summary>The map of the most recent raid, or null if none seen.</summary>
    public string? CurrentLocationId { get; private set; }

    public LogWatcher(string? installDirectory = null)
        => _installDirectory = installDirectory ?? GameInstallFinder.Find()?.Directory;

    public bool Available => _installDirectory is not null;

    public void Start(TimeSpan? interval = null)
    {
        if (!Available) return;

        var period = interval ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, period);
    }

    /// <summary>
    /// Reads whatever has been appended since the last look. Safe to call at any time; it is a
    /// seek and a read, not a re-parse.
    /// </summary>
    public void Poll()
    {
        if (_installDirectory is null) return;

        try
        {
            var logs = Path.Combine(_installDirectory, "Logs");
            var session = GameInstallFinder.NewestSession(logs);
            if (session is null) return;

            lock (_gate)
            {
                // A new session directory means the game restarted. Offsets from the previous
                // session would seek into the wrong file entirely.
                if (!string.Equals(session, _session, StringComparison.OrdinalIgnoreCase))
                {
                    _session = session;
                    _offsets.Clear();
                    _pending.Clear();
                    GameVersion = GameInstallFinder.VersionFrom(Path.GetFileName(session));
                }
            }

            ReadApplicationLog(session);
            ReadNotificationsLog(session);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game rolls its logs over from time to time. Next tick picks up where this left off.
        }
    }

    private void ReadApplicationLog(string session)
    {
        var path = LogSession.FindLog(session, "application");
        if (path is null) return;

        foreach (var line in ReadNewLines(path))
        {
            // "[Transit] Flag:None, RaidId:..., Locations:bigmap ->" is what names the map.
            var location = LocationLine().Match(line);
            if (location.Success)
            {
                var id = location.Groups["location"].Value.Trim();
                if (id.Length > 0)
                {
                    CurrentLocationId = id;
                    RaidStarted?.Invoke(this, new RaidStarted { LocationId = id });
                }
            }

            var version = VersionLine().Match(line);
            if (version.Success) GameVersion = version.Groups["version"].Value;
        }
    }

    /// <summary>
    /// Reads quest notifications, which are <b>pretty-printed across many lines</b> rather than
    /// one JSON object per line:
    ///
    /// <code>
    /// Got notification | ChatMessageReceived
    /// {
    ///   "type": "new_message",
    ///   "message": {
    ///     "type": 10,
    ///     "templateId": "657315df034d76585f032e01 description"
    ///   }
    /// }
    /// </code>
    ///
    /// So this accumulates text and pulls out whole brace-balanced objects, keeping whatever is
    /// left over for the next poll. A line-at-a-time parser reads this file forever and finds
    /// nothing — which is exactly the sort of failure that looks like "the game doesn't log it".
    /// </summary>
    private void ReadNotificationsLog(string session)
    {
        // The file is "notifications.log" on older clients and "push-notifications_000.log" on
        // 1.1.0. Matching on the word covers both.
        var path = LogSession.FindLog(session, "notifications");
        if (path is null) return;

        string buffer;
        lock (_gate) buffer = _pending.GetValueOrDefault(path, "");

        buffer += ReadNewText(path);

        foreach (var (json, remainder) in ObjectsIn(buffer))
        {
            buffer = remainder;

            QuestEvent? change;
            try
            {
                change = ReadQuestEvent(json);
            }
            catch (JsonException)
            {
                continue;   // some other notification shape; not ours to understand
            }

            if (change is not null) QuestChanged?.Invoke(this, change);
        }

        // A stray unmatched brace would otherwise grow this without limit for the whole session.
        if (buffer.Length > 64 * 1024) buffer = "";

        lock (_gate) _pending[path] = buffer;
    }

    /// <summary>
    /// Yields each brace-balanced object in the buffer along with what follows it. String
    /// contents are skipped so a brace inside a message body cannot end an object early.
    /// </summary>
    private static IEnumerable<(string Json, string Remainder)> ObjectsIn(string buffer)
    {
        var index = 0;

        while (true)
        {
            var start = buffer.IndexOf('{', index);
            if (start < 0) yield break;

            var depth = 0;
            var inString = false;
            var escaped = false;
            var end = -1;

            for (var i = start; i < buffer.Length; i++)
            {
                var c = buffer[i];

                if (escaped) { escaped = false; continue; }
                if (c == '\u005C' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) { end = i; break; }
            }

            // Incomplete: the rest is still being written, so keep it for next time.
            if (end < 0)
            {
                yield break;
            }

            yield return (buffer[start..(end + 1)], buffer[(end + 1)..]);

            buffer = buffer[(end + 1)..];
            index = 0;
        }
    }

    /// <summary>
    /// Pulls a quest change out of one notification, or null if it is any of the many other things
    /// that share this stream — flea sales, player chat, group invitations.
    /// </summary>
    private static QuestEvent? ReadQuestEvent(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("message", out var message)) return null;
        if (!message.TryGetProperty("templateId", out var template)) return null;
        if (!message.TryGetProperty("type", out var type) || !type.TryGetInt32(out var messageType)) return null;

        var state = StateFor(messageType);
        if (state is null) return null;

        // "<taskId> <suffix>" — the task id is everything before the first space, and it joins
        // directly to tarkov.dev's task ids.
        var templateId = template.GetString();
        if (templateId is not { Length: > 0 }) return null;

        var taskId = templateId.Split(' ')[0];
        return taskId.Length == 0 ? null : new QuestEvent { TaskId = taskId, State = state.Value };
    }

    /// <summary>
    /// The message types that mean a quest moved. Values from TarkovMonitor's reading of the
    /// game's own enum; anything outside the range is some other kind of notification.
    /// </summary>
    /// <summary>
    /// The message types that mean a quest moved.
    ///
    /// Type 10 is confirmed against a real notification captured from a live client. The other
    /// two come from TarkovMonitor's reading of the game's enum, where the quest types occupy a
    /// contiguous range — they are believed right but have not been seen in the wild here.
    /// Anything outside the range is one of the many other things sharing this stream.
    /// </summary>
    private static QuestState? StateFor(int messageType) => messageType switch
    {
        10 => QuestState.Active,      // confirmed: "<taskId> description" on quest accept
        11 => QuestState.Failed,
        12 => QuestState.Completed,
        _ => null,
    };

    private IEnumerable<string> ReadNewLines(string path) =>
        ReadNewText(path) is { Length: > 0 } text
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];

    /// <summary>Everything appended since the last read, remembering where it got to.</summary>
    private string ReadNewText(string path)
    {
        long offset;
        lock (_gate) offset = _offsets.GetValueOrDefault(path);

        var (text, next) = LogSession.ReadFrom(path, offset);

        lock (_gate) _offsets[path] = next;
        return text;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    [GeneratedRegex(@"Locations:(?<location>[^\s,\->]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationLine();

    [GeneratedRegex(@"pstrGameVersion:\s*(?<version>[\d.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionLine();
}
