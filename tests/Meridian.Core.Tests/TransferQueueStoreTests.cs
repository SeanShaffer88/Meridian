using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class TransferQueueStoreTests
{
    [Fact]
    public async Task RoundTrip_PreservesQueuedJobsAndTheirItems()
    {
        var folder = NewFolder();
        try
        {
            var store = new TransferQueueStore(folder);
            var job = TransferJob.Create(TransferJobKind.Copy, ["src"], "dest", CollisionPolicy.KeepBoth, verifyAfterCopy: true);
            job.Items.Add(new TransferItem
            {
                SourcePath = "src/a.txt",
                DestinationPath = "dest/a.txt",
                Size = 42,
                State = TransferItemState.WaitingOnConflict,
            });
            await store.EnqueueAsync(job);

            var reloaded = new TransferQueueStore(folder);
            await reloaded.LoadAsync();

            var loaded = Assert.Single(reloaded.Jobs);
            Assert.Equal(job.Id, loaded.Id);
            Assert.Equal(TransferJobKind.Copy, loaded.Kind);
            Assert.Equal(CollisionPolicy.KeepBoth, loaded.CollisionPolicy);
            Assert.True(loaded.VerifyAfterCopy);
            Assert.Equal(TransferJobState.Queued, loaded.State);
            var item = Assert.Single(loaded.Items);
            Assert.Equal("src/a.txt", item.SourcePath);
            Assert.Equal(42, item.Size);
            Assert.Equal(TransferItemState.WaitingOnConflict, item.State);
            Assert.Empty(reloaded.History);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Load_JobSavedWhileRunningComesBackPausedAndItsItemPending()
    {
        var folder = NewFolder();
        try
        {
            var store = new TransferQueueStore(folder);
            var job = TransferJob.Create(TransferJobKind.Move, ["src"], "dest");
            job.State = TransferJobState.Running;
            job.StartedUtc = DateTimeOffset.UtcNow;
            job.Items.Add(new TransferItem
            {
                SourcePath = "src/a.txt",
                DestinationPath = "dest/a.txt",
                State = TransferItemState.InProgress,
                BytesCopied = 10,
            });
            await store.EnqueueAsync(job);

            var reloaded = new TransferQueueStore(folder);
            await reloaded.LoadAsync();

            var loaded = Assert.Single(reloaded.Jobs);
            Assert.Equal(TransferJobState.Paused, loaded.State);
            var item = Assert.Single(loaded.Items);
            Assert.Equal(TransferItemState.Pending, item.State);
            Assert.Equal(0, item.BytesCopied);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Load_DamagedFilesReadAsEmpty()
    {
        var folder = NewFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "transfer-queue.json"), "{ this is not json");
            await File.WriteAllTextAsync(Path.Combine(folder, "transfer-history.json"), "[[[");

            var store = new TransferQueueStore(folder);
            await store.LoadAsync();

            Assert.Empty(store.Jobs);
            Assert.Empty(store.History);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RecordCompletion_MovesTheJobFromQueueToHistory()
    {
        var folder = NewFolder();
        try
        {
            var store = new TransferQueueStore(folder);
            var job = TransferJob.Create(TransferJobKind.Copy, ["src"], "dest");
            await store.EnqueueAsync(job);
            job.TransitionTo(TransferJobState.Running);
            job.TransitionTo(TransferJobState.Completed);

            var record = await store.RecordCompletionAsync(job);

            Assert.Empty(store.Jobs);
            var saved = Assert.Single(store.History);
            Assert.Equal(record.Id, saved.Id);
            Assert.Equal(job.Id, saved.JobId);
            Assert.Equal(TransferJobState.Completed, saved.FinalState);

            var reloaded = new TransferQueueStore(folder);
            await reloaded.LoadAsync();
            Assert.Empty(reloaded.Jobs);
            Assert.Single(reloaded.History);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task History_IsCappedAtTheMaximumWithTheNewestKept()
    {
        var folder = NewFolder();
        try
        {
            var store = new TransferQueueStore(folder);
            var total = TransferQueueStore.MaximumHistoryEntries + 5;
            var ids = new List<Guid>();
            for (var i = 0; i < total; i++)
            {
                var job = TransferJob.Create(TransferJobKind.Copy, ["src"], "dest");
                job.State = TransferJobState.Completed;
                job.FinishedUtc = DateTimeOffset.UtcNow;
                ids.Add(job.Id);
                await store.RecordCompletionAsync(job);
            }

            Assert.Equal(TransferQueueStore.MaximumHistoryEntries, store.History.Count);
            Assert.Equal(ids[^1], store.History[0].JobId); // most recent first
            Assert.Equal(ids[5], store.History[^1].JobId); // the five oldest were dropped
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"meridian-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
