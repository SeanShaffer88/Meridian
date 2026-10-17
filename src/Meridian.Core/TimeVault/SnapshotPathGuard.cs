namespace Meridian.Core.TimeVault;

/// <summary>Validates a historical path without opening it. Does not prevent filesystem races.</summary>
internal static class SnapshotPathGuard
{
    public static void RejectReparsePoints(ShadowCopy snapshot, string mappedPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappedPath);
        if (!snapshot.IsUsable) throw new ArgumentException("Unrecognized snapshot device.", nameof(snapshot));
        var root = snapshot.DeviceObject.TrimEnd('\\');
        if (!mappedPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path does not belong to this snapshot.", nameof(mappedPath));
        if ((File.GetAttributes(root + "\\") & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Snapshot device root is a reparse point.");
        var current = root;
        foreach (var part in mappedPath[(root.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or ".." || part.Contains(':'))
                throw new ArgumentException("Unsafe snapshot path component.", nameof(mappedPath));
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Snapshot path contains a reparse point.");
        }
    }
}
