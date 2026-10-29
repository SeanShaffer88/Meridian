using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// The pure routing and JSON-building half of the ThumbKit wiring. ThumbKit
/// (vendor/thumbkit/thumbkit.h) has real built-in decoders for exactly five
/// extensions — BMP, PPM, PNG, JPG, JPEG — and honestly reports every other
/// format "unsupported". Meridian therefore asks ThumbKit first only for
/// those, and lets everything else go straight to the shell thumbnail path
/// the app already had. This type owns the supported set plus the kit's
/// config/preset JSON (unknown keys are a hard parse error over there, so
/// the builders emit exactly the documented keys), keeping both unit-tested
/// and the native wrapper thin.
/// </summary>
public static class ThumbKitRouting
{
    /// <summary>The extensions with a built-in ThumbKit decoder, lower-case,
    /// no dot — the exact set th_create registers (bmp/ppm/png/jpg/jpeg).</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = ["bmp", "ppm", "png", "jpg", "jpeg"];

    private static readonly HashSet<string> Supported = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="extension"/> — with or without the
    /// leading dot, in any case — names a format ThumbKit decodes itself.</summary>
    public static bool IsSupportedExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var trimmed = extension.Trim();
        if (trimmed.StartsWith('.')) trimmed = trimmed[1..];
        return trimmed.Length > 0 && Supported.Contains(trimmed);
    }

    /// <summary>True when the file at <paramref name="path"/> has an
    /// extension ThumbKit decodes itself.</summary>
    public static bool IsSupportedPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsSupportedExtension(Path.GetExtension(path));

    /// <summary>The hard cap the pipeline is created with when the caller
    /// does not say otherwise — the kit's own default, and comfortably above
    /// the largest preset the app requests (192 px grid thumbnails).</summary>
    public const int DefaultMaximumThumbnailSize = 256;

    /// <summary>
    /// The th_create config JSON for a pipeline whose cache lives in
    /// <paramref name="cacheDirectory"/>. Only cache_dir and the thumbnail
    /// caps are set; the byte/entry bounds and worker counts keep the kit's
    /// documented defaults. Throws for a blank directory or a cap outside
    /// the kit's 1..=8192 range.
    /// </summary>
    public static string BuildConfigJson(
        string cacheDirectory,
        int maxThumbWidth = DefaultMaximumThumbnailSize,
        int maxThumbHeight = DefaultMaximumThumbnailSize)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("A thumbnail cache directory is required.", nameof(cacheDirectory));
        CheckDimension(maxThumbWidth, nameof(maxThumbWidth));
        CheckDimension(maxThumbHeight, nameof(maxThumbHeight));

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["cache_dir"] = cacheDirectory,
            ["max_thumb_width"] = maxThumbWidth,
            ["max_thumb_height"] = maxThumbHeight,
        });
    }

    /// <summary>The per-request preset JSON ({"max_w":N,"max_h":N}). The kit
    /// clamps presets to the config caps; values here must still be inside
    /// its 1..=8192 range.</summary>
    public static string BuildPresetJson(int maxWidth, int maxHeight)
    {
        CheckDimension(maxWidth, nameof(maxWidth));
        CheckDimension(maxHeight, nameof(maxHeight));
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["max_w"] = maxWidth,
            ["max_h"] = maxHeight,
        });
    }

    private static void CheckDimension(int value, string parameterName)
    {
        if (value is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(parameterName, value, "Thumbnail dimensions must be between 1 and 8192 (the kit's documented range).");
    }
}
