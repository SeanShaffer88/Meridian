using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Meridian.Services;

/// <summary>How an eject attempt ended, in the terms the status line reports.</summary>
internal enum DriveEjectOutcome
{
    /// <summary>Windows accepted the removal; the drive is safe to unplug.</summary>
    Ejected,
    /// <summary>The drive is not removable storage, so there is nothing to eject.</summary>
    NotRemovable,
    /// <summary>A program is still using the drive and Windows refused the removal.</summary>
    Busy,
    /// <summary>The removal could not be carried out; the message says why.</summary>
    Failed
}

internal sealed record DriveEjectResult(DriveEjectOutcome Outcome, string Message);

/// <summary>
/// Safely removes a removable drive ("eject"). Behind an interface so the
/// window depends on the outcome, not on the device plumbing.
/// </summary>
internal interface IDriveEjectService
{
    Task<DriveEjectResult> EjectAsync(string rootPath);
}

/// <summary>
/// The Windows implementation: the standard safe-remove route. The drive
/// letter's volume is opened to learn its storage device number, the disk
/// device interface with the same number is located through SetupAPI to
/// reach its device node, and cfgmgr32's CM_Request_Device_Remove asks
/// Windows to remove that node — the same request Explorer's "Eject" makes,
/// including its veto when a program still has the drive open.
///
/// Eject is only ever for removable drives: the interface is never offered
/// for other drives in the UI, and this service refuses them again itself,
/// so no caller can eject a fixed disk by mistake.
///
/// This is best-effort for composite USB devices (a reader presenting
/// several volumes under one parent may need the parent removed instead);
/// every failure comes back as a plain-words result rather than throwing.
/// </summary>
internal sealed class WindowsDriveEjectService : IDriveEjectService
{
    public Task<DriveEjectResult> EjectAsync(string rootPath) => Task.Run(() => Eject(rootPath));

    private static DriveEjectResult Eject(string rootPath)
    {
        DriveInfo drive;
        try { drive = new DriveInfo(rootPath); }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return new DriveEjectResult(DriveEjectOutcome.Failed, $"That drive could not be checked · {ex.Message}");
        }
        if (drive.DriveType != DriveType.Removable)
            return new DriveEjectResult(DriveEjectOutcome.NotRemovable, $"{rootPath} is not a removable drive, so it cannot be ejected.");

        var letter = rootPath.Length > 0 ? char.ToUpperInvariant(rootPath[0]) : '\0';
        if (letter is < 'A' or > 'Z')
            return new DriveEjectResult(DriveEjectOutcome.Failed, $"{rootPath} does not look like a drive letter, so it cannot be ejected.");

        var device = FindDiskDevice(letter);
        if (device is null)
            return new DriveEjectResult(DriveEjectOutcome.Failed, $"Windows could not find the device behind {rootPath}, so it could not be ejected.");

        var vetoName = new StringBuilder(256);
        var result = CM_Request_Device_Remove(device.Value, out _, vetoName, vetoName.Capacity, 0);
        return result switch
        {
            CR_SUCCESS => new DriveEjectResult(DriveEjectOutcome.Ejected, $"{rootPath} is safe to remove now."),
            CR_REMOVE_VETOED => new DriveEjectResult(
                DriveEjectOutcome.Busy,
                vetoName.Length > 0
                    ? $"{rootPath} could not be ejected — {vetoName} may still be using this drive. Close that program and try again."
                    : $"{rootPath} could not be ejected — a program may still be using this drive. Close any program using it and try again."),
            _ => new DriveEjectResult(DriveEjectOutcome.Failed, $"Windows could not eject {rootPath} (device error {result}). Nothing was removed.")
        };
    }

    /// <summary>
    /// Finds the device node (DEVINST) of the disk behind a drive letter:
    /// open the volume, read its storage device number, then walk the disk
    /// device interfaces until one's device node reports the same number.
    /// Returns null when any step fails — the caller reports that plainly.
    /// </summary>
    private static uint? FindDiskDevice(char letter)
    {
        var volume = CreateFileW($@"\\.\{letter}:", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (volume == INVALID_HANDLE_VALUE) return null;
        try
        {
            var number = new STORAGE_DEVICE_NUMBER();
            var size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
            if (!DeviceIoControl(volume, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, out number, size, out _, IntPtr.Zero))
                return null;
            return FindDiskDeviceByNumber(number.DeviceNumber);
        }
        finally
        {
            CloseHandle(volume);
        }
    }

    private static uint? FindDiskDeviceByNumber(uint deviceNumber)
    {
        var infoSet = SetupDiGetClassDevsW(GUID_DEVINTERFACE_DISK, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (infoSet == INVALID_HANDLE_VALUE) return null;
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, GUID_DEVINTERFACE_DISK, index, ref interfaceData))
                    return null; // ERROR_NO_MORE_ITEMS: no interface matched.
                var devInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiGetDeviceInterfaceDetailW(infoSet, ref interfaceData, IntPtr.Zero, 0, out var required, ref devInfo) &&
                    Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                    continue;
                var detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA: cbSize then the path.
                    // cbSize is 8 under 64-bit packing, 6 under 32-bit.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var detailInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!SetupDiGetDeviceInterfaceDetailW(infoSet, ref interfaceData, detail, required, out _, ref detailInfo))
                        continue;
                    var path = Marshal.PtrToStringUni(detail + IntPtr.Size);
                    if (path is null) continue;
                    if (DeviceNumberOfPath(path) == deviceNumber) return detailInfo.DevInst;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    /// <summary>The storage device number of a device-interface path, read the
    /// same way as for a volume: open it and ask.</summary>
    private static uint? DeviceNumberOfPath(string devicePath)
    {
        var handle = CreateFileW(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE) return null;
        try
        {
            var number = new STORAGE_DEVICE_NUMBER();
            return DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                out number, Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero)
                ? number.DeviceNumber
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ----- Native interop (cfgmgr32 safe-remove + SetupAPI device lookup) -----

    private const uint CR_SUCCESS = 0;
    private const uint CR_REMOVE_VETOED = 0x17;
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    private static readonly Guid GUID_DEVINTERFACE_DISK = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public int PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr device, uint controlCode, IntPtr inBuffer, int inSize,
        out STORAGE_DEVICE_NUMBER outBuffer, int outSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr infoSet, IntPtr devInfo, Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr infoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData,
        IntPtr detailData, uint detailSize, out uint requiredSize, ref SP_DEVINFO_DATA devInfo);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Request_Device_Remove(uint devInst, out uint vetoType, StringBuilder vetoName, int vetoNameLength, uint flags);
}
