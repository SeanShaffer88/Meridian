using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Bounded, read-only access to one ZIP entry. Never extracts to disk.</summary>
public static class ZipEntryReader
{
    public static async Task<byte[]> ReadPrefixAsync(
        string archivePath, string entryPath, int maxBytes = 65536,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        if (maxBytes is < 1 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        var normalized = ArchiveEntryPathPolicy.Normalize(entryPath);
        var catalog = ZipArchiveCatalog.List(archivePath, cancellationToken: cancellationToken);
        var matches = catalog.Where(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].IsDirectory)
            throw new FileNotFoundException("Regular archive entry not found.", entryPath);

        var full = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.SingleOrDefault(e =>
            string.Equals(ArchiveEntryPathPolicy.Normalize(e.FullName.TrimEnd('/')), normalized,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new FileNotFoundException("Archive entry changed since catalog validation.", entryPath);

        await using var input = entry.Open();
        using var output = new MemoryStream(Math.Min(maxBytes, 65536));
        var buffer = new byte[Math.Min(maxBytes, 8192)];
        while (output.Length < maxBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wanted = (int)Math.Min(buffer.Length, maxBytes - output.Length);
            var count = await input.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (count == 0) break;
            output.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }
}
