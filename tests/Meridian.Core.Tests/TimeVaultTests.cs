using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class TimeVaultTests
{
    private static ShadowCopy Snapshot() => new(
        "snapshot-1", @"C:\", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy12",
        DateTimeOffset.UtcNow, "Created");

    [Fact]
    public void SnapshotMetadataRecognizesVssDevice()
    {
        Assert.True(Snapshot().IsUsable);
        Assert.False((Snapshot() with { DeviceObject = @"C:\Windows" }).IsUsable);
    }

    [Fact]
    public void ResolveRejectsDifferentVolume()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => ShadowPath.Resolve(Snapshot(), @"D:\data\file.txt"));
    }

    [Fact]
    public void ResolveRejectsRelativePath()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => ShadowPath.Resolve(Snapshot(), @"data\file.txt"));
    }

    [Fact]
    public void ResolveMapsFileInsideVolume()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy12\data\file.txt",
            ShadowPath.Resolve(Snapshot(), @"C:\data\file.txt"));
    }

    [Fact]
    public void ResolveRejectsNetworkPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => ShadowPath.Resolve(Snapshot(), @"\\server\share\file.txt"));
    }
}
