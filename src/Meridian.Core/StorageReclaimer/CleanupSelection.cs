namespace Meridian.Core.StorageReclaimer;

/// <summary>
/// Fail-closed, pure advisory selection of cleanup candidates.
/// This type deliberately has no execution or deletion method.
/// </summary>
public static class CleanupSelection
{
    public static IReadOnlyList<CleanupCandidate> ReviewOnly(
        CleanupPlan plan, IEnumerable<string> requestedPaths)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(requestedPaths);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = plan.Candidates.ToDictionary(x => x.FullPath, comparer);
        var selected = new List<CleanupCandidate>();
        var seen = new HashSet<string>(comparer);
        foreach (var path in requestedPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                throw new ArgumentException("Selection has an empty or duplicate path.", nameof(requestedPaths));
            if (!candidates.TryGetValue(path, out var candidate))
                throw new ArgumentException("Selection includes a path outside the inventory.", nameof(requestedPaths));
            if (candidate.Risk == CleanupRisk.Protected)
                throw new InvalidOperationException("Protected items cannot be selected for cleanup.");
            selected.Add(candidate);
        }
        return Array.AsReadOnly(selected.ToArray());
    }
}
