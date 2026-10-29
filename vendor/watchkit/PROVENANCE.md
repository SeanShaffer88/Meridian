# WatchKit — vendored provenance

- **Kit:** WatchKit (folder change and cache coordinator; JSON C ABI over `watch_kit.dll`)
- **Version:** v0.2.0
- **Source release:** https://github.com/SeanShaffer88/WatchKit/releases/tag/v0.2.0
  (asset `WatchKit-v0.2.0-x64.zip`)
- **Release zip SHA-256:** `ba0ce5187b113c5971908e8a13b2f80df726cadb9253e475f281300b5da3773a`
- **`watch_kit.dll` SHA-256:** `0bfc0a6f502a2a9267789f419770e645aa86f2cf8b5a030772c576adc8b3467c`
- **Date vendored:** 2026-10-29
- **License:** MIT (see `LICENSE` in this folder)

## Contents (verbatim from the release zip)

| File | Role |
|---|---|
| `watch_kit.dll` | The x64 DLL. Copied beside `Meridian.exe` at build/publish time (see `src/Meridian.App/Meridian.App.csproj`); loaded at runtime through the P/Invoke wrapper in `src/Meridian.App/Native/WatchKitNative.cs`. |
| `watchkit.h` | The exact C ABI contract the wrapper is written against. |
| `README.md` | The kit's own documentation, including its verification status. |
| `LICENSE` | MIT license for the kit. |
| `watch_kit.dll.lib` | MSVC import library. **Not used by Meridian** — .NET P/Invoke binds to the DLL by name at runtime and needs no import library. Kept so this vendored copy matches the release one-for-one and a future native consumer has it. |

## Notes

- The DLL is **x64-only**. The csproj content item is conditioned so the
  win-arm64 publish does not carry it; on any machine where the DLL cannot be
  loaded, Meridian's watch service degrades silently to manual refresh —
  watching is an enhancement, never a requirement.
- The kit's own README states the `ReadDirectoryChangesW` runtime path is
  **Unverified** on a real Windows machine (it compiles and type-checks; the
  polling fallback is fully tested). Meridian requests backend `"auto"`, and
  WatchKit falls back to polling by itself if the native backend cannot run —
  `wa_status` reports the backend actually in use.
