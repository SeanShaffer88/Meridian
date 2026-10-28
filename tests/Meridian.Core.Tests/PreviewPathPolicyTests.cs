using Meridian.Core.Viewer;

namespace Meridian.Core.Tests;

public class PreviewPathPolicyTests
{
    [Fact]
    public void AcceptsOrdinaryAbsoluteFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Equal(Path.GetFullPath(path), PreviewPathPolicy.ValidateRegularFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsDirectoryAndRelativePath()
    {
        Assert.Throws<IOException>(() => PreviewPathPolicy.ValidateRegularFile(Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => PreviewPathPolicy.ValidateRegularFile("relative.txt"));
    }

    [Fact]
    public void HonorsCancellationBeforeFilesystemAccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file.txt");
        Assert.Throws<OperationCanceledException>(() =>
            PreviewPathPolicy.ValidateRegularFile(missing, cancellation.Token));
    }
}
