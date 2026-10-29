using System.Runtime.InteropServices;

namespace Meridian.Native;

/// <summary>
/// The WatchKit P/Invoke surface — the only place in the app that names
/// watch_kit.dll. Signatures are transcribed from vendor/watchkit/watchkit.h
/// (v0.2.0) and cover only the calls the folder-watch service uses:
/// create / poll events / status / close, plus the string free and the
/// error reader those calls require.
///
/// SAFETY — the ownership rules every caller must keep (from the header):
/// - A watcher handle comes from <see cref="Create"/> and is released ONLY
///   by <see cref="Close"/>, exactly once. The kit joins its background
///   thread inside wa_close; anything through the handle after wa_close is
///   use-after-close, so callers serialize all calls and the close on one
///   lock and never touch the handle afterwards.
/// - Every JSON string the kit hands out through an out parameter is
///   caller-owned and must be released with <see cref="FreeString"/>
///   exactly once. <see cref="LastError"/> is the exception: it returns
///   thread-local storage owned by the kit — read it immediately on the
///   thread that made the failing call, never free it, never store it.
/// - String inputs are marshaled as NUL-terminated UTF-8 copies that live
///   only for the duration of the call; the kit parses them synchronously
///   and keeps no pointer (the config is consumed by wa_create itself).
/// All entry points are called defensively by FolderWatchService: a missing
/// or unloadable DLL surfaces as an exception at the call site, which the
/// service catches and turns into "watching is off".
/// </summary>
internal static partial class WatchKitNative
{
    private const string DllName = "watch_kit.dll";

    /// <summary>wa_create: builds the baseline snapshot synchronously and
    /// starts the background thread. Returns 0 (WA_OK) with a handle, or a
    /// WA_ERR_* code with the handle null.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Create(string configJson, out nint watcher);

    /// <summary>wa_poll_events: consumes pending events with seq greater
    /// than <paramref name="sinceSeq"/> (maxEvents 0 = no limit) and writes
    /// a caller-owned JSON string to <paramref name="json"/>.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_poll_events")]
    internal static partial int PollEvents(nint watcher, ulong sinceSeq, uint maxEvents, out nint json);

    /// <summary>wa_status: counters and the backend actually running, as a
    /// caller-owned JSON string.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_status")]
    internal static partial int Status(nint watcher, out nint json);

    /// <summary>wa_close: signals and JOINS the background thread, then
    /// frees the handle. A null handle is a no-op in the kit; callers still
    /// guard, so a zero here means a bug in the caller, not the kit.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_close")]
    internal static partial void Close(nint watcher);

    /// <summary>wa_free_string: releases a caller-owned string from the
    /// kit. Only ever called with a pointer the kit produced.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_free_string")]
    internal static partial void FreeString(nint value);

    /// <summary>wa_last_error: the calling thread's last error message, as
    /// a borrowed pointer — do NOT pass this to
    /// <see cref="FreeString"/>.</summary>
    [LibraryImport(DllName, EntryPoint = "wa_last_error")]
    internal static partial nint LastError();
}
