namespace Meridian.Models;

public sealed class FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required string TypeLabel { get; init; }
    public required string SizeLabel { get; init; }
    public required string ModifiedLabel { get; init; }
    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    /// <summary>Chevron shown beside folders in the Miller-columns view, where a folder opens into the next column.</summary>
    public string ChevronGlyph => IsDirectory ? "\uE76C" : string.Empty;
}
