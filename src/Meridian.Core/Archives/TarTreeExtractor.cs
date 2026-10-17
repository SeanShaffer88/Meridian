using System.Formats.Tar;
using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Stages a validated TAR tree and publishes only a complete new directory.</summary>
public static class TarTreeExtractor
{
    public sealed record Receipt(string Directory, int FileCount, long BytesWritten);

    public static async Task<Receipt> ExtractAsync(string archivePath, string destinationDirectory,
        string newFolderName, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        var catalog = TarArchiveCatalog.List(archivePath, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(destinationDirectory))
            throw new ArgumentException("Destination parent must be absolute.", nameof(destinationDirectory));
        var folder = ArchiveEntryPathPolicy.Normalize(newFolderName);
        if (folder.Contains('/')) throw new ArgumentException("Output must be a single folder name.");
        var parent = Path.GetFullPath(destinationDirectory);
        CheckParents(parent, cancellationToken);
        var target = Path.Combine(parent, folder);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException("Extraction destination exists.");
        var source = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(archivePath, cancellationToken);
        var stage = Path.Combine(parent, ".meridian-tar-extract-" + Guid.NewGuid().ToString("N"));
        var staged = false;
        try
        {
            Directory.CreateDirectory(stage);
            staged = true;
            using var file = File.OpenRead(source);
            var gzip = source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            using Stream stream = gzip ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true) : file;
            using var reader = new TarReader(stream, leaveOpen: true);
            long total = 0;
            int count = 0;
            for (var i = 0; i < catalog.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = reader.GetNextEntry(copyData: false)
                    ?? throw new InvalidDataException("TAR ended during extraction.");
                var expected = catalog[i];
                var directory = entry.EntryType == TarEntryType.Directory;
                if ((!directory && entry.EntryType != TarEntryType.RegularFile &&
                     entry.EntryType != TarEntryType.V7RegularFile) ||
                    directory != expected.IsDirectory ||
                    ArchiveEntryPathPolicy.Normalize(entry.Name.TrimEnd('/')) != expected.Path ||
                    entry.Length != expected.Bytes)
                    throw new InvalidDataException("TAR contents changed after cataloging.");
                var outputPath = Path.Combine(new[] { stage }.Concat(expected.Path.Split('/')).ToArray());
                if (directory)
                    Directory.CreateDirectory(outputPath);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 8192, FileOptions.Asynchronous);
                    if (entry.DataStream is null) throw new InvalidDataException("TAR entry has no data.");
                    var buffer = new byte[8192];
                    long written = 0;
                    while (written < expected.Bytes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = await entry.DataStream.ReadAsync(buffer.AsMemory(
                            0, (int)Math.Min(buffer.Length, expected.Bytes - written)), cancellationToken);
                        if (read == 0) throw new InvalidDataException("Truncated TAR entry.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;
                        total += read;
                    }
                    count++;
                }
                progress?.Report(Math.Min(99, (i + 1) * 100 / Math.Max(1, catalog.Count)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.GetNextEntry(copyData: false) is not null)
                throw new InvalidDataException("TAR gained entries after cataloging.");
            cancellationToken.ThrowIfCancellationRequested();
            CheckParents(parent, cancellationToken);
            var stageAttributes = File.GetAttributes(stage);
            if ((stageAttributes & FileAttributes.ReparsePoint) != 0 ||
                (stageAttributes & FileAttributes.Directory) == 0)
                throw new IOException("Extraction stage is not a regular directory.");
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException("Extraction destination appeared.");
            Directory.Move(stage, target);
            staged = false;
            progress?.Report(100);
            return new Receipt(target, count, total);
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

    private static void CheckParents(string directory, CancellationToken cancellationToken)
    {
        var current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Extraction destination contains a reparse point.");
            var next = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(next) || next == current) break;
            current = next;
        }
        if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
            throw new IOException("Extraction parent must be a directory.");
    }
}
