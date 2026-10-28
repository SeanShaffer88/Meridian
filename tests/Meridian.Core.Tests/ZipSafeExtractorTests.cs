using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipSafeExtractorTests
{
    [Fact]
    public async Task ExtractsSingleFileWithoutExternalTools()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "sample.zip");
            var destination = Path.Combine(root, "output");
            Directory.CreateDirectory(destination);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("hello.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("sample content");
            }
            var bytes = await ZipSafeExtractor.ExtractSingleAsync(zipPath, "hello.txt", destination);
            Assert.Equal(14, bytes);
            Assert.Equal("sample content", await File.ReadAllTextAsync(Path.Combine(destination, "hello.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task NeverOverwritesExistingDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "sample.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                zip.CreateEntry("same.txt");
            var output = Path.Combine(root, "same.txt");
            await File.WriteAllTextAsync(output, "original");
            await Assert.ThrowsAsync<IOException>(() =>
                ZipSafeExtractor.ExtractSingleAsync(zipPath, "same.txt", root));
            Assert.Equal("original", await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
