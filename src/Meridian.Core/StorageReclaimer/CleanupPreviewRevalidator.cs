namespace Meridian.Core.StorageReclaimer;

/// <summary>Advisory-only comparison of an earlier preview with a fresh inventory.</summary>
public static class CleanupPreviewRevalidator
{
    public static bool Matches(CleanupPreview preview, CleanupPlan currentPlan)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(currentPlan);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var current = new Dictionary<string, CleanupCandidate>(comparer);
        foreach (var item in currentPlan.Candidates)
            if (!current.TryAdd(item.FullPath, item)) return false;
        if (preview.ItemCount != preview.Items.Count) return false;
        var seen = new HashSet<string>(comparer);
        long estimated = 0;
        var reviewRequired = 0;
        foreach (var previous in preview.Items)
        {
            if (!seen.Add(previous.FullPath) ||
                !current.TryGetValue(previous.FullPath, out var item) ||
                item != previous || item.Risk == CleanupRisk.Protected)
                return false;
            estimated = item.EstimatedBytes > long.MaxValue - estimated
                ? long.MaxValue : estimated + item.EstimatedBytes;
            if (item.Risk == CleanupRisk.ReviewRequired) reviewRequired++;
        }
        return preview.EstimatedBytes == estimated &&
            preview.ReviewRequiredCount == reviewRequired &&
            preview.ItemCount == seen.Count;
    }
}
