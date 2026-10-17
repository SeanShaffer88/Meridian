using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ArchiveEntryPathPolicyTests
{
    [Theory]
    [InlineData("docs/readme.txt", "docs/readme.txt")]
    [InlineData("docs\\notes.txt", "docs/notes.txt")]
    [InlineData("images/icon.png", "images/icon.png")]
    public void NormalizesSafeEntryNames(string input, string expected)
        => Assert.Equal(expected, ArchiveEntryPathPolicy.Normalize(input));

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("docs/../outside.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("\\server\\share.txt")]
    [InlineData("C:\\windows\\file.txt")]
    [InlineData("folder/file.txt:stream")]
    [InlineData("CON.txt")]
    [InlineData("dir/NUL")]
    [InlineData("LPT1.log")]
    [InlineData("trailing./file.txt")]
    [InlineData("trailing /file.txt")]
    [InlineData("a/./b")]
    [InlineData("folder/evil?.txt")]
    public void RejectsUnsafeEntryNames(string input)
        => Assert.Throws<ArgumentException>(() => ArchiveEntryPathPolicy.Normalize(input));

    [Fact]
    public void RejectsExcessiveDepth()
    {
        var name = string.Join("/", Enumerable.Repeat("sub", 129));
        Assert.Throws<ArgumentException>(() => ArchiveEntryPathPolicy.Normalize(name));
    }
}
