using Meridian.Models;

namespace Meridian.Services;

/// <summary>How a pane's items are grouped. Off (None) by default.</summary>
public enum GroupMode
{
    None,
    Type,
    Date,
    Size,
}

/// <summary>One titled group of items, in display order.</summary>
public sealed record ItemGroup(string Key, string Title, IReadOnlyList<FileEntry> Items);

/// <summary>
/// Buckets a pane's already sorted + filtered items into titled groups. Group order
/// is fixed per mode; the order inside a group is the incoming order, untouched.
/// Pure and unit-tested — the window only renders the result.
/// </summary>
public static class ItemGrouping
{
    public static IReadOnlyList<ItemGroup> Group(IReadOnlyList<FileEntry> items, GroupMode mode, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return [];
        }

        return mode switch
        {
            GroupMode.Type => GroupByType(items),
            GroupMode.Date => GroupByDate(items, nowUtc),
            GroupMode.Size => GroupBySize(items),
            _ => [new ItemGroup(string.Empty, string.Empty, items.ToArray())],
        };
    }

    private static IReadOnlyList<ItemGroup> GroupByType(IReadOnlyList<FileEntry> items)
    {
        var groups = new List<ItemGroup>();
        var folders = items.Where(item => item.IsDirectory).ToArray();
        if (folders.Length > 0)
        {
            groups.Add(new ItemGroup("folders", "Folders", folders));
        }

        foreach (var typeGroup in items
                     .Where(item => !item.IsDirectory)
                     .GroupBy(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(new ItemGroup($"type:{typeGroup.Key}", typeGroup.Key, typeGroup.ToArray()));
        }

        return groups;
    }

    private static IReadOnlyList<ItemGroup> GroupByDate(IReadOnlyList<FileEntry> items, DateTime nowUtc)
    {
        // Calendar-day difference in UTC: "Today" means the same UTC date, not under 24 hours.
        string[] titles = ["Today", "Yesterday", "Earlier this week", "Earlier this month", "Older"];
        var buckets = titles.Select(title => new List<FileEntry>()).ToArray();
        foreach (var item in items)
        {
            var dayDifference = (nowUtc.Date - item.ModifiedUtc.Date).Days;
            var bucket = dayDifference switch
            {
                <= 0 => 0,
                1 => 1,
                <= 6 => 2,
                <= 29 => 3,
                _ => 4,
            };
            buckets[bucket].Add(item);
        }

        var groups = new List<ItemGroup>();
        for (var i = 0; i < titles.Length; i++)
        {
            if (buckets[i].Count > 0)
            {
                groups.Add(new ItemGroup($"date:{i}", titles[i], buckets[i]));
            }
        }

        return groups;
    }

    private static IReadOnlyList<ItemGroup> GroupBySize(IReadOnlyList<FileEntry> items)
    {
        (string Key, string Title)[] definitions =
        [
            ("empty", "Empty"),
            ("small", "Small (under 100 KB)"),
            ("medium", "Medium (under 10 MB)"),
            ("large", "Large (under 100 MB)"),
            ("huge", "Huge (under 1 GB)"),
            ("enormous", "Enormous (1 GB and over)"),
        ];
        var buckets = definitions.Select(_ => new List<FileEntry>()).ToArray();
        var folders = new List<FileEntry>();
        foreach (var item in items)
        {
            if (item.IsDirectory)
            {
                folders.Add(item);
                continue;
            }

            var bucket = item.SizeBytes switch
            {
                0 => 0,
                < 100 * 1024 => 1,
                < 10 * 1024 * 1024 => 2,
                < 100 * 1024 * 1024 => 3,
                < 1024 * 1024 * 1024 => 4,
                _ => 5,
            };
            buckets[bucket].Add(item);
        }

        var groups = new List<ItemGroup>();
        if (folders.Count > 0)
        {
            groups.Add(new ItemGroup("folders", "Folders", folders));
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            if (buckets[i].Count > 0)
            {
                groups.Add(new ItemGroup($"size:{definitions[i].Key}", definitions[i].Title, buckets[i]));
            }
        }

        return groups;
    }
}
