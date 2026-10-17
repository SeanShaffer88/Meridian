using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FileOperationsTests
{
    [Fact]
    public void CopyItem_CopiesFileAndPreservesContents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            File.WriteAllText(Path.Combine(source, "note.txt"), "hello");
            var result = FileOperations.CopyItem(Path.Combine(source, "note.txt"), destination);
            Assert.Equal("hello", File.ReadAllText(result));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CopyItem_RefusesToOverwriteExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            var input = Path.Combine(source, "same.txt");
            File.WriteAllText(input, "source");
            File.WriteAllText(Path.Combine(destination, "same.txt"), "destination");
            Assert.Throws<IOException>(() => FileOperations.CopyItem(input, destination));
            Assert.Equal("destination", File.ReadAllText(Path.Combine(destination, "same.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
