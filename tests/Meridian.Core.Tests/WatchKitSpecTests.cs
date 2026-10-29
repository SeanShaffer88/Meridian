using System.Text.Json;
using Meridian.Services;

namespace Meridian.Tests;

public sealed class WatchKitSpecTests
{
    [Fact]
    public void ConfigCarriesExactlyTheDocumentedKeys()
    {
        var json = WatchKitSpec.BuildConfigJson(@"C:\Data\Photos");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var keys = root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(["backend", "poll_interval_ms", "roots"], keys);
        Assert.Equal("auto", root.GetProperty("backend").GetString());
        Assert.Equal(WatchKitSpec.DefaultPollIntervalMs, root.GetProperty("poll_interval_ms").GetInt32());
        var roots = root.GetProperty("roots").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.Equal([@"C:\Data\Photos"], roots);
    }

    [Fact]
    public void ConfigEscapesBackslashesAndQuotesInTheRoot()
    {
        // A root containing a quote would corrupt hand-built JSON; the
        // serializer must escape it so the kit parses the path verbatim.
        var weird = "C:\\Odd \"Quoted\" Folder";
        var json = WatchKitSpec.BuildConfigJson(weird);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(weird, document.RootElement.GetProperty("roots")[0].GetString());
    }

    [Theory]
    [InlineData("native")]
    [InlineData("polling")]
    public void ConfigAcceptsTheOtherDocumentedBackends(string backend)
    {
        var json = WatchKitSpec.BuildConfigJson(@"D:\Root", backend, 100);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(backend, document.RootElement.GetProperty("backend").GetString());
        Assert.Equal(100, document.RootElement.GetProperty("poll_interval_ms").GetInt32());
    }

    [Fact]
    public void ConfigRejectsAnUnknownBackend() =>
        Assert.Throws<ArgumentException>(() => WatchKitSpec.BuildConfigJson(@"D:\Root", "carrier-pigeon"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ConfigRejectsABlankRoot(string? root) =>
        Assert.Throws<ArgumentException>(() => WatchKitSpec.BuildConfigJson(root!));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ConfigRejectsAManualOrNegativeInterval(int interval) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WatchKitSpec.BuildConfigJson(@"D:\Root", pollIntervalMs: interval));
}
