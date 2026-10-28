using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class SnapshotVolumeMatcherTests
{
    [Fact]
    public void DriveLetterMetadataMatchesWithoutNativeLookup()
    {
        if (!OperatingSystem.IsWindows()) return;
        var snapshot = new ShadowCopy("id", @"C:\", @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1",
            DateTimeOffset.UtcNow, "12");
        Assert.True(SnapshotVolumeMatcher.MatchesDrive(snapshot, @"C:\"));
        Assert.False(SnapshotVolumeMatcher.MatchesDrive(snapshot, @"D:\"));
    }

    [Theory]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1", true)]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy", false)]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Windows", false)]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy-1", false)]
    public void DevicePathMustBeExactSnapshotRoot(string device, bool expected)
    {
        var snapshot = new ShadowCopy("id", @"C:\", device, DateTimeOffset.UtcNow, "12");
        Assert.Equal(expected, snapshot.IsUsable);
    }
}
