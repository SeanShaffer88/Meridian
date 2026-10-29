using Meridian.Models;

namespace Meridian.Services;

/// <summary>
/// The maths and wording behind a pane preview block's header line:
/// "23 items · 1.2 GB", plus "· 2 selected · 40 MB" while a selection
/// exists. File sizes sum directly from the listing. A folder contributes
/// only when its measured size is already known — the caller passes a
/// lookup over the folder-size cache — so the totals line never starts a
/// fresh folder-size walk just to fill itself in; a folder measured later
/// joins the total the next time the line is refreshed.
/// </summary>
public static class PaneTotals
{
    /// <summary>
    /// Sums the byte total of <paramref name="items"/> and of
    /// <paramref name="selected"/> under the same rule: a file contributes
    /// its length; a folder contributes
    /// <paramref name="cachedFolderSize"/> when it returns a value and
    /// nothing when it returns null (never measured, or since changed).
    /// </summary>
    public static (long TotalBytes, long SelectedBytes) SumSizes(
        IEnumerable<FileEntry> items,
        IEnumerable<FileEntry> selected,
        Func<FileEntry, long?> cachedFolderSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(cachedFolderSize);
        return (Sum(items), Sum(selected));

        long Sum(IEnumerable<FileEntry> entries)
        {
            long total = 0;
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    if (cachedFolderSize(entry) is { } cached) total += cached;
                }
                else
                {
                    total += entry.SizeBytes;
                }
            }
            return total;
        }
    }

    /// <summary>The header line itself. The selection clause appears only
    /// while something is selected.</summary>
    public static string FormatHeader(int itemCount, long totalBytes, int selectedCount, long selectedBytes)
    {
        var line = $"{itemCount:N0} items · {DirectoryReader.FormatSize(totalBytes)}";
        if (selectedCount > 0)
            line += $" · {selectedCount:N0} selected · {DirectoryReader.FormatSize(selectedBytes)}";
        return line;
    }
}
