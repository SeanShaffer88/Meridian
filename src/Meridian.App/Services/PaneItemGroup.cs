using System.Collections.ObjectModel;
using Meridian.Models;

namespace Meridian.Services;

/// <summary>
/// One group of items in a pane list while grouping is on. The group IS the item
/// collection (the CollectionViewSource grouping pattern): the list renders its
/// header from <see cref="Title"/> and its rows from the collection itself.
/// Groups are rebuilt wholesale on every filter pass, so the collection never
/// needs to change after construction.
/// </summary>
public sealed class PaneItemGroup : ObservableCollection<FileEntry>
{
    public PaneItemGroup(string title, IEnumerable<FileEntry> items)
        : base(items)
    {
        Title = title;
    }

    public string Title { get; }
}
