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
}
