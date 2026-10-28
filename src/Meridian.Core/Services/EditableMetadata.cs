using System.Globalization;

namespace Meridian.Services;

/// <summary>The file details Meridian offers for editing where Windows supports it.</summary>
public enum MetadataProperty
{
    Title,
    Comment,
    Rating,
    Keywords
}

/// <summary>
/// One editable detail: its identity, the label the interface shows, and the
/// Windows property-system name the value is stored under.
/// </summary>
public sealed record MetadataPropertyDescriptor(MetadataProperty Property, string Label, string ShellName);

/// <summary>
/// Which file details may be edited, and why not when they may not. The
/// honest rule: a detail is editable only when the file's format has a
/// Windows property handler known to store it. Everything else is shown
/// read-only with the reason, instead of accepting an edit Windows would
/// silently drop.
/// </summary>
public static class EditableMetadataPolicy
{
    public static IReadOnlyList<MetadataPropertyDescriptor> All { get; } =
    [
        new(MetadataProperty.Title, "Title", "System.Title"),
        new(MetadataProperty.Comment, "Comment", "System.Comment"),
        new(MetadataProperty.Rating, "Rating", "System.Rating"),
        new(MetadataProperty.Keywords, "Keywords (tags)", "System.Keywords")
    ];

    /// <summary>Formats whose Windows property handlers store title, comment, rating, and keywords.</summary>
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff",
        ".mp3", ".wma", ".m4a", ".mp4"
    };

    public static bool IsEditableExtension(string? extension) =>
        extension is not null && EditableExtensions.Contains(extension);

    public static bool TryGetEditableProperties(
        string path,
        bool isDirectory,
        bool isMarkedReadOnly,
        out IReadOnlyList<MetadataPropertyDescriptor> properties,
        out string? reason)
    {
        properties = [];
        reason = null;
        if (isDirectory)
        {
            reason = "Folders don't store a title, comment, rating, or keywords, so there is nothing here to edit.";
            return false;
        }

        if (isMarkedReadOnly)
        {
            reason = "This file is marked read-only in Windows, so its details can't be changed until that is turned off in the file's Properties.";
            return false;
        }

        if (!IsEditableExtension(Path.GetExtension(path)))
        {
            reason = "This kind of file doesn't store editable details. Title, comment, rating, and keywords can be edited on photos, music, and MP4 video files.";
            return false;
        }

        properties = All;
        return true;
    }
}

/// <summary>One detail whose value would change, with the text shown for the old and new values.</summary>
public sealed record MetadataChange(MetadataPropertyDescriptor Property, string OldDisplay, string NewDisplay);

/// <summary>
/// The reviewed plan for a metadata edit: the file and the exact changes,
/// built before anything is written. Saving happens only after the user
/// confirms this plan; executing it re-checks nothing silently — failures
/// are reported by the caller.
/// </summary>
public sealed record MetadataEditPlan(string Path, IReadOnlyList<MetadataChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// Builds a <see cref="MetadataEditPlan"/> by comparing the values currently
/// stored with the values in the editor, both as display text. Values are
/// compared after trimming; keywords are compared as a set of tags split on
/// ';' so reordering or respacing them is not a change.
/// </summary>
public static class MetadataEditPlanner
{
    public static MetadataEditPlan BuildPlan(
        string path,
        IReadOnlyDictionary<MetadataProperty, string> currentDisplay,
        IReadOnlyDictionary<MetadataProperty, string> requestedDisplay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(currentDisplay);
        ArgumentNullException.ThrowIfNull(requestedDisplay);
        var changes = new List<MetadataChange>();
        foreach (var descriptor in EditableMetadataPolicy.All)
        {
            var current = currentDisplay.TryGetValue(descriptor.Property, out var stored) ? stored : string.Empty;
            var requested = requestedDisplay.TryGetValue(descriptor.Property, out var edited) ? edited : string.Empty;
            if (ValuesEqual(descriptor.Property, current, requested)) continue;
            changes.Add(new MetadataChange(
                descriptor,
                DisplayOrPlaceholder(current),
                DisplayOrPlaceholder(requested)));
        }

        return new MetadataEditPlan(path, changes);
    }

    public static string NormalizeKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return string.Empty;
        var tags = SplitKeywords(keywords);
        return string.Join("; ", tags);
    }

    public static IReadOnlyList<string> SplitKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return [];
        return keywords
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length > 0)
            .ToArray();
    }

    private static bool ValuesEqual(MetadataProperty property, string current, string requested)
    {
        if (property == MetadataProperty.Keywords)
        {
            var currentTags = SplitKeywords(current).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
            var requestedTags = SplitKeywords(requested).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
            return currentTags.SequenceEqual(requestedTags, StringComparer.OrdinalIgnoreCase);
        }

        return string.Equals(current.Trim(), requested.Trim(), StringComparison.Ordinal);
    }

    private static string DisplayOrPlaceholder(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(not set)" : value.Trim();
}

/// <summary>
/// Converts between the 0–5 star rating the interface shows and the value
/// the Windows property system stores (1, 25, 50, 75, or 99; 0 means
/// unrated). Stored values between the anchors round to the nearest star.
/// </summary>
public static class RatingScale
{
    private static readonly (int Stars, uint ShellValue)[] Anchors =
    [
        (1, 1),
        (2, 25),
        (3, 50),
        (4, 75),
        (5, 99)
    ];

    public static uint ToShellValue(int stars)
    {
        if (stars <= 0) return 0;
        if (stars >= 5) return 99;
        foreach (var (anchorStars, shellValue) in Anchors)
        {
            if (anchorStars == stars) return shellValue;
        }

        return 0;
    }

    public static int ToStars(uint shellValue)
    {
        if (shellValue == 0) return 0;
        var best = 1;
        var bestDistance = long.MaxValue;
        foreach (var (stars, anchor) in Anchors)
        {
            var distance = Math.Abs((long)shellValue - anchor);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = stars;
            }
        }

        return best;
    }

    /// <summary>The editor's display text for a stored rating: the star count, or empty when unrated.</summary>
    public static string ToDisplayText(uint shellValue)
    {
        var stars = ToStars(shellValue);
        return stars == 0 ? string.Empty : stars.ToString(CultureInfo.InvariantCulture);
    }
}
