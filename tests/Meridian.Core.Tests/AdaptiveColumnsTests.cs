using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Adaptive Details columns: thresholds are the column minimums plus the fixed
/// chrome overhead (44 + 140 + 100 + 110 + 130 = 524 for all four), columns hide
/// in priority order (Modified, then Age, then Size), and Name never hides.
/// </summary>
public sealed class AdaptiveColumnsTests
{
    private static readonly DetailsColumn[] All = [DetailsColumn.Name, DetailsColumn.Size, DetailsColumn.Age, DetailsColumn.Modified];

    [Fact]
    public void WideEnough_ShowsEverything() =>
        Assert.Equal(All, AdaptiveColumns.VisibleForWidth(524));

    [Fact]
    public void OnePixelNarrower_HidesModifiedFirst() =>
        Assert.Equal(
            new[] { DetailsColumn.Name, DetailsColumn.Size, DetailsColumn.Age },
            AdaptiveColumns.VisibleForWidth(523));

    [Fact]
    public void NextThreshold_HidesAgeToo() =>
        Assert.Equal(
            new[] { DetailsColumn.Name, DetailsColumn.Size },
            AdaptiveColumns.VisibleForWidth(393));

    [Fact]
    public void AtTheSizeThreshold_OnlyNameAndSizeRemain() =>
        Assert.Equal(
            new[] { DetailsColumn.Name, DetailsColumn.Size },
            AdaptiveColumns.VisibleForWidth(284));

    [Fact]
    public void BelowEveryThreshold_NameStillNeverHides() =>
        Assert.Equal(new[] { DetailsColumn.Name }, AdaptiveColumns.VisibleForWidth(283));

    [Fact]
    public void ZeroWidth_StillShowsName() =>
        Assert.Equal(new[] { DetailsColumn.Name }, AdaptiveColumns.VisibleForWidth(0));

    [Fact]
    public void VisibleForWidth_AlwaysReturnsCanonicalOrder()
    {
        foreach (var width in new[] { 2000.0, 524, 400, 300, 100 })
        {
            var visible = AdaptiveColumns.VisibleForWidth(width);
            Assert.Equal(visible.OrderBy(column => (int)column), visible);
        }
    }

    [Fact]
    public void Snap_ReachableSetsPassThrough()
    {
        Assert.Equal(All, AdaptiveColumns.SnapToReachableSet(All));
        Assert.True(AdaptiveColumns.IsReachableSet([DetailsColumn.Name, DetailsColumn.Size]));
        Assert.False(AdaptiveColumns.IsReachableSet([DetailsColumn.Name, DetailsColumn.Age]));
    }

    [Fact]
    public void Snap_UnreachableSetsFallToTheWidestContainedReachableSet()
    {
        // Age without Size is unreachable: the widest reachable subset is Name alone.
        Assert.Equal(
            new[] { DetailsColumn.Name },
            AdaptiveColumns.SnapToReachableSet([DetailsColumn.Name, DetailsColumn.Age]));
        // Size + Modified without Age snaps to Name + Size.
        Assert.Equal(
            new[] { DetailsColumn.Name, DetailsColumn.Size },
            AdaptiveColumns.SnapToReachableSet([DetailsColumn.Name, DetailsColumn.Size, DetailsColumn.Modified]));
    }
}
