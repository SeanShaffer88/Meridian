using System.Runtime.InteropServices;
using Meridian.Native;

namespace Meridian.Services;

/// <summary>
/// The app's one ThumbKit pipeline, shared by every thumbnail request.
/// ThumbKit decodes BMP/PPM/PNG/JPEG itself (from scratch, inside the DLL)
/// and caches the results on disk under %LocalAppData%\Meridian\thumbcache;
/// this service is the managed side of that contract: request, wait in
/// short slices, poll, and read the finished thumbnail's bytes.
///
/// Failure honesty: thumbnails have a perfectly good fallback — the Windows
/// shell path ThumbnailCache used before ThumbKit existed. So every failure
/// here (DLL missing or unloadable, the kit refusing a file, an unsupported
/// variant like a progressive JPEG, a timeout, a vanished cache file) reads
/// as null, and the caller falls back. A failed pipeline creation is not
/// retried for every thumbnail: one recorded failure marks ThumbKit
/// unavailable for the session. Nothing in here can throw into the app.
/// </summary>
internal sealed class ThumbKitService : IDisposable
{
    /// <summary>How long one th_wait slice may block. Slicing the wait is
    /// what keeps every native call short: shutdown never waits behind a
    /// long call, it just declines the next slice.</summary>
    private const uint WaitSliceMilliseconds = 250;

    /// <summary>Wait slices per request before giving up (40 × 250 ms =
    /// 10 s — far beyond a bounded 256 px decode; a request that outlives
    /// it finishes in the kit's cache and serves the next ask).</summary>
    private const int MaximumWaitSlices = 40;

    private static readonly object SharedGate = new();
    private static ThumbKitService? _shared;
    private static bool _sharedUnavailable;

    private readonly object _nativeGate = new();
    private nint _handle;
    private bool _disposed;

    private ThumbKitService(nint handle) => _handle = handle;

    /// <summary>The process-wide pipeline, created on first use; null when
    /// ThumbKit is unavailable (and stays null for the session after the
    /// first failed creation).</summary>
    public static ThumbKitService? Shared
    {
        get
        {
            lock (SharedGate)
            {
                if (_shared is not null) return _shared;
                if (_sharedUnavailable) return null;
                _shared = TryCreate();
                if (_shared is null) _sharedUnavailable = true;
                return _shared;
            }
        }
    }

    /// <summary>Closes the shared pipeline, if any. Called when the main
    /// window closes; thumbnail requests after this simply fall back.</summary>
    public static void ShutdownShared()
    {
        lock (SharedGate)
        {
            _shared?.Dispose();
            _shared = null;
        }
    }

    private static ThumbKitService? TryCreate()
    {
        nint handle = nint.Zero;
        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian", "thumbcache");
            var config = ThumbKitRouting.BuildConfigJson(cacheDirectory);
            // SAFETY: the config string is a marshaled copy alive only for
            // this call, which the kit parses synchronously (see
            // ThumbKitNative). On success the handle is owned by the
            // returned service; on any failure it is closed here, once.
            var result = ThumbKitNative.Create(config, out handle);
            if (result != 0 || handle == nint.Zero)
            {
                FailureLog.TryRecord(new InvalidOperationException(
                    $"ThumbKit refused to start (code {result}): {ReadLastError()}"));
                return null;
            }

            var service = new ThumbKitService(handle);
            handle = nint.Zero; // ownership moved to the service
            return service;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            // No usable thumb_kit.dll beside the exe (ARM64 build, damaged
            // install): shell thumbnails carry on alone.
            FailureLog.TryRecord(ex);
            return null;
        }
        catch (Exception ex)
        {
            FailureLog.TryRecord(ex);
            return null;
        }
        finally
        {
            if (handle != nint.Zero)
            {
                try { ThumbKitNative.Close(handle); }
                catch (Exception) { /* a failed cleanup must not replace the real outcome */ }
            }
        }
    }

    /// <summary>
    /// Asks the kit for a thumbnail of <paramref name="sourcePath"/> fitting
    /// the given box and returns the finished BMP's bytes, or null when the
    /// kit cannot produce one (for any reason — the caller falls back). The
    /// bytes are read promptly because the thumbnail file belongs to the
    /// kit's bounded cache and may be evicted at any time; the BMP is small
    /// (the pipeline caps thumbnails at 256 px). Runs the blocking native
    /// sequence on a background thread.
    /// </summary>
    public async Task<byte[]?> GetThumbnailBytesAsync(string sourcePath, int maxWidth, int maxHeight)
    {
        string preset;
        try
        {
            preset = ThumbKitRouting.BuildPresetJson(maxWidth, maxHeight);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return await Task.Run(() => Generate(sourcePath, preset)).ConfigureAwait(false);
    }

    private byte[]? Generate(string sourcePath, string preset)
    {
        try
        {
            ulong requestId;
            lock (_nativeGate)
            {
                if (_disposed || _handle == nint.Zero) return null;
                // SAFETY: the handle is used only under _nativeGate, and
                // Dispose zeroes it under the same lock before closing, so
                // this call can neither overlap nor follow th_close.
                if (ThumbKitNative.Request(_handle, sourcePath, preset, out requestId) != 0) return null;
            }

            var terminal = false;
            for (var slice = 0; slice < MaximumWaitSlices && !terminal; slice++)
            {
                int waitResult;
                lock (_nativeGate)
                {
                    if (_disposed || _handle == nint.Zero) return null;
                    waitResult = ThumbKitNative.Wait(_handle, requestId, WaitSliceMilliseconds);
                }

                if (waitResult == 0) terminal = true;
                else if (waitResult != ThumbKitNative.WaitTimeout) return null; // unknown id, dead pipeline…
            }

            if (!terminal) return null;

            string json;
            lock (_nativeGate)
            {
                if (_disposed || _handle == nint.Zero) return null;
                if (ThumbKitNative.Poll(_handle, requestId, out var jsonPointer) != 0) return null;
                try
                {
                    json = Marshal.PtrToStringUTF8(jsonPointer) ?? string.Empty;
                }
                finally
                {
                    // Caller-owned per the header: freed exactly once.
                    if (jsonPointer != nint.Zero) ThumbKitNative.FreeString(jsonPointer);
                }
            }

            var result = ThumbKitResult.Parse(json);
            if (result.State != ThumbKitState.Done || string.IsNullOrWhiteSpace(result.ThumbPath)) return null;
            return File.ReadAllBytes(result.ThumbPath);
        }
        catch (Exception)
        {
            // Any native, parse or IO failure: the caller falls back to the
            // shell thumbnail. Per-file failures are routine (corrupt image,
            // evicted cache entry), so they are not failure-log material.
            return null;
        }
    }

    /// <summary>The kit's thread-local error text for the call that just
    /// failed on THIS thread. The pointer is borrowed (never freed); it is
    /// copied into a managed string immediately.</summary>
    private static string ReadLastError()
    {
        try
        {
            var pointer = ThumbKitNative.LastError();
            return pointer == nint.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        nint handle;
        lock (_nativeGate)
        {
            if (_disposed) return;
            _disposed = true;
            handle = _handle;
            _handle = nint.Zero;
        }

        if (handle == nint.Zero) return;
        // SAFETY: _disposed is set and _handle is zeroed under the lock, and
        // every native call site checks both under the same lock before
        // calling — so no call is in flight (each holds the lock only for
        // one bounded slice) and none can start. Closing outside the lock
        // therefore cannot race a call, and the lock is not held while
        // th_close joins the kit's workers.
        try { ThumbKitNative.Close(handle); }
        catch (Exception) { /* window teardown must never throw */ }
    }
}
