using System.Runtime.InteropServices;
using Meridian.Native;
using Microsoft.UI.Dispatching;

namespace Meridian.Services;

/// <summary>
/// One pane's live folder watch, backed by WatchKit. A service instance owns
/// one watcher over one folder (created with backend "auto": native
/// ReadDirectoryChangesW on Windows, the kit's own polling fallback when
/// that cannot run). A timer drains the kit's event queue, the pure Core
/// logic decides whether a batch can change the pane's listing
/// (<see cref="WatchRefresh"/>) and when a burst has settled enough to act
/// on it (<see cref="WatchDebouncer"/>), and the refresh itself is handed to
/// the UI thread, which reloads the pane through the same path manual
/// refresh uses.
///
/// Failure honesty: watching is an enhancement, never a requirement. If the
/// DLL is missing or unloadable (the ARM64 build carries no x64 DLL), if
/// the kit refuses the folder, or if anything in the drain loop breaks, the
/// watch simply stops — the app behaves exactly as it did before watching
/// existed, with manual refresh. Nothing in here can throw into the app:
/// every native call is wrapped, and every failure is recorded in the local
/// failure log and swallowed.
/// </summary>
internal sealed class FolderWatchService : IDisposable
{
    /// <summary>How often the drain timer polls the kit. The kit's native
    /// backend notices changes immediately; this only bounds how long a
    /// noticed change waits before the debounce clock sees it.</summary>
    private const int DrainTickMilliseconds = 100;

    private readonly object _nativeGate = new();
    private readonly WatchDebouncer _debouncer = new();
    private readonly Func<bool> _includeDescendants;
    private readonly Action _refreshDue;
    private readonly DispatcherQueue _dispatcher;
    private Timer? _timer;
    private nint _handle;
    private ulong _lastSeq;
    private bool _disposed;

    private FolderWatchService(nint handle, string folder, DispatcherQueue dispatcher, Func<bool> includeDescendants, Action refreshDue)
    {
        _handle = handle;
        Folder = folder;
        _dispatcher = dispatcher;
        _includeDescendants = includeDescendants;
        _refreshDue = refreshDue;
    }

    /// <summary>The folder this watch covers — the exact string the watcher
    /// was created for, so the window can tell a stale watch from a current
    /// one.</summary>
    public string Folder { get; }

    /// <summary>The backend WatchKit reports actually running ("native" or
    /// "polling"), read once at start for diagnostics; null when the status
    /// call itself failed. Never used to decide anything — the kit's
    /// fallback rules make the running backend correct by construction.</summary>
    public string? RunningBackend { get; private set; }

    /// <summary>
    /// Starts a watch over <paramref name="folder"/>, or returns null when
    /// watching is unavailable for any reason. Must be called on the UI
    /// thread: the dispatcher is captured for refresh callbacks, and
    /// <paramref name="refreshDue"/> always runs on it.
    /// </summary>
    public static FolderWatchService? TryStart(string folder, DispatcherQueue dispatcher, Func<bool> includeDescendants, Action refreshDue)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(includeDescendants);
        ArgumentNullException.ThrowIfNull(refreshDue);

