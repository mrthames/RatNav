namespace RatNav.Core.Tests;

using RatNav.Core.Tracking;

public class CustomWaypointStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ratnav-waypoints-" + Guid.NewGuid().ToString("n"));

    private CustomWaypointStore New()
    {
        var store = new CustomWaypointStore(_directory);
        store.Load();

        return store;
    }

    [Fact]
    public void A_mark_comes_back_for_its_own_map_and_no_other()
    {
        var store = New();

        store.Add("woods", "car batteries", 0.4, 0.6);

        Assert.Single(store.For("woods"));
        Assert.Empty(store.For("customs"));
    }

    [Fact]
    public void Marks_survive_a_restart()
    {
        New().Add("woods", "car batteries", 0.4, 0.6);

        var mark = Assert.Single(New().For("woods"));

        Assert.Equal("car batteries", mark.Label);
        Assert.Equal(0.4, mark.X, 5);
    }

    /// <summary>An unnamed dot on a map is a puzzle rather than a mark.</summary>
    [Fact]
    public void A_blank_label_becomes_something_readable()
    {
        Assert.Equal("mark", New().Add("woods", "", 0.5, 0.5).Label);
    }

    /// <summary>A label that runs across a quarter of the map is worse than a truncated one.</summary>
    [Fact]
    public void A_long_label_is_cut_to_something_drawable()
    {
        var mark = New().Add("woods", new string('x', 80), 0.5, 0.5);

        Assert.Equal(24, mark.Label.Length);
    }

    [Fact]
    public void Coordinates_outside_the_map_are_pulled_back_onto_it()
    {
        var mark = New().Add("woods", "somewhere", -3, 42);

        Assert.Equal(0, mark.X);
        Assert.Equal(1, mark.Y);
    }

    [Fact]
    public void Renaming_keeps_the_place()
    {
        var store = New();
        var mark = store.Add("woods", "batteries", 0.4, 0.6);

        Assert.True(store.Rename(mark.Id, "car batteries"));

        var renamed = Assert.Single(store.For("woods"));

        Assert.Equal("car batteries", renamed.Label);
        Assert.Equal(0.4, renamed.X, 5);
    }

    [Fact]
    public void Renaming_something_that_is_not_there_says_so_rather_than_throwing()
    {
        Assert.False(New().Rename("nothing", "anything"));
    }

    [Fact]
    public void Removing_takes_it_off_the_map_and_off_disk()
    {
        var store = New();
        var mark = store.Add("woods", "batteries", 0.4, 0.6);

        Assert.True(store.Remove(mark.Id));
        Assert.Empty(New().For("woods"));
    }

    [Fact]
    public void A_corrupt_file_costs_the_marks_rather_than_the_app()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "waypoints.json"), "{ this is not json");

        Assert.Empty(New().All);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
