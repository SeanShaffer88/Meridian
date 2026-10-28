using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class TransferModelTests
{
    [Theory]
    [InlineData(TransferJobState.Queued, TransferJobState.Running)]
    [InlineData(TransferJobState.Queued, TransferJobState.Cancelled)]
    [InlineData(TransferJobState.Running, TransferJobState.Paused)]
    [InlineData(TransferJobState.Running, TransferJobState.WaitingOnConflict)]
    [InlineData(TransferJobState.Running, TransferJobState.Completed)]
    [InlineData(TransferJobState.Running, TransferJobState.CompletedWithErrors)]
    [InlineData(TransferJobState.Running, TransferJobState.Cancelled)]
    [InlineData(TransferJobState.Running, TransferJobState.Failed)]
    [InlineData(TransferJobState.Paused, TransferJobState.Running)]
    [InlineData(TransferJobState.Paused, TransferJobState.Cancelled)]
    [InlineData(TransferJobState.WaitingOnConflict, TransferJobState.Running)]
    [InlineData(TransferJobState.WaitingOnConflict, TransferJobState.Paused)]
    [InlineData(TransferJobState.WaitingOnConflict, TransferJobState.Cancelled)]
    public void LegalTransitionsAreAccepted(TransferJobState from, TransferJobState to)
    {
        Assert.True(TransferJob.CanTransition(from, to));
        var job = new TransferJob { State = from };
        job.TransitionTo(to);
        Assert.Equal(to, job.State);
    }

    [Theory]
    [InlineData(TransferJobState.Queued, TransferJobState.Completed)]
    [InlineData(TransferJobState.Queued, TransferJobState.Paused)]
    [InlineData(TransferJobState.Queued, TransferJobState.Failed)]
    [InlineData(TransferJobState.Paused, TransferJobState.Completed)]
    [InlineData(TransferJobState.Paused, TransferJobState.WaitingOnConflict)]
    [InlineData(TransferJobState.WaitingOnConflict, TransferJobState.Completed)]
    [InlineData(TransferJobState.Completed, TransferJobState.Running)]
    [InlineData(TransferJobState.CompletedWithErrors, TransferJobState.Running)]
    [InlineData(TransferJobState.Cancelled, TransferJobState.Running)]
    [InlineData(TransferJobState.Failed, TransferJobState.Running)]
    public void IllegalTransitionsAreRejected(TransferJobState from, TransferJobState to)
    {
        Assert.False(TransferJob.CanTransition(from, to));
        var job = new TransferJob { State = from };
        Assert.Throws<InvalidOperationException>(() => job.TransitionTo(to));
        Assert.Equal(from, job.State);
    }

    [Fact]
    public void TransitionToStampsStartAndFinish()
    {
        var job = TransferJob.Create(TransferJobKind.Copy, ["a"], "b");
        Assert.Null(job.StartedUtc);
        Assert.Null(job.FinishedUtc);

        job.TransitionTo(TransferJobState.Running);
        Assert.NotNull(job.StartedUtc);
        Assert.Null(job.FinishedUtc);

        job.TransitionTo(TransferJobState.Paused);
        job.TransitionTo(TransferJobState.Running);
        job.TransitionTo(TransferJobState.Completed);
        Assert.NotNull(job.FinishedUtc);
        Assert.True(job.IsTerminal);
    }

    [Fact]
    public void TotalsCountFileBytesAndCompletedItems()
    {
        var job = TransferJob.Create(TransferJobKind.Copy, ["a"], "b");
        job.Items.Add(new TransferItem { SourcePath = "a", Size = 100, IsDirectory = false, State = TransferItemState.Completed, BytesCopied = 100 });
        job.Items.Add(new TransferItem { SourcePath = "dir", IsDirectory = true, State = TransferItemState.Completed });
        job.Items.Add(new TransferItem { SourcePath = "c", Size = 50, IsDirectory = false, BytesCopied = 20 });

        Assert.Equal(150, job.TotalBytes);
        Assert.Equal(120, job.BytesCopied);
        Assert.Equal(3, job.TotalItems);
        Assert.Equal(2, job.CompletedItems);
    }

    [Fact]
    public void ResolveConflict_RequiresAWaitingItem()
    {
        var item = new TransferItem { State = TransferItemState.Pending };
        Assert.Throws<InvalidOperationException>(() =>
            item.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Skip, null, "test")));
    }

    [Fact]
    public void ResolveConflict_SkipMarksTheItemSkipped()
    {
        var item = new TransferItem { State = TransferItemState.WaitingOnConflict };
        item.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Skip, null, "test"));
        Assert.Equal(TransferItemState.Skipped, item.State);
        Assert.Equal(CollisionDecisionKind.Skip, item.CollisionOverride);
    }

    [Fact]
    public void ResolveConflict_ReplaceRequeuesTheItem()
    {
        var item = new TransferItem { State = TransferItemState.WaitingOnConflict, DestinationPath = "dest.txt" };
        item.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Replace, null, "test"));
        Assert.Equal(TransferItemState.Pending, item.State);
        Assert.Equal(CollisionDecisionKind.Replace, item.CollisionOverride);
        Assert.Equal("dest.txt", item.DestinationPath);
    }

    [Fact]
    public void ResolveConflict_RenameNeedsAndAppliesTheNewPath()
    {
        var item = new TransferItem { State = TransferItemState.WaitingOnConflict, DestinationPath = "dest.txt" };
        Assert.Throws<ArgumentException>(() =>
            item.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Rename, null, "test")));

        item.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Rename, "dest (2).txt", "test"));
        Assert.Equal(TransferItemState.Pending, item.State);
        Assert.Equal("dest (2).txt", item.DestinationPath);
        Assert.Equal(CollisionDecisionKind.Rename, item.CollisionOverride);
    }
}
