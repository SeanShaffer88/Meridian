using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class DirectoryReaderTests
{
    [Fact]
    public void Read_ReturnsDirectoriesBeforeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "z-folder"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var entries = DirectoryReader.Read(root);
            Assert.Equal("z-folder", entries[0].Name);
            Assert.Equal("a.txt", entries[1].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FormatSize_FormatsBytesAndKilobytes()
    {
        Assert.Equal("12 B", DirectoryReader.FormatSize(12));
        Assert.Equal("1 KB", DirectoryReader.FormatSize(1024));
    }
}
