using Meridian.Core.StorageReclaimer;

namespace Meridian.Core.Tests;

public class TemporaryFileInventoryTests
{
    [Fact]
    public async Task ScansOnlyImmediateFilesWithoutChangingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "sample.bin");
            await File.WriteAllBytesAsync(file, new byte[37]);
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
            var child = Path.Combine(nested.FullName, "child.bin");
            await File.WriteAllBytesAsync(child, new byte[99]);

            var plan = await new TemporaryFileInventory().ScanAsync(root);
            Assert.Single(plan.Candidates);
            Assert.Equal(37, plan.ReviewableBytes);
            Assert.Equal(CleanupRisk.ReviewRequired, plan.Candidates[0].Risk);
            Assert.True(File.Exists(file));
            Assert.True(File.Exists(child));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EnforcesMaximumEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "first"), "1");
            await File.WriteAllTextAsync(Path.Combine(root, "second"), "2");
            await Assert.ThrowsAsync<IOException>(() =>
                new TemporaryFileInventory().ScanAsync(root, maxEntries: 1));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsInvalidEntryLimit()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new TemporaryFileInventory().ScanAsync(Path.GetTempPath(), maxEntries: 0));
    }

    [Fact]
    public async Task RespectsPreCancelledToken()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TemporaryFileInventory().ScanAsync(Path.GetTempPath(), cancellationToken: source.Token));
    }
}
