using Meridian.Core.Viewer;

namespace Meridian.Core.Tests;

public class ViewerRouterTests
{
    [Theory]
    [InlineData("movie.mp4", ViewerKind.Video)]
    [InlineData("movie.mkv", ViewerKind.Video)]
    [InlineData("music.flac", ViewerKind.Audio)]
    [InlineData("photo.png", ViewerKind.Image)]
    [InlineData("notes.md", ViewerKind.Text)]
    [InlineData("report.pdf", ViewerKind.Pdf)]
    [InlineData("unknown.bin", ViewerKind.Hex)]
    public void RoutesWithoutExecutingFiles(string path, ViewerKind expected)
    {
        Assert.Equal(expected, ViewerRouter.Classify(path).Kind);
    }

    [Fact]
    public void UnknownExtensionIsNotAutomaticallyLaunched()
    {
        var result = ViewerRouter.Classify("download.exe");
        Assert.Equal(ViewerKind.Hex, result.Kind);
        Assert.False(result.RequiresExternalHandler);
    }

    [Fact]
    public void VideoDoesNotPromiseCodecSupport()
    {
        Assert.Contains("codec", ViewerRouter.Classify("clip.mkv").Reason);
    }
}
