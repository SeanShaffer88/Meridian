using Meridian.Core.Consequences;

namespace Meridian.Core.Tests;

public class ConsequencePlannerTests
{
    [Fact]
    public void DuplicateDestinationNamesAreBlocked()
    {
        var dest = Path.Combine(Path.GetTempPath(), "meridian-preview-" + Guid.NewGuid().ToString("N"));
        var plan = ConsequencePlanner.PreviewCopies(
            [Path.Combine("alpha", "same.txt"), Path.Combine("beta", "same.txt")], dest);
        Assert.Equal(1, plan.ReadyCount);
        Assert.Equal(1, plan.BlockedCount);
        Assert.False(plan.CanExecute);
        Assert.Equal(PlanDisposition.Conflict, plan.Actions[1].Disposition);
    }

    [Fact]
    public void EmptySourceIsRejected()
    {
        var plan = ConsequencePlanner.PreviewCopies([""], Path.GetTempPath());
        Assert.Equal(PlanDisposition.Invalid, plan.Actions[0].Disposition);
    }

    [Fact]
    public void EmptyBatchIsNotExecutable()
    {
        var plan = ConsequencePlanner.PreviewCopies([], Path.GetTempPath());
        Assert.False(plan.CanExecute);
    }
}
