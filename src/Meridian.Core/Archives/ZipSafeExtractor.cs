using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>
/// Extracts one validated ZIP regular file into a newly created destination file.
/// This early prototype requires a pre-existing trusted destination directory;
/// it does not create directories or overwrite existing files.
/// </summary>
public static class ZipSafeExtractor
{
    public static Task<long> ExtractSingleAsync(
        string archivePath, string entryPath, string destinationDirectory,
        long maxUncompressedBytes = 256L * 1024 * 1024,
        CancellationToken cancellationToken = default)
        => ExtractSingleCoreAsync(archivePath, entryPath, destinationDirectory,
            static path => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous), maxUncompressedBytes, cancellationToken);

    internal static async Task<long> ExtractSingleCoreAsync(
        string archivePath, string entryPath, string destinationDirectory,
        Func<string, Stream> createOutput, long maxUncompressedBytes = 256L * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Path.IsPathFullyQualified(destinationDirectory))
            throw new ArgumentException("Destination directory must be absolute.", nameof(destinationDirectory));
        if (maxUncompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxUncompressedBytes));

        var name = ArchiveEntryPathPolicy.Normalize(entryPath);
        if (name.Contains('/'))
            throw new NotSupportedException("This first extraction milestone supports root-level entries only.");

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetFullPath(destinationDirectory);
        if ((File.GetAttributes(directory) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException("Destination must be a regular directory.");

        // Reject reparse-point ancestors; this is a pre-open check, not a
        // race-free guarantee against concurrent filesystem modifications.
        var ancestor = directory;
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Destination cannot traverse reparse-point directories.");
            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || parent == ancestor) break;
            ancestor = parent;
        }

        var catalog = ZipArchiveCatalog.List(archivePath, cancellationToken: cancellationToken);
        var selected = catalog.SingleOrDefault(e =>
            string.Equals(e.Path, name, StringComparison.OrdinalIgnoreCase));
        if (selected is null || selected.IsDirectory)
            throw new FileNotFoundException("Regular ZIP entry not found.", name);
        if (selected.UncompressedBytes > maxUncompressedBytes)
            throw new InvalidDataException("ZIP entry exceeds extraction size limit.");

        var full = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        await using var source = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.SingleOrDefault(e =>
            string.Equals(ArchiveEntryPathPolicy.Normalize(e.FullName.TrimEnd('/', (char)92)), name,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null || entry.Length != selected.UncompressedBytes ||
            entry.CompressedLength != selected.CompressedBytes)
            throw new InvalidDataException("ZIP entry changed during extraction planning.");

        var destination = Path.Combine(directory, name);
        // CreateNew is atomic for the final name and never overwrites.
        // A partial file can remain if the process crashes; on managed failure,
        // remove only the file this call created.
        var created = false;
        var completed = false;
        try
        {
            long total;
            await using (var output = createOutput(destination))
            {
                created = true;
                total = await ZipEntryCopy.CopyAsync(entry, output, maxUncompressedBytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            // Closing the output is part of extraction: a close failure must
            // follow the same cleanup path as a write or flush failure.
            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
            return total;
        }
        catch
        {
            if (created && !completed)
            {
                try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }
}
