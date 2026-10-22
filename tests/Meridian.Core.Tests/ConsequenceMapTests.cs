using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class ConsequenceMapTests
{
    [Fact]
    public void Explain_GroupsRisksWithoutTouchingDisk()
    {
        var items = new[]
        {
            new OperationPreview.Item("a", "x", OperationPreview.Status.Ready, "ready"),
            new OperationPreview.Item("b", "x", OperationPreview.Status.DestinationExists, "collision"),
            new OperationPreview.Item("c", null, OperationPreview.Status.InvalidPath, "invalid"),
            new OperationPreview.Item("d", "x", OperationPreview.Status.DestinationExists, "collision")
        };
        var plan = new OperationPreview.Plan("destination", items);
        var summary = ConsequenceMap.Explain(plan, displayLimit: 2);

        Assert.Equal(4, summary.TotalItems);
        Assert.Equal(1, summary.ReadyItems);
        Assert.Equal(3, summary.BlockedItems);
        Assert.Equal(2, summary.OmittedItems);
        Assert.Equal(2, summary.DisplayedItems.Count);
        Assert.Equal(2, summary.Risks.Single(group => group.Status == OperationPreview.Status.DestinationExists).Count);
        Assert.Equal(1, summary.Risks.Single(group => group.Status == OperationPreview.Status.InvalidPath).Count);
        Assert.False(summary.SafeToExecuteWithoutRecheck);
    }

    [Fact]
    public void Explain_ReadyPreviewIsStillNotAnExecutionGuarantee()
    {
        var plan = new OperationPreview.Plan("destination", new[]
        {
            new OperationPreview.Item("source", "target", OperationPreview.Status.Ready, "No conflict detected.")
        });

        var summary = ConsequenceMap.Explain(plan);

        Assert.Equal(0, summary.BlockedItems);
        Assert.False(summary.SafeToExecuteWithoutRecheck);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Explain_RejectsUnboundedDisplayRequest(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConsequenceMap.Explain(new OperationPreview.Plan("destination", Array.Empty<OperationPreview.Item>()), count));
    }
}
