namespace Meridian.Services;

/// <summary>
/// Presentation math for one drive, kept in Core so it is testable: the
/// "84 GB free of 492 GB" label and the used-space fraction behind the
/// Home page's free-space bars. Pure functions of the byte counts the app
/// reads from the drive; an unreadable or zero-size drive yields an empty
/// bar and a plain label instead of a division by zero.
/// </summary>
public static class DriveSummary
{
    /// <summary>The fraction of the drive in use, from 0 (empty) to 1 (full). Zero or unknown totals give 0.</summary>
    public static double UsedFraction(long totalBytes, long freeBytes)
    {
        if (totalBytes <= 0) return 0;
        var used = totalBytes - Math.Clamp(freeBytes, 0, totalBytes);
        return (double)used / totalBytes;
    }

    /// <summary>Whole percent of the drive in use (0–100), for text like the
    /// sidebar's "80%". Zero or unknown totals give 0.</summary>
    public static int UsedPercent(long totalBytes, long freeBytes) =>
        (int)Math.Round(UsedFraction(totalBytes, freeBytes) * 100);

    /// <summary>"84 GB free of 492 GB", using the same size formatting as the file lists.</summary>
    public static string FreeSpaceLabel(long totalBytes, long freeBytes)
    {
        if (totalBytes <= 0) return "Size not available";
        var free = Math.Clamp(freeBytes, 0, totalBytes);
        return $"{DirectoryReader.FormatSize(free)} free of {DirectoryReader.FormatSize(totalBytes)}";
    }
}
