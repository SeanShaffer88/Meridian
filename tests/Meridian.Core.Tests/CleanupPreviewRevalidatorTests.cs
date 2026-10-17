using Meridian.Core.StorageReclaimer;

namespace Meridian.Core.Tests;

public class CleanupPreviewRevalidatorTests
{
    private static CleanupCandidate Candidate(long bytes) =>
        new("sample", bytes, "Temporary", CleanupRisk.ReviewRequired, "Review");

    [Fact]
    public void AcceptsMatchingAdvisoryInventory()
    {
        var original = new CleanupPlan(new[] { Candidate(10) });
        var preview = CleanupPreviewBuilder.Build(original, new[] { "sample" });
        Assert.True(CleanupPreviewRevalidator.Matches(preview, new CleanupPlan(new[] { Candidate(10) })));
    }

    [Fact]
    public void RejectsChangedSize()
    {
        var preview = CleanupPreviewBuilder.Build(new CleanupPlan(new[] { Candidate(10) }), new[] { "sample" });
        Assert.False(CleanupPreviewRevalidator.Matches(preview, new CleanupPlan(new[] { Candidate(11) })));
    }

    [Fact]
    public void RejectsMissingFile()
    {
        var preview = CleanupPreviewBuilder.Build(new CleanupPlan(new[] { Candidate(10) }), new[] { "sample" });
        Assert.False(CleanupPreviewRevalidator.Matches(preview, new CleanupPlan(Array.Empty<CleanupCandidate>())));
    }

    [Fact]
    public void RejectsChangedRisk()
    {
        var preview = CleanupPreviewBuilder.Build(new CleanupPlan(new[] { Candidate(10) }), new[] { "sample" });
        var protectedPlan = new CleanupPlan(new[]
        {
            Candidate(10) with { Risk = CleanupRisk.Protected }
        });
        Assert.False(CleanupPreviewRevalidator.Matches(preview, protectedPlan));
    }
    [Fact]
    public void RejectsTamperedPreviewTotal()
    {
        var plan = new CleanupPlan(new[] { Candidate(10) });
        var preview = CleanupPreviewBuilder.Build(plan, new[] { "sample" }) with { EstimatedBytes = 999 };
        Assert.False(CleanupPreviewRevalidator.Matches(preview, plan));
    }

    [Fact]
    public void RejectsTamperedReviewCount()
    {
        var plan = new CleanupPlan(new[] { Candidate(10) });
        var preview = CleanupPreviewBuilder.Build(plan, new[] { "sample" }) with { ReviewRequiredCount = 0 };
        Assert.False(CleanupPreviewRevalidator.Matches(preview, plan));
    }

    [Fact]
    public void RejectsInjectedProtectedPreviewItem()
    {
        var plan = new CleanupPlan(new[]
        {
            Candidate(10) with { Risk = CleanupRisk.Protected }
        });
        var forged = new CleanupPreview(1, 10, 0, plan.Candidates, "Preview");
        Assert.False(CleanupPreviewRevalidator.Matches(forged, plan));
    }


}
