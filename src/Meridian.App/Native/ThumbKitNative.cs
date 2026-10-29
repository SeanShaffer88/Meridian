using System.Runtime.InteropServices;

namespace Meridian.Native;

/// <summary>
/// The ThumbKit P/Invoke surface — the only place in the app that names
/// thumb_kit.dll. Signatures are transcribed from vendor/thumbkit/thumbkit.h
/// (v0.2.0) and cover only the calls the thumbnail service uses:
/// create / request / wait / poll / close, plus the string free and the
/// error reader those calls require. (No provider is ever registered:
/// Meridian uses the kit's built-in BMP/PPM/PNG/JPEG decoders only.)
///
/// SAFETY — the ownership rules every caller must keep (from the header):
/// - A pipeline handle comes from <see cref="Create"/> and is released ONLY
///   by <see cref="Close"/>, exactly once. The kit cancels queued work, lets
///   a running generation finish, and joins every worker inside th_close;
///   anything through the handle after th_close is use-after-close, so
///   callers guard every call with the handle under one lock and zero the
///   handle before closing (see ThumbKitService).
/// - The one JSON string the kit hands out (th_poll's result) is
///   caller-owned and must be released with <see cref="FreeString"/>
///   exactly once. <see cref="LastError"/> is the exception: thread-local
///   storage owned by the kit — read immediately, never free, never store.
/// - String inputs are marshaled as NUL-terminated UTF-8 copies that live
///   only for the duration of the call. The preset argument may be null,
///   which the kit reads as "no preset".
/// All entry points are called defensively by ThumbKitService: a missing or
/// unloadable DLL surfaces as an exception at the call site, which the
/// service catches and turns into "fall back to shell thumbnails".
/// </summary>
internal static partial class ThumbKitNative
{
    private const string DllName = "thumb_kit.dll";

    /// <summary>th_wait result when the wait elapsed and the request is
    /// still in flight (TH_ERR_TIMEOUT in the header's enum).</summary>
    internal const int WaitTimeout = 10;

    /// <summary>th_create: builds the cache index and starts the worker
    /// pool. Returns 0 (TH_OK) with a handle, or a TH_ERR_* code with the
    /// handle null.</summary>
    [LibraryImport(DllName, EntryPoint = "th_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Create(string configJson, out nint handle);

    /// <summary>th_request: queues a thumbnail for
    /// <paramref name="sourcePath"/> under a preset ({"max_w":N,"max_h":N}
    /// or null) and writes the request id to <paramref name="requestId"/>.</summary>
    [LibraryImport(DllName, EntryPoint = "th_request", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Request(nint handle, string sourcePath, string? presetJson, out ulong requestId);

    /// <summary>th_wait: blocks until the request is terminal (returns 0)
    /// or the timeout elapses (returns <see cref="WaitTimeout"/>; the
    /// request is untouched).</summary>
    [LibraryImport(DllName, EntryPoint = "th_wait")]
    internal static partial int Wait(nint handle, ulong requestId, uint timeoutMilliseconds);

    /// <summary>th_poll: the request's state JSON, caller-owned.</summary>
    [LibraryImport(DllName, EntryPoint = "th_poll")]
    internal static partial int Poll(nint handle, ulong requestId, out nint json);

    /// <summary>th_close: cancels queued requests, finishes a running
    /// generation, joins all workers, frees the handle.</summary>
    [LibraryImport(DllName, EntryPoint = "th_close")]
    internal static partial void Close(nint handle);

    /// <summary>th_free_string: releases a caller-owned string from the
    /// kit. Only ever called with a pointer the kit produced.</summary>
    [LibraryImport(DllName, EntryPoint = "th_free_string")]
    internal static partial void FreeString(nint value);

    /// <summary>th_last_error: the calling thread's last error message, as
    /// a borrowed pointer — do NOT pass this to
    /// <see cref="FreeString"/>.</summary>
    [LibraryImport(DllName, EntryPoint = "th_last_error")]
    internal static partial nint LastError();
}
