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
}
