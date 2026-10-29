using Meridian.Services;

namespace Meridian.Tests;

public sealed class BookmarkGroupSetTests
{
    private static readonly string One = @"C:\One";
    private static readonly string Two = @"C:\Two";
    private static readonly string Three = @"C:\Three";
    private static readonly string Four = @"C:\Four";

    private static BookmarkGroupSet Set(params string[] paths) => BookmarkGroupSet.Reconcile(paths, null);

    [Fact]
    public void ReconcileWithoutGroups_LeavesEverythingUngrouped()
    {
        // The pre-groups migration: a saved bookmarks list with no group
        // data at all reads as no groups, every bookmark ungrouped, and
        // nothing lost.
        var set = BookmarkGroupSet.Reconcile([One, Two, Three], null);

        Assert.Empty(set.Groups);
        Assert.Equal([One, Two, Three], set.AllPaths);
        Assert.Equal([One, Two, Three], set.UngroupedPaths);
        Assert.Null(set.GroupOf(One));
    }

    [Fact]
    public void ReconcileWithGroups_KeepsOrderMembershipAndCollapse()
    {
        var set = BookmarkGroupSet.Reconcile(
            [One, Two, Three, Four],
            [
                new BookmarkGroup("Work", true, [Two, One]),
                new BookmarkGroup("Play", false, [Four])
            ]);

        Assert.Equal(["Work", "Play"], set.Groups.Select(group => group.Name));
        Assert.True(set.Groups[0].Collapsed);
        Assert.Equal([Two, One], set.Groups[0].BookmarkPaths);
        Assert.Equal([Four], set.Groups[1].BookmarkPaths);
        Assert.Equal([Three], set.UngroupedPaths);
        Assert.Equal("Work", set.GroupOf(One));
        Assert.Null(set.GroupOf(Three));
    }

    [Fact]
    public void Reconcile_DropsStaleDuplicateAndBlankData()
    {
        var set = BookmarkGroupSet.Reconcile(
            [One, Two],
            [
                new BookmarkGroup("First", false, [One, @"C:\Gone", One]),
                new BookmarkGroup("first", false, [Two]),   // duplicate name — dropped, Two stays ungrouped
                new BookmarkGroup("  ", false, [Two]),       // blank name — dropped
                new BookmarkGroup("Second", false, [One])    // One already claimed by First
            ]);

        Assert.Equal(["First", "Second"], set.Groups.Select(group => group.Name));
        Assert.Equal([One], set.Groups[0].BookmarkPaths);
        Assert.Empty(set.Groups[1].BookmarkPaths);
        Assert.Equal([Two], set.UngroupedPaths);
    }

    [Fact]
    public void CreateGroup_RejectsBlankAndDuplicateNames()
    {
        var set = Set(One);
        Assert.False(set.TryCreateGroup("  ", out var blankError));
        Assert.False(string.IsNullOrWhiteSpace(blankError));

        Assert.True(set.TryCreateGroup("Work", out var okError));
        Assert.Null(okError);
        Assert.False(set.TryCreateGroup("work", out var duplicateError));
        Assert.False(string.IsNullOrWhiteSpace(duplicateError));
        Assert.Single(set.Groups);
    }

    [Fact]
    public void RenameGroup_KeepsBookmarksAndRejectsClashes()
    {
        var set = Set(One, Two);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.True(set.TryCreateGroup("Play", out _));
        Assert.True(set.MoveBookmark(One, "Work"));

        Assert.True(set.TryRenameGroup("Work", "Jobs", out var error));
        Assert.Null(error);
        Assert.Equal("Jobs", set.GroupOf(One));
        Assert.False(set.TryRenameGroup("Jobs", "Play", out var clashError));
        Assert.False(string.IsNullOrWhiteSpace(clashError));
        Assert.False(set.TryRenameGroup("Missing", "X", out _));
        // Renaming a group to its own name (any casing) is a no-op success.
        Assert.True(set.TryRenameGroup("Jobs", "jobs", out _));
    }

