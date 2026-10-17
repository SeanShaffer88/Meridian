namespace Meridian.Core.Archives;

/// <summary>Conservative validation of untrusted archive entry names before extraction planning.</summary>
public static class ArchiveEntryPathPolicy
{
    public static string Normalize(string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        if (entryName.IndexOf('\0') >= 0)
            throw new ArgumentException("Archive entry contains a NUL character.", nameof(entryName));
        if (entryName.StartsWith('/') || entryName.StartsWith('\\') || entryName.Contains(':'))
            throw new ArgumentException("Archive entry is rooted or contains a stream/drive prefix.", nameof(entryName));

        var segments = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Length > 128)
            throw new ArgumentException("Archive entry has invalid path depth.", nameof(entryName));

        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Length > 255 ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0 ||
                segment.Any(char.IsControl))
                throw new ArgumentException("Archive entry contains an unsafe path component.", nameof(entryName));

            var deviceBase = segment.Split('.')[0].TrimEnd(' ');
            if (deviceBase.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                deviceBase.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                deviceBase.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                deviceBase.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (deviceBase.Length == 4 && (deviceBase.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    deviceBase.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    deviceBase[3] is >= '1' and <= '9'))
                throw new ArgumentException("Archive entry uses a reserved Windows device name.", nameof(entryName));
        }
        return string.Join('/', segments);
    }
}
