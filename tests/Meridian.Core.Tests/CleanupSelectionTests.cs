using Meridian.Core.StorageReclaimer;

namespace Meridian.Core.Tests;

public class CleanupSelectionTests
{
    private static CleanupPlan Example => new(new[]
    {
        new CleanupCandidate("temp-a", 10, "Temp", CleanupRisk.ReviewRequired, "Review first"),
        new CleanupCandidate("protected-b", 20, "Documents", CleanupRisk.Protected, "Personal data")
    });

    [Fact]
    public void AcceptsKnownReviewableItemWithoutChangingPlan()
    {
        var selected = CleanupSelection.ReviewOnly(Example, new[] { "temp-a" });
        Assert.Single(selected);
        Assert.Equal("temp-a", selected[0].FullPath);
    }

    [Fact]
    public void RejectsProtectedItem()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CleanupSelection.ReviewOnly(Example, new[] { "protected-b" }));
    }

    [Fact]
    public void RejectsUninventoriedItem()
    {
        Assert.Throws<ArgumentException>(() =>
            CleanupSelection.ReviewOnly(Example, new[] { "unknown" }));
    }

    [Fact]
    public void RejectsDuplicateSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            CleanupSelection.ReviewOnly(Example, new[] { "temp-a", "temp-a" }));
    }
    [Fact]
    public void RejectsDuplicateInventoryTargets()
    {
        Assert.Throws<ArgumentException>(() => new CleanupPlan(new[]
        {
            new CleanupCandidate("duplicate", 1, "Temp", CleanupRisk.Low, "Review"),
            new CleanupCandidate("duplicate", 2, "Temp", CleanupRisk.Protected, "Protected")
        }));
    }

    [Fact]
    public void SelectionCannotBeModifiedByCaller()
    {
        var selected = CleanupSelection.ReviewOnly(Example, new[] { "temp-a" });
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CleanupCandidate>)selected).Clear());
    }


}
