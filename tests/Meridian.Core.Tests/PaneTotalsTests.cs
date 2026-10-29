using Meridian.Models;
using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Pins the preview block header's contract: files always count at their
/// length, folders count only at a size that was already measured (the
/// lookup stands in for the folder-size cache), and the selection clause
/// appears only while a selection exists.
/// </summary>
public sealed class PaneTotalsTests
{
    private static FileEntry File(string name, long sizeBytes) => new()
    {
        Name = name,
        FullPath = Path.Combine(Path.GetTempPath(), name),
        IsDirectory = false,
        TypeLabel = "File",
        SizeLabel = string.Empty,
        ModifiedLabel = string.Empty,
        SizeBytes = sizeBytes
    };

    private static FileEntry Folder(string name) => new()
    {
        Name = name,
        FullPath = Path.Combine(Path.GetTempPath(), name),
        IsDirectory = true,
        TypeLabel = "Folder",
        SizeLabel = string.Empty,
        ModifiedLabel = string.Empty,
        SizeBytes = 0
    };

    private static long? NoCachedSizes(FileEntry entry) => null;

    [Fact]
    public void SumSizes_FilesSumAtTheirLength()
    {
        var items = new[] { File("a.bin", 100), File("b.bin", 250) };
        var (total, selected) = PaneTotals.SumSizes(items, [], NoCachedSizes);
        Assert.Equal(350, total);
        Assert.Equal(0, selected);
    }

    [Fact]
    public void SumSizes_UnmeasuredFoldersContributeNothing()
    {
        var items = new FileEntry[] { File("a.bin", 100), Folder("stuff") };
        var (total, _) = PaneTotals.SumSizes(items, [], NoCachedSizes);
        Assert.Equal(100, total);
    }

    [Fact]
    public void SumSizes_MeasuredFoldersContributeTheirCachedSize()
    {
        var folder = Folder("stuff");
        var items = new FileEntry[] { File("a.bin", 100), folder };
        var (total, _) = PaneTotals.SumSizes(items, [], entry => ReferenceEquals(entry, folder) ? 9_000 : null);
        Assert.Equal(9_100, total);
    }

    [Fact]
    public void SumSizes_SelectionIsSummedUnderTheSameRule()
    {
        var folder = Folder("stuff");
        var file = File("a.bin", 100);
        var items = new FileEntry[] { file, folder, File("b.bin", 7) };
        var selected = new FileEntry[] { file, folder };
        var (total, selectedTotal) = PaneTotals.SumSizes(
            items, selected, entry => ReferenceEquals(entry, folder) ? 50 : null);
        Assert.Equal(157, total);
        Assert.Equal(150, selectedTotal);
    }

    [Fact]
    public void FormatHeader_WithoutSelection_IsItemsAndTotalOnly()
    {
        Assert.Equal(
            $"23 items · {DirectoryReader.FormatSize(1_500_000)}",
            PaneTotals.FormatHeader(itemCount: 23, totalBytes: 1_500_000, selectedCount: 0, selectedBytes: 0));
    }

    [Fact]
    public void FormatHeader_WithSelection_AppendsTheSelectionClause()
    {
        Assert.Equal(
            $"23 items · {DirectoryReader.FormatSize(1_500_000)} · 2 selected · {DirectoryReader.FormatSize(40_000)}",
            PaneTotals.FormatHeader(itemCount: 23, totalBytes: 1_500_000, selectedCount: 2, selectedBytes: 40_000));
    }

    [Fact]
    public void FormatHeader_EmptyFolder_StillReadsCleanly()
    {
        Assert.Equal(
            $"0 items · {DirectoryReader.FormatSize(0)}",
            PaneTotals.FormatHeader(itemCount: 0, totalBytes: 0, selectedCount: 0, selectedBytes: 0));
    }
}
