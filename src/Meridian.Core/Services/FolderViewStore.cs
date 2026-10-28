using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// The view state remembered for one folder: layout, view mode, sort field and
/// direction, grouping, and the visible Details columns. Values are stored as names
/// (strings) so this Core store stays free of window types; the window parses them
/// defensively and falls back to its defaults for anything unknown.
/// </summary>
public sealed record FolderViewState(
    string? Layout,
    string? ViewMode,
    string? SortField,
    bool Descending,
    string? Grouping,
    string[]? VisibleColumns);

/// <summary>One folder's remembered view state.</summary>
public sealed record FolderViewEntry(string Path, FolderViewState State);

/// <summary>
/// Remembers how each folder was last viewed so a pane returning to a folder
/// restores its layout, view mode, sort, grouping, and columns. Stored as JSON
/// beside the other Meridian stores (temp file + atomic move, following
/// RecentPathsStore); a missing or damaged file simply starts empty. Capped, with
/// the least-recently-viewed folders evicted first.
/// </summary>
public sealed class FolderViewStore
{
    public const int MaximumEntries = 256;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly List<FolderViewEntry> _entries = []; // oldest first, most recent last

    public FolderViewStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public IReadOnlyList<FolderViewEntry> Entries => _entries;

    /// <summary>The remembered state for a folder, or null when it was never recorded.</summary>
    public FolderViewState? Find(string folderPath) =>
        string.IsNullOrWhiteSpace(folderPath)
            ? null
            : _entries.LastOrDefault(entry => string.Equals(entry.Path, folderPath, StringComparison.OrdinalIgnoreCase))?.State;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<List<FolderViewEntry>>(json, JsonOptions) ?? [];
            foreach (var entry in loaded)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path) || entry.State is null)
                {
                    continue;
                }

                // Keep only the newest record per folder, in visit order.
                _entries.RemoveAll(existing => string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                _entries.Add(entry);
            }

            TrimToCap();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _entries.Clear(); // damaged view memory is disposable — start empty
        }
    }

    /// <summary>Records (or refreshes) a folder's view state and saves quietly.</summary>
    public async Task RecordAsync(string folderPath, FolderViewState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        _entries.RemoveAll(existing => string.Equals(existing.Path, folderPath, StringComparison.OrdinalIgnoreCase));
        _entries.Add(new FolderViewEntry(folderPath, state));
        TrimToCap();
        await SaveQuietlyAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        await SaveQuietlyAsync(cancellationToken);
    }

    private void TrimToCap()
    {
        while (_entries.Count > MaximumEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    private async Task SaveQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // View memory is a convenience; failing to save it must never surface as an error.
        }
    }
}
