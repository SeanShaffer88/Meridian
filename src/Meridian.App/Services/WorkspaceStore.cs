using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// Persists pane paths, bookmark labels/paths, paired tab names/paths and the active tab index.
/// State is stored in the user's local application data directory; the in-memory shelf is excluded.
/// </summary>
internal sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "workspace.json");
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<WorkspaceSnapshot?> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _writeLock.Release();
        }
    }
}

internal sealed record WorkspaceSnapshot(string? LeftPath, string? RightPath, BookmarkSnapshot[]? Bookmarks = null, WorkspaceTabSnapshot[]? Tabs = null, int ActiveTabIndex = 0);

internal sealed record WorkspaceTabSnapshot(string Name, string? LeftPath, string? RightPath);

internal sealed record BookmarkSnapshot(string Name, string Path);
