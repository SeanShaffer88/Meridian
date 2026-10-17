using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class SnapshotDirectoryReaderInputTests
{
    private static readonly ShadowCopy Snapshot = new(
        "test", @"C:\", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7",
        DateTimeOffset.UtcNow, "12");

    [Theory]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"C:\folder\..\Windows")]
    [InlineData(@"C:\folder\file:stream")]
    [InlineData(@"C:\folder.\subfolder")]
    [InlineData(@"C:\folder\\subfolder")]
    [InlineData("relative-folder")]
    public async Task RejectsUnsafeFolderBeforeSnapshotAccess(string folder)
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SnapshotDirectoryReader().ListAsync(Snapshot, folder));
    }
}
