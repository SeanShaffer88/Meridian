using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipEntryReaderTests
{
    [Fact]
    public async Task ReadsOnlyRequestedPrefix()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("docs/readme.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello world");
            }
            var prefix = await ZipEntryReader.ReadPrefixAsync(path, "docs/readme.txt", 5);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(prefix));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RejectsMissingEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                zip.CreateEntry("existing.txt");
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                ZipEntryReader.ReadPrefixAsync(path, "missing.txt"));
        }
        finally { File.Delete(path); }
    }
}
