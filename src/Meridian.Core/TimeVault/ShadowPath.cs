namespace Meridian.Core.TimeVault;

/// <summary>Maps a path on a protected volume to its snapshot equivalent.</summary>
public static class ShadowPath
{
    public static string Resolve(ShadowCopy snapshot, string originalPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (!snapshot.IsUsable) throw new ArgumentException("Unrecognized snapshot device.", nameof(snapshot));
        var volume = Path.GetFullPath(snapshot.OriginalVolume).TrimEnd('\\') + "\\";
        var file = Path.GetFullPath(originalPath);
        if (!file.StartsWith(volume, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path is outside the snapshot's original volume.", nameof(originalPath));
        var relative = file[volume.Length..];
        if (relative.Split('\\').Any(part => part is "." or ".."))
            throw new ArgumentException("Unsafe relative path.", nameof(originalPath));
        return snapshot.DeviceObject.TrimEnd('\\') + "\\" + relative;
    }
}
