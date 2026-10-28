namespace Meridian.Core.StorageReclaimer;

/// <summary>
/// Read-only report of a proposed cleanup. Deliberately provides no execution method.
/// </summary>
public sealed record CleanupPreview(
    int ItemCount,
    long EstimatedBytes,
    int ReviewRequiredCount,
    IReadOnlyList<CleanupCandidate> Items,
    string Warning);

public static class CleanupPreviewBuilder
{
    public static CleanupPreview Build(CleanupPlan plan, IEnumerable<string> requestedPaths)
    {
        var selected = CleanupSelection.ReviewOnly(plan, requestedPaths);
        long estimated = 0;
        foreach (var item in selected)
        {
            estimated = item.EstimatedBytes > long.MaxValue - estimated
                ? long.MaxValue : estimated + item.EstimatedBytes;
        }
        return new CleanupPreview(
            selected.Count,
            estimated,
            selected.Count(x => x.Risk == CleanupRisk.ReviewRequired),
            selected,
            "Estimate only. Nothing has been deleted. Files may change before any future operation.");
    }
}
