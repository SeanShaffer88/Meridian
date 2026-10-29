using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// Persists the single session's pane paths and layout names, plus bookmark
/// labels/paths. State is stored in the user's local application data
/// directory; the in-memory shelf is excluded.
/// Files written while the app had workspace tabs also carry tab names/paths
/// and an active tab index; those fields stay readable so <see cref="ResolveSession"/>
/// can migrate the file — the active tab becomes the one session and the rest
/// are ignored. Layout names are deliberately not validated — an unrecognised
/// name (written by a newer build) reads as Standard in the window instead of
/// invalidating the whole workspace file.
/// Bookmark groups and the per-pane folder-tree visibility are optional
/// fields written by newer builds; files without them load with no groups
/// (every bookmark ungrouped) and both trees hidden.
/// </summary>
internal sealed class WorkspaceStore
{
    internal const int MaximumTabs = 128;
    internal const int MaximumBookmarks = 512;
    internal const int MaximumBookmarkGroups = 128;
    internal const int MaximumStateBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16 };
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal WorkspaceStore(string? filePath = null)
    {
        _filePath = Path.GetFullPath(filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meridian", "workspace.json"));
    }

    /// <summary>
    /// Resolves the one session a saved workspace represents. When the file
    /// carries workspace tabs (written by 0.3.x), the tab that was active
    /// wins, with the top-level paths as fallback for either pane it left
    /// unset; without tabs the top-level state is the session.
    /// </summary>
    internal static SessionSnapshot ResolveSession(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Tabs is { Length: > 0 } tabs)
        {
            var active = tabs[Math.Clamp(snapshot.ActiveTabIndex, 0, tabs.Length - 1)];
            if (active is not null)
                return new SessionSnapshot(
                    active.LeftPath ?? snapshot.LeftPath,
                    active.RightPath ?? snapshot.RightPath,
                    active.LeftLayout,
                    active.RightLayout,
                    snapshot.LeftFolderTreeVisible,
                    snapshot.RightFolderTreeVisible);
        }
        return new SessionSnapshot(
            snapshot.LeftPath, snapshot.RightPath, snapshot.LeftLayout, snapshot.RightLayout,
            snapshot.LeftFolderTreeVisible, snapshot.RightFolderTreeVisible);
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
        if (snapshot.Bookmarks?.Any(bookmark => bookmark is null ||
            !ValidLabel(bookmark.Name, optional: false) || !ValidPath(bookmark.Path, optional: false)) == true)
            return false;
        if (snapshot.BookmarkGroups?.Length > MaximumBookmarkGroups)
            return false;
        return snapshot.BookmarkGroups?.Any(group => group is null ||
            !ValidLabel(group.Name, optional: false) ||
            group.Paths?.Length > MaximumBookmarks ||
            group.Paths?.Any(path => !ValidPath(path, optional: false)) == true) != true;
    }

    private static bool ValidPath(string? path, bool optional) => string.IsNullOrEmpty(path)
        ? optional : path.Length <= 32767 && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path);

    private static bool ValidLabel(string? label, bool optional) => label is null
        ? optional : label.Length <= 1024 && !label.Any(char.IsControl) && (optional || !string.IsNullOrWhiteSpace(label));
}

internal sealed record WorkspaceSnapshot(string? LeftPath, string? RightPath, BookmarkSnapshot[]? Bookmarks = null, WorkspaceTabSnapshot[]? Tabs = null, int ActiveTabIndex = 0, string? LeftLayout = null, string? RightLayout = null, BookmarkGroupSnapshot[]? BookmarkGroups = null, bool LeftFolderTreeVisible = false, bool RightFolderTreeVisible = false);

/// <summary>
/// The one browsing session the window restores: both pane paths and both
/// pane layout names, after any multi-tab file has been resolved. The
/// folder-tree flags ride along from the top level of the file — tabs
/// never carried them, so a migrated multi-tab file reads as trees hidden.
/// </summary>
internal sealed record SessionSnapshot(string? LeftPath, string? RightPath, string? LeftLayout, string? RightLayout, bool LeftFolderTreeVisible = false, bool RightFolderTreeVisible = false);

internal sealed record WorkspaceTabSnapshot(string Name, string? LeftPath, string? RightPath, string? LeftLayout = null, string? RightLayout = null);

internal sealed record BookmarkSnapshot(string Name, string Path);

/// <summary>
/// One saved favorites group: its name, the bookmark paths filed under it
/// (in display order), and whether the sidebar shows it collapsed. Written
/// by builds with favorites groups; older files simply have no groups and
/// every bookmark reads as ungrouped.
/// </summary>
internal sealed record BookmarkGroupSnapshot(string Name, string[]? Paths = null, bool Collapsed = false);
