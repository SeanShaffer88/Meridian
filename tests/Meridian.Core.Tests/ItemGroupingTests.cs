using Meridian.Models;
using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Grouping buckets: type groups sort folders first, date groups use calendar-day
/// buckets newest-first, size groups use fixed byte thresholds — and the incoming
/// order inside a group is never disturbed.
/// </summary>
public sealed class ItemGroupingTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);

    private static FileEntry Entry(string name, bool isDirectory = false, string typeLabel = "Text file", long sizeBytes = 10, DateTime? modifiedUtc = null) => new()
    {
        Name = name,
        FullPath = $@"C:\Root\{name}",
        IsDirectory = isDirectory,
        TypeLabel = typeLabel,
        SizeLabel = "—",
        ModifiedLabel = "2026-10-28 12:00",
        SizeBytes = sizeBytes,
        ModifiedUtc = modifiedUtc ?? Now,
    };

    [Fact]
    public void None_ReturnsOneUntitledGroupWithEverything()
    {
        var items = new[] { Entry("a.txt"), Entry("b.txt") };
        var groups = ItemGrouping.Group(items, GroupMode.None, Now);
        var group = Assert.Single(groups);
        Assert.Equal(string.Empty, group.Title);
        Assert.Equal(items, group.Items);
    }

    [Fact]
    public void EmptyInput_ReturnsNoGroups_ForEveryMode()
    {
        foreach (var mode in Enum.GetValues<GroupMode>())
        {
            Assert.Empty(ItemGrouping.Group([], mode, Now));
        }
    }

    [Fact]
    public void Type_FoldersFirst_ThenTypesAlphabetical_InputOrderKeptInsideGroups()
    {
        var items = new[]
        {
            Entry("b.txt", typeLabel: "Text file"),
            Entry("Sub", isDirectory: true, typeLabel: "Folder"),
            Entry("a.txt", typeLabel: "Text file"),
            Entry("pic.png", typeLabel: "PNG file"),
        };
        var groups = ItemGrouping.Group(items, GroupMode.Type, Now);
        Assert.Equal(new[] { "Folders", "PNG file", "Text file" }, groups.Select(group => group.Title).ToArray());
        Assert.Equal(new[] { "b.txt", "a.txt" }, groups[2].Items.Select(item => item.Name).ToArray());
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(-1, "Today")] // future write times count as today
    [InlineData(1, "Yesterday")]
    [InlineData(2, "Earlier this week")]
    [InlineData(6, "Earlier this week")]
    [InlineData(7, "Earlier this month")]
    [InlineData(29, "Earlier this month")]
    [InlineData(30, "Older")]
    [InlineData(400, "Older")]
    public void Date_BucketByCalendarDayDifference(int daysAgo, string expectedTitle)
    {
        var groups = ItemGrouping.Group([Entry("a.txt", modifiedUtc: Now.Date.AddDays(-daysAgo))], GroupMode.Date, Now);
        Assert.Equal(expectedTitle, Assert.Single(groups).Title);
    }

    [Fact]
    public void Date_GroupsComeOutNewestFirst_AndEmptyBucketsAreOmitted()
    {
        var items = new[]
        {
            Entry("old.txt", modifiedUtc: Now.AddDays(-90)),
            Entry("today.txt", modifiedUtc: Now.AddHours(-2)),
            Entry("week.txt", modifiedUtc: Now.AddDays(-4)),
        };
        var groups = ItemGrouping.Group(items, GroupMode.Date, Now);
        Assert.Equal(new[] { "Today", "Earlier this week", "Older" }, groups.Select(group => group.Title).ToArray());
    }

    [Theory]
    [InlineData(0, "Empty")]
    [InlineData(1, "Small (under 100 KB)")]
    [InlineData(100 * 1024 - 1, "Small (under 100 KB)")]
    [InlineData(100 * 1024, "Medium (under 10 MB)")]
    [InlineData(10 * 1024 * 1024 - 1, "Medium (under 10 MB)")]
    [InlineData(10 * 1024 * 1024, "Large (under 100 MB)")]
    [InlineData(100 * 1024 * 1024 - 1, "Large (under 100 MB)")]
    [InlineData(100 * 1024 * 1024, "Huge (under 1 GB)")]
    [InlineData(1024 * 1024 * 1024 - 1, "Huge (under 1 GB)")]
    [InlineData(1024 * 1024 * 1024, "Enormous (1 GB and over)")]
    public void Size_BucketByByteThresholds(long sizeBytes, string expectedTitle)
    {
        var groups = ItemGrouping.Group([Entry("a.bin", sizeBytes: sizeBytes)], GroupMode.Size, Now);
        Assert.Equal(expectedTitle, Assert.Single(groups).Title);
    }

    [Fact]
    public void Size_FoldersGetTheirOwnGroupFirst_EvenWithZeroBytes()
    {
        var items = new[]
        {
            Entry("empty.txt", sizeBytes: 0),
            Entry("Sub", isDirectory: true, typeLabel: "Folder", sizeBytes: 0),
        };
        var groups = ItemGrouping.Group(items, GroupMode.Size, Now);
        Assert.Equal(new[] { "Folders", "Empty" }, groups.Select(group => group.Title).ToArray());
    }
}
