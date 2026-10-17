namespace Meridian.Core.TimeVault;

/// <summary>Maps an existing local drive path to a VSS device path without accessing the filesystem.</summary>
/// <remarks>This is lexical validation only. Never use it as an authorization or reparse-point security boundary.</remarks>
public static class ShadowPath
{
    public static string Resolve(ShadowCopy snapshot, string originalPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (!snapshot.IsUsable) throw new ArgumentException("Unrecognized snapshot device.", nameof(snapshot));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("VSS is Windows-only.");
        var volume = Path.GetFullPath(snapshot.OriginalVolume).TrimEnd('\\');
        if (volume.Length != 2 || !char.IsLetter(volume[0]) || volume[1] != ':')
            throw new ArgumentException("Only drive-letter volumes are supported.", nameof(snapshot));
        if (!Path.IsPathFullyQualified(originalPath) || originalPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("A fully qualified local drive path is required.", nameof(originalPath));
        var file = Path.GetFullPath(originalPath);
        var root = volume + "\\";
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file, volume, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path is outside the snapshot's original volume.", nameof(originalPath));
        var relative = file.Length <= root.Length ? "" : file[root.Length..];
        return snapshot.DeviceObject.TrimEnd('\\') + "\\" + relative;
    }
}
