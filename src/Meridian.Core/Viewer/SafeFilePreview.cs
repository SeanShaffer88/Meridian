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
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        // Reject link-bearing ancestors as well as the final file. This is a
        // pre-open check, not a race-free filesystem identity guarantee.
        var ancestor = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Preview cannot traverse reparse-point directories.");
            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, ancestor, StringComparison.Ordinal))
                break;
            ancestor = parent;
        }
        if ((File.GetAttributes(full) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("Preview requires a regular file.");
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
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
