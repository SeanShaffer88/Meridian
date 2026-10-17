namespace Meridian.Core.Archives;

public static class ArchiveSourcePlanner
{
    public static IReadOnlyDictionary<string, string> Plan(IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var path in paths)
        {
            if (files.Count >= 10000) throw new InvalidDataException("Archive entry count limit exceeded.");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                Add(Path.GetFileName(path), path);
                continue;
            }
            if (!Directory.Exists(path)) throw new FileNotFoundException("Missing archive source.", path);
            var root = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Archive sources must be absolute paths.");
            var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked archive root rejected.");
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                if (!visitedDirectories.Add(directory))
                    throw new InvalidDataException("Archive source folder was selected more than once.");
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attrs = File.GetAttributes(child);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Linked archive source rejected.");
                    if ((attrs & FileAttributes.Directory) != 0)
                        pending.Push(child);
                    else
                        Add(prefix + "/" + Path.GetRelativePath(root, child).Replace('\\', '/'), child);
                }
            }
        }
        if (files.Count == 0) throw new InvalidDataException("No files selected.");
        return files;

        void Add(string name, string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeName = ArchiveEntryPathPolicy.Normalize(name);
            var safePath = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(path, cancellationToken);
            var length = new FileInfo(safePath).Length;
            if (length > 4L * 1024 * 1024 * 1024 - totalBytes)
                throw new InvalidDataException("Archive source size budget exceeded.");
            if (files.Count >= 10000 || !files.TryAdd(safeName, safePath))
                throw new InvalidDataException("Archive entry count or name collision.");
            totalBytes += length;
        }
    }
}
