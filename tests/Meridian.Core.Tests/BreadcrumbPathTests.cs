using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// Pins the breadcrumb bar's segment rules: root first (drive root or UNC
/// share), one segment per folder down to the current folder, the Home
/// sentinel as a single "Home" segment, and no segments at all for paths a
/// pane should never hold. Fixture paths are built from the platform's own
/// root so the same tests run on Windows and on this Linux gate machine;
/// the Windows spellings (drive letters, UNC) are pinned separately behind
/// a platform check.
/// </summary>
public sealed class BreadcrumbPathTests
{
    private const string HomeSentinel = "meridian://home";

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MeridianBreadcrumbTests-" + Guid.NewGuid().ToString("N"))).FullName;
            Alpha = Directory.CreateDirectory(Path.Combine(Root, "Alpha")).FullName;
            Deep = Directory.CreateDirectory(Path.Combine(Alpha, "Deep")).FullName;
        }

        public string Root { get; }
        public string Alpha { get; }
        public string Deep { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void SegmentsFor_DeepPath_RunsRootFirstDownToTheFolderItself()
    {
        using var fixture = new Fixture();
        var platformRoot = Path.GetPathRoot(fixture.Root)!;

        var segments = BreadcrumbPath.SegmentsFor(fixture.Deep, HomeSentinel);

        Assert.Equal(platformRoot, segments[0].Path);
        Assert.Equal(fixture.Deep, segments[^1].Path);
        Assert.Equal("Deep", segments[^1].Label);
        Assert.Contains(segments, segment => segment.Path == fixture.Alpha && segment.Label == "Alpha");
        Assert.Contains(segments, segment => segment.Path == fixture.Root);
        // Every step is the parent of the next, so the buttons walk down the path.
        for (var i = 1; i < segments.Count; i++)
            Assert.Equal(segments[i - 1].Path, Path.GetDirectoryName(segments[i].Path));
        // The root's label is the root without its trailing separator —
        // except where the root is nothing but the separator ("/" on
        // Linux), where the root itself is the label. The drive-letter and
        // share spellings are pinned by the Windows-only tests below.
        var expectedRootLabel = platformRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (expectedRootLabel.Length == 0) expectedRootLabel = platformRoot;
        Assert.Equal(expectedRootLabel, segments[0].Label);
    }

    [Fact]
    public void SegmentsFor_ARoot_IsJustTheRootSegment()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var segments = BreadcrumbPath.SegmentsFor(root, HomeSentinel);
        var only = Assert.Single(segments);
        Assert.Equal(root, only.Path);
    }

    [Fact]
    public void SegmentsFor_ToleratesATrailingSeparator()
    {
        using var fixture = new Fixture();
        var withSeparator = fixture.Alpha + Path.DirectorySeparatorChar;
        var segments = BreadcrumbPath.SegmentsFor(withSeparator, HomeSentinel);
        Assert.Equal(fixture.Alpha, segments[^1].Path);
        Assert.Equal("Alpha", segments[^1].Label);
    }

    [Fact]
    public void SegmentsFor_HomeSentinel_IsASingleHomeSegment()
    {
        var segments = BreadcrumbPath.SegmentsFor(HomeSentinel, HomeSentinel);
        var only = Assert.Single(segments);
        Assert.Equal("Home", only.Label);
        Assert.Equal(HomeSentinel, only.Path);
        // The pane compares paths case-insensitively; the sentinel match follows.
        Assert.Single(BreadcrumbPath.SegmentsFor("MERIDIAN://HOME", HomeSentinel));
    }

    [Fact]
    public void SegmentsFor_EmptyOrRelativePaths_YieldNoSegments()
    {
        Assert.Empty(BreadcrumbPath.SegmentsFor(null, HomeSentinel));
        Assert.Empty(BreadcrumbPath.SegmentsFor("", HomeSentinel));
        Assert.Empty(BreadcrumbPath.SegmentsFor("   ", HomeSentinel));
        Assert.Empty(BreadcrumbPath.SegmentsFor(Path.Combine("relative", "folder"), HomeSentinel));
    }

    [Fact]
    public void SegmentsFor_WindowsDrivePath_LabelsTheRootWithTheDriveLetter()
    {
        if (!OperatingSystem.IsWindows()) return; // drive-letter spelling is Windows-only
        var segments = BreadcrumbPath.SegmentsFor(@"C:\Users\Sean\Documents", HomeSentinel);
        Assert.Equal(4, segments.Count);
        Assert.Equal("C:", segments[0].Label);
        Assert.Equal(@"C:\", segments[0].Path);
        Assert.Equal("Users", segments[1].Label);
        Assert.Equal(@"C:\Users", segments[1].Path);
        Assert.Equal("Sean", segments[2].Label);
        Assert.Equal(@"C:\Users\Sean", segments[2].Path);
        Assert.Equal("Documents", segments[3].Label);
        Assert.Equal(@"C:\Users\Sean\Documents", segments[3].Path);
    }

    [Fact]
    public void SegmentsFor_WindowsUncPath_LabelsTheRootWithTheShare()
    {
        if (!OperatingSystem.IsWindows()) return; // UNC roots are a Windows spelling
        var segments = BreadcrumbPath.SegmentsFor(@"\\server\share\folder\deep", HomeSentinel);
        Assert.Equal(3, segments.Count);
        Assert.Equal(@"\\server\share", segments[0].Label);
        // The root segment's path keeps the platform root spelling; the
        // runtime may or may not end a UNC root with a separator, and both
        // spellings name the same share root, so either is accepted.
        Assert.True(segments[0].Path is @"\\server\share\" or @"\\server\share",
            $"Unexpected UNC root path: {segments[0].Path}");
        Assert.Equal("folder", segments[1].Label);
        Assert.Equal(@"\\server\share\folder", segments[1].Path);
        Assert.Equal("deep", segments[2].Label);
        Assert.Equal(@"\\server\share\folder\deep", segments[2].Path);
    }
}
