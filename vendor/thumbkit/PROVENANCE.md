# ThumbKit — vendored provenance

- **Kit:** ThumbKit (thumbnail and preview pipeline; JSON C ABI over `thumb_kit.dll`)
- **Version:** v0.2.0
- **Source release:** https://github.com/SeanShaffer88/ThumbKit/releases/tag/v0.2.0
  (asset `ThumbKit-v0.2.0-x64.zip`)
- **Release zip SHA-256:** `327c35016dbf9edf24b0cb0e8286b63adddc2131158a6d2d2a4ffacfbeae8150`
- **`thumb_kit.dll` SHA-256:** `0e5b9a0c1959fc1d506a45dd2c3fb61fd80bdea4f1f434a28805630a40e9be6c`
- **Date vendored:** 2026-10-29
- **License:** MIT (see `LICENSE` in this folder)

## Contents (verbatim from the release zip)

| File | Role |
|---|---|
| `thumb_kit.dll` | The x64 DLL. Copied beside `Meridian.exe` at build/publish time (see `src/Meridian.App/Meridian.App.csproj`); loaded at runtime through the P/Invoke wrapper in `src/Meridian.App/Native/ThumbKitNative.cs`. |
| `thumbkit.h` | The exact C ABI contract the wrapper is written against. |
| `README.md` | The kit's own documentation: built-in decoders (BMP, PPM, PNG, JPEG), honest `unsupported` behaviour for everything else, and the cache rules. |
| `LICENSE` | MIT license for the kit. |
| `thumb_kit.dll.lib` | MSVC import library. **Not used by Meridian** — .NET P/Invoke binds to the DLL by name at runtime and needs no import library. Kept so this vendored copy matches the release one-for-one and a future native consumer has it. |

## Notes

- The DLL is **x64-only**. The csproj content item is conditioned so the
  win-arm64 publish does not carry it; on any machine where the DLL cannot be
  loaded, Meridian's thumbnail cache falls back to the Windows shell
  thumbnails it used before — ThumbKit is an enhancement, never a requirement.
- Built-in decoded formats (per `thumbkit.h` / `README.md`): BMP, PPM, PNG,
  JPG/JPEG. Progressive and CMYK JPEGs report `unsupported`, and Meridian
  falls back to the shell path for those files exactly as it does for any
  other ThumbKit failure.
- Thumbnails the kit produces are 24-bit BMP files in its own bounded
  on-disk cache (`%LocalAppData%\Meridian\thumbcache`), never placeholder
  images.
