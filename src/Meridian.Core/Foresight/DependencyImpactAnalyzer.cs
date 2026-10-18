namespace Meridian.Core.Foresight;

/// <summary>
/// Deterministic analysis of caller-provided dependency edges.
/// Edges are supplied by a trusted format-specific parser, not inferred from filenames.
/// This model never opens, changes or executes files.
/// </summary>
public sealed record DependencyEdge(string ReferrerPath, string ReferencedPath, string Evidence);

public sealed record DependencyImpact(
    string TargetPath,
    IReadOnlyList<DependencyEdge> IncomingReferences,
    string Warning);

public static class DependencyImpactAnalyzer
{
    public static DependencyImpact Preview(
        string targetPath, IEnumerable<DependencyEdge> edges, int maxEdges = 10000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(edges);
        if (maxEdges is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxEdges));
        if (!Path.IsPathFullyQualified(targetPath))
            throw new ArgumentException("Target must be absolute.", nameof(targetPath));
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var target = Path.GetFullPath(targetPath);
        var incoming = new List<DependencyEdge>();
        var seen = new HashSet<string>(comparer);
        var count = 0;
        foreach (var edge in edges)
        {
            if (++count > maxEdges)
                throw new ArgumentException("Dependency evidence limit exceeded.", nameof(edges));
            ArgumentNullException.ThrowIfNull(edge);
            if (string.IsNullOrWhiteSpace(edge.Evidence) ||
                string.IsNullOrWhiteSpace(edge.ReferrerPath) ||
                string.IsNullOrWhiteSpace(edge.ReferencedPath) ||
                !Path.IsPathFullyQualified(edge.ReferrerPath) ||
                !Path.IsPathFullyQualified(edge.ReferencedPath))
                throw new ArgumentException("Evidence edges require absolute paths and provenance.", nameof(edges));
            var source = Path.GetFullPath(edge.ReferrerPath);
            var destination = Path.GetFullPath(edge.ReferencedPath);
            if (!comparer.Equals(destination, target)) continue;
            if (comparer.Equals(source, target)) continue;
            if (seen.Add(source))
                incoming.Add(new DependencyEdge(source, destination, edge.Evidence));
        }
        return new DependencyImpact(target, Array.AsReadOnly(incoming.ToArray()),
            "Only supplied evidence was checked. References may be incomplete or stale; no operation is authorized.");
    }
}
