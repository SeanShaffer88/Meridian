namespace Meridian.Core.TimeVault;

/// <summary>Maps an existing local drive path to a VSS device path without accessing snapshot contents.</summary>
/// <remarks>Lexical validation is not an authorization or reparse-point security boundary.</remarks>
public static class ShadowPath
{
    public static string Resolve(ShadowCopy snapshot, string originalPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("VSS is Windows-only.");
        var volume = Path.GetFullPath(snapshot.OriginalVolume).TrimEnd('\\');
        if (volume.Length != 2 || !char.IsLetter(volume[0]) || volume[1] != ':')
            throw new ArgumentException("Use ResolveForDrive for volume-GUID snapshots.", nameof(snapshot));
        return MapPath(snapshot, originalPath, volume + "\\");
    }

    /// <summary>Maps a path after verifying that the supplied drive belongs to the snapshot's volume.</summary>
    public static string ResolveForDrive(ShadowCopy snapshot, string originalPath, string driveRoot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        if (!SnapshotVolumeMatcher.MatchesDrive(snapshot, driveRoot))
            throw new ArgumentException("Drive is not a mount of the snapshot volume.", nameof(driveRoot));
        var root = Path.GetPathRoot(Path.GetFullPath(driveRoot))!;
        return MapPath(snapshot, originalPath, root);
    }

    private static string MapPath(ShadowCopy snapshot, string originalPath, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (!snapshot.IsUsable) throw new ArgumentException("Unrecognized snapshot device.", nameof(snapshot));
        if (!Path.IsPathFullyQualified(originalPath) || originalPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("A fully qualified local drive path is required.", nameof(originalPath));
        var file = Path.GetFullPath(originalPath);
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path is outside the snapshot's original volume.", nameof(originalPath));
        var relative = file.Length <= root.Length ? "" : file[root.Length..];
        return snapshot.DeviceObject.TrimEnd('\\') + "\\" + relative;
    }
}
