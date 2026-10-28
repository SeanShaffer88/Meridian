using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class EditableMetadataTests
{
    [Fact]
    public void Policy_SupportedPhotoFile_AllowsAllFourDetails()
    {
        var allowed = EditableMetadataPolicy.TryGetEditableProperties(
            @"C:\Pictures\holiday.jpg", isDirectory: false, isMarkedReadOnly: false,
            out var properties, out var reason);

        Assert.True(allowed);
        Assert.Null(reason);
        Assert.Equal(4, properties.Count);
        Assert.Equal("System.Title", properties[0].ShellName);
        Assert.Equal("System.Keywords", properties[3].ShellName);
    }

    [Theory]
    [InlineData(".JPG")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".tiff")]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".mp4")]
    public void Policy_SupportedExtensions_AreCaseInsensitive(string extension)
    {
        Assert.True(EditableMetadataPolicy.IsEditableExtension(extension));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".docx")]
    [InlineData(".zip")]
    [InlineData(".exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Policy_UnsupportedExtensions_AreRejected(string? extension)
    {
        Assert.False(EditableMetadataPolicy.IsEditableExtension(extension));
    }

    [Fact]
    public void Policy_Folder_IsRejectedWithAPlainReason()
    {
        var allowed = EditableMetadataPolicy.TryGetEditableProperties(
            @"C:\Pictures", isDirectory: true, isMarkedReadOnly: false,
            out var properties, out var reason);

        Assert.False(allowed);
        Assert.Empty(properties);
        Assert.Contains("Folders don't store", reason);
    }

    [Fact]
    public void Policy_ReadOnlyFile_IsRejectedWithAPlainReason()
    {
        var allowed = EditableMetadataPolicy.TryGetEditableProperties(
            @"C:\Pictures\holiday.jpg", isDirectory: false, isMarkedReadOnly: true,
            out _, out var reason);

        Assert.False(allowed);
        Assert.Contains("marked read-only", reason);
    }

    [Fact]
    public void Policy_UnsupportedFile_IsRejectedWithAPlainReason()
    {
        var allowed = EditableMetadataPolicy.TryGetEditableProperties(
            @"C:\Documents\notes.txt", isDirectory: false, isMarkedReadOnly: false,
            out _, out var reason);

        Assert.False(allowed);
        Assert.Contains("doesn't store editable details", reason);
    }

    [Fact]
    public void Plan_NoDifferences_HasNoChanges()
    {
        var current = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Title] = "Holiday",
            [MetadataProperty.Comment] = "",
            [MetadataProperty.Rating] = "4",
            [MetadataProperty.Keywords] = "beach; sunset"
        };
        var requested = new Dictionary<MetadataProperty, string>(current);

        var plan = MetadataEditPlanner.BuildPlan(@"C:\Pictures\holiday.jpg", current, requested);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Plan_ReportsOnlyTheChangedDetails_WithDisplayText()
    {
        var current = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Title] = "",
            [MetadataProperty.Comment] = "old note",
            [MetadataProperty.Rating] = "",
            [MetadataProperty.Keywords] = "beach"
        };
        var requested = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Title] = "  Holiday  ",
            [MetadataProperty.Comment] = "old note",
            [MetadataProperty.Rating] = "5",
            [MetadataProperty.Keywords] = "beach"
        };

        var plan = MetadataEditPlanner.BuildPlan(@"C:\Pictures\holiday.jpg", current, requested);

        Assert.Equal(2, plan.Changes.Count);
        var title = plan.Changes.Single(change => change.Property.Property == MetadataProperty.Title);
        Assert.Equal("(not set)", title.OldDisplay);
        Assert.Equal("Holiday", title.NewDisplay);
        var rating = plan.Changes.Single(change => change.Property.Property == MetadataProperty.Rating);
        Assert.Equal("5", rating.NewDisplay);
    }

    [Fact]
    public void Plan_ReorderedKeywords_AreNotAChange()
    {
        var current = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Keywords] = "beach; Sunset; family"
        };
        var requested = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Keywords] = "family;beach;sunset"
        };

        var plan = MetadataEditPlanner.BuildPlan(@"C:\Pictures\holiday.jpg", current, requested);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Plan_AddedKeyword_IsAChange()
    {
        var current = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Keywords] = "beach"
        };
        var requested = new Dictionary<MetadataProperty, string>
        {
            [MetadataProperty.Keywords] = "beach; sunset"
        };

        var plan = MetadataEditPlanner.BuildPlan(@"C:\Pictures\holiday.jpg", current, requested);

        Assert.True(plan.HasChanges);
        Assert.Equal("beach; sunset", MetadataEditPlanner.NormalizeKeywords(" beach ;sunset "));
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(2, 25u)]
    [InlineData(3, 50u)]
    [InlineData(4, 75u)]
    [InlineData(5, 99u)]
    [InlineData(9, 99u)]
    public void RatingScale_ToShellValue_UsesTheWindowsAnchors(int stars, uint expected)
    {
        Assert.Equal(expected, RatingScale.ToShellValue(stars));
    }

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, 1)]
    [InlineData(12u, 1)]
    [InlineData(24u, 2)]
    [InlineData(50u, 3)]
    [InlineData(60u, 3)]
    [InlineData(75u, 4)]
    [InlineData(90u, 5)]
    [InlineData(99u, 5)]
    public void RatingScale_ToStars_RoundsToTheNearestAnchor(uint shellValue, int expected)
    {
        Assert.Equal(expected, RatingScale.ToStars(shellValue));
    }

    [Fact]
    public void RatingScale_DisplayText_BlankWhenUnrated()
    {
        Assert.Equal("", RatingScale.ToDisplayText(0));
        Assert.Equal("4", RatingScale.ToDisplayText(75));
    }
}
