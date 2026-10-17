using System.Text;

namespace Meridian.Core.Viewer;

/// <summary>Bounded, read-only text and byte preview. Does not run content or invoke shell handlers.</summary>
public static class SafeFilePreview
{
    public static async Task<byte[]> ReadPrefixAsync(
        string path, int maxBytes = 65536, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes is < 1 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Preview requires an absolute path.", nameof(path));
        var full = PreviewPathPolicy.ValidateRegularFile(path, cancellationToken);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Revalidate after opening: narrows (but does not eliminate) the window
        // in which a directory or file link could change between inspection and open.
        PreviewPathPolicy.ValidateRegularFile(full, cancellationToken);
        var bytes = new byte[maxBytes];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return bytes[..total];
    }

    public static string HexDump(ReadOnlySpan<byte> bytes)
    {
        var output = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            output.Append(offset.ToString("X8")).Append("  ");
            var row = bytes.Slice(offset, Math.Min(16, bytes.Length - offset));
            foreach (var value in row) output.Append(value.ToString("X2")).Append(' ');
            output.AppendLine();
        }
        return output.ToString();
    }
}
