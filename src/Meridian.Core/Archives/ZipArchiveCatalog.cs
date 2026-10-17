using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Bounded read-only ZIP listing. No extraction or archive mutation.</summary>
public static class ZipArchiveCatalog
{
    public sealed record Entry(string Path, long UncompressedBytes, long CompressedBytes, bool IsDirectory);

    public static IReadOnlyList<Entry> List(string archivePath, int maxEntries = 10000,
        long maxTotalUncompressedBytes = 4L * 1024 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (maxEntries is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxTotalUncompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTotalUncompressedBytes));

        var full = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.SequentialScan);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = new List<Entry>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= maxEntries)
                throw new InvalidDataException("Archive entry count exceeds the listing limit.");

            var directory = item.FullName.EndsWith('/');
            var name = directory ? item.FullName[..^1] : item.FullName;
            var normalized = ArchiveEntryPathPolicy.Normalize(name);
            if (!unique.Add(normalized))
                throw new InvalidDataException("Archive contains colliding entry paths.");

            if (item.Length < 0 || item.CompressedLength < 0 ||
                item.Length > maxTotalUncompressedBytes - total)
                throw new InvalidDataException("Archive declared size exceeds the listing budget.");
            total += item.Length;
            entries.Add(new Entry(normalized, item.Length, item.CompressedLength, directory));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(entries.ToArray());
    }
}
