using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class SnapshotExportReceiptTests
{
    [Fact]
    public void ReceiptPreservesExportIdentityAndByteCount()
    {
        var start = DateTimeOffset.UtcNow;
        var receipt = new SnapshotExportReceipt(Guid.NewGuid(), "snapshot-42",
            @"C:\Documents\example.txt", @"D:\Recovered\example.txt", 4096,
            start, start.AddSeconds(2));
        Assert.NotEqual(Guid.Empty, receipt.Id);
        Assert.Equal("snapshot-42", receipt.SnapshotId);
        Assert.Equal(4096, receipt.BytesCopied);
        Assert.True(receipt.FinishedAt >= receipt.StartedAt);
    }

    [Fact]
    public async Task ExportIsWindowsOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        var snapshot = new ShadowCopy("snapshot-1", @"C:\",
            @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1", DateTimeOffset.UtcNow, "12");
        var exporter = new SnapshotFileExporter();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            exporter.ExportAsync(snapshot, @"C:\example.txt", @"D:\example.txt"));
    }
}
