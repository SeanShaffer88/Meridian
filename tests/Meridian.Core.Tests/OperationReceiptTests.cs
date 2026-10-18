using Meridian.Core.Consequences;

namespace Meridian.Core.Tests;

public class OperationReceiptTests
{
    [Fact]
    public void CountsObservedOutcomesOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var receipt = new OperationReceipt(
            Guid.NewGuid(), now, now,
            [
                new(PlannedAction.Copy, "a", "b", OperationOutcome.Completed, null),
                new(PlannedAction.Copy, "c", "d", OperationOutcome.Failed, "Access denied"),
                new(PlannedAction.Copy, "e", "f", OperationOutcome.Skipped, null)
            ]);
        Assert.Equal(1, receipt.CompletedCount);
        Assert.Equal(1, receipt.FailedCount);
    }
}
