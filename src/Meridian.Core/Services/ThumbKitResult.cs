using System.Text.Json;

namespace Meridian.Services;

/// <summary>The lifecycle states of one ThumbKit request, mirroring the
/// state strings in th_poll's JSON. A state string this build does not know
/// maps to <see cref="Unknown"/> rather than failing the parse.</summary>
public enum ThumbKitState
{
    Queued,
    Running,
    Done,
    Failed,
    Unsupported,
    Cancelled,
    Unknown,
}

/// <summary>
/// One parsed th_poll result. When <see cref="State"/> is Done,
/// <see cref="ThumbPath"/> names a 24-bit BMP file inside the kit's cache —
/// a file the cache owns, so a caller that needs the pixels beyond the
/// moment must read them promptly rather than hold the path. Dimensions are
/// 0 when the kit does not know them; <see cref="Error"/> carries the kit's
/// own failure message for the Failed/Unsupported states.
/// </summary>
public sealed record ThumbKitResult(
    ThumbKitState State,
    string? ThumbPath,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    string? Error)
{
    /// <summary>True for the states no request leaves: done, failed,
    /// unsupported, cancelled — the set th_wait waits for.</summary>
    public bool IsTerminal => State is ThumbKitState.Done or ThumbKitState.Failed or ThumbKitState.Unsupported or ThumbKitState.Cancelled;

    /// <summary>Parses one poll result. Throws <see cref="JsonException"/>
    /// on malformed input: the JSON comes from the kit under its documented
    /// contract, so an unreadable shape is a contract break, and the caller
    /// treats it exactly like a failed request (fall back, never crash).</summary>
    public static ThumbKitResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var state = root.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String
            ? stateElement.GetString() switch
            {
                "queued" => ThumbKitState.Queued,
                "running" => ThumbKitState.Running,
                "done" => ThumbKitState.Done,
                "failed" => ThumbKitState.Failed,
                "unsupported" => ThumbKitState.Unsupported,
                "cancelled" => ThumbKitState.Cancelled,
                _ => ThumbKitState.Unknown,
            }
            : ThumbKitState.Unknown;
        var thumbPath = root.TryGetProperty("thumb_path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String
            ? pathElement.GetString()
            : null;
        var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;
        return new ThumbKitResult(
            state,
            thumbPath,
            ReadInt(root, "width"),
            ReadInt(root, "height"),
            ReadInt(root, "source_width"),
            ReadInt(root, "source_height"),
            error);
    }

    private static int ReadInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.TryGetInt32(out var value) ? value : 0;
}
