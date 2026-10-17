using System.Diagnostics;
using Meridian.Services;

namespace Meridian.Core.Tests;

public sealed class MoveSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameVolumeDriveAliasMovesWithoutCopying(bool folder)
    {
        using var fixture = new DriveAlias();
        var source = Path.Combine(fixture.Root, "item");
        if (folder)
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "inside.txt"), "payload");
        }
        else File.WriteAllText(source, "payload");
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination")).FullName;
        var aliasDestination = Path.Combine(fixture.Drive + "\\", "destination");

        var result = FileOperations.MoveItem(source, aliasDestination);

        Assert.False(File.Exists(source));
        Assert.False(Directory.Exists(source));
        Assert.Equal("payload", File.ReadAllText(folder ? Path.Combine(result, "inside.txt") : result));
        Assert.Single(Directory.GetFileSystemEntries(destination));
    }

    [Fact]
    public void FailedRenameThroughDriveAliasDoesNotPublishACopy()
    {
        using var fixture = new DriveAlias();
        var source = Path.Combine(fixture.Root, "source.txt");
        File.WriteAllText(source, "preserve");
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination")).FullName;
        using (var heldSource = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => FileOperations.MoveItem(source,
                Path.Combine(fixture.Drive + "\\", "destination")));
            Assert.Empty(Directory.GetFileSystemEntries(destination));
            Assert.Equal("preserve", File.ReadAllText(source));
        }
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveAndRenameSupportLongPathsAndUnicode()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-long-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var nested = root;
            while (nested.Length < 280) nested = Path.Combine(nested, new string('a', 50));
            Directory.CreateDirectory(nested);
            var source = Path.Combine(nested, "résumé-文件.txt");
            File.WriteAllText(source, "binary-safe payload");
            var renamed = FileOperations.RenameItem(source, "renamed-文件.txt");
            Assert.False(File.Exists(source));
            var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            var moved = FileOperations.MoveItem(renamed, destination);
            Assert.False(File.Exists(renamed));
            Assert.Equal("binary-safe payload", File.ReadAllText(moved));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class DriveAlias : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-move-" + Guid.NewGuid().ToString("N"));
        public string Drive { get; }

        public DriveAlias()
        {
            Directory.CreateDirectory(Root);
            var used = Directory.GetLogicalDrives();
            Drive = "ZYXWVUTSRQPONMLKJIHGF".Select(letter => letter + ":")
                .First(candidate => !used.Contains(candidate + "\\", StringComparer.OrdinalIgnoreCase));
            try { RunSubst(Drive, Root); }
            catch { Directory.Delete(Root); throw; }
        }

        public void Dispose()
        {
            RunSubst(Drive, "/D");
            Directory.Delete(Root, recursive: true);
        }

        private static void RunSubst(params string[] arguments)
        {
            var start = new ProcessStartInfo("subst.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(10000), "Drive alias command timed out.");
            Assert.Equal(0, process.ExitCode);
        }
    }
}
