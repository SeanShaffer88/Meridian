namespace Meridian.Core.TimeVault;

/// <summary>Read-only metadata describing a Windows shadow-copy snapshot.</summary>
public sealed record ShadowCopy(
    string Id,
    string OriginalVolume,
    string DeviceObject,
    DateTimeOffset CreatedAt,
    string State)
{
    private const string DevicePrefix = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy";

    public bool IsUsable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DeviceObject) ||
                !DeviceObject.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var suffix = DeviceObject[DevicePrefix.Length..];
            return suffix.Length > 0 && suffix.All(char.IsAsciiDigit);
        }
    }
}
