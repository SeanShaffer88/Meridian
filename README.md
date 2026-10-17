# Meridian

Meridian is an early-stage, free and open-source dual-pane file manager for Windows 11. It aims to combine modern navigation with serious file-management tools while prioritizing safe operations, responsiveness, and user control.

> **Status: early foundation; not release-ready.** The initial source has not yet been compiled or run on Windows in the current development environment.

## Current foundation

- Dual panes with editable folder paths, folder navigation, parent/back/forward controls, and refresh.
- Background directory enumeration with directory-first sorting.
- Basic file launching through the Windows shell.
- Conservative cross-pane copy for files and directories.
- Copy staging in the destination directory; existing destinations are never overwritten.
- Reparse points are rejected rather than followed during copy.
- Dark UI with restrained red accents. No telemetry or network calls are implemented.
- Unit-test source for directory listing and copy behavior.

## Product vision

Meridian's long-term goals—including advanced panes and tabs, a transfer queue, search, previews, archive support, and developer tools—are documented in the [Product Vision](docs/PRODUCT_VISION.md). These are aspirations, not claims that those features are already implemented. The [Roadmap](docs/ROADMAP.md) tracks planned work.

## Planned

Flexible 1–4 pane layouts, tabs, workspace presets, folder tree and Miller Columns, advanced search, batch rename, previews, archive support, checksums, duplicate discovery, file comparison, extension points, settings, and packaged releases.

## Platform support

- **Target OS:** Windows 11 only (minimum supported build: 22000).
- **Supported architectures:** x64 and ARM64, with each architecture built and tested separately in CI.
- Windows 10 is not a supported target. ARM64 compatibility must be validated on Windows 11 ARM64 hardware or an appropriate test environment before release.
- .NET 10 SDK and Visual Studio with the WinUI application development workload.
- Network access for the first NuGet restore unless dependencies are cached.

## Build and test

Run from PowerShell at the repository root:

```powershell
./scripts/test.ps1
./scripts/build.ps1 -Configuration Release -Runtime win-x64
```

A successful build is not a release sign-off. Windows runtime, filesystem edge-case, accessibility, and performance checks remain necessary.

## Safety notes

- Runs with the current user's privileges; no administrator elevation is requested.
- Copy refuses to overwrite existing destinations and does not yet verify copied data with hashes.
- The current UI does not expose cancellation. A single large-file copy is not interruptible until the OS copy call returns.
- A failed cleanup may leave a `.meridian-*.partial` staging item.
- Do not use this development build for irreplaceable data; test with disposable folders first.

## Competitive research

See [the competitor research and feature matrix](docs/COMPETITIVE_RESEARCH.md) covering Q-Dir, Tablacus Explorer, Multi Commander, Unreal Commander, and Double Commander. This is desk research, not hands-on benchmarking.

## Architecture

- `src/Meridian.App`: WinUI presentation and navigation.
- `src/Meridian.Core`: UI-independent directory listing and conservative copy operations.
- `tests/Meridian.Core.Tests`: core filesystem tests.
- `docs/`: architecture, threat model, roadmap, performance, release gates, and verification.

## License

MIT. See [LICENSE](LICENSE). Third-party dependencies remain under their own licenses; see [CREDITS.md](CREDITS.md).
