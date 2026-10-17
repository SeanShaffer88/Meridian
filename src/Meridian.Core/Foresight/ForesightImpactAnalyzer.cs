namespace Meridian.Core.Foresight;

/// <summary>
/// Immutable advisory summary of known relationship hints around a proposed change.
/// A hint is not a verified dependency, and this report cannot authorize execution.
/// </summary>
public sealed record ForesightImpactReport(
    string TargetPath,
    IReadOnlyList<FileRelationship> RelatedHints,
    int ExaminedPathCount,
    string Limitation);

public static class ForesightImpactAnalyzer
{
    public static ForesightImpactReport Preview(
        string targetPath, IEnumerable<string> contextPaths, int maxPaths = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(contextPaths);
        if (!Path.IsPathFullyQualified(targetPath))
            throw new ArgumentException("Target must be absolute.", nameof(targetPath));
        var target = Path.GetFullPath(targetPath);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new List<string> { target };
        foreach (var path in contextPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Context paths must be absolute.", nameof(contextPaths));
            var full = Path.GetFullPath(path);
            if (!comparer.Equals(full, target))
                paths.Add(full);
        }
        var relationships = RelationshipAnalyzer.Analyze(paths, maxPaths);
        var related = relationships.Where(r =>
            comparer.Equals(r.SourcePath, target) || comparer.Equals(r.RelatedPath, target)).ToArray();
        return new ForesightImpactReport(target, Array.AsReadOnly(related),
            paths.Count,
            "Only filename and directory correlations were inspected. No file contents or verified dependencies were examined; absence of hints does not imply safety.");
    }
}
