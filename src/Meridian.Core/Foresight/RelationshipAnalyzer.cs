namespace Meridian.Core.Foresight;

/// <summary>Explainable, metadata-only relationship candidate. Never an execution instruction.</summary>
public sealed record FileRelationship(
    string SourcePath,
    string RelatedPath,
    string Reason,
    RelationshipEvidence Evidence);

public enum RelationshipEvidence
{
    FilenameStem,
    SameDirectory
}

/// <summary>
/// Deterministic first milestone of Meridian Foresight.
/// Only examines caller-supplied paths; does not open files, enumerate directories,
/// contact a model, or mutate the filesystem. Relationships are hints, not dependencies.
/// </summary>
public static class RelationshipAnalyzer
{
    public static IReadOnlyList<FileRelationship> Analyze(
        IEnumerable<string> paths, int maxPaths = 1000, int maxRelationships = 10000,
        CancellationToken cancellationToken = default, TimeSpan? timeBudget = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (maxPaths is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxPaths));

        if (maxRelationships is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxRelationships));

        if (timeBudget is { } budget && (budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes(5)))
            throw new ArgumentOutOfRangeException(nameof(timeBudget));
        var stopwatch = timeBudget.HasValue ? System.Diagnostics.Stopwatch.StartNew() : null;
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch is not null && stopwatch.Elapsed > timeBudget!.Value)
                throw new TimeoutException("Foresight analysis time budget exceeded; narrow the input set.");
        }

        CheckBudget();
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var items = new List<string>();
        var unique = new HashSet<string>(comparer);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("Foresight requires absolute paths.", nameof(paths));
            if (items.Count >= maxPaths)
                throw new ArgumentException("Foresight path limit exceeded.", nameof(paths));
            var full = Path.GetFullPath(path);
            if (!unique.Add(full))
                throw new ArgumentException("Duplicate path in Foresight input.", nameof(paths));
            items.Add(full);
        }

        // Index candidates by stem and directory instead of comparing every input pair.
        // Lists retain input order, preserving the previous deterministic output order.
        var byStem = new Dictionary<string, List<int>>(comparer);
        var byDirectory = new Dictionary<string, List<int>>(comparer);
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(items[i]);
            if (stem.Length > 0)
            {
                if (!byStem.TryGetValue(stem, out var stemIndices))
                    byStem[stem] = stemIndices = new List<int>();
                stemIndices.Add(i);
            }

            var directory = Path.GetDirectoryName(items[i]) ?? string.Empty;
            if (!byDirectory.TryGetValue(directory, out var directoryIndices))
                byDirectory[directory] = directoryIndices = new List<int>();
            directoryIndices.Add(i);
        }

        var result = new List<FileRelationship>();
        for (var i = 0; i < items.Count; i++)
        {
            var stem = Path.GetFileNameWithoutExtension(items[i]);
            var stemIndices = stem.Length > 0 ? byStem[stem] : null;
            var directoryIndices = byDirectory[Path.GetDirectoryName(items[i]) ?? string.Empty];
            var stemPosition = stemIndices is null ? 0 : stemIndices.BinarySearch(i) + 1;
            var directoryPosition = directoryIndices.BinarySearch(i) + 1;

            // Merge two ascending candidate lists. A matching stem wins over
            // same-directory evidence when a pair qualifies for both.
            while ((stemIndices is not null && stemPosition < stemIndices.Count) ||
                   directoryPosition < directoryIndices.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextStem = stemIndices is not null && stemPosition < stemIndices.Count
                    ? stemIndices[stemPosition] : int.MaxValue;
                var nextDirectory = directoryPosition < directoryIndices.Count
                    ? directoryIndices[directoryPosition] : int.MaxValue;
                var matchingStem = nextStem <= nextDirectory;
                var j = Math.Min(nextStem, nextDirectory);
                if (nextStem == j) stemPosition++;
                if (nextDirectory == j) directoryPosition++;

                if (result.Count == maxRelationships)
                    throw new InvalidOperationException("Foresight relationship limit exceeded; narrow the input set.");
                result.Add(matchingStem
                    ? new FileRelationship(items[i], items[j],
                        "Matching filename stems; this does not establish a dependency.",
                        RelationshipEvidence.FilenameStem)
                    : new FileRelationship(items[i], items[j],
                        "Files share a directory; this does not establish a dependency.",
                        RelationshipEvidence.SameDirectory));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
