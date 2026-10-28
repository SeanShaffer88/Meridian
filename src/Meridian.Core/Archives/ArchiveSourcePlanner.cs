namespace Meridian.Core.Archives;

public static class ArchiveSourcePlanner
{
    public static IReadOnlyDictionary<string, string> Plan(IEnumerable<string> paths,
        CancellationToken cancellationToken = default, int maxDirectories = 10000)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDirectories);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entryParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Archive sources must be absolute paths.");
            if (File.Exists(path))
            {
                RejectLinkedAncestors(Path.GetFullPath(path), cancellationToken);
                Add(Path.GetFileName(path), path);
                continue;
            }
            if (!Directory.Exists(path)) throw new FileNotFoundException("Missing archive source.", path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            RejectLinkedAncestors(root, cancellationToken);
            if (string.Equals(Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select folders within a drive, not the drive root itself.");
            if (visitedDirectories.Contains(root))
                throw new InvalidDataException("Archive source folder was selected more than once.");
            var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            var pending = new Stack<string>();
            ReserveDirectory(root);
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                RejectLinkedAncestors(directory, cancellationToken);
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attrs = File.GetAttributes(child);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Linked archive source rejected.");
                    if ((attrs & FileAttributes.Directory) != 0)
                    {
                        ReserveDirectory(child);
                        pending.Push(child);
                    }
                    else
                        Add(prefix + "/" + Path.GetRelativePath(root, child).Replace('\\', '/'), child);
                }
            }
        }
        if (files.Count == 0) throw new InvalidDataException("No files selected.");
        return files;

        void ReserveDirectory(string directory)
        {
            if (visitedDirectories.Contains(directory))
                throw new InvalidDataException("Archive source folder was selected more than once.");
            // Bound discovered directories, including work still waiting on the stack.
            if (visitedDirectories.Count >= maxDirectories)
                throw new InvalidDataException("Archive source directory traversal budget exceeded.");
            visitedDirectories.Add(directory);
        }

        static void RejectLinkedAncestors(string path, CancellationToken cancellationToken)
        {
            var ancestor = path;
            while (!string.IsNullOrEmpty(ancestor))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked archive source ancestor rejected.");
                var next = Path.GetDirectoryName(ancestor);
                if (string.IsNullOrEmpty(next) || next == ancestor) break;
                ancestor = next;
            }
        }

        void Add(string name, string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeName = ArchiveEntryPathPolicy.Normalize(name);
            var safePath = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(path, cancellationToken);
            if (sourceFiles.Contains(safePath))
                throw new InvalidDataException("Archive source file selected more than once.");
            var length = new FileInfo(safePath).Length;
            if (length > 4L * 1024 * 1024 * 1024 - totalBytes)
                throw new InvalidDataException("Archive source size budget exceeded.");
            if (files.Count >= 10000 || files.ContainsKey(safeName))
                throw new InvalidDataException("Archive entry count or name collision.");
            if (entryParents.Contains(safeName))
                throw new InvalidDataException("Archive file conflicts with a descendant entry.");
            var parent = safeName;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (files.ContainsKey(parent))
                    throw new InvalidDataException("Archive file conflicts with a parent file entry.");
            }
            parent = safeName;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                entryParents.Add(parent);
            }
            files.Add(safeName, safePath);
            sourceFiles.Add(safePath);
            totalBytes += length;
        }
    }
}
