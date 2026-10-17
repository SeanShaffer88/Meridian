namespace Meridian.Core.Viewer;

/// <summary>Conservative routing hints, not a guarantee a codec or parser is available.</summary>
public enum ViewerKind { Text, Image, Audio, Video, Pdf, Hex, Unsupported }

public sealed record ViewerDecision(ViewerKind Kind, string Reason, bool RequiresExternalHandler);

/// <summary>
/// Pure extension-based routing. Unknown formats default to bounded hexadecimal inspection,
/// never shell execution or automatic third-party codec installation.
/// </summary>
public static class ViewerRouter
{
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".cs", ".py", ".js", ".html", ".css" };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma" };

    public static ViewerDecision Classify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = Path.GetExtension(path);
        if (Text.Contains(ext)) return new(ViewerKind.Text, "Read-only text preview with size and encoding limits.", false);
        if (Images.Contains(ext)) return new(ViewerKind.Image, "Image decoder support depends on the installed Windows components.", false);
        if (Videos.Contains(ext)) return new(ViewerKind.Video, "Playback requires a supported Windows media codec; extension alone is insufficient.", false);
        if (Audio.Contains(ext)) return new(ViewerKind.Audio, "Playback requires a supported Windows media codec.", false);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return new(ViewerKind.Pdf, "PDF requires a sandboxed or otherwise reviewed renderer.", true);
        return new(ViewerKind.Hex, "Unknown format: bounded read-only byte inspection only.", false);
    }
}
