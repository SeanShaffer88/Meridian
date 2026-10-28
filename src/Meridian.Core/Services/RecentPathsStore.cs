using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// One recently used location: a folder a pane navigated to, or a file the
/// user opened. Paths only — no file contents, no metadata beyond the kind
/// and the moment of last use.
/// </summary>
public sealed record RecentPathEntry(string Path, bool IsDirectory, DateTimeOffset LastUsedUtc);

/// <summary>
/// The local recent-locations list behind the Home page. Most recent first,
/// deduplicated by path (case-insensitive, like Windows paths), capped, and
/// clearable. Persistence follows the settings-store pattern: a JSON file
/// written through a temporary file and an atomic move, and a missing or
/// damaged file reads as an empty list rather than an error — the list is a
/// convenience, never a reason to fail. Recording and clearing update the
/// in-memory list even if the write fails.
/// </summary>
public sealed class RecentPathsStore
{
    /// <summary>The most entries kept; older entries fall off the end.</summary>
    public const int MaximumEntries = 24;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly List<RecentPathEntry> _entries = [];

    public RecentPathsStore(string filePath) => _filePath = Path.GetFullPath(filePath);

    /// <summary>The current entries, most recent first.</summary>
    public IReadOnlyList<RecentPathEntry> Entries => _entries;

    /// <summary>Loads the saved list, replacing the in-memory one. A missing or unreadable file yields an empty list.</summary>
    public async Task LoadAsync()
    {
        _entries.Clear();
        try
        {
            if (!File.Exists(_filePath)) return;
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var saved = await JsonSerializer.DeserializeAsync<List<RecentPathEntry>>(stream, JsonOptions);
            if (saved is null) return;
            foreach (var entry in saved)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                if (_entries.Any(existing => string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase))) continue;
                _entries.Add(entry);
                if (_entries.Count >= MaximumEntries) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged recents file must not prevent the app from opening.
            _entries.Clear();
        }
    }

    /// <summary>
    /// Records one use: the path moves to the front with a fresh timestamp,
    /// any older entry for the same path is dropped, and the list is trimmed
    /// to <see cref="MaximumEntries"/>. Blank paths are ignored. The updated
    /// list is then saved on a best-effort basis.
    /// </summary>
    public async Task RecordAsync(string path, bool isDirectory, DateTimeOffset? usedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var trimmed = path.Trim();
        _entries.RemoveAll(existing => string.Equals(existing.Path, trimmed, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new RecentPathEntry(trimmed, isDirectory, usedUtc ?? DateTimeOffset.UtcNow));
        if (_entries.Count > MaximumEntries) _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
        await SaveQuietlyAsync();
    }

    /// <summary>Empties the list and saves the empty state.</summary>
    public async Task ClearAsync()
    {
        _entries.Clear();
        await SaveQuietlyAsync();
    }

    private async Task SaveQuietlyAsync()
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, _entries, JsonOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Best-effort: the in-memory list stays correct for this session.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
