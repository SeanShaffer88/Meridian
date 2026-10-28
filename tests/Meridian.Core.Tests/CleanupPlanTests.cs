using Meridian.Core.StorageReclaimer;

namespace Meridian.Core.Tests;

public class CleanupPlanTests
{
    [Fact]
    public void KeepsProtectedCandidatesOutOfReviewableTotal()
    {
        var plan = new CleanupPlan(new[]
        {
            new CleanupCandidate(@"C:\Temp\cache.tmp", 100, "Temporary files", CleanupRisk.Low, "Temporary"),
            new CleanupCandidate(@"C:\Users\Test\Downloads\file.zip", 200, "Downloads", CleanupRisk.Protected, "Personal file"),
            new CleanupCandidate(@"C:\Temp\old.log", 50, "Logs", CleanupRisk.ReviewRequired, "Check first")
        });

        Assert.Equal(150, plan.ReviewableBytes);
        Assert.Equal(200, plan.ProtectedBytes);
        Assert.Equal(3, plan.Candidates.Count);
    }

    [Fact]
    public void RejectsNegativeSizes()
    {
        Assert.Throws<ArgumentException>(() => new CleanupPlan(new[]
        {
            new CleanupCandidate(@"C:\Temp\a", -1, "Temp", CleanupRisk.Low, "Temporary")
        }));
    }

    [Fact]
    public void SaturatesRatherThanOverflowsLargeEstimates()
    {
        var plan = new CleanupPlan(new[]
        {
            new CleanupCandidate("a", long.MaxValue, "Temp", CleanupRisk.Low, "Temporary"),
            new CleanupCandidate("b", 100, "Temp", CleanupRisk.Low, "Temporary")
        });
        Assert.Equal(long.MaxValue, plan.ReviewableBytes);
    }

    [Fact]
    public void CopiesInputToPreventLaterMutation()
    {
        var source = new List<CleanupCandidate>
        {
            new("a", 10, "Temp", CleanupRisk.Low, "Temporary")
        };
        var plan = new CleanupPlan(source);
        source.Clear();
        Assert.Single(plan.Candidates);
    }
}
