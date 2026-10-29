namespace Meridian.Services;

/// <summary>One named favorites group: its display state and the bookmark
/// paths filed under it, in display order.</summary>
public sealed record BookmarkGroup(string Name, bool Collapsed, IReadOnlyList<string> BookmarkPaths);

/// <summary>
/// The organization layer over the saved bookmarks: an ordered set of named
/// groups, each holding an ordered list of bookmark paths. A bookmark path
/// is in at most one group; paths in no group are "ungrouped" and keep the
/// order of the full bookmark list. Pure state — no filesystem and no UI —
/// so the rules are unit-tested as data and the window renders whatever the
/// set reports.
///
/// The set is built (and rebuilt) through <see cref="Reconcile"/>, which is
/// also the migration path: saved data written before groups existed
/// reconciles to no groups, so every bookmark reads as ungrouped and nothing
/// is lost. Reconcile is deliberately forgiving of damaged data — blank or
/// duplicate group names are dropped (their bookmarks fall back to
/// ungrouped), a path filed under two groups stays in the first, and paths
/// that are no longer bookmarked are forgotten.
/// </summary>
public sealed class BookmarkGroupSet
{
    private sealed class MutableGroup(string name)
    {
        public string Name { get; set; } = name;
        public bool Collapsed { get; set; }
        public List<string> Paths { get; } = [];
    }

    private readonly List<string> _allPaths = [];
    private readonly List<MutableGroup> _groups = [];

    private BookmarkGroupSet()
    {
    }

    /// <summary>Every bookmark path the set knows about, in saved order.</summary>
    public IReadOnlyList<string> AllPaths => _allPaths;

    /// <summary>The groups in display order, as immutable snapshots.</summary>
    public IReadOnlyList<BookmarkGroup> Groups =>
        _groups.Select(group => new BookmarkGroup(group.Name, group.Collapsed, group.Paths.ToArray())).ToArray();

    /// <summary>The bookmark paths filed under no group, in saved order.</summary>
    public IReadOnlyList<string> UngroupedPaths
    {
        get
        {
            var grouped = new HashSet<string>(_groups.SelectMany(group => group.Paths), StringComparer.OrdinalIgnoreCase);
            return _allPaths.Where(path => !grouped.Contains(path)).ToArray();
        }
    }

    /// <summary>
    /// Builds a set from the full bookmark list and any saved group data.
    /// <paramref name="groups"/> may be null (a file written before groups
    /// existed): the result has no groups and every bookmark is ungrouped.
    /// </summary>
    public static BookmarkGroupSet Reconcile(IEnumerable<string> allBookmarkPaths, IEnumerable<BookmarkGroup>? groups)
    {
        ArgumentNullException.ThrowIfNull(allBookmarkPaths);
        var set = new BookmarkGroupSet();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allBookmarkPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (known.Add(path)) set._allPaths.Add(path);
        }

        if (groups is null) return set;
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            if (group is null) continue;
            var name = group.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !usedNames.Add(name)) continue;
            var mutable = new MutableGroup(name) { Collapsed = group.Collapsed };
            foreach (var path in group.BookmarkPaths ?? [])
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                // A saved path counts only while the bookmark itself exists,
                // and only for the first group that claims it.
                if (!known.Contains(path) || !claimed.Add(path)) continue;
                mutable.Paths.Add(path);
            }
            set._groups.Add(mutable);
        }
        return set;
    }

    /// <summary>The name of the group a bookmark is filed under, or null when it is ungrouped (or unknown).</summary>
    public string? GroupOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return _groups.FirstOrDefault(group => group.Paths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))?.Name;
    }

    /// <summary>Creates an empty group. Names are trimmed and must be non-empty and unique (ignoring case).</summary>
    public bool TryCreateGroup(string? name, out string? error)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Enter a name for the group.";
            return false;
        }
        if (FindGroup(trimmed) is not null)
        {
            error = $"A group named “{trimmed}” already exists.";
            return false;
        }
        _groups.Add(new MutableGroup(trimmed));
        error = null;
        return true;
    }

    /// <summary>Renames a group, keeping its bookmarks and collapsed state.</summary>
    public bool TryRenameGroup(string? currentName, string? newName, out string? error)
    {
        var group = currentName is null ? null : FindGroup(currentName.Trim());
        if (group is null)
        {
            error = "That group no longer exists.";
            return false;
        }
        var trimmed = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Enter a name for the group.";
            return false;
        }
        var clash = FindGroup(trimmed);
        if (clash is not null && !ReferenceEquals(clash, group))
        {
            error = $"A group named “{trimmed}” already exists.";
            return false;
        }
        group.Name = trimmed;
        error = null;
        return true;
    }

    /// <summary>
    /// Removes a group. Its bookmarks are NOT removed — they simply stop
    /// being filed anywhere and read as ungrouped again.
    /// </summary>
    public bool DeleteGroup(string name)
    {
        var group = FindGroup(name?.Trim());
        if (group is null) return false;
        _groups.Remove(group);
        return true;
    }

    /// <summary>Moves a group to a new position in the group order; the index is clamped into range.</summary>
    public bool MoveGroup(string name, int newIndex)
    {
        var group = FindGroup(name?.Trim());
        if (group is null) return false;
        _groups.Remove(group);
        _groups.Insert(Math.Clamp(newIndex, 0, _groups.Count), group);
        return true;
    }

    /// <summary>Records a group's collapsed state for the sidebar.</summary>
    public bool SetGroupCollapsed(string name, bool collapsed)
    {
        var group = FindGroup(name?.Trim());
        if (group is null) return false;
        group.Collapsed = collapsed;
        return true;
    }

    /// <summary>
    /// Files a bookmark under a group (null = ungrouped). Within the target
    /// group the bookmark lands at <paramref name="index"/> when one is
    /// given (clamped), otherwise at the end. Returns false when the
    /// bookmark or the named group is unknown.
    /// </summary>
    public bool MoveBookmark(string path, string? targetGroupName, int index = -1)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var known = _allPaths.FirstOrDefault(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (known is null) return false;
        MutableGroup? target = null;
        if (!string.IsNullOrWhiteSpace(targetGroupName))
        {
            target = FindGroup(targetGroupName.Trim());
            if (target is null) return false;
        }
        foreach (var group in _groups)
            group.Paths.RemoveAll(item => string.Equals(item, known, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            var at = index < 0 ? target.Paths.Count : Math.Clamp(index, 0, target.Paths.Count);
            target.Paths.Insert(at, known);
        }
        return true;
    }

    /// <summary>Adds a newly created bookmark; it starts ungrouped. Returns false when the path is already bookmarked.</summary>
    public bool AddBookmark(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (_allPaths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase))) return false;
        _allPaths.Add(path);
        return true;
    }

    /// <summary>Forgets a removed bookmark, including its group membership.</summary>
    public bool RemoveBookmark(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var removed = _allPaths.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var group in _groups)
            group.Paths.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        return removed;
    }

    private MutableGroup? FindGroup(string? name) =>
        name is null ? null : _groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
}
