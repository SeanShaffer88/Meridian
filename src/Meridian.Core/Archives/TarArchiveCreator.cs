using System.Formats.Tar;
using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>Creates a TAR or gzip-compressed TAR from an explicit validated file map.</summary>
public static class TarArchiveCreator
{
    public static async Task CreateAsync(string destinationArchive,
        IReadOnlyDictionary<string, string> sourceFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchive);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        if (sourceFiles.Count is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(sourceFiles));
        if (!Path.IsPathFullyQualified(destinationArchive))
            throw new ArgumentException("Destination must be absolute.", nameof(destinationArchive));
        var gzip = destinationArchive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            destinationArchive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
        if (!gzip && !destinationArchive.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("TAR creation supports .tar, .tar.gz, and .tgz.");
        var target = Path.GetFullPath(destinationArchive);
        var parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException("Archive destination already exists.");
        var plan = new List<(string Name, string Source, long Length)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ArchiveEntryPathPolicy.Normalize(item.Key);
            if (names.Contains(name)) throw new InvalidDataException("Duplicate TAR entry name.");
            var source = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Value, cancellationToken);
            if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Archive cannot contain itself.");
            if (!sourcePaths.Add(source))
                throw new InvalidDataException("A TAR source file was selected more than once.");
            var length = new FileInfo(source).Length;
            if (length > 4L * 1024 * 1024 * 1024 - totalBytes)
                throw new InvalidDataException("TAR source exceeds 4 GiB aggregate budget.");
            totalBytes += length;
            if (parentPaths.Contains(name))
                throw new InvalidDataException("TAR file entry conflicts with a descendant path.");
            var parentPath = name;
            while (parentPath.Contains('/'))
            {
                parentPath = parentPath[..parentPath.LastIndexOf('/')];
                if (names.Contains(parentPath))
                    throw new InvalidDataException("TAR file entry conflicts with a parent path.");
                parentPaths.Add(parentPath);
            }
            names.Add(name);
            plan.Add((name, source, length));
        }
        var ancestor = parent;
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("TAR destination contains a reparse point.");
            var next = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(next) || next == ancestor) break;
            ancestor = next;
        }
        var stage = Path.Combine(parent, ".meridian-tar-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                using (var compressor = gzip ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : null)
                using (var writer = new TarWriter(compressor ?? (Stream)output, leaveOpen: true))
                {
                    foreach (var item in plan)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Source, cancellationToken);
                        var currentLength = new FileInfo(item.Source).Length;
                        if (currentLength != item.Length)
                            throw new InvalidDataException("TAR source file length changed after planning.");
                        using var input = new FileStream(item.Source, FileMode.Open,
                            FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
                        if (input.Length != item.Length)
                            throw new InvalidDataException("TAR source length changed.");
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, item.Name)
                        {
                            DataStream = input
                        });
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException("TAR destination appeared during creation.");
            var currentParent = parent;
            while (!string.IsNullOrEmpty(currentParent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(currentParent) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("TAR destination became a reparse point.");
                var nextParent = Path.GetDirectoryName(currentParent);
                if (string.IsNullOrEmpty(nextParent) || nextParent == currentParent) break;
                currentParent = nextParent;
            }
            File.Move(stage, target, overwrite: false);
        }
        finally
        {
            try { File.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
