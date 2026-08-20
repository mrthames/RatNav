namespace RatNav.Core;

/// <summary>
/// Which character RatNav is tracking.
///
/// <para>Escape from Tarkov gives you more than one: a PvE character, a PvP character, and a
/// seasonal PvP character that is wiped on a schedule. They share nothing — different quests
/// accepted, different hideout, different trader loyalty — so tracking them against one set of
/// files meant the quests you finished on PvE reading as done on PvP.</para>
///
/// <para>A profile is a directory. Everything that belongs to a character lives inside it, and
/// everything that belongs to the machine — the game's install path, your hotkeys, the cached
/// copy of tarkov.dev, the map images — stays outside and is shared, because none of it changes
/// when you switch character.</para>
/// </summary>
public sealed class RatNavProfile(string dataDirectory)
{
    /// <summary>The profiles RatNav offers. Fixed: these are the three the game has.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> All =
    [
        ("pvp", "PvP"),
        ("pve", "PvE"),
        ("pvp-seasonal", "PvP Seasonal"),
    ];

    private readonly object _gate = new();
    private string _current = "pvp";

    /// <summary>Raised after a switch, so each store can reload from the new directory.</summary>
    public event Action? Changed;

    public string Current
    {
        get { lock (_gate) return _current; }
    }

    public string Name => NameOf(Current);

    public static string NameOf(string id) =>
        All.FirstOrDefault(p => p.Id == id).Name ?? id;

    public static bool IsKnown(string id) => All.Any(p => p.Id == id);

    /// <summary>Where this character's files live.</summary>
    public string Directory => DirectoryFor(Current);

    public string DirectoryFor(string id)
    {
        var path = Path.Combine(dataDirectory, "profiles", Sanitize(id));
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Switches character. Returns false for a profile that does not exist.</summary>
    public bool Use(string id)
    {
        if (!IsKnown(id)) return false;

        lock (_gate)
        {
            if (_current == id) return true;
            _current = id;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Empties a profile back to a fresh character.
    ///
    /// <para>The files are deleted rather than blanked, so anything RatNav learns to store later
    /// is cleared by the same act — a wipe that has to be taught about each new file is one that
    /// eventually misses one.</para>
    /// </summary>
    public bool Wipe(string id)
    {
        if (!IsKnown(id)) return false;

        var path = DirectoryFor(id);

        try
        {
            System.IO.Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            return false;
        }

        System.IO.Directory.CreateDirectory(path);

        if (id == Current) Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Moves a pre-profiles install into the PvP profile, once.
    ///
    /// <para>Copied rather than moved: the originals stay where they were. If any of this goes
    /// wrong the person's real progress is still on disk, and a file left behind costs a few
    /// kilobytes against losing a wipe's worth of quest state.</para>
    /// </summary>
    public void AdoptLooseFiles()
    {
        var into = DirectoryFor("pvp");

        foreach (var name in new[] { "tracking.json", "progress.json", "waypoints.json" })
        {
            var from = Path.Combine(dataDirectory, name);
            var to = Path.Combine(into, name);

            if (!File.Exists(from) || File.Exists(to)) continue;

            try
            {
                File.Copy(from, to);
            }
            catch (IOException)
            {
                // A profile that starts empty is recoverable; a start-up that throws is not.
            }
        }

        var plansFrom = Path.Combine(dataDirectory, "plans");
        var plansTo = Path.Combine(into, "plans");

        if (System.IO.Directory.Exists(plansFrom) && !System.IO.Directory.Exists(plansTo))
        {
            try
            {
                System.IO.Directory.CreateDirectory(plansTo);

                foreach (var plan in System.IO.Directory.GetFiles(plansFrom))
                    File.Copy(plan, Path.Combine(plansTo, Path.GetFileName(plan)), overwrite: false);
            }
            catch (IOException)
            {
                // Same reasoning.
            }
        }
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
