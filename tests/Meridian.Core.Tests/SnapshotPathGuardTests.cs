using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class SnapshotPathGuardTests
{
    private static ShadowCopy Snapshot => new("test", @"C:\",
        @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7", DateTimeOffset.UtcNow, "12");

    [Fact]
    public void RejectsPathsOutsideSnapshotBeforeAccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() =>
            SnapshotPathGuard.RejectReparsePoints(Snapshot,
                @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy70\Windows"));
    }

    [Fact]
    public void RejectsUnrecognizedSnapshotBeforeAccess()
    {
        var invalid = Snapshot with { DeviceObject = @"C:\Windows" };
        Assert.Throws<ArgumentException>(() =>
            SnapshotPathGuard.RejectReparsePoints(invalid, @"C:\Windows\test.txt"));
    }

    [Fact]
    public void RejectsNonWindowsInspection()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() =>
            SnapshotPathGuard.RejectReparsePoints(Snapshot,
                @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7\file.txt"));
    }
}
