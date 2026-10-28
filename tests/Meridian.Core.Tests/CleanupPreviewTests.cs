using Meridian.Core.StorageReclaimer;

namespace Meridian.Core.Tests;

public class CleanupPreviewTests
{
    [Fact]
    public void PreviewShowsExactScopeAndRiskWithoutExecuting()
    {
        var plan = new CleanupPlan(new[]
        {
            new CleanupCandidate("first", 15, "Temp", CleanupRisk.Low, "Cache"),
            new CleanupCandidate("second", 25, "Logs", CleanupRisk.ReviewRequired, "Review"),
            new CleanupCandidate("personal", 90, "Documents", CleanupRisk.Protected, "Never auto-delete")
        });
        var preview = CleanupPreviewBuilder.Build(plan, new[] { "first", "second" });
        Assert.Equal(2, preview.ItemCount);
        Assert.Equal(40, preview.EstimatedBytes);
        Assert.Equal(1, preview.ReviewRequiredCount);
        Assert.DoesNotContain(preview.Items, item => item.FullPath == "personal");
        Assert.Contains("Nothing has been deleted", preview.Warning);
    }

    [Fact]
    public void EmptySelectionHasZeroEstimate()
    {
        var preview = CleanupPreviewBuilder.Build(new CleanupPlan(Array.Empty<CleanupCandidate>()), Array.Empty<string>());
        Assert.Equal(0, preview.ItemCount);
        Assert.Equal(0, preview.EstimatedBytes);
    }

    [Fact]
    public void ProtectedSelectionFailsClosed()
    {
        var plan = new CleanupPlan(new[]
        {
            new CleanupCandidate("personal", 90, "Documents", CleanupRisk.Protected, "Never auto-delete")
        });
        Assert.Throws<InvalidOperationException>(() =>
            CleanupPreviewBuilder.Build(plan, new[] { "personal" }));
    }
}
