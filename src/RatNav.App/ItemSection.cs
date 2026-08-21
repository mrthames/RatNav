using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace RatNav.App;

/// <summary>
/// One foldable group of the overlay's items list.
///
/// <para>Three of these, split by what you can act on: what active quests and buildable upgrades
/// want, what you put on the watchlist by hand, and everything gated behind something else. The
/// last starts folded — it is worth knowing before you vendor something, and not worth reading
/// while you are being shot at.</para>
/// </summary>
public sealed class ItemSection : INotifyPropertyChanged
{
    private bool _expanded;

    public ItemSection(string title, IReadOnlyList<ItemRow> rows, bool expanded)
    {
        Title = title;
        Rows = rows;
        _expanded = expanded;

        Toggle = new RelayCommand(() =>
        {
            Expanded = !Expanded;
            Toggled?.Invoke(this, EventArgs.Empty);
        });
    }

    public string Title { get; }
    public IReadOnlyList<ItemRow> Rows { get; }
    public ICommand Toggle { get; }

    /// <summary>Raised when folded or unfolded, so the choice can be remembered.</summary>
    public event EventHandler? Toggled;

    public bool Expanded
    {
        get => _expanded;
        private set
        {
            if (_expanded == value) return;

            _expanded = value;
            Changed(nameof(Expanded));
            Changed(nameof(Heading));
            Changed(nameof(Visibility));
        }
    }

    /// <summary>
    /// The header, and its count only while there is nothing else saying it.
    ///
    /// <para>It counted always, which put a number above a list you can see the length of — and on
    /// sections whose own title already carries one it counted twice, so the quest log read
    /// "QUEST LOG · 3 · 3". With the rows underneath, the number was telling you something already
    /// on screen.</para>
    ///
    /// <para>Folded is the case that is different, and the reason the count was there at all: a
    /// section you cannot see into should still say how much is in it, or folding it away hides
    /// that it matters.</para>
    /// </summary>
    public string Heading => Expanded ? $"▾ {Title}" : $"▸ {Title} · {Rows.Count}";

    public Visibility Visibility => Expanded ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The smallest possible ICommand, so a header can be a button without a framework.</summary>
public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
