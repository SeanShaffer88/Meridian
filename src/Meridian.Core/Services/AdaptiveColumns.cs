namespace Meridian.Services;

/// <summary>The columns a Details view can show, in their canonical display order.</summary>
public enum DetailsColumn
{
    Name,
    Size,
    Age,
    Modified,
}

/// <summary>
/// Which Details columns fit a pane width. Each column has a minimum width and a
/// hide order; as the pane narrows, the lowest-priority column still showing hides
/// first (Modified, then Age, then Size). Name never hides. Pure and unit-tested —
/// the window applies the result by swapping the row template.
/// </summary>
public static class AdaptiveColumns
{
    /// <summary>Fixed horizontal space a row needs beyond the columns: icon, margins, spacing.</summary>
    public const double ChromeOverhead = 44;

    public sealed record ColumnSpec(DetailsColumn Column, double MinimumWidth, int HideOrder);

    /// <summary>Hide order: the lowest value hides first. Name uses the top value and never hides.</summary>
    public static IReadOnlyList<ColumnSpec> Specs { get; } =
    [
        new(DetailsColumn.Name, 140, int.MaxValue),
        new(DetailsColumn.Size, 100, 3),
        new(DetailsColumn.Age, 110, 2),
        new(DetailsColumn.Modified, 130, 1),
    ];

    /// <summary>The columns visible at a width, in canonical display order.</summary>
    public static IReadOnlyList<DetailsColumn> VisibleForWidth(double availableWidth)
    {
        var visible = Specs.ToList();
        while (ChromeOverhead + visible.Sum(spec => spec.MinimumWidth) > availableWidth)
        {
            var candidate = visible
                .Where(spec => spec.HideOrder != int.MaxValue)
                .OrderBy(spec => spec.HideOrder)
                .FirstOrDefault();
            if (candidate is null)
            {
                break;
            }

            visible.Remove(candidate);
        }

        return Specs.Where(spec => visible.Contains(spec)).Select(spec => spec.Column).ToArray();
    }

    /// <summary>
    /// The column sets the width walk can actually produce, widest first. A stored
    /// per-folder column set is snapped to one of these so it always maps to a row
    /// template: the widest reachable set fully contained in the stored one.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<DetailsColumn>> ReachableSets { get; } =
    [
        [DetailsColumn.Name, DetailsColumn.Size, DetailsColumn.Age, DetailsColumn.Modified],
        [DetailsColumn.Name, DetailsColumn.Size, DetailsColumn.Age],
        [DetailsColumn.Name, DetailsColumn.Size],
        [DetailsColumn.Name],
    ];

    public static bool IsReachableSet(IReadOnlyList<DetailsColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return ReachableSets.Any(set => set.SequenceEqual(columns.OrderBy(ColumnOrder)));
    }

    public static IReadOnlyList<DetailsColumn> SnapToReachableSet(IReadOnlyList<DetailsColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var wanted = columns.ToHashSet();
        foreach (var set in ReachableSets)
        {
            if (set.All(wanted.Contains))
            {
                return set;
            }
        }

        return ReachableSets[^1];
    }

    private static int ColumnOrder(DetailsColumn column) => column switch
    {
        DetailsColumn.Name => 0,
        DetailsColumn.Size => 1,
        DetailsColumn.Age => 2,
        _ => 3,
    };
}
