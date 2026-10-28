namespace Meridian.Services;

/// <summary>One item selected in a Miller column: its full path and whether it is a folder.</summary>
public sealed record ColumnSelection(string Path, bool IsDirectory);

/// <summary>
/// The selection state behind one pane's Miller-columns view: a root folder
/// plus the item selected at each depth. Pure state — no filesystem access
/// and no UI types — so the view can be rebuilt from it after a refresh and
/// the rules can be unit-tested as data.
///
/// The chain maintains one invariant: a file can only ever be the last
/// selection, because selecting anything at a shallower depth discards every
/// deeper selection (the columns to the right are rebuilt from the new
/// selection). From the chain the view derives everything it shows:
/// <see cref="FolderColumnCount"/> folder columns (the root plus one per
/// selected folder), a preview column when the deepest selection is a file,
/// and <see cref="CurrentFolderPath"/> — the deepest selected folder, which
/// is the path the pane reports as its current folder while cascading.
/// </summary>
public sealed class ColumnChain
{
    private readonly List<ColumnSelection> _selections = [];

    public ColumnChain(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
    }

    /// <summary>The folder listed by the first (leftmost) column.</summary>
    public string RootPath { get; private set; }

    /// <summary>The selected item at each depth, shallowest first. A file, when present, is last.</summary>
    public IReadOnlyList<ColumnSelection> Selections => _selections;

    /// <summary>How many folder columns the chain shows: the root column plus one per selected folder.</summary>
    public int FolderColumnCount
    {
        get
        {
            var count = 1;
            foreach (var selection in _selections)
            {
                if (!selection.IsDirectory) break;
                count++;
            }
            return count;
        }
    }

    /// <summary>True when the deepest selection is a file, so a preview column follows the folder columns.</summary>
    public bool ShowsPreviewColumn => _selections.Count > 0 && !_selections[^1].IsDirectory;

    /// <summary>The deepest selection — the file or folder the pane treats as selected — or null when nothing is selected.</summary>
    public ColumnSelection? ActiveSelection => _selections.Count > 0 ? _selections[^1] : null;

    /// <summary>The deepest selected folder, or the root when no folder is selected.</summary>
    public string CurrentFolderPath
    {
        get
        {
            var path = RootPath;
            foreach (var selection in _selections)
            {
                if (!selection.IsDirectory) break;
                path = selection.Path;
            }
            return path;
        }
    }

    /// <summary>
    /// The folder whose items the column at <paramref name="depth"/> lists:
    /// the root at depth 0, then each selected folder in turn. Null when the
    /// chain has no column at that depth.
    /// </summary>
    public string? FolderPathAtDepth(int depth)
    {
        if (depth < 0 || depth >= FolderColumnCount) return null;
        return depth == 0 ? RootPath : _selections[depth - 1].Path;
    }

    /// <summary>The selection recorded at <paramref name="depth"/>, or null when there is none.</summary>
    public ColumnSelection? SelectionAtDepth(int depth) =>
        depth >= 0 && depth < _selections.Count ? _selections[depth] : null;

    /// <summary>
    /// Records a selection in the column at <paramref name="depth"/> and
    /// discards every deeper selection. The depth may be one past the last
    /// recorded selection — the column a selected folder has just revealed —
    /// but no further: a column exists at a depth only when the selection
    /// before it is a folder.
    /// </summary>
    public void Select(int depth, string path, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (depth < 0 || depth > _selections.Count)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "The depth must be within the recorded selections, or one past the last one.");
        if (depth > 0 && !_selections[depth - 1].IsDirectory)
            throw new InvalidOperationException(
                "A column exists at this depth only when the previous selection is a folder.");
        var selection = new ColumnSelection(path, isDirectory);
        if (depth < _selections.Count)
        {
            _selections[depth] = selection;
            _selections.RemoveRange(depth + 1, _selections.Count - depth - 1);
        }
        else
        {
            _selections.Add(selection);
        }
    }

    /// <summary>
    /// Drops the selection at <paramref name="depth"/> and every deeper one,
    /// as when a column's selection is cleared. Depths at or past the end
    /// change nothing.
    /// </summary>
    public void ClearFromDepth(int depth)
    {
        if (depth < 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "The depth cannot be negative.");
        if (depth < _selections.Count)
            _selections.RemoveRange(depth, _selections.Count - depth);
    }

    /// <summary>Starts a new chain at a different root folder, discarding all selections.</summary>
    public void Reset(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
        _selections.Clear();
    }
}
