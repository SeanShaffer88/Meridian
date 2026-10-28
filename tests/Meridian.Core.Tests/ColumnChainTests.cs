using Meridian.Services;

namespace Meridian.Core.Tests;

/// <summary>
/// The Miller-columns chain as data: paths are opaque strings here — the
/// model never touches the filesystem, so unreadable or empty folders are
/// just selections whose column would list nothing.
/// </summary>
public sealed class ColumnChainTests
{
    private const string Root = @"C:\Root";
    private const string FolderA = @"C:\Root\Alpha";
    private const string FolderB = @"C:\Root\Alpha\Beta";
    private const string FolderC = @"C:\Root\Gamma";
    private const string FileDeep = @"C:\Root\Alpha\Beta\notes.txt";
    private const string FileRoot = @"C:\Root\readme.txt";

    [Fact]
    public void NewChain_ShowsOnlyTheRootColumn()
    {
        var chain = new ColumnChain(Root);

        Assert.Equal(Root, chain.RootPath);
        Assert.Empty(chain.Selections);
        Assert.Equal(1, chain.FolderColumnCount);
        Assert.False(chain.ShowsPreviewColumn);
        Assert.Null(chain.ActiveSelection);
        Assert.Equal(Root, chain.CurrentFolderPath);
        Assert.Equal(Root, chain.FolderPathAtDepth(0));
        Assert.Null(chain.FolderPathAtDepth(1));
    }

    [Fact]
    public void SelectFolder_RevealsItsColumnAndMovesTheCurrentFolder()
    {
        var chain = new ColumnChain(Root);

        chain.Select(0, FolderA, isDirectory: true);

        Assert.Equal(2, chain.FolderColumnCount);
        Assert.Equal(FolderA, chain.FolderPathAtDepth(1));
        Assert.Equal(FolderA, chain.CurrentFolderPath);
        Assert.False(chain.ShowsPreviewColumn);
        Assert.Equal(new ColumnSelection(FolderA, true), chain.ActiveSelection);
        Assert.Equal(new ColumnSelection(FolderA, true), chain.SelectionAtDepth(0));
        Assert.Null(chain.SelectionAtDepth(1));
    }

