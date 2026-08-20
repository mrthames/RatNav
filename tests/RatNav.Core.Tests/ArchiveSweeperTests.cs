namespace RatNav.Core.Tests;

using RatNav.Core.Watchers;

/// <summary>
/// Keeping the archive from eating the disk. These are 13 MB PNGs of a 4K screen, and one
/// development machine had accumulated 3.9 GB of them before anybody looked.
/// </summary>
public class ArchiveSweeperTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ratnav-archive-" + Guid.NewGuid().ToString("n"));

    private readonly DateTimeOffset _now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private string Archived(string name, int bytes, TimeSpan old)
    {
        var archive = Path.Combine(_directory, ArchiveSweeper.FolderName);
        Directory.CreateDirectory(archive);

        var path = Path.Combine(archive, name);

        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, (_now - old).UtcDateTime);

        return path;
    }

    private SweepResult Sweep(TimeSpan olderThan, long mostBytes) =>
        ArchiveSweeper.Sweep(_directory, olderThan, mostBytes, _now);

    private int Remaining =>
        Directory.GetFiles(Path.Combine(_directory, ArchiveSweeper.FolderName)).Length;

    [Fact]
    public void Anything_past_its_age_goes()
    {
        Archived("old.png", 100, TimeSpan.FromDays(5));
        Archived("new.png", 100, TimeSpan.FromHours(1));

        var result = Sweep(TimeSpan.FromDays(2), mostBytes: 1_000_000);

        Assert.Equal(1, result.Removed);
        Assert.Equal(100, result.BytesFreed);
        Assert.Equal(1, Remaining);
    }

    [Fact]
    public void Nothing_past_its_age_stays()
    {
        Archived("a.png", 100, TimeSpan.FromHours(1));
        Archived("b.png", 100, TimeSpan.FromHours(2));

        Assert.Equal(0, Sweep(TimeSpan.FromDays(2), mostBytes: 1_000_000).Removed);
        Assert.Equal(2, Remaining);
    }

    /// <summary>
    /// The backstop. Age alone would not have saved that machine: three hundred files in two days
    /// of playing is still four gigabytes.
    /// </summary>
    [Fact]
    public void The_size_cap_takes_the_oldest_first()
    {
        Archived("oldest.png", 100, TimeSpan.FromHours(6));
        Archived("middle.png", 100, TimeSpan.FromHours(4));
        Archived("newest.png", 100, TimeSpan.FromHours(1));

        var result = Sweep(TimeSpan.FromDays(30), mostBytes: 250);

        Assert.Equal(1, result.Removed);
        Assert.True(File.Exists(Path.Combine(_directory, ArchiveSweeper.FolderName, "newest.png")));
        Assert.False(File.Exists(Path.Combine(_directory, ArchiveSweeper.FolderName, "oldest.png")));
    }

    [Fact]
    public void What_it_freed_is_reported_so_it_can_be_said_out_loud()
    {
        Archived("a.png", 5_000, TimeSpan.FromDays(9));
        Archived("b.png", 3_000, TimeSpan.FromDays(9));

        var result = Sweep(TimeSpan.FromDays(2), mostBytes: 1_000_000);

        Assert.Equal(2, result.Removed);
        Assert.Equal(8_000, result.BytesFreed);
    }

    [Fact]
    public void No_archive_at_all_is_not_an_error()
    {
        Assert.Equal(SweepResult.Nothing, ArchiveSweeper.Sweep(_directory, TimeSpan.FromDays(2), 1_000));
    }

    [Fact]
    public void An_empty_archive_is_not_an_error()
    {
        Directory.CreateDirectory(Path.Combine(_directory, ArchiveSweeper.FolderName));

        Assert.Equal(0, Sweep(TimeSpan.FromDays(2), 1_000).Removed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
