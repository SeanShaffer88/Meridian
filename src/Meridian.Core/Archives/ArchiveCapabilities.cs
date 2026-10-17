namespace Meridian.Core.Archives;

/// <summary>Truthful feature discovery for the embedded archive backend.</summary>
public static class ArchiveCapabilities
{
    public sealed record Format(string Extension, bool CanList, bool CanPreviewEntry,
        bool CanExtract, bool CanCreate, string Notes);

    public static IReadOnlyList<Format> Supported { get; } = Array.AsReadOnly(new[]
    {
        new Format(".zip", true, true, true, true,
            "Built-in .NET ZIP; bounded previews, staged tree extraction and explicit-file creation. Not yet Windows-verified."),
        new Format(".7z", false, false, false, false, "Planned embedded codec; not implemented."),
        new Format(".rar", false, false, false, false, "Planned extraction codec; not implemented."),
        new Format(".tar", true, false, false, false, "Read-only TAR catalog prototype; no extraction or creation."),
        new Format(".tgz", true, false, false, false, "Read-only gzip-compressed TAR catalog prototype."),
        new Format(".gz", false, false, false, false, "Standalone gzip not supported; .tar.gz listing prototype only."),
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
