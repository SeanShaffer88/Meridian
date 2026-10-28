using System.Security.Cryptography;

namespace Meridian.Core.Transfers;

/// <summary>Streaming SHA-256 hashing, returned as lowercase hex. Used for verify-after-copy
/// and available to the standalone hash tool; hashes are computed in one pass over a stream,
/// so file size does not drive memory use.</summary>
public static class FileHash
{
    /// <summary>Hashes everything readable from a stream, from its current position to the end.</summary>
    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Hashes one file from disk.</summary>
    public static async Task<string> Sha256HexAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await Sha256HexAsync(stream, cancellationToken);
    }

    /// <summary>Hashes an in-memory buffer (test vectors and small payloads).</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
