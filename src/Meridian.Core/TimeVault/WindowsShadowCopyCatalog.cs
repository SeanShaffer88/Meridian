using System.Management;

namespace Meridian.Core.TimeVault;

/// <summary>
/// Enumerates existing VSS snapshots through Windows CIM/WMI without creating or deleting snapshots.
/// Windows access permissions and WMI availability determine what is returned.
/// </summary>
public sealed class WindowsShadowCopyCatalog : IShadowCopyCatalog
{
    public Task<IReadOnlyList<ShadowCopy>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Volume Shadow Copy Service requires Windows.");

        return Task.Run<IReadOnlyList<ShadowCopy>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<ShadowCopy>();
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT ID, VolumeName, DeviceObject, InstallDate, State FROM Win32_ShadowCopy");
            using var entries = searcher.Get();
            foreach (ManagementObject entry in entries)
            {
                using (entry)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = entry["ID"]?.ToString() ?? "";
                    var volume = entry["VolumeName"]?.ToString() ?? "";
                    var device = entry["DeviceObject"]?.ToString() ?? "";
                    var rawDate = entry["InstallDate"]?.ToString();
                    var created = DateTimeOffset.MinValue;
                    if (!string.IsNullOrWhiteSpace(rawDate))
                    {
                        try { created = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(rawDate)); }
                        catch (FormatException) { /* malformed metadata remains unknown */ }
                    }
                    var state = entry["State"]?.ToString() ?? "Unknown";
                    results.Add(new ShadowCopy(id, volume, device, created, state));
                }
            }
            return results.AsReadOnly();
        }, cancellationToken);
    }
}
