using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Pins the details-strip → preview-block settings migration: a saved
/// per-pane value always wins for its pane, and while a pane has no saved
/// value the old strip's single visibility value decides for it.
/// </summary>
public sealed class PreviewBlockMigrationTests
{
    [Fact]
    public void Resolve_NoSavedValues_LegacyStripShown_ExpandsBothBlocks()
    {
        var (left, right) = PreviewBlockMigration.Resolve(null, null, legacyDetailsStripVisible: true);
        Assert.True(left);
        Assert.True(right);
    }

    [Fact]
    public void Resolve_NoSavedValues_LegacyStripHidden_CollapsesBothBlocks()
    {
        var (left, right) = PreviewBlockMigration.Resolve(null, null, legacyDetailsStripVisible: false);
        Assert.False(left);
        Assert.False(right);
    }

    [Fact]
    public void Resolve_SavedValues_WinOverTheLegacyValue()
    {
        var (left, right) = PreviewBlockMigration.Resolve(true, false, legacyDetailsStripVisible: false);
        Assert.True(left);
        Assert.False(right);

        var (left2, right2) = PreviewBlockMigration.Resolve(false, true, legacyDetailsStripVisible: true);
        Assert.False(left2);
        Assert.True(right2);
    }

    [Fact]
    public void Resolve_OneSavedValue_TheOtherPaneStillFollowsTheLegacyValue()
    {
        var (left, right) = PreviewBlockMigration.Resolve(true, null, legacyDetailsStripVisible: false);
        Assert.True(left);
        Assert.False(right);

        var (left2, right2) = PreviewBlockMigration.Resolve(null, false, legacyDetailsStripVisible: true);
        Assert.True(left2);
        Assert.False(right2);
    }
}
