using Meridian.Models;
using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FolderDnaTests
{
    private static FileEntry Entry(string name, bool directory, string type = "TXT file", long bytes = 0, DateTime modified = default)
        => new()
        {
            Name = name, FullPath = name, IsDirectory = directory,
            TypeLabel = type, SizeLabel = "", ModifiedLabel = "",
            SizeBytes = bytes, ModifiedUtc = modified
        };

    [Fact]
    public void Analyze_ClampsNegativeSizesAndSaturatesOverflow()
    {
        var summary = FolderDna.Analyze([
            Entry("invalid.dat", false, "DAT file", -10),
            Entry("huge-a.dat", false, "DAT file", long.MaxValue),
            Entry("huge-b.dat", false, "DAT file", 42)
        ]);

        Assert.Equal(3, summary.Files);
        Assert.Equal(long.MaxValue, summary.KnownFileBytes);
        Assert.Equal(long.MaxValue, Assert.Single(summary.Types).Bytes);
    }

    [Fact]
    public void Analyze_EmptyListingIsSafe()
    {
        var summary = FolderDna.Analyze([]);
        Assert.Equal(0, summary.Files);
        Assert.Equal(0, summary.Folders);
        Assert.Empty(summary.Types);
        Assert.Null(summary.OldestModifiedUtc);
    }

    [Fact]
    public void Analyze_GroupsOnlyImmediateFilesAndOrdersByBytes()
    {
        var older = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var summary = FolderDna.Analyze([
            Entry("subfolder", true, bytes: 9999),
            Entry("a.txt", false, "TXT file", 30, older),
            Entry("b.txt", false, "TXT file", 10, newer),
            Entry("image.png", false, "PNG file", 100, newer)
        ]);
        Assert.Equal(1, summary.Folders);
        Assert.Equal(3, summary.Files);
        Assert.Equal(140, summary.KnownFileBytes);
        Assert.Equal(older, summary.OldestModifiedUtc);
        Assert.Equal(newer, summary.NewestModifiedUtc);
        Assert.Equal(new[] { "PNG file", "TXT file" }, summary.Types.Select(t => t.Label));
        Assert.Equal(2, summary.Types[1].Count);
        Assert.Equal(40, summary.Types[1].Bytes);
    }

    [Fact]
    public void Analyze_RespectsCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderDna.Analyze([Entry("a", false)], source.Token));
    }
}
