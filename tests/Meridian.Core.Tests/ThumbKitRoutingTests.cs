using System.Text.Json;
using Meridian.Services;

namespace Meridian.Tests;

public sealed class ThumbKitRoutingTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData("png")]
    [InlineData(".PNG")]
    [InlineData("JPG")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    [InlineData("ppm")]
    [InlineData(" .jpg ")]
    public void BuiltInFormatsAreSupported(string extension) =>
        Assert.True(ThumbKitRouting.IsSupportedExtension(extension));

    [Theory]
    [InlineData(".gif")] // no built-in decoder, per the kit's own README
    [InlineData(".webp")]
    [InlineData(".txt")]
    [InlineData(".tif")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(null)]
    public void EverythingElseIsNotSupported(string? extension) =>
        Assert.False(ThumbKitRouting.IsSupportedExtension(extension));

    [Theory]
    [InlineData(@"C:\Photos\team.JPEG", true)]
    [InlineData(@"C:\Photos\team.png", true)]
    [InlineData(@"C:\Photos\team.gif", false)]
    [InlineData(@"C:\Photos\noextension", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void PathsRouteByTheirExtension(string? path, bool expected) =>
        Assert.Equal(expected, ThumbKitRouting.IsSupportedPath(path));

    [Fact]
    public void TheSupportedSetIsExactlyTheKitsBuiltIns() =>
        Assert.Equal(["bmp", "jpeg", "jpg", "png", "ppm"],
            ThumbKitRouting.SupportedExtensions.OrderBy(name => name, StringComparer.Ordinal).ToArray());

    [Fact]
    public void ConfigCarriesExactlyTheDocumentedKeys()
    {
        var json = ThumbKitRouting.BuildConfigJson(@"C:\Users\Sean\AppData\Local\Meridian\thumbcache");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var keys = root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(["cache_dir", "max_thumb_height", "max_thumb_width"], keys);
        Assert.Equal(@"C:\Users\Sean\AppData\Local\Meridian\thumbcache", root.GetProperty("cache_dir").GetString());
        Assert.Equal(256, root.GetProperty("max_thumb_width").GetInt32());
        Assert.Equal(256, root.GetProperty("max_thumb_height").GetInt32());
    }

    [Fact]
    public void PresetJsonCarriesBothDimensions()
    {
        using var document = JsonDocument.Parse(ThumbKitRouting.BuildPresetJson(192, 192));
        Assert.Equal(192, document.RootElement.GetProperty("max_w").GetInt32());
        Assert.Equal(192, document.RootElement.GetProperty("max_h").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    [InlineData(-1)]
    public void DimensionsOutsideTheKitRangeAreRejected(int dimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbKitRouting.BuildPresetJson(dimension, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbKitRouting.BuildConfigJson(@"C:\cache", maxThumbWidth: dimension));
    }

    [Fact]
    public void ABlankCacheDirectoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => ThumbKitRouting.BuildConfigJson("  "));
}
