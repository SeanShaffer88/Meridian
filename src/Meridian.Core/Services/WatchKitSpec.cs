using System.Text.Json;

namespace Meridian.Services;

/// <summary>
/// Builds the JSON config handed to WatchKit's <c>wa_create</c>. The shape is
/// the kit's contract (vendor/watchkit/watchkit.h): unknown keys are a hard
/// parse error over there, so this builder emits exactly the keys Meridian
/// means to set — the watched root, the change-discovery backend, and the
/// scan interval — and nothing else. Kept pure so the exact bytes the native
/// side will see are unit-tested.
/// </summary>
public static class WatchKitSpec
{
    /// <summary>Let the kit pick: native (ReadDirectoryChangesW) on Windows,
    /// polling elsewhere, with the kit's own fallback to polling when the
    /// native backend cannot start.</summary>
    public const string BackendAuto = "auto";

    /// <summary>Force the kit's native Windows backend (it still falls back
    /// to polling when it cannot start, and says so in its status).</summary>
    public const string BackendNative = "native";

    /// <summary>Force the portable polling engine even on Windows.</summary>
    public const string BackendPolling = "polling";

    /// <summary>The scan interval the app asks for. Under the native backend
    /// discovery is event-driven and this only bounds the polling fallback;
    /// under polling it is the snapshot cadence.</summary>
    public const int DefaultPollIntervalMs = 250;

    private static readonly string[] KnownBackends = [BackendAuto, BackendNative, BackendPolling];

    /// <summary>
    /// The config JSON for watching one folder recursively. Throws for a
    /// blank root, an unknown backend, or a non-positive interval (the kit's
    /// manual mode at interval 0 is a test driver, not something the app
    /// ever wants): catching a nonsense spec here is cheaper than a native
    /// call that fails with a code.
    /// </summary>
    public static string BuildConfigJson(string rootPath, string backend = BackendAuto, int pollIntervalMs = DefaultPollIntervalMs)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A watch root is required.", nameof(rootPath));
        if (!KnownBackends.Contains(backend, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown WatchKit backend \"{backend}\"; expected auto, native or polling.", nameof(backend));
        if (pollIntervalMs < 1)
            throw new ArgumentOutOfRangeException(nameof(pollIntervalMs), pollIntervalMs, "The poll interval must be positive; 0 selects the kit's manual mode, which the app never uses.");

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["roots"] = new[] { rootPath },
            ["backend"] = backend,
            ["poll_interval_ms"] = pollIntervalMs,
        });
    }
}
