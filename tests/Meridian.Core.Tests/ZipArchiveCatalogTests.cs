using System.IO.Compression;
using Meridian.Core.Archives;

namespace Meridian.Core.Tests;

public class ZipArchiveCatalogTests
{
    [Fact]
    public void ListsZipWithoutExtractingAnything()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("docs/readme.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello");
            }

            var entries = ZipArchiveCatalog.List(path);
            Assert.Single(entries);
            Assert.Equal("docs/readme.txt", entries[0].Path);
            Assert.Equal(5, entries[0].UncompressedBytes);
            Assert.False(entries[0].IsDirectory);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsTraversalEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../escape.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("unsafe");
            }
            Assert.Throws<ArgumentException>(() => ZipArchiveCatalog.List(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EnforcesEntryLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("a.txt");
                zip.CreateEntry("b.txt");
            }
            Assert.Throws<InvalidDataException>(() => ZipArchiveCatalog.List(path, maxEntries: 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsFileThatIsAlsoParentOfAnotherEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("docs");
                zip.CreateEntry("docs/readme.txt");
            }
            Assert.Throws<InvalidDataException>(() => ZipArchiveCatalog.List(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsUnixSymbolicLinkEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("shortcut");
                entry.ExternalAttributes = unchecked((int)(0xA1FF0000u));
            }
            Assert.Throws<InvalidDataException>(() => ZipArchiveCatalog.List(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsExplicitDirectoryAndFileAtSamePath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("docs/");
                zip.CreateEntry("docs");
            }
            Assert.Throws<InvalidDataException>(() => ZipArchiveCatalog.List(path));
        }
        finally { File.Delete(path); }
    }
}
