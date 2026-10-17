using System.Text;
using Meridian.Core.Viewer;

namespace Meridian.Core.Tests;

public class PreviewTextDecoderTests
{
    [Fact]
    public void DecodesUtf8()
    {
        Assert.True(PreviewTextDecoder.TryDecode(Encoding.UTF8.GetBytes("Meridian ✓"), out var text));
        Assert.Equal("Meridian ✓", text);
    }

    [Fact]
    public void StripsUtf8Bom()
    {
        Assert.True(PreviewTextDecoder.TryDecode(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, out var text));
        Assert.Equal("A", text);
    }

    [Fact]
    public void DecodesUtf16LittleEndian()
    {
        Assert.True(PreviewTextDecoder.TryDecode(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, out var text));
        Assert.Equal("A", text);
    }

    [Fact]
    public void DecodesUtf16BigEndian()
    {
        Assert.True(PreviewTextDecoder.TryDecode(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, out var text));
        Assert.Equal("A", text);
    }

    [Fact]
    public void RejectsInvalidUtf8()
    {
        Assert.False(PreviewTextDecoder.TryDecode(new byte[] { 0xC3, 0x28 }, out _));
    }

    [Fact]
    public void RejectsEmbeddedNull()
    {
        Assert.False(PreviewTextDecoder.TryDecode(new byte[] { 0x41, 0x00, 0x42 }, out _));
    }

    [Fact]
    public void RejectsTruncatedUtf16()
    {
        Assert.False(PreviewTextDecoder.TryDecode(new byte[] { 0xFF, 0xFE, 0x41 }, out _));
    }
}
