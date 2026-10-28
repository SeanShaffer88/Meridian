namespace Meridian.Core.StorageReclaimer;

/// <summary>Risk classification for an explicitly inventoried cleanup candidate.</summary>
public enum CleanupRisk
{
    Low,
    ReviewRequired,
    Protected
}

/// <summary>
/// Immutable read-only cleanup finding. A finding is never authority to delete.
/// </summary>
public sealed record CleanupCandidate(
    string FullPath,
    long EstimatedBytes,
    string Category,
    CleanupRisk Risk,
    string Explanation);

/// <summary>
/// Pure advisory model: no filesystem access, deletion, elevation, or hidden cleanup.
/// </summary>
public sealed class CleanupPlan
{
    public IReadOnlyList<CleanupCandidate> Candidates { get; }
    public long ReviewableBytes { get; }
    public long ProtectedBytes { get; }

    public CleanupPlan(IEnumerable<CleanupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var snapshot = candidates.ToArray();
        foreach (var item in snapshot)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrWhiteSpace(item.FullPath) ||
                string.IsNullOrWhiteSpace(item.Category) ||
                string.IsNullOrWhiteSpace(item.Explanation) ||
                item.EstimatedBytes < 0 ||
                !Enum.IsDefined(item.Risk))
                throw new ArgumentException("Cleanup candidates require valid paths, categories, explanations, nonnegative sizes and risks.", nameof(candidates));
        }
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var uniquePaths = new HashSet<string>(comparer);
        foreach (var candidate in snapshot)
        {
            if (!uniquePaths.Add(candidate.FullPath))
                throw new ArgumentException("Cleanup inventory contains duplicate paths.", nameof(candidates));
        }
        Candidates = Array.AsReadOnly(snapshot);
        ReviewableBytes = SumSaturating(snapshot.Where(x => x.Risk != CleanupRisk.Protected).Select(x => x.EstimatedBytes));
        ProtectedBytes = SumSaturating(snapshot.Where(x => x.Risk == CleanupRisk.Protected).Select(x => x.EstimatedBytes));
    }

    private static long SumSaturating(IEnumerable<long> sizes)
    {
        long total = 0;
        foreach (var size in sizes)
        {
            if (size > long.MaxValue - total) return long.MaxValue;
            total += size;
        }
        return total;
    }
}