    [Fact]
    public void SelectFolderThenFile_ShowsPreviewAndKeepsCurrentFolderAtTheFolder()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);
        chain.Select(1, FolderB, isDirectory: true);
        chain.Select(2, FileDeep, isDirectory: false);

        Assert.Equal(3, chain.FolderColumnCount);
        Assert.True(chain.ShowsPreviewColumn);
        // The pane's current folder is the deepest selected folder, not the file.
        Assert.Equal(FolderB, chain.CurrentFolderPath);
        Assert.Equal(new ColumnSelection(FileDeep, false), chain.ActiveSelection);
        Assert.Equal(Root, chain.FolderPathAtDepth(0));
        Assert.Equal(FolderA, chain.FolderPathAtDepth(1));
        Assert.Equal(FolderB, chain.FolderPathAtDepth(2));
        Assert.Null(chain.FolderPathAtDepth(3));
    }

    [Fact]
    public void SelectFileAtRoot_KeepsCurrentFolderAtTheRoot()
    {
        var chain = new ColumnChain(Root);

        chain.Select(0, FileRoot, isDirectory: false);

        Assert.Equal(1, chain.FolderColumnCount);
        Assert.True(chain.ShowsPreviewColumn);
        Assert.Equal(Root, chain.CurrentFolderPath);
    }

    [Fact]
    public void ReselectShallowerFolder_TruncatesDeeperSelections()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);
        chain.Select(1, FolderB, isDirectory: true);
        chain.Select(2, FileDeep, isDirectory: false);

        chain.Select(0, FolderC, isDirectory: true);

        Assert.Single(chain.Selections);
        Assert.Equal(new ColumnSelection(FolderC, true), chain.ActiveSelection);
        Assert.Equal(2, chain.FolderColumnCount);
        Assert.Equal(FolderC, chain.CurrentFolderPath);
        Assert.False(chain.ShowsPreviewColumn);
        Assert.Null(chain.FolderPathAtDepth(2));
    }

    [Fact]
    public void ReplaceFolderWithFileAtSameDepth_TruncatesAndShowsPreview()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);
        chain.Select(1, FolderB, isDirectory: true);

        chain.Select(0, FileRoot, isDirectory: false);

        Assert.Single(chain.Selections);
        Assert.Equal(1, chain.FolderColumnCount);
        Assert.True(chain.ShowsPreviewColumn);
        Assert.Equal(Root, chain.CurrentFolderPath);
    }

    [Fact]
    public void SelectInNewlyRevealedColumn_Appends()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);

        // Depth 1 is the column FolderA just revealed; it has no selection yet.
        chain.Select(1, FolderB, isDirectory: true);

        Assert.Equal(2, chain.Selections.Count);
        Assert.Equal(3, chain.FolderColumnCount);
    }

    [Fact]
    public void SelectBeyondTheRevealedColumn_Throws()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Select(2, FolderB, isDirectory: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Select(-1, FolderA, isDirectory: true));
    }

    [Fact]
    public void SelectAfterAFile_Throws()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FileRoot, isDirectory: false);

        // A file reveals a preview, not a column: there is no depth 1 to select in.
        Assert.Throws<InvalidOperationException>(() => chain.Select(1, FolderA, isDirectory: true));
    }

    [Fact]
    public void BlankPaths_Throw()
    {
        Assert.Throws<ArgumentException>(() => new ColumnChain(" "));
        var chain = new ColumnChain(Root);
        Assert.Throws<ArgumentException>(() => chain.Select(0, "", isDirectory: true));
        Assert.Throws<ArgumentNullException>(() => chain.Reset(null!));
    }

    [Fact]
    public void ClearFromDepth_RemovesThatSelectionAndEverythingDeeper()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);
        chain.Select(1, FolderB, isDirectory: true);
        chain.Select(2, FileDeep, isDirectory: false);

        chain.ClearFromDepth(1);

        Assert.Single(chain.Selections);
        Assert.Equal(2, chain.FolderColumnCount);
        Assert.False(chain.ShowsPreviewColumn);
        Assert.Equal(FolderA, chain.CurrentFolderPath);
    }

    [Fact]
    public void ClearFromDepth_ZeroReturnsToTheRootOnly()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);

        chain.ClearFromDepth(0);

        Assert.Empty(chain.Selections);
        Assert.Equal(1, chain.FolderColumnCount);
        Assert.Equal(Root, chain.CurrentFolderPath);
    }

    [Fact]
    public void ClearFromDepth_AtOrPastTheEnd_ChangesNothing()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);

        chain.ClearFromDepth(1);
        chain.ClearFromDepth(99);

        Assert.Single(chain.Selections);
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.ClearFromDepth(-1));
    }

    [Fact]
    public void Reset_ReplacesTheRootAndClearsSelections()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);
        chain.Select(1, FileDeep, isDirectory: false);

        chain.Reset(FolderC);

        Assert.Equal(FolderC, chain.RootPath);
        Assert.Empty(chain.Selections);
        Assert.Equal(1, chain.FolderColumnCount);
        Assert.Equal(FolderC, chain.CurrentFolderPath);
    }

    [Fact]
    public void DepthLookups_OutsideTheChain_ReturnNull()
    {
        var chain = new ColumnChain(Root);
        chain.Select(0, FolderA, isDirectory: true);

        Assert.Null(chain.FolderPathAtDepth(-1));
        Assert.Null(chain.FolderPathAtDepth(2));
        Assert.Null(chain.SelectionAtDepth(-1));
        Assert.Null(chain.SelectionAtDepth(5));
    }
}
