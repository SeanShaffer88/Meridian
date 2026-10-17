using System.Runtime.InteropServices;
using System.Text;

namespace Meridian.Core.TimeVault;

/// <summary>Resolves a local drive root to the stable Windows volume GUID name.</summary>
public static partial class VolumeMountResolver
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(
        string mountPoint, [Out] char[] volumeName, uint bufferLength);

    public static string GetVolumeGuidName(string driveRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        var root = Path.GetPathRoot(Path.GetFullPath(driveRoot)) ?? "";
        if (root.Length != 3 || !char.IsLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new ArgumentException("Expected a local drive-letter root.", nameof(driveRoot));
        var buffer = new char[128];
        if (!GetVolumeNameForVolumeMountPoint(root, buffer, (uint)buffer.Length))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end && end >= 0 ? end : buffer.Length);
    }
}
