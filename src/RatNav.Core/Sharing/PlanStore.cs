using System.Text.Json;

namespace RatNav.Core.Sharing;

/// <summary>
/// Saved raid plans on disk, one file each.
///
/// <para>Deliberately files rather than a database: a plan is a small document whose entire
/// purpose is to be shared, and a file can be sent to someone. The saved form and the exported
/// form are the same thing, so "export" is a copy rather than a conversion that could drift.</para>
/// </summary>
public sealed class PlanStore(string dataDirectory)
{
    private string Directory => Path.Combine(dataDirectory, "plans");

    /// <summary>Every saved plan, newest first.</summary>
    public IReadOnlyList<SavedPlan> All()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];

        var plans = new List<SavedPlan>();

        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.ratnav"))
        {
            try
            {
                var document = PlanDocument.FromJson(File.ReadAllText(path), out _);
                if (document is not null)
                    plans.Add(new SavedPlan { Id = Path.GetFileNameWithoutExtension(path), Document = document });
            }
            catch (IOException)
            {
                // One unreadable plan should not hide the rest.
            }
        }

        return [.. plans.OrderByDescending(p => p.Document.CreatedAt)];
    }

    public SavedPlan? Get(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;

        try
        {
            var document = PlanDocument.FromJson(File.ReadAllText(path), out _);
            return document is null ? null : new SavedPlan { Id = id, Document = document };
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Saves a plan, returning the id it was stored under.</summary>
    public string Save(PlanDocument document, string? id = null)
    {
        System.IO.Directory.CreateDirectory(Directory);

        id ??= NewId(document);
        var path = PathFor(id);

        // Written then moved, so an interrupted save cannot destroy a plan that was fine.
        var temp = path + ".tmp";
        File.WriteAllText(temp, document.ToJson());
        File.Move(temp, path, overwrite: true);

        return id;
    }

    public void Delete(string id)
    {
        try
        {
            File.Delete(PathFor(id));
        }
        catch (IOException)
        {
            // Already gone is the outcome we wanted.
        }
    }

    /// <summary>Ids are derived from the map and time so a folder of plans reads sensibly.</summary>
    private static string NewId(PlanDocument document) =>
        $"{Sanitize(document.MapId)}-{document.CreatedAt:yyyyMMdd-HHmmss}";

    private string PathFor(string id) => Path.Combine(Directory, Sanitize(id) + ".ratnav");

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
}

public sealed record SavedPlan
{
    public required string Id { get; init; }
    public required PlanDocument Document { get; init; }
}
