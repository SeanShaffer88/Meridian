using System.Formats.Tar;
using System.IO.Compression;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class TarArchiveCatalogTests
{
    [Fact]
    public void RejectsFileAsParentOfAnotherEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "conflict.tar");
            using (var output = File.Create(path))
            using (var writer = new TarWriter(output))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "parent")
                {
                    DataStream = new MemoryStream(new byte[] { 1 })
                });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "parent/child")
                {
                    DataStream = new MemoryStream(new byte[] { 2 })
                });
            }
            Assert.Throws<InvalidDataException>(() => TarArchiveCatalog.List(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ListsRegularFilesInTar()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "sample.tar");
            using (var output = File.Create(path))
            using (var writer = new TarWriter(output))
            {
                var item = new PaxTarEntry(TarEntryType.RegularFile, "folder/hello.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 1, 2, 3 })
                };
                writer.WriteEntry(item);
            }
            var entries = TarArchiveCatalog.List(path);
            Assert.Single(entries);
            Assert.Equal("folder/hello.txt", entries[0].Path);
            Assert.Equal(3, entries[0].Bytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ListsGzipCompressedTar()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tgz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "sample.tar.gz");
            using (var output = File.Create(path))
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            using (var writer = new TarWriter(gzip))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "hello.txt")
                {
                    DataStream = new MemoryStream(new byte[] { 7 })
                });
            Assert.Single(TarArchiveCatalog.List(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
