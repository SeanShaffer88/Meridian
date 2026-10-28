namespace Meridian.Core.TimeVault;

/// <summary>Validates a historical path without opening it. Does not prevent filesystem races.</summary>
public static class SnapshotPathGuard
{
    public static void RejectReparsePoints(ShadowCopy snapshot, string mappedPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(mappedPath);
        if (!snapshot.IsUsable) throw new ArgumentException("Unrecognized snapshot device.", nameof(snapshot));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = snapshot.DeviceObject.TrimEnd('\\');
        if (!mappedPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path does not belong to this snapshot.", nameof(mappedPath));
        var relative = mappedPath[(root.Length + 1)..];
        if (relative.Contains("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Snapshot path contains empty components.", nameof(mappedPath));
        var components = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (components.Any(part =>
            part is "." or ".." || part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')))
            throw new ArgumentException("Unsafe snapshot path component.", nameof(mappedPath));
        if ((File.GetAttributes(root + "\\") & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Snapshot device root is a reparse point.");
        var current = root;
        foreach (var part in components)
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Snapshot path contains a reparse point.");
        }
    }
}
