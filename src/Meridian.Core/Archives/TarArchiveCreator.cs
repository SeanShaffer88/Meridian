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
        var plan = new List<(string Name, string Source)>();
        foreach (var item in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ArchiveEntryPathPolicy.Normalize(item.Key);
            var source = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Value, cancellationToken);
            if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Archive cannot contain itself.");
            plan.Add((name, source));
        }
        var stage = Path.Combine(parent, ".meridian-tar-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                using Stream stream = gzip ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : output;
                using (var writer = new TarWriter(stream, leaveOpen: true))
                {
                    foreach (var item in plan)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(item.Source, cancellationToken);
                        writer.WriteEntry(item.Source, item.Name);
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stage, target, overwrite: false);
        }
        finally
        {
            try { File.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
