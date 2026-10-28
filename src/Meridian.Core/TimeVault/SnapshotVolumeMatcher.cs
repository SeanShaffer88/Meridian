namespace Meridian.Core.TimeVault;

/// <summary>Maps WMI VSS volume GUID identities to local drive-letter mount points.</summary>
public static class SnapshotVolumeMatcher
{
    public static bool MatchesDrive(ShadowCopy snapshot, string driveRoot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root = Path.GetPathRoot(Path.GetFullPath(driveRoot)) ?? "";
        if (root.Length != 3 || root[1] != ':' || root[2] != '\\')
            return false;

        var volume = snapshot.OriginalVolume.TrimEnd('\\');
        if (string.Equals(volume, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return true;

        // Win32_ShadowCopy.VolumeName normally contains a volume GUID path.
        if (!volume.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
            !volume.EndsWith('}'))
            return false;
        return string.Equals(
            VolumeMountResolver.GetVolumeGuidName(root).TrimEnd('\\'),
            volume, StringComparison.OrdinalIgnoreCase);
    }
}
