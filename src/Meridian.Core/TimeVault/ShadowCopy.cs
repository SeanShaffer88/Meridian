namespace Meridian.Core.TimeVault;

/// <summary>Read-only metadata describing a Windows shadow-copy snapshot.</summary>
public sealed record ShadowCopy(
    string Id,
    string OriginalVolume,
    string DeviceObject,
    DateTimeOffset CreatedAt,
    string State)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(DeviceObject)
        && DeviceObject.StartsWith(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy", StringComparison.OrdinalIgnoreCase);
}
