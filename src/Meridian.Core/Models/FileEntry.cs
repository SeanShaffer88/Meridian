using System.ComponentModel;
using Meridian.Services;

namespace Meridian.Models;

public sealed class FileEntry : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required string TypeLabel { get; init; }
    public required string SizeLabel { get; init; }
    public required string ModifiedLabel { get; init; }
    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }

    /// <summary>Friendly relative age of the last write ("2 hours ago"), computed when the folder was read.</summary>
    public string AgeLabel { get; init; } = string.Empty;

    /// <summary>The age band of the last write; the window uses it to pick a restrained text tier for the Age column.</summary>
    public AgeBucket AgeBucket => RelativeAge.BucketOf(ModifiedUtc, DateTime.UtcNow);

    private string? _folderSizeLabel;

    /// <summary>
    /// Live size text for a folder while the window measures it in the background
    /// ("Measuring…", then the formatted size). Null until measured, and reset to
    /// null when a measurement fails so the row falls back to the static label.
    /// </summary>
    public string? FolderSizeLabel
    {
        get => _folderSizeLabel;
        set
        {
            if (string.Equals(_folderSizeLabel, value, StringComparison.Ordinal))
            {
                return;
            }

            _folderSizeLabel = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderSizeLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveSizeLabel)));
        }
    }

    /// <summary>The size text a row shows: the live folder measurement when present, else the label from the read.</summary>
    public string EffectiveSizeLabel => FolderSizeLabel ?? SizeLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Glyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    /// <summary>Chevron shown beside folders in the Miller-columns view, where a folder opens into the next column.</summary>
    public string ChevronGlyph => IsDirectory ? "\uE76C" : string.Empty;
}
