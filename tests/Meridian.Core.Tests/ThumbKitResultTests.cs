using System.Text.Json;
using Meridian.Services;

namespace Meridian.Tests;

public sealed class ThumbKitResultTests
{
    // The exact shape thumbkit.h documents for th_poll.
    private const string DoneExample = """
        {"id":1,"state":"done","thumb_path":"C:/app/thumbcache/ab12.thumb",
         "width":128,"height":96,"source_width":1024,"source_height":768,"error":null}
        """;

    [Fact]
    public void ParsesADoneResult()
    {
        var result = ThumbKitResult.Parse(DoneExample);
        Assert.Equal(ThumbKitState.Done, result.State);
        Assert.Equal("C:/app/thumbcache/ab12.thumb", result.ThumbPath);
        Assert.Equal(128, result.Width);
        Assert.Equal(96, result.Height);
        Assert.Equal(1024, result.SourceWidth);
        Assert.Equal(768, result.SourceHeight);
        Assert.Null(result.Error);
        Assert.True(result.IsTerminal);
    }

    [Fact]
    public void ParsesAFailureWithItsMessage()
    {
        var result = ThumbKitResult.Parse("""
            {"id":2,"state":"failed","thumb_path":null,"width":0,"height":0,
             "source_width":0,"source_height":0,"error":"provider returned code 3"}
            """);
        Assert.Equal(ThumbKitState.Failed, result.State);
        Assert.Null(result.ThumbPath);
        Assert.Equal("provider returned code 3", result.Error);
        Assert.True(result.IsTerminal);
    }

    [Fact]
    public void ParsesUnsupportedAndInFlightStates()
    {
        var unsupported = ThumbKitResult.Parse("""{"id":3,"state":"unsupported","thumb_path":null,"error":"no provider for gif"}""");
        Assert.Equal(ThumbKitState.Unsupported, unsupported.State);
        Assert.True(unsupported.IsTerminal);

        var running = ThumbKitResult.Parse("""{"id":4,"state":"running","thumb_path":null,"error":null}""");
        Assert.Equal(ThumbKitState.Running, running.State);
        Assert.False(running.IsTerminal);
    }

    [Fact]
    public void AnUnknownStateReadsAsUnknownNotAnError()
    {
        var result = ThumbKitResult.Parse("""{"id":5,"state":"beamed","thumb_path":null,"error":null}""");
        Assert.Equal(ThumbKitState.Unknown, result.State);
        Assert.False(result.IsTerminal);
    }

    [Fact]
    public void MalformedJsonThrows() =>
        Assert.ThrowsAny<JsonException>(() => ThumbKitResult.Parse("{nope"));
}
