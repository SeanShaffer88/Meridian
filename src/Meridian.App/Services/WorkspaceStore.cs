using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// Persists pane paths, bookmark labels/paths, paired tab names/paths and the active tab index.
/// State is stored in the user's local application data directory; the in-memory shelf is excluded.
/// </summary>
internal sealed class WorkspaceStore
{
    internal const int MaximumTabs = 128;
    internal const int MaximumBookmarks = 512;
    internal const int MaximumStateBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16 };
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal WorkspaceStore(string? filePath = null)
    {
        _filePath = Path.GetFullPath(filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "workspace.json"));
    }

    public async Task<WorkspaceSnapshot?> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (stream.Length > MaximumStateBytes) return null;
            var snapshot = await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(stream, JsonOptions);
            return snapshot is not null && IsValid(snapshot) ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            // A damaged or inaccessible preference file must not prevent the app from opening.
            return null;
        }
    }

    public async Task SaveAsync(WorkspaceSnapshot snapshot)
    {
        await _writeLock.WaitAsync();
        string? temporaryPath = null;
        try
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!IsValid(snapshot)) throw new IOException("Workspace contains invalid or excessive saved state.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            if (bytes.Length > MaximumStateBytes) throw new IOException("Workspace exceeds the 1 MiB saved-state limit.");
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            try
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                }
            }
            finally { _writeLock.Release(); }
        }
    }

    private static bool IsValid(WorkspaceSnapshot snapshot)
    {
        if (!ValidPath(snapshot.LeftPath, optional: true) || !ValidPath(snapshot.RightPath, optional: true) ||
            snapshot.Tabs?.Length > MaximumTabs || snapshot.Bookmarks?.Length > MaximumBookmarks)
            return false;
        if (snapshot.Tabs?.Any(tab => tab is null || !ValidLabel(tab.Name, optional: true) ||
            !ValidPath(tab.LeftPath, optional: true) || !ValidPath(tab.RightPath, optional: true)) == true)
            return false;
        return snapshot.Bookmarks?.Any(bookmark => bookmark is null ||
            !ValidLabel(bookmark.Name, optional: false) || !ValidPath(bookmark.Path, optional: false)) != true;
    }

    private static bool ValidPath(string? path, bool optional) => string.IsNullOrEmpty(path)
        ? optional : path.Length <= 32767 && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path);

    private static bool ValidLabel(string? label, bool optional) => label is null
        ? optional : label.Length <= 1024 && !label.Any(char.IsControl) && (optional || !string.IsNullOrWhiteSpace(label));
}

internal sealed record WorkspaceSnapshot(string? LeftPath, string? RightPath, BookmarkSnapshot[]? Bookmarks = null, WorkspaceTabSnapshot[]? Tabs = null, int ActiveTabIndex = 0);

internal sealed record WorkspaceTabSnapshot(string Name, string? LeftPath, string? RightPath);

internal sealed record BookmarkSnapshot(string Name, string Path);
