namespace Meridian.Services;

/// <summary>
/// Bounded, read-only explanation of an OperationPreview plan.
/// This is not a dependency scanner, a filesystem reservation or an execution authorization.
/// </summary>
public static class ConsequenceMap
{
    public const int MaximumDisplayedItems = 100;

    public sealed record RiskGroup(OperationPreview.Status Status, int Count, string Explanation);

    public sealed record Summary(int TotalItems, int ReadyItems, int BlockedItems,
        IReadOnlyList<RiskGroup> Risks, IReadOnlyList<OperationPreview.Item> DisplayedItems,
        int OmittedItems, bool SafeToExecuteWithoutRecheck)
    {
        // No preview can guarantee execution safety: the filesystem can change after preview.
    }

    public static Summary Explain(OperationPreview.Plan plan, int displayLimit = MaximumDisplayedItems)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (displayLimit < 0 || displayLimit > MaximumDisplayedItems)
            throw new ArgumentOutOfRangeException(nameof(displayLimit));

        var items = plan.Items;
        var counts = new Dictionary<OperationPreview.Status, int>();
        var shown = new List<OperationPreview.Item>(Math.Min(items.Count, displayLimit));
        var ready = 0;
        foreach (var item in items)
        {
            if (item.Result == OperationPreview.Status.Ready) ready++;
            else
            {
                counts.TryGetValue(item.Result, out var existing);
                counts[item.Result] = checked(existing + 1);
            }
            if (shown.Count < displayLimit) shown.Add(item);
        }

        var groups = counts
            .OrderBy(group => group.Key)
            .Select(group => new RiskGroup(group.Key, group.Value, ExplainRisk(group.Key)))
            .ToArray();

        return new Summary(items.Count, ready, items.Count - ready,
            Array.AsReadOnly(groups), shown.AsReadOnly(), items.Count - shown.Count,
            SafeToExecuteWithoutRecheck: false);
    }

    private static string ExplainRisk(OperationPreview.Status status) => status switch
    {
        OperationPreview.Status.MissingSource => "The source could not be found at preview time.",
        OperationPreview.Status.DestinationExists => "A target exists or multiple sources propose the same target.",
        OperationPreview.Status.SameLocation => "The target would be the source itself.",
        OperationPreview.Status.NestedDestination => "The destination lies within the selected source folder.",
        OperationPreview.Status.ReparsePoint => "The selected source is a reparse point; copying is blocked.",
        OperationPreview.Status.InvalidPath => "One or more source paths are invalid.",
        _ => "The preview encountered a condition requiring review."
    };
}
