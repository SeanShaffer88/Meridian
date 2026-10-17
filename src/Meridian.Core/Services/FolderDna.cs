using Meridian.Models;

namespace Meridian.Services;

/// <summary>
/// Fast, non-recursive profile of an already loaded directory listing.
/// Folder contents and disk allocation are intentionally not scanned.
/// </summary>
public static class FolderDna
{
    public sealed record TypeGroup(string Label, int Count, long Bytes);

    public sealed record Snapshot(
        int Folders,
        int Files,
        long KnownFileBytes,
        DateTime? OldestModifiedUtc,
        DateTime? NewestModifiedUtc,
        IReadOnlyList<TypeGroup> Types);

    public static Snapshot Analyze(IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var folders = 0;
        var files = 0;
        long knownBytes = 0;
        DateTime? oldest = null;
        DateTime? newest = null;
        var groups = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsDirectory)
            {
                folders++;
                continue;
            }

            files++;
            knownBytes = knownBytes > long.MaxValue - item.SizeBytes ? long.MaxValue : knownBytes + item.SizeBytes;
            if (item.ModifiedUtc != default)
            {
                if (oldest is null || item.ModifiedUtc < oldest) oldest = item.ModifiedUtc;
                if (newest is null || item.ModifiedUtc > newest) newest = item.ModifiedUtc;
            }

            var label = string.IsNullOrWhiteSpace(item.TypeLabel) ? "File" : item.TypeLabel;
            groups.TryGetValue(label, out var previous);
            groups[label] = (previous.Count + 1,
                previous.Bytes > long.MaxValue - item.SizeBytes ? long.MaxValue : previous.Bytes + item.SizeBytes);
        }

        var types = groups
            .Select(pair => new TypeGroup(pair.Key, pair.Value.Count, pair.Value.Bytes))
            .OrderByDescending(group => group.Bytes)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new Snapshot(folders, files, knownBytes, oldest, newest, types);
    }
}
