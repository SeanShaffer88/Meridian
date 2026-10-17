using System.Formats.Tar;
using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Read-only bounded catalog for TAR and gzip-compressed TAR, used by staged extraction.</summary>
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
        if (!source.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) &&
            !source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
            !source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only TAR and compressed TAR catalogs are supported.");
        using var file = File.OpenRead(source);
        var compressed = source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
        using Stream stream = compressed ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true) : file;
        using var reader = new TarReader(stream, leaveOpen: true);
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var parent = name;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (fileNames.Contains(parent))
                    throw new InvalidDataException("TAR entry conflicts with a parent file.");
            }
            if (!directory && names.Any(existing => existing.StartsWith(name + "/",
                StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("TAR file conflicts with existing descendants.");
            if (entries.Count >= maxEntries || entry.Length < 0 || entry.Length > maxTotalBytes - total)
                throw new InvalidDataException("TAR catalog size or entry limit exceeded.");
            if (directory && entry.Length != 0)
                throw new InvalidDataException("Directory TAR entry has a payload.");
            total += entry.Length;
            entries.Add(new Entry(name, entry.Length, directory));
            if (!directory) fileNames.Add(name);
        }
        return entries.AsReadOnly();
    }
}
