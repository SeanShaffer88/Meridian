using System.Formats.Tar;
using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Read-only bounded catalog for TAR and gzip-compressed TAR; no extraction.</summary>
public static class TarArchiveCatalog
{
    public sealed record Entry(string Path, long Bytes, bool IsDirectory);

    public static IReadOnlyList<Entry> List(string archivePath, int maxEntries = 10000,
        long maxTotalBytes = 4L * 1024 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (maxEntries is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxTotalBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        var source = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        using var file = File.OpenRead(source);
        var compressed = source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
        using Stream stream = compressed ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true) : file;
        using var reader = new TarReader(stream, leaveOpen: true);
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = entry.EntryType == TarEntryType.Directory;
            if (!directory && entry.EntryType != TarEntryType.RegularFile &&
                entry.EntryType != TarEntryType.V7RegularFile)
                throw new InvalidDataException("TAR contains unsupported link or special entry.");
            var name = ArchiveEntryPathPolicy.Normalize(entry.Name.TrimEnd('/'));
            if (!names.Add(name)) throw new InvalidDataException("Duplicate TAR entry.");
            if (entries.Count >= maxEntries || entry.Length < 0 || entry.Length > maxTotalBytes - total)
                throw new InvalidDataException("TAR catalog size or entry limit exceeded.");
            total += entry.Length;
            entries.Add(new Entry(name, entry.Length, directory));
        }
        return entries.AsReadOnly();
    }
}
