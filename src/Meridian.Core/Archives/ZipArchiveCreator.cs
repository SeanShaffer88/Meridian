using System.IO.Compression;

namespace Meridian.Core.Archives;

/// <summary>
/// Creates a ZIP from explicit regular files, without following links or overwriting
/// an existing archive. This is a bounded initial creation API, not a folder walker.
/// </summary>
public static class ZipArchiveCreator
{
    public static async Task CreateAsync(
        string destinationArchive, IReadOnlyDictionary<string, string> sourceFiles,
        long maxTotalBytes = 4L * 1024 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchive);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        if (maxTotalBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        if (!Path.IsPathFullyQualified(destinationArchive))
            throw new ArgumentException("Archive destination must be absolute.", nameof(destinationArchive));
        if (sourceFiles.Count == 0 || sourceFiles.Count > 10000)
            throw new ArgumentOutOfRangeException(nameof(sourceFiles));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(string Name, string Source, long Length)>();
        long total = 0;
        foreach (var item in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ArchiveEntryPathPolicy.Normalize(item.Key);
            if (!names.Add(name)) throw new InvalidDataException("Archive entry names collide.");
            var source = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Value, cancellationToken);
            var length = new FileInfo(source).Length;
            if (length < 0 || length > maxTotalBytes - total)
                throw new InvalidDataException("Archive source size budget exceeded.");
            total += length;
            plan.Add((name, source, length));
        }
        var fileNames = new HashSet<string>(plan.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan)
        {
            var slash = item.Name.IndexOf('/');
            while (slash >= 0)
            {
                if (fileNames.Contains(item.Name[..slash]))
                    throw new InvalidDataException("File entry conflicts with another entry's parent.");
                slash = item.Name.IndexOf('/', slash + 1);
            }
        }

        var target = Path.GetFullPath(destinationArchive);
        var parent = Path.GetDirectoryName(target)!;
        var ancestor = parent;
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Archive destination contains a reparse-point ancestor.");
            var next = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(next) || next == ancestor) break;
            ancestor = next;
        }
        if ((File.GetAttributes(parent) & FileAttributes.Directory) == 0)
            throw new IOException("Archive destination parent must be a directory.");
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException("Archive destination already exists.");

        var stage = Path.Combine(parent, ".meridian-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            await using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                created = true;
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var buffer = new byte[8192];
                    foreach (var item in plan)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // Revalidate before opening; not a handle-identity guarantee.
                        Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Source, cancellationToken);
                        await using var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        var entry = zip.CreateEntry(item.Name, CompressionLevel.Optimal);
                        await using var compressed = entry.Open();
                        long copied = 0;
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var count = await input.ReadAsync(buffer, cancellationToken);
                            if (count == 0) break;
                            if (count > item.Length - copied)
                                throw new InvalidDataException("Source file grew during archive creation.");
                            await compressed.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                            copied += count;
                        }
                        if (copied != item.Length)
                            throw new InvalidDataException("Source file changed size during archive creation.");
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck the destination parent immediately before publication.
            ancestor = parent;
            while (!string.IsNullOrEmpty(ancestor))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Archive destination changed to a reparse point.");
                var next = Path.GetDirectoryName(ancestor);
                if (string.IsNullOrEmpty(next) || next == ancestor) break;
                ancestor = next;
            }
            File.Move(stage, target, overwrite: false);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
