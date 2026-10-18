namespace Meridian.Core.Archives;

/// <summary>Truthful feature discovery for the embedded archive backend.</summary>
public static class ArchiveCapabilities
{
    public sealed record Format(string Extension, bool CanList, bool CanPreviewEntry,
        bool CanExtract, bool CanCreate, string Notes);

    public static IReadOnlyList<Format> Supported { get; } = Array.AsReadOnly(new[]
    {
        new Format(".zip", true, true, true, true,
            "Built-in .NET ZIP; bounded backend previews, staged tree extraction and file-map creation. Interactive workflows and broad codec compatibility remain unverified."),
        new Format(".7z", false, false, false, false, "Planned embedded codec; not implemented."),
        new Format(".rar", false, false, false, false, "Planned extraction codec; not implemented."),
        new Format(".tar", true, false, true, true, "TAR catalog, staged extraction and file-map creation; interactive workflows and full dialect coverage remain unverified."),
        new Format(".tgz", true, false, true, true, "Compressed TAR catalog, staged extraction and file-map creation; interactive workflows remain unverified."),
        new Format(".gz", false, false, false, false, "Standalone gzip is unsupported; .tar.gz routes to compressed TAR capabilities."),
        new Format(".bz2", false, false, false, false, "Planned; not implemented."),
        new Format(".xz", false, false, false, false, "Planned; not implemented."),
        new Format(".zst", false, false, false, false, "Planned; not implemented.")
    });

    public static Format? ForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tgz" : Path.GetExtension(path);
        return Supported.FirstOrDefault(item =>
            string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase));
    }
}
