using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>
/// Extracts a complete ZIP into a newly named directory, staged under the same
/// parent and published with a no-overwrite directory rename. Existing output
/// trees are never merged. Reparse-point checks are not a handle-based sandbox.
/// </summary>
public static class ZipTreeExtractor
{
    public sealed record Receipt(string Directory, int FileCount, long BytesWritten);

    public static async Task<Receipt> ExtractAsync(
        string archivePath, string destinationDirectory, string newFolderName,
        int maxEntries = 10000, long maxTotalBytes = 4L * 1024 * 1024 * 1024,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Path.IsPathFullyQualified(destinationDirectory))
            throw new ArgumentException("Destination parent must be absolute.", nameof(destinationDirectory));
        if (maxEntries is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxTotalBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        var folder = ArchiveEntryPathPolicy.Normalize(newFolderName);
        if (folder.Contains('/')) throw new ArgumentException("Output must be one folder name.", nameof(newFolderName));
        var parent = Path.GetFullPath(destinationDirectory);
        CheckDirectoryChain(parent, cancellationToken);
        var target = Path.Combine(parent, folder);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException("Extraction destination already exists.");

        var catalog = ZipArchiveCatalog.List(archivePath, maxEntries, maxTotalBytes, cancellationToken);
        var sourcePath = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != catalog.Count)
            throw new InvalidDataException("Archive entry count changed after planning.");

        // Verify the entire reopened archive against the validated catalog before writing.
        for (var i = 0; i < catalog.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[i];
            var directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith((char)92);
            var name = ArchiveEntryPathPolicy.Normalize(directory ? entry.FullName[..^1] : entry.FullName);
            var expected = catalog[i];
            var kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if (name != expected.Path || directory != expected.IsDirectory ||
                entry.Length != expected.UncompressedBytes || entry.CompressedLength != expected.CompressedBytes ||
                (kind != 0 && kind != (directory ? 0x4000 : 0x8000)))
                throw new InvalidDataException("Archive entries changed after planning.");
        }

        var stage = Path.Combine(parent, ".meridian-extract-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(stage) || File.Exists(stage))
            throw new IOException("Extraction staging path already exists.");
        var staged = false;

        try
        {
            // Stage must be a newly created directory, not a pre-existing path.
            Directory.CreateDirectory(stage);
            staged = true;
            long total = 0;
            var fileCount = 0;
            for (var i = 0; i < catalog.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = catalog[i];
                var segments = item.Path.Split('/');
                var outputPath = Path.Combine(new[] { stage }.Concat(segments).ToArray());
                if (item.IsDirectory)
                {
                    Directory.CreateDirectory(outputPath);
                    progress?.Report((int)Math.Min(99L, (i + 1L) * 100 / catalog.Count));
                    continue;
                }
                var containing = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(containing);
                await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 8192, FileOptions.Asynchronous);
                await using var input = archive.Entries[i].Open();
                var buffer = new byte[8192];
                long written = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    if (read > item.UncompressedBytes - written || read > maxTotalBytes - total)
                        throw new InvalidDataException("ZIP decompression exceeded declared size or extraction budget.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    total += read;
                }
                if (written != item.UncompressedBytes)
                    throw new InvalidDataException("ZIP decompressed size did not match catalog.");
                fileCount++;
                progress?.Report((int)Math.Min(99L, (i + 1L) * 100 / catalog.Count));
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckDirectoryChain(parent, cancellationToken);
            if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Extraction stage became a reparse point.");
            // The stage must remain a regular directory, not a file or redirect.
            if ((File.GetAttributes(stage) & FileAttributes.Directory) == 0)
                throw new IOException("Extraction staging path is not a directory.");
            // Recheck for destination conflicts before publishing the complete tree.
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException("Extraction destination appeared during staging.");
            // Atomic on the same volume when the filesystem supports rename.
            Directory.Move(stage, target);
            staged = false;
            progress?.Report(100);
            return new Receipt(target, fileCount, total);
        }
        finally
        {
            if (staged)
            {
                try
                {
                    if (Directory.Exists(stage) &&
                        (File.GetAttributes(stage) & FileAttributes.ReparsePoint) == 0)
                        Directory.Delete(stage, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void CheckDirectoryChain(string directory, CancellationToken token)
    {
        var current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            token.ThrowIfCancellationRequested();
            var attrs = File.GetAttributes(current);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Extraction destination contains a reparse point.");
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
            throw new IOException("Extraction parent must be a directory.");
    }
}
