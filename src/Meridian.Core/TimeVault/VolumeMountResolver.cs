using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Meridian.Core.TimeVault;

/// <summary>Resolves a local drive root to its Windows volume GUID path.</summary>
public static class VolumeMountResolver
{
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW",
        CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string mountPoint, StringBuilder volumeName, uint bufferLength);

    public static string GetVolumeGuidName(string driveRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        if (driveRoot.Length != 3 || !char.IsLetter(driveRoot[0]) ||
            driveRoot[1] != ':' || driveRoot[2] != '\\')
            throw new ArgumentException("Expected a drive-letter root such as C:\\.", nameof(driveRoot));

        var buffer = new StringBuilder(128);
        if (!GetVolumeNameForVolumeMountPoint(driveRoot, buffer, (uint)buffer.Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = buffer.ToString();
        if (!result.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
            !result.EndsWith("}\\", StringComparison.Ordinal))
            throw new IOException("Windows returned an unexpected volume identity.");
        return result;
    }
}