    [Fact]
    public void DeleteGroup_ReturnsItsBookmarksToUngrouped()
    {
        var set = Set(One, Two, Three);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.True(set.MoveBookmark(One, "Work"));
        Assert.True(set.MoveBookmark(Two, "Work"));

        Assert.True(set.DeleteGroup("Work"));

        Assert.Empty(set.Groups);
        // Nothing was deleted: both bookmarks still exist, now ungrouped.
        Assert.Equal([One, Two, Three], set.AllPaths);
        Assert.Equal([One, Two, Three], set.UngroupedPaths);
        Assert.False(set.DeleteGroup("Work"));
    }

    [Fact]
    public void MoveBookmark_BetweenGroupsAndBackToUngrouped()
    {
        var set = Set(One, Two, Three);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.True(set.TryCreateGroup("Play", out _));

        Assert.True(set.MoveBookmark(One, "Work"));
        Assert.True(set.MoveBookmark(Two, "Work"));
        Assert.True(set.MoveBookmark(Three, "Play"));
        Assert.Equal([One, Two], set.Groups[0].BookmarkPaths);
        Assert.Empty(set.UngroupedPaths);

        Assert.True(set.MoveBookmark(One, "Play"));
        Assert.Equal("Play", set.GroupOf(One));
        Assert.Equal([Two], set.Groups[0].BookmarkPaths);

        Assert.True(set.MoveBookmark(Two, null));
        Assert.Null(set.GroupOf(Two));
        Assert.Equal([Two], set.UngroupedPaths);

        Assert.False(set.MoveBookmark(@"C:\Missing", "Work"));
        Assert.False(set.MoveBookmark(Three, "Missing group"));
    }

    [Fact]
    public void MoveBookmark_HonoursIndexWithinGroup()
    {
        var set = Set(One, Two, Three);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.True(set.MoveBookmark(One, "Work"));
        Assert.True(set.MoveBookmark(Two, "Work"));
        Assert.True(set.MoveBookmark(Three, "Work", index: 0));
        Assert.Equal([Three, One, Two], set.Groups[0].BookmarkPaths);
        // Reordering inside the same group works through the same call.
        Assert.True(set.MoveBookmark(Two, "Work", index: 0));
        Assert.Equal([Two, Three, One], set.Groups[0].BookmarkPaths);
    }

    [Fact]
    public void MoveGroup_ReordersAndClamps()
    {
        var set = Set(One);
        Assert.True(set.TryCreateGroup("A", out _));
        Assert.True(set.TryCreateGroup("B", out _));
        Assert.True(set.TryCreateGroup("C", out _));

        Assert.True(set.MoveGroup("C", 0));
        Assert.Equal(["C", "A", "B"], set.Groups.Select(group => group.Name));
        Assert.True(set.MoveGroup("C", 99));
        Assert.Equal(["A", "B", "C"], set.Groups.Select(group => group.Name));
        Assert.False(set.MoveGroup("Missing", 0));
    }

    [Fact]
    public void CollapseState_RoundTripsThroughReconcile()
    {
        var set = Set(One);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.False(set.Groups[0].Collapsed);
        Assert.True(set.SetGroupCollapsed("Work", true));
        Assert.True(set.Groups[0].Collapsed);
        Assert.False(set.SetGroupCollapsed("Missing", true));

        var restored = BookmarkGroupSet.Reconcile(set.AllPaths, set.Groups);
        Assert.True(restored.Groups[0].Collapsed);
    }

    [Fact]
    public void AddAndRemoveBookmark_StayInSync()
    {
        var set = Set(One, Two);
        Assert.True(set.TryCreateGroup("Work", out _));
        Assert.True(set.MoveBookmark(One, "Work"));

        Assert.False(set.AddBookmark(One));
        Assert.True(set.AddBookmark(Three));
        Assert.Null(set.GroupOf(Three));

        Assert.True(set.RemoveBookmark(One));
        Assert.Equal([Two, Three], set.AllPaths);
        Assert.Empty(set.Groups[0].BookmarkPaths);
        Assert.False(set.RemoveBookmark(One));
    }
}
