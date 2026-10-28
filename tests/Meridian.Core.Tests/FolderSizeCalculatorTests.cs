using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FolderSizeCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_SumsFilesAcrossSubfolders()
    {
        var root = CreateTree();
        try
        {
            var result = await FolderSizeCalculator.CalculateAsync(root);

            Assert.Equal(100 + 250 + 1000, result.TotalBytes);
            Assert.Equal(3, result.FileCount);
            Assert.Equal(2, result.DirectoryCount);
            Assert.Equal(0, result.SkippedCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CalculateAsync_EmptyFolder_IsZero()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeridianFolderSize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await FolderSizeCalculator.CalculateAsync(root);
            Assert.Equal(new FolderSizeResult(0, 0, 0, 0), result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CalculateAsync_MissingFolder_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "MeridianFolderSize-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => FolderSizeCalculator.CalculateAsync(missing));
    }

    [Fact]
    public async Task CalculateAsync_PreCancelledToken_Throws()
    {
        var root = CreateTree();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => FolderSizeCalculator.CalculateAsync(root, cancellation.Token));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CalculateAsync_CancellationDuringProgress_ThrowsAndStops()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeridianFolderSize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Enough files that the periodic progress callback must fire.
            for (var i = 0; i < 600; i++)
                File.WriteAllBytes(Path.Combine(root, $"file-{i:D3}.bin"), new byte[8]);

            using var cancellation = new CancellationTokenSource();
            var progressReports = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                FolderSizeCalculator.CalculateAsync(root, cancellation.Token, _ =>
                {
                    progressReports++;
                    cancellation.Cancel();
                }));
            Assert.True(progressReports >= 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CalculateAsync_DanglingLink_IsSkippedNotFollowed()
    {
        var root = CreateTree();
        try
        {
            var linkPath = Path.Combine(root, "dangling-link");
            File.CreateSymbolicLink(linkPath, Path.Combine(root, "no-such-target.bin"));

            var result = await FolderSizeCalculator.CalculateAsync(root);

            Assert.Equal(100 + 250 + 1000, result.TotalBytes);
            Assert.Equal(3, result.FileCount);
            Assert.True(result.SkippedCount >= 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CalculateAsync_ReportsProgressSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeridianFolderSize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 300; i++)
                File.WriteAllBytes(Path.Combine(root, $"file-{i:D3}.bin"), new byte[16]);

            FolderSizeResult? last = null;
            var result = await FolderSizeCalculator.CalculateAsync(root, progress: snapshot => last = snapshot);

            Assert.NotNull(last);
            Assert.True(last!.FileCount >= 256);
            Assert.True(last.TotalBytes <= result.TotalBytes);
            Assert.Equal(300 * 16, result.TotalBytes);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeridianFolderSize-" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "sub");
        var nested = Path.Combine(sub, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(sub, "b.bin"), new byte[250]);
        File.WriteAllBytes(Path.Combine(nested, "c.bin"), new byte[1000]);
        return root;
    }
}
