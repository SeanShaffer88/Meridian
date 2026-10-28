using System.Text.Json;

namespace Meridian.Services;

internal enum ThemePreference
{
    Dark,
    Light,
    System
}

internal enum DensityPreference
{
    Comfortable,
    Compact
}

/// <summary>
/// Persists user interface preferences locally, beside the workspace store.
/// The file is stored in the user's local application data directory. A missing
/// or unreadable file yields the defaults, which reproduce the pre-settings look:
/// dark theme, comfortable density, brand accent, centered pane split.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian",
        "settings.json");
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<SettingsSnapshot?> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<SettingsSnapshot>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or inaccessible settings file must not prevent the app from opening.
            return null;
        }
    }

    public async Task SaveAsync(SettingsSnapshot snapshot)
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

/// <summary>
/// Theme and Density are stored as names so future values degrade to the
/// defaults instead of breaking older files. PaneSplitFraction is the left
/// pane's share of the work area width (0.5 = equal panes). OrganizeSection
/// is "None", "Bookmarks", or "Shelf".
/// </summary>
internal sealed record SettingsSnapshot(
    string? Theme = null,
    string? Density = null,
    bool FollowWindowsAccent = false,
    double PaneSplitFraction = 0.5,
    string? OrganizeSection = null);
