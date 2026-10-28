using System.Text;
using Meridian.Core.Transfers;

namespace Meridian.Core.Tests;

public sealed class FileHashTests
{
    // Reference digests from the SHA-256 specification's well-known test vectors.
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void BufferHash_MatchesKnownVectors()
    {
        Assert.Equal(AbcDigest, FileHash.Sha256Hex(Encoding.ASCII.GetBytes("abc")));
        Assert.Equal(EmptyDigest, FileHash.Sha256Hex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task StreamAndFileHashes_AgreeWithBufferHash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "data.bin");
            var bytes = new byte[200_000];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            var expected = FileHash.Sha256Hex(bytes);
            Assert.Equal(expected, await FileHash.Sha256HexAsync(path));
            using var stream = File.OpenRead(path);
            Assert.Equal(expected, await FileHash.Sha256HexAsync(stream));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
