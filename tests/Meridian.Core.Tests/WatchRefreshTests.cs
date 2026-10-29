using Meridian.Services;

namespace Meridian.Tests;

public sealed class WatchRefreshTests
{
    private const string Folder = @"C:\Data";

    private static WatchEvent Change(WatchEventKind kind, string path, string? oldPath = null) =>
        new(kind, path, oldPath, 1, 1);

    [Fact]
    public void ADirectChildCreationAffectsTheListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Created, @"C:\Data\report.txt")], includeDescendants: false));

    [Fact]
    public void ADirectChildDeletionAffectsTheListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Deleted, @"C:\Data\report.txt")], includeDescendants: false));

    [Fact]
    public void ARenameIntoTheFolderAffectsTheListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Renamed, @"C:\Data\arrived.txt", @"C:\Elsewhere\arrived.txt")], includeDescendants: false));

    [Fact]
    public void ARenameOutOfTheFolderAffectsTheListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Renamed, @"C:\Elsewhere\left.txt", @"C:\Data\left.txt")], includeDescendants: false));

    [Fact]
    public void AGrandchildChangeDoesNotAffectAStandardListing() =>
        Assert.False(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Modified, @"C:\Data\Sub\deep.txt")], includeDescendants: false));

    [Fact]
    public void AGrandchildChangeAffectsAColumnsListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Modified, @"C:\Data\Sub\deep.txt")], includeDescendants: true));

    [Fact]
    public void ASiblingFolderChangeDoesNotAffectTheListing() =>
        Assert.False(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Created, @"C:\Database\new.txt")], includeDescendants: true));

    [Fact]
    public void TheFolderItselfBeingDeletedAffectsTheListing() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [Change(WatchEventKind.Deleted, Folder)], includeDescendants: false));

    [Fact]
    public void MatchingIsCaseInsensitiveAndSeparatorTolerant() =>
        Assert.True(WatchRefresh.AffectsListing(@"c:\data\",
            [Change(WatchEventKind.Created, "C:/DATA/File.TXT")], includeDescendants: false));

    [Fact]
    public void ADriveRootPaneSeesItsDirectChildren() =>
        Assert.True(WatchRefresh.AffectsListing(@"C:\",
            [Change(WatchEventKind.Created, @"C:\top.txt")], includeDescendants: false));

    [Fact]
    public void ADriveRootPaneIgnoresDeepChangesInStandardMode() =>
        Assert.False(WatchRefresh.AffectsListing(@"C:\",
            [Change(WatchEventKind.Modified, @"C:\Windows\System32\deep.dll")], includeDescendants: false));

    [Fact]
    public void AUncSharePaneSeesItsDirectChildren() =>
        Assert.True(WatchRefresh.AffectsListing(@"\\server\share\dir",
            [Change(WatchEventKind.Created, @"\\server\share\dir\file.txt")], includeDescendants: false));

    [Fact]
    public void AUncShareRootPaneSeesItsDirectChildren() =>
        Assert.True(WatchRefresh.AffectsListing(@"\\server\share",
            [Change(WatchEventKind.Created, @"\\server\share\file.txt")], includeDescendants: false));

    [Fact]
    public void NoEventsMeansNoRefresh() =>
        Assert.False(WatchRefresh.AffectsListing(Folder, [], includeDescendants: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPaneFolderNeverRefreshes(string? folder) =>
        Assert.False(WatchRefresh.AffectsListing(folder,
            [Change(WatchEventKind.Created, @"C:\Data\report.txt")], includeDescendants: true));

    [Fact]
    public void OneRelevantEventInABatchIsEnough() =>
        Assert.True(WatchRefresh.AffectsListing(Folder,
            [
                Change(WatchEventKind.Modified, @"C:\Data\Sub\deep.txt"),
                Change(WatchEventKind.Created, @"C:\Other\nope.txt"),
                Change(WatchEventKind.Deleted, @"C:\Data\gone.txt"),
            ],
            includeDescendants: false));

    [Fact]
    public void NormalizeUnifiesRootsAndTrailingSeparators()
    {
        Assert.Equal(@"C:", WatchRefresh.Normalize(@"C:\"));
        Assert.Equal(@"C:\Data", WatchRefresh.Normalize("C:/Data/"));
        Assert.Equal(@"\\server\share", WatchRefresh.Normalize(@"\\server\share\"));
        Assert.Equal(@"C:\Data", WatchRefresh.Normalize(@"C:\Data"));
    }
}