        nint handle = nint.Zero;
        FolderWatchService? service = null;
        try
        {
            var config = WatchKitSpec.BuildConfigJson(folder, WatchKitSpec.BackendAuto);
            // SAFETY: the config string is a marshaled copy alive only for
            // this call, which the kit parses synchronously (see
            // WatchKitNative). On success the handle is owned by the
            // returned service; on any failure it is closed here, once.
            var result = WatchKitNative.Create(config, out handle);
            if (result != 0 || handle == nint.Zero)
            {
                FailureLog.TryRecord(new InvalidOperationException(
                    $"WatchKit refused to watch \"{folder}\" (code {result}): {ReadLastError()}"));
                return null;
            }

            service = new FolderWatchService(handle, folder, dispatcher, includeDescendants, refreshDue);
            handle = nint.Zero; // ownership moved to the service
            service.QueryRunningBackend();
            service._timer = new Timer(service.OnDrainTick, null, DrainTickMilliseconds, DrainTickMilliseconds);
            return service;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            // No usable watch_kit.dll beside the exe (ARM64 build, damaged
            // install): watching is off, the app is unchanged.
            FailureLog.TryRecord(ex);
            service?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            FailureLog.TryRecord(ex);
            service?.Dispose();
            return null;
        }
        finally
        {
            if (service is null && handle != nint.Zero)
            {
                try { WatchKitNative.Close(handle); }
                catch (Exception) { /* a failed cleanup must not replace the real outcome */ }
            }
        }
    }

    /// <summary>Reads wa_status once so diagnostics can say which backend is
    /// really running. Best-effort: any failure leaves
    /// <see cref="RunningBackend"/> null and the watch running.</summary>
    private void QueryRunningBackend()
    {
        try
        {
            // SAFETY: serialized with every other native call on
            // _nativeGate; the returned string is caller-owned and freed
            // exactly once, in the finally.
            string json;
            nint jsonPointer = nint.Zero;
            lock (_nativeGate)
            {
                if (_handle == nint.Zero || WatchKitNative.Status(_handle, out jsonPointer) != 0) return;
            }

            try
            {
                json = Marshal.PtrToStringUTF8(jsonPointer) ?? string.Empty;
            }
            finally
            {
                if (jsonPointer != nint.Zero) WatchKitNative.FreeString(jsonPointer);
            }

            RunningBackend = WatchStatus.Parse(json).Backend;
        }
        catch (Exception)
        {
            RunningBackend = null;
        }
    }

    /// <summary>The drain loop: poll the kit, advance the sequence cursor,
    /// feed relevant batches to the debouncer, and dispatch at most one
    /// refresh when the debouncer says the burst has settled. Any failure
    /// stops the watch quietly — a half-broken watcher that keeps a folder
    /// half-fresh is worse than none.</summary>
    private void OnDrainTick(object? state)
    {
        var due = false;
        lock (_nativeGate)
        {
            if (_disposed || _handle == nint.Zero) return;
            try
            {
                // SAFETY: this lock serializes every native call on the
                // handle, and Dispose takes the same lock before closing,
                // so no call can overlap or follow wa_close. The events
                // string is caller-owned: copied out, then freed once.
                var result = WatchKitNative.PollEvents(_handle, _lastSeq, maxEvents: 0, out var jsonPointer);
                if (result != 0)
                {
                    FailLocked(new InvalidOperationException(
                        $"WatchKit event poll failed for \"{Folder}\" (code {result}): {ReadLastError()}"));
                    return;
                }

                string json;
                try
                {
                    json = Marshal.PtrToStringUTF8(jsonPointer) ?? string.Empty;
                }
                finally
                {
                    if (jsonPointer != nint.Zero) WatchKitNative.FreeString(jsonPointer);
                }

                var batch = WatchEventBatch.Parse(json);
                _lastSeq = batch.NextSeq;
                var now = DateTimeOffset.UtcNow;
                if (batch.Events.Count > 0 && WatchRefresh.AffectsListing(Folder, batch.Events, _includeDescendants()))
                    _debouncer.NoteChange(now);
                if (_debouncer.IsDue(now))
                {
                    _debouncer.MarkFired();
                    due = true;
                }
            }
            catch (Exception ex)
            {
                FailLocked(ex);
                return;
            }
        }

        if (!due) return;
        try
        {
            // The callback re-validates everything on the UI thread (the
            // pane may have navigated or the window may be closing), and
            // the disposed check keeps a queued callback from acting for a
            // watch that has already been torn down.
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed) _refreshDue();
            });
        }
        catch (Exception)
        {
            // The dispatcher is gone (window closing): nothing to refresh.
        }
    }

    /// <summary>Stops the watch after a native or parse failure. Caller must
    /// hold _nativeGate. Records the failure, then performs the same
    /// teardown as Dispose — the window still holds this instance and will
    /// dispose it later, which is a no-op by then.</summary>
    private void FailLocked(Exception exception)
    {
        FailureLog.TryRecord(exception);
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        if (_handle != nint.Zero)
        {
            try { WatchKitNative.Close(_handle); }
            catch (Exception) { /* teardown must never throw */ }
            _handle = nint.Zero;
        }
    }

    /// <summary>The kit's thread-local error text for the call that just
    /// failed on THIS thread. The pointer is borrowed (never freed); it is
    /// copied into a managed string immediately.</summary>
    private static string ReadLastError()
    {
        try
        {
            var pointer = WatchKitNative.LastError();
            return pointer == nint.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        lock (_nativeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            if (_handle != nint.Zero)
            {
                // SAFETY: every other native call holds this same lock for
                // its whole duration and checks _disposed under it first,
                // so no call is in flight now and none can start after —
                // wa_close's thread join cannot race a poll.
                try { WatchKitNative.Close(_handle); }
                catch (Exception) { /* window teardown must never throw */ }
                _handle = nint.Zero;
            }
        }
    }
}
