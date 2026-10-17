using Meridian.Core.Viewer;

namespace Meridian.Core.Tests;

public class SafeFilePreviewTests
{
    [Fact]
    public async Task ReadsOnlyRequestedPrefixWithoutModifyingFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });
            var result = await SafeFilePreview.ReadPrefixAsync(path, maxBytes: 3);
            Assert.Equal(new byte[] { 1, 2, 3 }, result);
            Assert.Equal(5, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HexDumpFormatsBytes()
    {
        Assert.Contains("00 FF", SafeFilePreview.HexDump(new byte[] { 0, 255 }));
    }

    [Fact]
    public async Task RejectsOversizedPreviewRequest()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SafeFilePreview.ReadPrefixAsync(Path.GetTempPath(), maxBytes: 1024 * 1024 + 1));
    }

    [Fact]
    public async Task AlreadyCancelledPreviewDoesNotOpenFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.txt");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeFilePreview.ReadPrefixAsync(missing, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task RejectsReparsePointDirectoryAncestorWhenSupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-preview-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "linked");
        Directory.CreateDirectory(real);
        await File.WriteAllTextAsync(Path.Combine(real, "test.txt"), "preview");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, real);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                or PlatformNotSupportedException or NotSupportedException)
            {
                return; // Symlink creation is restricted on this host.
            }

            await Assert.ThrowsAsync<IOException>(() =>
                SafeFilePreview.ReadPrefixAsync(Path.Combine(link, "test.txt")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }
}
