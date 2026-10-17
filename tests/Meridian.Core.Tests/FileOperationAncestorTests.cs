using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class FileOperationAncestorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BatchPreflightRejectsLinkedAncestorsBeforeCopying(bool linkedDestination, bool execute)
    {
        using var temp = new TemporaryDirectory();
        var real = Directory.CreateDirectory(Path.Combine(temp.Path, "real")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(real, "nested")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var first = Path.Combine(temp.Path, "first.txt");
        var second = Path.Combine(nested, "second.txt");
        File.WriteAllText(first, "first source");
        File.WriteAllText(second, "second source");
        var link = Path.Combine(temp.Path, "alias");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, real }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var target = linkedDestination ? Path.Combine(link, "nested") : destination;
            string[] sources = linkedDestination ? [first] : [first, Path.Combine(link, "nested", "second.txt")];
            if (execute)
                Assert.Throws<InvalidOperationException>(() => BatchCopy.Execute(sources, target));
            else if (linkedDestination)
                Assert.Throws<InvalidOperationException>(() => OperationPreview.PreviewCopy(sources, target));
            else
            {
                var plan = OperationPreview.PreviewCopy(sources, target);
                Assert.True(plan.HasBlockers);
                Assert.Equal(OperationPreview.Status.Ready, plan.Items[0].Result);
                Assert.Equal(OperationPreview.Status.ReparsePoint, plan.Items[1].Result);
                Assert.Contains(link, plan.Items[1].Explanation);
            }
            Assert.Empty(Directory.GetFileSystemEntries(destination));
            Assert.Equal(new[] { second }, Directory.GetFiles(nested));
            Assert.Equal("first source", File.ReadAllText(first));
            Assert.Equal("second source", File.ReadAllText(second));
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData("copy-source")]
    [InlineData("copy-destination")]
    [InlineData("move-source")]
    [InlineData("move-destination")]
    [InlineData("rename")]
    [InlineData("create-folder")]
    public void RejectsJunctionAncestorWithoutChangingFiles(string operation)
    {
        using var temp = new TemporaryDirectory();
        var real = Directory.CreateDirectory(Path.Combine(temp.Path, "real")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(real, "nested")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        var source = Path.Combine(nested, "source.txt");
        File.WriteAllText(source, "preserve source");
        var externalSource = Path.Combine(temp.Path, "external.txt");
        File.WriteAllText(externalSource, "preserve external");
        var link = Path.Combine(temp.Path, "alias");

        // Junctions need no developer-mode or symbolic-link privilege on Windows.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, real })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var aliasedFolder = Path.Combine(link, "nested");
            var aliasedSource = Path.Combine(aliasedFolder, "source.txt");
            Assert.Throws<IOException>(() =>
            {
                switch (operation)
                {
                    case "copy-source": FileOperations.CopyItem(aliasedSource, destination); break;
                    case "copy-destination": FileOperations.CopyItem(externalSource, aliasedFolder); break;
                    case "move-source": FileOperations.MoveItem(aliasedSource, destination); break;
                    case "move-destination": FileOperations.MoveItem(externalSource, aliasedFolder); break;
                    case "rename": FileOperations.RenameItem(aliasedSource, "renamed.txt"); break;
                    case "create-folder": FileOperations.CreateFolder(aliasedFolder, "new"); break;
                }
            });
            Assert.Equal("preserve source", File.ReadAllText(source));
            Assert.Equal("preserve external", File.ReadAllText(externalSource));
            Assert.Equal(new[] { source }, Directory.GetFiles(nested));
            Assert.Empty(Directory.GetDirectories(nested));
            Assert.Empty(Directory.GetFileSystemEntries(destination));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"meridian-ancestor-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
