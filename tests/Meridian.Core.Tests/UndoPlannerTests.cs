using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class UndoPlannerTests
{
    private static readonly ITransferFileOperations Files = new SystemTransferFileOperations();

    [Fact]
    public async Task CopyUndo_IsAvailableAndExecutingItDeletesOnlyWhatTheCopyCreated()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "keep.txt");
        await File.WriteAllTextAsync(source, "original");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);
        var createdPath = Path.Combine(fixture.VolumeB, "keep.txt");
        Assert.True(File.Exists(createdPath));

        var plan = UndoPlanner.Plan(record, Files);

        Assert.True(plan.IsAvailable, plan.Reason);
        Assert.NotNull(plan.InverseJob);
        Assert.Equal(TransferJobKind.Delete, plan.InverseJob!.Kind);
        Assert.Equal([createdPath], plan.InverseJob.Sources);

        await new TransferExecutor().ExecuteAsync(plan.InverseJob);
        Assert.False(File.Exists(createdPath));
        Assert.Equal("original", await File.ReadAllTextAsync(source)); // the source was never in danger
    }

    [Fact]
    public async Task CopyUndo_CreatedFileChanged_IsUnavailable()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "doc.txt");
        await File.WriteAllTextAsync(source, "v1");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);

        var createdPath = Path.Combine(fixture.VolumeB, "doc.txt");
        await File.WriteAllTextAsync(createdPath, "v1 — edited since");
        File.SetLastWriteTimeUtc(createdPath, DateTime.UtcNow.AddMinutes(5));

        var plan = UndoPlanner.Plan(record, Files);

        Assert.False(plan.IsAvailable);
        Assert.Null(plan.InverseJob);
        Assert.Contains("has changed", plan.Reason);
    }

    [Fact]
    public async Task CopyUndo_CreatedFileMissing_IsUnavailable()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "doc.txt");
        await File.WriteAllTextAsync(source, "v1");
        var job = TransferJob.Create(TransferJobKind.Copy, [source], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);
        File.Delete(Path.Combine(fixture.VolumeB, "doc.txt"));

        var plan = UndoPlanner.Plan(record, Files);

        Assert.False(plan.IsAvailable);
        Assert.Contains("no longer exists", plan.Reason);
    }

    [Fact]
    public async Task CopyUndo_NothingCompleted_IsUnavailable()
    {
        using var fixture = new Fixture();
        var missing = Path.Combine(fixture.VolumeA, "missing.txt");
        var job = TransferJob.Create(TransferJobKind.Copy, [missing], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        Assert.Equal(TransferJobState.Failed, job.State);
        var record = TransferHistoryRecord.FromJob(job);

        var plan = UndoPlanner.Plan(record, Files);

        Assert.False(plan.IsAvailable);
        Assert.Contains("Nothing was copied", plan.Reason);
    }

    [Fact]
    public async Task MoveUndo_MovesEverythingBack()
    {
        using var fixture = new Fixture();
        var tree = Path.Combine(fixture.VolumeA, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        await File.WriteAllTextAsync(Path.Combine(tree, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(tree, "sub", "two.txt"), "two");
        var job = TransferJob.Create(TransferJobKind.Move, [tree], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);
        Assert.False(Directory.Exists(tree));

        var plan = UndoPlanner.Plan(record, Files);

        Assert.True(plan.IsAvailable, plan.Reason);
        Assert.Equal(TransferJobKind.Move, plan.InverseJob!.Kind);
        await new TransferExecutor().ExecuteAsync(plan.InverseJob);

        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(tree, "one.txt")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(tree, "sub", "two.txt")));
        Assert.False(Directory.Exists(Path.Combine(fixture.VolumeB, "tree")));
    }

    [Fact]
    public async Task MoveUndo_OriginalOccupiedAgain_ComesBackUnderANumberedName()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.VolumeA, "report.txt");
        await File.WriteAllTextAsync(source, "moved-content");
        var job = TransferJob.Create(TransferJobKind.Move, [source], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);
        await File.WriteAllTextAsync(source, "someone else's new file");

        var plan = UndoPlanner.Plan(record, Files);

        Assert.True(plan.IsAvailable, plan.Reason);
        await new TransferExecutor().ExecuteAsync(plan.InverseJob!);

        Assert.Equal("someone else's new file", await File.ReadAllTextAsync(source)); // never overwritten
        Assert.Equal("moved-content",
            await File.ReadAllTextAsync(Path.Combine(fixture.VolumeA, "report (2).txt")));
        Assert.False(File.Exists(Path.Combine(fixture.VolumeB, "report.txt")));
    }

    [Fact]
    public async Task MoveUndo_OriginalFolderGone_IsUnavailable()
    {
        using var fixture = new Fixture();
        var folder = Path.Combine(fixture.VolumeA, "gone");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "doc.txt");
        await File.WriteAllTextAsync(source, "doc");
        var job = TransferJob.Create(TransferJobKind.Move, [source], fixture.VolumeB, CollisionPolicy.Skip);
        await new TransferExecutor().ExecuteAsync(job);
        var record = TransferHistoryRecord.FromJob(job);
        Directory.Delete(folder, recursive: true);

        var plan = UndoPlanner.Plan(record, Files);

        Assert.False(plan.IsAvailable);
        Assert.Contains("original folder", plan.Reason);
    }

    [Fact]
    public void DeleteRecord_IsNeverUndoable()
    {
        var record = new TransferHistoryRecord(
            Guid.NewGuid(), Guid.NewGuid(), TransferJobKind.Delete, TransferJobState.Completed,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, string.Empty,
            [new TransferHistoryItem("a.txt", null, false, 3, null, null, null, TransferItemState.Completed, null)]);

        var plan = UndoPlanner.Plan(record, Files);

        Assert.False(plan.IsAvailable);
        Assert.Contains("Recycle Bin", plan.Reason);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"meridian-undo-{Guid.NewGuid():N}");
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
}
