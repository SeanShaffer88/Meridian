# Universal Viewer and Integrated Player

**Status:** backend routing and bounded binary preview are implemented on the feature branch. An initial WinUI Preview dialog and MediaPlayerElement transport controls are committed, but **Windows compilation and actual codec playback are not yet verified**. An initial Windows image decoder path is also committed but not verified. PDF rendering remains unimplemented.

## Intended experience

- **Universal Viewer:** read-only preview for text/code, images, audio, video, PDF and unknown binary data, with hexadecimal fallback. Specialized formats will require reviewed decoders or opt-in external handlers; “universal” describes a unified entry point, not literal support for every format.
- **Integrated Video Player:** in-app playback with play/pause, seeking, volume, mute, full-screen, playback speed, subtitles where supported, and media metadata. Windows MediaPlayer/MediaPlayerElement is the proposed initial backend; actual codecs vary by Windows installation and installed extensions.
- **Safety:** never execute a file merely to preview it; no silent codec installation, no untrusted shell handlers, no automatic cloud uploads, no modification of originals. Use size/time limits and separate-process isolation for risky parsers where feasible.
- **Accessibility:** keyboard controls, captions/subtitle selection when supported, readable error messages, high contrast and screen reader labels.

## Implemented backend foundation

- `ViewerRouter` classifies common extensions and explicitly distinguishes codec/renderer requirements.
- `SafeFilePreview.ReadPrefixAsync` reads at most 1 MiB of an explicitly selected regular file; `HexDump` formats the bounded bytes. The current lexical/reparse checks are not race-proof.

## Release gates

- [x] Commit a Preview toolbar action with read-only text/hex display and an initial MediaPlayerElement dialog (not yet Windows-tested).
- [ ] Verify lifecycle disposal, selection changes and viewer usability on Windows.
- [ ] Add MediaPlayerElement and verify actual video playback, seek, audio, subtitles and codec failures on Windows 11 x64/ARM64.
- [x] Commit an initial image display path using Windows image decoding (not yet Windows-tested).
- [ ] Add bounded text decoding, image decode resource limits, and sandboxed or reviewed PDF handling.
- [ ] Test malformed media, decompression bombs, symlinks/junctions, huge files, missing codecs, cancellation, and rapid selection changes.
- [ ] Document supported formats from verified playback tests, not extension guesses.
