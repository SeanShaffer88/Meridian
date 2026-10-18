# Universal Viewer and Integrated Player

**Status:** backend routing and bounded binary preview are implemented on the feature branch. The WinUI Preview dialog, image path and MediaPlayerElement controls compile in the verified x64/ARM64 baseline. **Actual image decoding, codec playback and interactive lifecycle behavior remain unverified**; see [evidence](VERIFICATION_REPORT.md). PDF rendering remains unimplemented.

## Intended experience

- **Universal Viewer:** read-only preview for text/code, images, audio, video, PDF and unknown binary data, with hexadecimal fallback. Specialized formats will require reviewed decoders or opt-in external handlers; “universal” describes a unified entry point, not literal support for every format.
- **Integrated Video Player:** in-app playback with play/pause, seeking, volume, mute, full-screen, playback speed, subtitles where supported, and media metadata. Windows MediaPlayer/MediaPlayerElement is the proposed initial backend; actual codecs vary by Windows installation and installed extensions.
- **Safety:** never execute a file merely to preview it; no silent codec installation, no untrusted shell handlers, no automatic cloud uploads, no modification of originals. Use size/time limits and separate-process isolation for risky parsers where feasible.
- **Accessibility:** keyboard controls, captions/subtitle selection when supported, readable error messages, high contrast and screen reader labels.

## Implemented backend foundation

- `ViewerRouter` classifies common extensions and explicitly distinguishes codec/renderer requirements.
- `SafeFilePreview.ReadPrefixAsync` defaults to 64 KiB, accepts a caller limit up to 1 MiB and reads an explicitly selected regular file; `HexDump` formats the bounded bytes. The current lexical/reparse checks are not race-proof.

## Release gates

- [x] Commit a Preview toolbar action with read-only text/hex display and an initial MediaPlayerElement dialog (build verified; interactive workflow testing pending).
- [ ] Verify lifecycle disposal, selection changes and viewer usability on Windows.
- [x] Integrate MediaPlayerElement and no-autoplay transport controls.
- [ ] Verify actual playback, seek, audio, lifecycle and codec failures on Windows 11 x64/ARM64; dedicated subtitle controls remain planned.
- [x] Commit an initial image display path using Windows image decoding (build verified; interactive workflow testing pending).
- [x] Add bounded strict UTF-8/BOM-marked UTF-16 decoding with binary/invalid-sequence fallback.
- [ ] Add decoded-image resource limits and sandboxed or reviewed PDF handling.
- [ ] Test malformed media, decompression bombs, symlinks/junctions, huge files, missing codecs, cancellation, and rapid selection changes.
- [ ] Document supported formats from verified playback tests, not extension guesses.

## Current preview resource limits

The WinUI prototype renders at most 4 KiB of hexadecimal data, reads at most 64 KiB for text previews, uses strict UTF-8 or BOM-marked UTF-16 LE/BE with hexadecimal fallback for invalid sequences or NUL-containing content, and refuses image files larger than 64 MiB. These are initial defensive limits, **not** a guarantee against decompression bombs, codec vulnerabilities, or filesystem races. Decoded pixel count, parser isolation, media resource budgets and Windows hands-on testing remain open release blockers.

## Preview path boundaries

The bounded text/hex preview reader now rejects reparse-point ancestors and the final reparse-point file, and honors cooperative cancellation before opening and while reading. This is a **pre-open inspection**, not a race-free handle-based identity check: concurrent filesystem changes can invalidate it. The image and media playback paths now use the same shared pre-open ancestor-validation policy as text and hex preview. A unified handle-based policy and adversarial Windows tests are required before treating untrusted filesystem previews as hardened.

The bounded byte reader performs a second pre-read path inspection after opening its stream. This reduces some race windows but cannot establish that the opened handle refers to the same object as the path inspected; race-free handle verification remains an outstanding security requirement.
