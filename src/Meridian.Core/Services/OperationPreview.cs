namespace Meridian.Services;

/// <summary>
/// Read-only preflight for a collection of files/folders before a cross-folder copy.
/// Plans are snapshots, not reservations: the filesystem may change before execution.
/// </summary>
public static class OperationPreview
{
    public enum Status { Ready, MissingSource, DestinationExists, SameLocation, NestedDestination, ReparsePoint, InvalidPath }
    public sealed record Item(string Source, string? Target, Status Result, string Explanation);
    public sealed record Plan(string DestinationDirectory, IReadOnlyList<Item> Items)
    {
        public int ReadyCount => Items.Count(item => item.Result == Status.Ready);
        public bool HasBlockers => Items.Any(item => item.Result != Status.Ready);
    }

    public static Plan PreviewCopy(IEnumerable<string> sourcePaths, string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"Destination folder does not exist: {destination}");
        var linkedDestination = FileOperations.FindReparsePointAncestor(destination);
        if (linkedDestination is not null)
            throw new InvalidOperationException($"Destination folder passes through a reparse point and cannot be used for batch copying: {linkedDestination}");

        var results = new List<Item>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSource in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source;
            try
            {
                source = Path.GetFullPath(rawSource);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                results.Add(new Item(rawSource ?? "", null, Status.InvalidPath, "Invalid source path."));
                continue;
            }

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            if (string.IsNullOrWhiteSpace(name))
            {
                results.Add(new Item(source, null, Status.InvalidPath, "Cannot copy a volume root."));
                continue;
            }
            var target = Path.Combine(destination, name);
            var isDirectory = Directory.Exists(source);
            if (!isDirectory && !File.Exists(source))
            {
                results.Add(new Item(source, target, Status.MissingSource, "Source is missing."));
                continue;
            }
            var linkedSource = FileOperations.FindReparsePointAncestor(source);
            if (linkedSource is not null)
            {
                results.Add(new Item(source, target, Status.ReparsePoint, $"Source passes through an unsupported reparse point: {linkedSource}"));
                continue;
            }
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new Item(source, target, Status.SameLocation, "Source and destination are the same."));
                continue;
            }
            if (isDirectory)
            {
                var relative = Path.GetRelativePath(source, destination);
                if (!Path.IsPathRooted(relative) &&
                    (relative == "." || (relative != ".." &&
                     !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))))
                {
                    results.Add(new Item(source, target, Status.NestedDestination, "Cannot copy a folder into itself or a descendant."));
                    continue;
                }
            }
            if (File.Exists(target) || Directory.Exists(target) || !reserved.Add(target))
            {
                results.Add(new Item(source, target, Status.DestinationExists, "Destination name already exists or is duplicated in this plan."));
                continue;
            }
            results.Add(new Item(source, target, Status.Ready, "No conflict detected at preview time."));
        }
        return new Plan(destination, results);
    }
}
