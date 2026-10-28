using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class TransferExecutorTests
{
    [Fact]
    public async Task CopyTree_CopiesEverythingAndRecordsDestinationFacts()
    {
        using var fixture = new Fixture();
        var tree = Path.Combine(fixture.VolumeA, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        var top = Path.Combine(tree, "top.txt");
        var nested = Path.Combine(tree, "sub", "nested.txt");
        await File.WriteAllTextAsync(top, "top-content");
        await File.WriteAllTextAsync(nested, "nested-content");

        var job = TransferJob.Create(TransferJobKind.Copy, [tree], fixture.VolumeB, CollisionPolicy.Skip);
        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        var copiedTop = Path.Combine(fixture.VolumeB, "tree", "top.txt");
        var copiedNested = Path.Combine(fixture.VolumeB, "tree", "sub", "nested.txt");
        Assert.Equal("top-content", await File.ReadAllTextAsync(copiedTop));
        Assert.Equal("nested-content", await File.ReadAllTextAsync(copiedNested));
        Assert.True(Directory.Exists(Path.Combine(fixture.VolumeB, "tree", "empty")));
        Assert.True(File.Exists(top)); // a copy leaves the sources alone
        var topItem = result.Items.Single(item => item.SourcePath == top);
        Assert.Equal(TransferItemState.Completed, topItem.State);
        Assert.Equal(11, topItem.DestinationSize);
        Assert.NotNull(topItem.DestinationLastWriteUtc);
        Assert.NotNull(result.FinishedUtc);
    }

    [Fact]
    public async Task Copy_ReportsProgressReachingTheJobTotals()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "data.bin");
        await File.WriteAllBytesAsync(source, new byte[300_000]);
        var updates = new List<TransferProgress>();
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB);

        var result = await new TransferExecutor().ExecuteAsync(job, new SynchronousProgress<TransferProgress>(updates.Add));

        Assert.Equal(TransferJobState.Completed, result.State);
        var last = updates[^1];
        Assert.Equal(300_000, last.TotalBytes);
        Assert.Equal(300_000, last.BytesCopied);
        Assert.Equal(last.TotalItems, last.ItemsCompleted);
    }

    [Fact]
    public async Task MoveAcrossRoots_RemovesSourcesAndTheirFolders()
    {
        using var fixture = new Fixture();
        var tree = Path.Combine(fixture.VolumeA, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        await File.WriteAllTextAsync(Path.Combine(tree, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(tree, "sub", "two.txt"), "two");

        var job = TransferJob.Create(TransferJobKind.Move, [tree], fixture.VolumeB, CollisionPolicy.Skip);
        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "tree", "one.txt")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "tree", "sub", "two.txt")));
        Assert.False(Directory.Exists(tree)); // files and their now-empty folders are gone
    }

    [Fact]
    public async Task PauseResume_PausesMidFileThenCompletes()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "big.bin");
        var bytes = new byte[2 * 1024 * 1024];
        new Random(7).NextBytes(bytes);
        await File.WriteAllBytesAsync(source, bytes);

        var executor = new TransferExecutor();
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB);
        var pauseArmed = true;
        var progress = new SynchronousProgress<TransferProgress>(update =>
        {
            if (pauseArmed && update.BytesCopied > 0)
            {
                pauseArmed = false;
                executor.Pause();
            }
        });
        var run = executor.ExecuteAsync(job, progress);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (job.State != TransferJobState.Paused && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(TransferJobState.Paused, job.State);
        Assert.False(File.Exists(Path.Combine(fixture.VolumeB, "big.bin"))); // nothing published yet

        executor.Resume();
        var result = await run;

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.Equal(
            FileHash.Sha256Hex(bytes),
            await FileHash.Sha256HexAsync(Path.Combine(fixture.VolumeB, "big.bin")));
    }

    [Fact]
    public async Task CancelAfterFirstItem_PreservesCompletedAndSkipsTheRest()
    {
        using var fixture = new Fixture();
        var first = Path.Combine(fixture.VolumeA, "a.txt");
        var second = Path.Combine(fixture.VolumeA, "b.txt");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");

        var executor = new TransferExecutor();
        var job = TransferJob.Create(TransferJobKind.Copy, [first, second], fixture.VolumeB);
        var progress = new SynchronousProgress<TransferProgress>(update =>
        {
            if (update.ItemsCompleted == 1) executor.Cancel();
        });

        var result = await executor.ExecuteAsync(job, progress);

        Assert.Equal(TransferJobState.Cancelled, result.State);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "a.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.VolumeB, "b.txt")));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task CancelDuringFile_RemovesStagingAndPublishesNothing()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "big.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);

        var executor = new TransferExecutor();
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB);
        var progress = new SynchronousProgress<TransferProgress>(update =>
        {
            if (update.BytesCopied > 0) executor.Cancel();
        });

        var result = await executor.ExecuteAsync(job, progress);

        Assert.Equal(TransferJobState.Cancelled, result.State);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.VolumeB)); // no file, no .partial staging
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task CollisionSkip_LeavesTheExistingFileUntouched()
    {
        using var fixture = new Fixture();
        var (source, _) = await WritePair(fixture, "new");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.Skip);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report.txt")));
        Assert.Equal(TransferItemState.Skipped, Assert.Single(result.Items).State);
    }

    [Fact]
    public async Task CollisionReplace_OverwritesTheExistingFile()
    {
        using var fixture = new Fixture();
        var (source, _) = await WritePair(fixture, "new");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.Replace);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report.txt")));
    }

    [Fact]
    public async Task CollisionReplaceIfNewer_SourceNewerReplaces()
    {
        using var fixture = new Fixture();
        var (source, destination) = await WritePair(fixture, "new");
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(destination, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.ReplaceIfNewer);

        await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal("new", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task CollisionReplaceIfNewer_SourceOlderSkips()
    {
        using var fixture = new Fixture();
        var (source, destination) = await WritePair(fixture, "new");
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(destination, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.ReplaceIfNewer);

        await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal("old", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task CollisionKeepBoth_UsesTheNextFreeNumberedName()
    {
        using var fixture = new Fixture();
        var (source, _) = await WritePair(fixture, "new");
        await File.WriteAllTextAsync(Path.Combine(fixture.VolumeB, "report (2).txt"), "second");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.KeepBoth);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report.txt")));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report (2).txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report (3).txt")));
    }

    [Fact]
    public async Task CollisionAsk_ParksTheItemUntilTheUserResolvesIt()
    {
        using var fixture = new Fixture();
        var (conflicting, _) = await WritePair(fixture, "new");
        var plain = Path.Combine(fixture.VolumeA, "plain.txt");
        await File.WriteAllTextAsync(plain, "plain");
        var executor = new TransferExecutor();
        var job = TransferJob.Create(TransferJobKind.Copy, [conflicting, plain], fixture.VolumeB, CollisionPolicy.Ask);

        var first = await executor.ExecuteAsync(job);

        Assert.Equal(TransferJobState.WaitingOnConflict, first.State);
        Assert.Equal("plain", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "plain.txt"))); // other items kept going
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report.txt")));
        var waiting = first.Items.Single(item => item.State == TransferItemState.WaitingOnConflict);

        waiting.ResolveConflict(new CollisionDecision(CollisionDecisionKind.Replace, null, "test decision"));
        var second = await executor.ExecuteAsync(first);

        Assert.Equal(TransferJobState.Completed, second.State);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "report.txt")));
    }

    [Fact]
    public async Task Verify_CleanCopyPasses()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "data.bin");
        await File.WriteAllBytesAsync(source, new byte[500_000]);
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB,
            CollisionPolicy.Skip, verifyAfterCopy: true);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
    }

    [Fact]
    public async Task VerifyFailure_RemovesTheStagedCopyAndKeepsTheExistingDestination()
    {
        using var fixture = new Fixture();
        var (source, destination) = await WritePair(fixture, "new");
        var executor = new TransferExecutor(new CorruptingStagingReadOps(new SystemTransferFileOperations()));
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB,
            CollisionPolicy.Replace, verifyAfterCopy: true);

        var result = await executor.ExecuteAsync(job);

        Assert.Equal(TransferJobState.Failed, result.State);
        var item = Assert.Single(result.Items);
        Assert.Equal(TransferItemState.Failed, item.State);
        Assert.Contains("SHA-256", item.Error);
        Assert.Equal("old", await File.ReadAllTextAsync(destination)); // replace never published
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MoveVerifyFailure_KeepsTheSource()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "move-me.bin");
        await File.WriteAllBytesAsync(source, new byte[10_000]);
        var executor = new TransferExecutor(new CorruptingStagingReadOps(new SystemTransferFileOperations()));
        var job = TransferJob.Create(TransferJobKind.Move, [source], fixture.VolumeB,
            CollisionPolicy.Skip, verifyAfterCopy: true);

        var result = await executor.ExecuteAsync(job);

        Assert.Equal(TransferJobState.Failed, result.State);
        Assert.True(File.Exists(source)); // a failed verify never costs the original
        Assert.False(File.Exists(Path.Combine(fixture.VolumeB, "move-me.bin")));
    }

    [Fact]
    public async Task MoveDeleteFailure_ReportsCopiedButNotRemoved()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "stuck.txt");
        await File.WriteAllTextAsync(source, "stuck");
        var executor = new TransferExecutor(
            new DeleteRefusingOps(new SystemTransferFileOperations(), source));
        var job = TransferJob.Create(TransferJobKind.Move, [source], fixture.VolumeB, CollisionPolicy.Skip);

        var result = await executor.ExecuteAsync(job);

        var item = Assert.Single(result.Items);
        Assert.Equal(TransferItemState.Failed, item.State);
        Assert.Contains("could not be removed", item.Error);
        Assert.Equal("stuck", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "stuck.txt")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task OneMissingSource_DoesNotAbortTheJob()
    {
        using var fixture = new Fixture();
        var good = Path.Combine(fixture.VolumeA, "good.txt");
        await File.WriteAllTextAsync(good, "good");
        var missing = Path.Combine(fixture.VolumeA, "missing.txt");
        var job = TransferJob.Create(TransferJobKind.Copy, [good, missing], fixture.VolumeB, CollisionPolicy.Skip);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.CompletedWithErrors, result.State);
        Assert.Equal("good", await File.ReadAllTextAsync(Path.Combine(fixture.VolumeB, "good.txt")));
        var failed = result.Items.Single(item => item.SourcePath == missing);
        Assert.Equal(TransferItemState.Failed, failed.State);
        Assert.Contains("no longer exists", failed.Error);
    }

    [Fact]
    public async Task DeleteJob_RemovesTheFile()
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.VolumeA, "delete-me.txt");
        await File.WriteAllTextAsync(target, "bye");
        var job = TransferJob.Create(TransferJobKind.Delete, [target], string.Empty);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Completed, result.State);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task CopyFolderIntoItself_FailsWithoutCopying()
    {
        using var fixture = new Fixture();
        var tree = Path.Combine(fixture.VolumeA, "tree");
        var inner = Path.Combine(tree, "inner");
        Directory.CreateDirectory(inner);
        await File.WriteAllTextAsync(Path.Combine(tree, "a.txt"), "a");
        var job = TransferJob.Create(TransferJobKind.Copy, [tree], inner, CollisionPolicy.Skip);

        var result = await new TransferExecutor().ExecuteAsync(job);

        Assert.Equal(TransferJobState.Failed, result.State);
        Assert.Empty(Directory.EnumerateFileSystemEntries(inner));
    }

    /// <summary>Writes report.txt with new content in volume A and old content in volume B.</summary>
    private static async Task<(string Source, string Destination)> WritePair(Fixture fixture, string sourceContent)
    {
        var source = Path.Combine(fixture.VolumeA, "report.txt");
        var destination = Path.Combine(fixture.VolumeB, "report.txt");
        await File.WriteAllTextAsync(source, sourceContent);
        await File.WriteAllTextAsync(destination, "old");
        return (source, destination);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"meridian-transfer-{Guid.NewGuid():N}");
            VolumeA = Path.Combine(Root, "volume-a");
            VolumeB = Path.Combine(Root, "volume-b");
            Directory.CreateDirectory(VolumeA);
            Directory.CreateDirectory(VolumeB);
        }

        public string Root { get; }
        public string VolumeA { get; }
        public string VolumeB { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private class DelegatingOps(ITransferFileOperations inner) : ITransferFileOperations
    {
        public virtual bool FileExists(string path) => inner.FileExists(path);
        public virtual bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public virtual FileFacts? GetFileFacts(string path) => inner.GetFileFacts(path);
        public virtual IEnumerable<string> EnumerateFiles(string directoryPath) => inner.EnumerateFiles(directoryPath);
        public virtual IEnumerable<string> EnumerateDirectories(string directoryPath) => inner.EnumerateDirectories(directoryPath);
        public virtual Stream OpenRead(string path) => inner.OpenRead(path);
        public virtual Stream CreateFile(string path) => inner.CreateFile(path);
        public virtual void CreateDirectory(string path) => inner.CreateDirectory(path);
        public virtual void PublishFile(string stagingPath, string destinationPath, bool overwrite) =>
            inner.PublishFile(stagingPath, destinationPath, overwrite);
        public virtual void DeleteFile(string path) => inner.DeleteFile(path);
        public virtual void DeleteDirectory(string path, bool recursive) => inner.DeleteDirectory(path, recursive);
        public virtual void SetLastWriteUtc(string path, DateTimeOffset lastWriteUtc) => inner.SetLastWriteUtc(path, lastWriteUtc);
    }

    /// <summary>Corrupts the first byte of anything read back from a staging (.partial) file,
    /// so verify-after-copy must fail even though the bytes on disk were written cleanly.</summary>
    private sealed class CorruptingStagingReadOps(ITransferFileOperations inner) : DelegatingOps(inner)
    {
        public override Stream OpenRead(string path) =>
            path.Contains(".partial", StringComparison.Ordinal)
                ? new CorruptFirstByteStream(base.OpenRead(path))
                : base.OpenRead(path);
    }

    private sealed class DeleteRefusingOps(ITransferFileOperations inner, string refusedPath) : DelegatingOps(inner)
    {
        public override void DeleteFile(string path)
        {
            if (string.Equals(path, refusedPath, StringComparison.Ordinal))
                throw new IOException("Injected delete refusal.");
            base.DeleteFile(path);
        }
    }

    private sealed class CorruptFirstByteStream(Stream inner) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Corrupt(inner.Read(buffer, offset, count), buffer.AsSpan(offset));

        public override int Read(Span<byte> buffer) => Corrupt(inner.Read(buffer), buffer);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Corrupt(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken), buffer.AsSpan(offset));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Corrupt(await inner.ReadAsync(buffer, cancellationToken), buffer.Span);

        private int Corrupt(int read, Span<byte> buffer)
        {
            if (read > 0 && _position == 0) buffer[0] ^= 0xFF;
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
