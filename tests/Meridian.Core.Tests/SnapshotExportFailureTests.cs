using Meridian.Core.TimeVault;

namespace Meridian.Core.Tests;

public class SnapshotExportFailureTests
{
    private static ShadowCopy Example => new("snapshot-1", @"C:\",
        @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1", DateTimeOffset.UtcNow, "12");

    [Fact]
    public async Task CancellationBeforeExportDoesNotCreateDestination()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var destination = Path.Combine(Path.GetTempPath(), $"meridian-{Guid.NewGuid():N}.tmp");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SnapshotFileExporter().ExportAsync(Example, @"C:\not-present.txt", destination, cancellation.Token));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task RejectsRelativeDestinationOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SnapshotFileExporter().ExportAsync(Example, @"C:\source.txt", "relative.txt"));
    }

    [Fact]
    public async Task RejectsUncDestinationOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SnapshotFileExporter().ExportAsync(Example, @"C:\source.txt", @"\\server\share\recovery.txt"));
    }

    [Theory]
    [InlineData(@"C:\Recovered\file.txt:hidden")]
    [InlineData(@"C:\Recovered\CON.txt")]
    [InlineData(@"C:\Recovered\LPT1.txt")]
    [InlineData(@"C:\Recovered\trailing.")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Recovered\file.txt:stream")]
    [InlineData(@"C:\Recovered\AUX.log")]
    [InlineData(@"C:\Recovered\\file.txt")]
    [InlineData(@"C:\Recovered\\")]
    [InlineData(@"C:\Recovered\..\other.txt")]
    [InlineData(@"C:\Recovered\.\other.txt")]
    public async Task RejectsUnsafeWindowsDestinationNames(string destination)
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SnapshotFileExporter().ExportAsync(Example, @"C:\source.txt", destination));
    }
    [Theory]
    [InlineData(@"C:\Recovered\..\source.txt")]
    [InlineData(@"C:\Recovered\.\source.txt")]
    [InlineData(@"\\server\share\source.txt")]
    [InlineData("source.txt")]
    [InlineData(@"C:\Recovered\file.txt:stream")]
    [InlineData(@"C:\Recovered\trailing.")]
    public async Task RejectsUnsafeOriginalSourcePaths(string source)
    {
        if (!OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SnapshotFileExporter().ExportAsync(Example, source, @"C:\Recovered\safe.txt"));
    }

}
