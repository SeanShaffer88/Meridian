using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>
/// Stages a single root-level ZIP file in the destination directory and publishes
/// it without overwriting. Not a race-free destination-identity security boundary.
/// </summary>
public static class ZipStagedExtractor
{
    public static async Task<long> ExtractSingleAsync(
        string archivePath, string entryName, string destinationDirectory,
        long maxUncompressedBytes = 256L * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (maxUncompressedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxUncompressedBytes));
        var name = ArchiveEntryPathPolicy.Normalize(entryName);
        if (name.Contains('/'))
            throw new NotSupportedException("Nested extraction is not supported by this API.");
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Path.IsPathFullyQualified(destinationDirectory))
            throw new ArgumentException("Destination must be absolute.", nameof(destinationDirectory));

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetFullPath(destinationDirectory);
        var ancestor = directory;
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attrs = File.GetAttributes(ancestor);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Destination path contains a reparse point.");
            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || parent == ancestor) break;
            ancestor = parent;
        }
        if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
            throw new IOException("Destination must be a directory.");

        var catalog = ZipArchiveCatalog.List(archivePath, cancellationToken: cancellationToken);
        var selected = catalog.SingleOrDefault(e => string.Equals(e.Path, name, StringComparison.OrdinalIgnoreCase));
        if (selected is null || selected.IsDirectory)
            throw new FileNotFoundException("ZIP regular file not found.", name);
        if (selected.UncompressedBytes > maxUncompressedBytes)
            throw new InvalidDataException("ZIP entry exceeds size budget.");

        var sourcePath = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var actual = archive.Entries.SingleOrDefault(e =>
            string.Equals(ArchiveEntryPathPolicy.Normalize(e.FullName.TrimEnd('/', (char)92)),
                name, StringComparison.OrdinalIgnoreCase));
        if (actual is null || actual.Length != selected.UncompressedBytes ||
            actual.CompressedLength != selected.CompressedBytes)
            throw new InvalidDataException("Archive entry changed after listing.");

        var final = Path.Combine(directory, name);
        var staging = Path.Combine(directory, ".meridian-" + Guid.NewGuid().ToString("N") + ".tmp");
        var stagingCreated = false;
        try
        {
            long total = 0;
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                stagingCreated = true;
                await using var input = actual.Open();
                var buffer = new byte[8192];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    if (read > maxUncompressedBytes - total)
                        throw new InvalidDataException("ZIP data exceeded extraction limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                }
                if (total != selected.UncompressedBytes)
                    throw new InvalidDataException("ZIP entry size mismatch.");
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // File.Move without overwrite refuses an existing final destination.
            // The stage remains in the same directory/volume for atomic rename.
            File.Move(staging, final, overwrite: false);
            stagingCreated = false;
            return total;
        }
        finally
        {
            if (stagingCreated)
            {
                try { File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
