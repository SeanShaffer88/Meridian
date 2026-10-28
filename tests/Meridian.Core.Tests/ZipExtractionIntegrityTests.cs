using System.Buffers.Binary;
using System.IO.Compression;
using Meridian.Core.Archives;
using Xunit;

namespace Meridian.Core.Tests;

public sealed class ZipExtractionIntegrityTests
{
    public static IEnumerable<object[]> CorruptCases()
    {
        foreach (var extractor in new[] { "direct", "staged", "tree" })
        foreach (var corruption in new[] { "payload", "crc", "size" })
            yield return new object[] { extractor, corruption };
    }

    [Theory]
    [MemberData(nameof(CorruptCases))]
    public async Task RejectsCorruptStoredEntryAndRemovesOutput(string extractor, string corruption)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-zip-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "bundle.zip");
            await CreateArchive(archive, new byte[] { 11, 22, 33, 44 }, CompressionLevel.NoCompression);
            var bytes = await File.ReadAllBytesAsync(archive);
            var central = FindCentralHeader(bytes);
            if (corruption == "payload")
            {
                var dataOffset = 30 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) +
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
                bytes[dataOffset] ^= 0xff;
            }
            else if (corruption == "crc") bytes[central + 16] ^= 0xff;
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), 5);
            await File.WriteAllBytesAsync(archive, bytes);

            // Cataloging validates paths and budgets, not the entry's payload.
            Assert.Single(ZipArchiveCatalog.List(archive));
            await Assert.ThrowsAsync<InvalidDataException>(() => Extract(extractor, archive, root));
            Assert.Equal(new[] { archive }, Directory.GetFiles(root));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("staged")]
    [InlineData("tree")]
    public async Task RejectsDeflatedEntryWithFalseCrc(string extractor)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "bundle.zip");
            await CreateArchive(archive, new byte[32768], CompressionLevel.Optimal);
            var bytes = await File.ReadAllBytesAsync(archive);
            bytes[FindCentralHeader(bytes) + 16] ^= 0xff;
            await File.WriteAllBytesAsync(archive, bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => Extract(extractor, archive, root));
            Assert.Equal(new[] { archive }, Directory.GetFiles(root));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("staged", false)]
    [InlineData("tree", false)]
    [InlineData("direct", true)]
    [InlineData("staged", true)]
    [InlineData("tree", true)]
    public async Task PreservesValidEmptyAndMultiBufferBinaryEntries(string extractor, bool empty)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var payload = empty ? Array.Empty<byte>() : Enumerable.Range(0, 32769).Select(i => (byte)i).ToArray();
            var archive = Path.Combine(root, "bundle.zip");
            await CreateArchive(archive, payload, CompressionLevel.Optimal);
            Assert.Equal(payload.Length, await Extract(extractor, archive, root));
            var destination = extractor == "tree" ? Path.Combine(root, "output", "data.bin") : Path.Combine(root, "data.bin");
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.GetFiles(root, ".meridian-*.tmp"));
            Assert.Empty(Directory.GetDirectories(root, ".meridian-extract-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CreateArchive(string path, byte[] payload, CompressionLevel compression)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        await using var output = archive.CreateEntry("data.bin", compression).Open();
        await output.WriteAsync(payload);
    }

    private static async Task<long> Extract(string extractor, string archive, string root) => extractor switch
    {
        "direct" => await ZipSafeExtractor.ExtractSingleAsync(archive, "data.bin", root),
        "staged" => await ZipStagedExtractor.ExtractSingleAsync(archive, "data.bin", root),
        "tree" => (await ZipTreeExtractor.ExtractAsync(archive, root, "output")).BytesWritten,
        _ => throw new ArgumentOutOfRangeException(nameof(extractor))
    };

    private static int FindCentralHeader(byte[] bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x02014b50) return i;
        throw new InvalidDataException("Missing test ZIP central directory.");
    }
}
