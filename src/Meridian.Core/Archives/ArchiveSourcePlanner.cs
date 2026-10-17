namespace Meridian.Core.Archives;

public static class ArchiveSourcePlanner
{
    public static IReadOnlyDictionary<string, string> Plan(IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                Add(Path.GetFileName(path), path);
                continue;
            }
            if (!Directory.Exists(path)) throw new FileNotFoundException("Missing archive source.", path);
            var root = Path.GetFullPath(path);
            var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            foreach (var child in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add(prefix + "/" + Path.GetRelativePath(root, child).Replace('\\', '/'), child);
            }
        }
        if (files.Count == 0) throw new InvalidDataException("No files selected.");
        return files;

        void Add(string name, string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeName = ArchiveEntryPathPolicy.Normalize(name);
            var safePath = Meridian.Core.Viewer.PreviewPathPolicy.ValidateRegularFile(path, cancellationToken);
            if (files.Count >= 10000 || !files.TryAdd(safeName, safePath))
                throw new InvalidDataException("Archive entry count or name collision.");
        }
    }
}
