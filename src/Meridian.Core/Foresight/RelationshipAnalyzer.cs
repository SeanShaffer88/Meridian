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
        IEnumerable<string> paths, int maxPaths = 1000)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (maxPaths is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxPaths));

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var items = new List<string>();
        var unique = new HashSet<string>(comparer);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("Foresight requires absolute paths.", nameof(paths));
            if (items.Count >= maxPaths)
                throw new ArgumentException("Foresight path limit exceeded.", nameof(paths));
            var full = Path.GetFullPath(path);
            if (!unique.Add(full))
                throw new ArgumentException("Duplicate path in Foresight input.", nameof(paths));
            items.Add(full);
        }

        var result = new List<FileRelationship>();
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var a = items[i];
                var b = items[j];
                var aStem = Path.GetFileNameWithoutExtension(a);
                var bStem = Path.GetFileNameWithoutExtension(b);
                if (aStem.Length > 0 && comparer.Equals(aStem, bStem))
                    result.Add(new FileRelationship(a, b,
                        "Matching filename stems; this does not establish a dependency.",
                        RelationshipEvidence.FilenameStem));
                else if (comparer.Equals(Path.GetDirectoryName(a), Path.GetDirectoryName(b)))
                    result.Add(new FileRelationship(a, b,
                        "Files share a directory; this does not establish a dependency.",
                        RelationshipEvidence.SameDirectory));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
